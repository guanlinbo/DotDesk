using DotDesk.Core.Logging;
using DotDesk.Core.Network;
using DotDesk.Core.Protocol;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DotDesk.Controller.Network
{
    public sealed class WebRtcReceiver : IDisposable
    {
        public event Action? OnPeerJoined2;
        public event Action? OnAuthSuccess;
        public event Action? OnAuthFailed;
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action<string>? OnConnectionFailed;
        public event Action<string>? OnConnectionStatus;
        public event Action<string>? OnLog;
        public event Action<byte[], int, int>? OnVideoFrame;
        public event Action<string>? OnRemoteCursorChanged;

        public bool IsConnected =>
            _mediaReady || _pc?.connectionState == RTCPeerConnectionState.connected;

        public string RemoteDeviceName { get; private set; } = "远程设备";

        private readonly SignalingClient _sig;
        private readonly List<RTCIceCandidateInit> _pendingCandidates = new();

        private RTCPeerConnection? _pc;
        private RTCDataChannel? _dc;
        private H264Decoder? _decoder;
        private H264RtpDepacketizer? _h264Depacketizer;

        private bool _disposed;
        private bool _authPassed;
        private bool _disconnecting;
        private bool _mediaReady;
        private bool _connectedReported;
        private bool _firstFrameReceived;
        private bool _allowRelay;
        private bool _remoteDescSet;
        private string? _pendingOfferSdp;
        private int _connectionGeneration;
        private int _receivedPayloadCount;
        private int _decodedFrameCount;
        private DateTime _lastFrameAt = DateTime.MinValue;

        public WebRtcReceiver(string signalingServerUrl, string targetDeviceCode)
        {
            _sig = new SignalingClient(signalingServerUrl, targetDeviceCode, "guest");

            _sig.OnLog += msg => Log(msg);

            _sig.OnPeerJoined += () =>
            {
                Log("被控端在线，等待 Offer...");
                OnPeerJoined2?.Invoke();
            };

            _sig.OnAuthResultInfo += OnAuthResultReceived;
            _sig.OnOffer += OnOfferReceived;
            _sig.OnIceCandidate += OnRemoteIce;

            _sig.OnPeerLeftGraceful += () =>
            {
                Log("被控端主动断开");
                Disconnect();
            };

            _sig.OnPeerLeftAbnormal += () =>
            {
                Log("被控端掉线");
                Disconnect();
            };
        }

        public async Task ConnectAsync()
        {
            _authPassed = false;
            _disconnecting = false;
            _mediaReady = false;
            _connectedReported = false;
            _firstFrameReceived = false;
            _pendingOfferSdp = null;

            _sig.AutoReconnect = false;

            await _sig.ConnectAsync();

            Log("等待被控端...");
        }

        public void SendPassword(string password)
        {
            if (_disconnecting) return;

            _sig.SendAuth(password);
            Log("已发送密码验证");
        }

        public void SendInput(string json)
        {
            SendProtocolMessage(json);
        }

        public void SendProtocolMessage(string json)
        {
            try
            {
                if (_dc?.readyState == RTCDataChannelState.open)
                    _dc.send(json);
            }
            catch (Exception ex)
            {
                Log("发送输入失败：" + ex.Message);
            }
        }

        public void Disconnect()
        {
            if (_disconnecting) return;

            _disconnecting = true;
            _authPassed = false;

            try { _sig.SendBye(); } catch { }
            try { _sig.Disconnect(); } catch { }

            CleanUp();

            OnDisconnected?.Invoke();
        }

        private void OnAuthResultReceived(bool ok, string? deviceName)
        {
            if (_disconnecting) return;

            if (ok)
            {
                RemoteDeviceName = string.IsNullOrWhiteSpace(deviceName)
                    ? "远程设备"
                    : deviceName.Trim();
                _authPassed = true;
                Log($"密码验证通过，被控端电脑名: {RemoteDeviceName}");
                OnAuthSuccess?.Invoke();

                var pendingOffer = _pendingOfferSdp;
                _pendingOfferSdp = null;
                if (!string.IsNullOrWhiteSpace(pendingOffer))
                {
                    Log("认证通过后处理缓存 Offer");
                    _ = ProcessOfferAsync(pendingOffer);
                }
            }
            else
            {
                _authPassed = false;
                Log("密码错误");
                OnAuthFailed?.Invoke();
                Disconnect();
            }
        }

        private async void OnOfferReceived(string sdp)
        {
            if (_disconnecting)
            {
                Log("正在断开，忽略 Offer");
                return;
            }

            if (!_authPassed)
            {
                _pendingOfferSdp = sdp;
                Log("未认证，缓存 Offer，等待 auth-result 后继续处理");
                return;
            }

            _ = ProcessOfferAsync(sdp);
        }

        private async Task ProcessOfferAsync(string sdp)
        {
            Log($"收到 Offer，开始处理... sdpLength={sdp.Length}");

            try
            {
                bool allowRelay = IceCandidateTools.HasRelayCandidate(sdp)
                    || sdp.Contains("a=x-dotdesk-relay:1", StringComparison.OrdinalIgnoreCase);
                Log(allowRelay ? "Offer 标记为 TURN relay 流程" : "Offer 标记为 P2P 流程");
                await CreatePcAndAnswerAsync(sdp, allowRelay);
            }
            catch (Exception ex)
            {
                Log("处理 Offer 失败: " + ex);
                Disconnect();
            }
        }

        private void OnRemoteIce(IceCandidate ice)
        {
            if (_disconnecting) return;
            if (ShouldDropRemoteCandidate(ice.Candidate))
            {
                Log($"跳过远端 ICE: {IceCandidateTools.Describe(ice.Candidate)}");
                return;
            }

            var init = new RTCIceCandidateInit
            {
                candidate = ice.Candidate,
                sdpMid = ice.SdpMid,
                sdpMLineIndex = (ushort)(ice.SdpMLineIndex ?? 0),
            };

            if (_pc != null && _remoteDescSet)
            {
                try
                {
                    Log($"直接添加远端 ICE: {IceCandidateTools.Describe(ice.Candidate)}");
                    _pc.addIceCandidate(init);
                }
                catch (Exception ex)
                {
                    Log("添加远端 ICE 失败：" + ex.Message);
                }
            }
            else
            {
                Log($"缓存远端 ICE: {IceCandidateTools.Describe(ice.Candidate)}");
                lock (_pendingCandidates)
                    _pendingCandidates.Add(init);
            }
        }

        private async Task CreatePcAndAnswerAsync(string offerSdp, bool allowRelay)
        {
            // 注意：Offer 前可能已经收到远端 ICE，不能在这里清空 _pendingCandidates。
            CleanUp(clearPendingCandidates: false);
            int generation = ++_connectionGeneration;
            _allowRelay = allowRelay;
            _firstFrameReceived = false;
            _lastFrameAt = DateTime.MinValue;
            _receivedPayloadCount = 0;
            _decodedFrameCount = 0;
            Log(allowRelay
                ? "收到 TURN 中继 Offer，使用 STUN + TURN 创建 Answer"
                : "收到 P2P Offer，仅使用 STUN 创建 Answer");
            OnConnectionStatus?.Invoke(allowRelay ? "正在通过 TURN 中继连接..." : "正在 P2P 打洞...");

            _decoder = new H264Decoder();
            _h264Depacketizer = new H264RtpDepacketizer();

            _decoder.OnFrame += (bgr, w, h) =>
            {
                _firstFrameReceived = true;
                _lastFrameAt = DateTime.UtcNow;
                int decoded = ++_decodedFrameCount;
                if (decoded <= 5 || decoded % 60 == 0)
                    AppLogger.Log("Receiver", $"解码成功: {w}x{h}, {bgr.Length} bytes, frame={decoded}");
                OnVideoFrame?.Invoke(bgr, w, h);
            };

            _pc = new RTCPeerConnection(new RTCConfiguration
            {
                iceServers = CreateIceServers(allowRelay)
            });

            // 控制端只接收视频
            var videoTrack = new MediaStreamTrack(
                new List<VideoFormat>
                {
                    new VideoFormat(VideoCodecsEnum.H264, 96)
                },
                MediaStreamStatusEnum.RecvOnly);

            _pc.addTrack(videoTrack);

            // 注意：控制端不要 createDataChannel
            // 等被控端创建 DataChannel，控制端在这里接收
            _pc.ondatachannel += channel =>
            {
                _dc = channel;

                Log($"收到 DataChannel: {channel.label}");

                channel.onopen += () =>
                {
                    Log("DataChannel 已开启");

                    Task.Delay(200).ContinueWith(_ =>
                    {
                        SendProtocolMessage(DotDeskMessageCodec.RequestKeyFrame());
                    });
                };

                channel.onclose += () =>
                {
                    Log("DataChannel 已关闭");
                };

                channel.onmessage += (dc, proto, data) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(data);
                        var message = DotDeskMessageCodec.Parse(json);
                        if (message.MessageType == DotDeskMessageType.CursorChanged)
                        {
                            OnRemoteCursorChanged?.Invoke(message.Cursor ?? "arrow");
                        }
                        else if (message.MessageType == DotDeskMessageType.ConnectionStatus)
                        {
                            var statusText = string.IsNullOrWhiteSpace(message.Text)
                                ? message.Status ?? "远程状态更新"
                                : message.Text;
                            Log($"远端状态: {statusText}");
                            OnConnectionStatus?.Invoke(statusText);
                        }
                        else if (message.MessageType == DotDeskMessageType.Ping)
                        {
                            SendProtocolMessage(DotDeskMessageCodec.Pong());
                        }
                        else if (message.MessageType == DotDeskMessageType.Pong)
                        {
                            Log("收到远端 Pong");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("处理 DataChannel 消息失败：" + ex.Message);
                    }
                };
            };

            _pc.OnVideoFrameReceived += (ep, duration, frame, format) =>
            {
                if (frame == null || frame.Length == 0) return;

                var head = frame.Length >= 4
                    ? $"{frame[0]:X2} {frame[1]:X2} {frame[2]:X2} {frame[3]:X2}"
                    : "too short";

                int payloadIndex = ++_receivedPayloadCount;
                if (payloadIndex <= 5 || payloadIndex % 60 == 0 || frame.Length > 30000)
                    AppLogger.Log("Receiver", $"收到 RTP/H264 payload: {frame.Length}bytes 头={head}, packet={payloadIndex}");

                try
                {
                    var depacketizer = _h264Depacketizer;
                    if (depacketizer == null) return;

                    foreach (var nal in depacketizer.Depacketize(frame))
                    {
                        if (payloadIndex <= 5 || payloadIndex % 60 == 0 || nal.Length > 30000)
                            AppLogger.Log("Receiver", $"还原 H264 NAL: {nal.Length}bytes");
                        _decoder?.Decode(nal);
                    }
                }
                catch (Exception ex)
                {
                    Log("H264 解包/解码失败：" + ex.Message);
                    _h264Depacketizer?.Reset();
                    SendProtocolMessage(DotDeskMessageCodec.RequestKeyFrame());
                }
            };

            _pc.onicecandidate += c =>
            {
                if (c == null)
                {
                    Log("ICE Gathering: complete candidate");
                    return;
                }

                if (_disconnecting) return;
                if (ShouldDropLocalCandidate(c.candidate))
                {
                    Log($"跳过 ICE: {IceCandidateTools.Describe(c.candidate)}");
                    return;
                }

                AppLogger.Log("Receiver", $"本地ICE: {IceCandidateTools.Describe(c.candidate)}");

                try
                {
                    Log($"准备发送本地 ICE: {IceCandidateTools.Describe(c.candidate)}");
                    _sig.SendIce(new IceCandidate
                    {
                        Candidate = c.candidate,
                        SdpMid = c.sdpMid,
                        SdpMLineIndex = c.sdpMLineIndex,
                    });
                    Log($"已发送本地 ICE: {IceCandidateTools.Describe(c.candidate)}");
                }
                catch (Exception ex)
                {
                    Log("发送 ICE 失败：" + ex.Message);
                }
            };

            _pc.onicegatheringstatechange += state =>
            {
                Log($"ICE Gathering: {state}");
            };

            _pc.onconnectionstatechange += state =>
            {
                Log($"P2P: {state}");

                if (state == RTCPeerConnectionState.connected)
                {
                    ReportConnected();
                }
                else if (state is RTCPeerConnectionState.failed
                         or RTCPeerConnectionState.disconnected
                         or RTCPeerConnectionState.closed)
                {
                    ScheduleDisconnectIfStillDown(generation);
                }
            };

            _pc.oniceconnectionstatechange += state =>
            {
                Log($"ICE: {state}");

                if (state == RTCIceConnectionState.connected)
                {
                    ReportConnected();
                }
                else if (state is RTCIceConnectionState.failed
                         or RTCIceConnectionState.disconnected
                         or RTCIceConnectionState.closed)
                {
                    ScheduleDisconnectIfStillDown(generation, state == RTCIceConnectionState.failed
                        ? "P2P直连失败：当前两端网络无法直接打通"
                        : null);
                }
            };

            // 去掉 fmtp，避免 SIPSorcery H264 fmtp 不一致
            string cleanSdp = Regex.Replace(
                offerSdp,
                @"a=fmtp:[^\n]+\n?",
                "");
            if (!allowRelay)
                cleanSdp = IceCandidateTools.StripRelayCandidates(cleanSdp);

            Log($"准备 setRemoteDescription，cleanSdpLength={cleanSdp.Length}");
            var remoteResult = _pc.setRemoteDescription(
                new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = cleanSdp
                });

            Log($"setRemoteDescription: {remoteResult}");

            if (remoteResult != SetDescriptionResultEnum.OK)
            {
                Log("setRemoteDescription 失败");
                return;
            }

            _remoteDescSet = true;
            Log("setRemoteDescription OK，开始刷新缓存 ICE");

            lock (_pendingCandidates)
            {
                foreach (var c in _pendingCandidates)
                {
                    try
                    {
                        Log($"刷新缓存远端 ICE: {IceCandidateTools.Describe(c.candidate)}");
                        _pc.addIceCandidate(c);
                    }
                    catch (Exception ex)
                    {
                        Log("刷新缓存 ICE 失败：" + ex.Message);
                    }
                }

                _pendingCandidates.Clear();
            }

            Log("开始 createAnswer");
            var answer = _pc.createAnswer();
            Log($"createAnswer 完成，answerSdpLength={answer.sdp?.Length ?? 0}");

            Log("开始 setLocalDescription");
            await _pc.setLocalDescription(answer);
            Log($"setLocalDescription 完成，signalingState={_pc.signalingState}");

            if (_disconnecting || _pc == null)
            {
                Log("连接已断开，不发送 Answer");
                return;
            }

            // 立即发送 Answer，ICE candidate 通过 onicecandidate 后续 trickle。
            string finalSdp = _pc.localDescription?.sdp?.ToString() ?? answer.sdp ?? "";
            if (string.IsNullOrWhiteSpace(finalSdp))
            {
                Log("Answer SDP 为空，停止发送");
                return;
            }
            if (!allowRelay)
                finalSdp = IceCandidateTools.StripRelayCandidates(finalSdp);
            if (allowRelay && !finalSdp.Contains("a=x-dotdesk-relay:1", StringComparison.OrdinalIgnoreCase))
                finalSdp += "a=x-dotdesk-relay:1\r\n";

            Log($"发送 Answer，candidate 数: {finalSdp.Split("candidate:").Length - 1}");

            _sig.SendAnswer(finalSdp);

            Log("Answer 已发送，后续 ICE 使用 trickle 实时发送");
        }

        private void CleanUp(bool clearPendingCandidates = true)
        {
            _mediaReady = false;
            _connectedReported = false;
            _remoteDescSet = false;
            if (clearPendingCandidates)
                _pendingOfferSdp = null;

            if (clearPendingCandidates)
            {
                lock (_pendingCandidates)
                {
                    _pendingCandidates.Clear();
                }
            }

            try
            {
                _dc?.close();
                _dc = null;
            }
            catch { }

            try
            {
                _pc?.Close("reset");
                _pc?.Dispose();
                _pc = null;
            }
            catch { }

            try
            {
                _decoder?.Dispose();
                _decoder = null;
            }
            catch { }

            try
            {
                _h264Depacketizer?.Reset();
                _h264Depacketizer = null;
            }
            catch { }

            _firstFrameReceived = false;
            _lastFrameAt = DateTime.MinValue;
            _receivedPayloadCount = 0;
            _decodedFrameCount = 0;
        }

        private void Log(string msg)
        {
            AppLogger.Log("Receiver", msg);
            OnLog?.Invoke($"[Receiver] {msg}");
        }

        private void ReportConnected()
        {
            if (_disposed || _disconnecting) return;

            _mediaReady = true;
            if (_connectedReported) return;

            _connectedReported = true;
            Log(_allowRelay ? "最终连接路径: TURN relay" : "最终连接路径: P2P");
            OnConnectionStatus?.Invoke(_allowRelay ? "中继连接" : "P2P成功");
            OnConnected?.Invoke();
            StartKeyFrameRequestsUntilFirstFrame();
            StartFrameWatchdog();
        }

        private void StartKeyFrameRequestsUntilFirstFrame()
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    if (_disposed || _disconnecting) return;
                    if (_firstFrameReceived && DateTime.UtcNow - _lastFrameAt < TimeSpan.FromSeconds(2)) return;

                    SendProtocolMessage(DotDeskMessageCodec.RequestKeyFrame());
                    Log(i == 0 ? "等待视频帧，已请求关键帧" : "仍未收到可解码视频帧，继续请求关键帧");

                    await Task.Delay(i < 6 ? 700 : 1200);
                }
            });
        }

        private void StartFrameWatchdog()
        {
            int generation = _connectionGeneration;
            Task.Run(async () =>
            {
                while (!_disposed && !_disconnecting && generation == _connectionGeneration)
                {
                    await Task.Delay(3000);
                    if (_disposed || _disconnecting || generation != _connectionGeneration) return;
                    if (!_mediaReady) continue;

                    if (_lastFrameAt == DateTime.MinValue ||
                        DateTime.UtcNow - _lastFrameAt > TimeSpan.FromSeconds(3))
                    {
                        Log("视频帧长时间未刷新，重新请求关键帧");
                        SendProtocolMessage(DotDeskMessageCodec.RequestKeyFrame());
                    }
                }
            });
        }

        private void ScheduleDisconnectIfStillDown(int generation, string? reason = null)
        {
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (_disposed || _disconnecting || _pc == null) return;
                if (generation != _connectionGeneration) return;
                if (_pc.connectionState == RTCPeerConnectionState.connected) return;

                if (_pc.iceConnectionState == RTCIceConnectionState.connected)
                {
                    return;
                }

                _mediaReady = false;
                _connectedReported = false;

                if (!_allowRelay)
                {
                    // P2P 失败不代表整次连接失败。被控端会在短暂等待后重新发 TURN Offer，
                    // 控制端必须保持 WebSocket，不要 SendBye，否则中继兜底永远接不上。
                    Log("P2P 已失败，保持信令连接，等待被控端 TURN 中继重试");
                    OnConnectionStatus?.Invoke("P2P失败，等待中继重试...");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    OnConnectionStatus?.Invoke("连接失败");
                    OnConnectionFailed?.Invoke(reason);
                }
                OnDisconnected?.Invoke();
            });
        }

        private static List<RTCIceServer> CreateIceServers(bool allowRelay)
        {
            var servers = new List<RTCIceServer>
            {
                new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
                new RTCIceServer { urls = "stun:stun1.l.google.com:19302" },
                new RTCIceServer { urls = "stun:stun.qq.com:3478" },
                new RTCIceServer { urls = "stun:stun.miwifi.com:3478" },
            };

            if (allowRelay)
            {
                servers.Add(new RTCIceServer
                {
                    urls = "turn:159.75.93.74:3478?transport=udp",
                    username = "dotdesk",
                    credential = "DotDesk2025",
                });
                servers.Add(new RTCIceServer
                {
                    urls = "turn:159.75.93.74:3478?transport=tcp",
                    username = "dotdesk",
                    credential = "DotDesk2025",
                });
                servers.Add(new RTCIceServer
                {
                    urls = "turns:159.75.93.74:5349?transport=tcp",
                    username = "dotdesk",
                    credential = "DotDesk2025",
                });
            }

            return servers;
        }

        private bool ShouldDropLocalCandidate(string? candidate)
        {
            var info = IceCandidateTools.Parse(candidate);
            if (info == null) return false;
            // P2P 阶段只禁 relay，host/srflx/prflx 包括 IPv6 都允许参与打洞。
            if (!_allowRelay && info.IsRelay) return true;
            return false;
        }

        private bool ShouldDropRemoteCandidate(string? candidate)
        {
            var info = IceCandidateTools.Parse(candidate);
            if (info == null) return false;
            // P2P 阶段只禁 relay，不能误删 IPv6 host/srflx/prflx。
            if (!_allowRelay && info.IsRelay) return true;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try { _sig.Disconnect(); } catch { }

            CleanUp();

            try { _sig.Dispose(); } catch { }
        }
    }
}
