using DotDesk.Client.Encoder;
using DotDesk.Client.Input;
using DotDesk.Core;
using DotDesk.Core.Network;
using DotDesk.Core.Protocol;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DotDesk.Client.Network
{
    public sealed class WebRtcPusher : IDisposable
    {
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action? OnAuthSuccess;
        public event Action? OnAuthFailed;
        public event Action<string>? OnConnectionStatus;
        public event Action<string>? OnLog;

        public bool IsConnected =>
            _mediaReady || _pc?.connectionState == RTCPeerConnectionState.connected;
        public bool IsSignalingConnected => _sig.IsConnected;

        public string Password => _otp.Current;
        public string RefreshPassword() => _otp.Refresh();
        public string SetFixedPassword(string? password)
        {
            _otp.SetFixed(password);
            return _otp.Current;
        }

        private readonly SignalingClient _sig;
        private readonly DotDesk.Core.Models.OneTimePassword _otp = new();
        private readonly List<RTCIceCandidateInit> _pendingCandidates = new();
        private readonly object _encoderLock = new();
        private readonly object _sendBudgetLock = new();
        private readonly SemaphoreSlim _turnFallbackLock = new(1, 1);

        private RTCPeerConnection? _pc;
        private RTCDataChannel? _dataChannel;
        private H264Encoder? _encoder;

        private bool _disposed;
        private bool _remoteDescSet;
        private bool _authPassed;
        private bool _disconnecting;
        private bool _forceNextKeyFrame;
        private bool _mediaReady;
        private bool _connectedReported;
        private bool _allowRelay;
        private bool _p2pFailed;
        private bool _turnStarted;
        private bool _isRetryingTurn;
        private int _connectionAttemptId;

        private int _width;
        private int _height;
        private int _fps;
        private int _sentVideoFrames;
        private int _droppedRawFrames;
        private int _droppedEncodedFrames;
        private long _lastRawFrameTick;
        private long _lastKeyFrameRequestTick;
        private long _lastPeriodicKeyFrameTick;
        private long _sendBudgetWindowTick;
        private int _sendBudgetBytes;

        public WebRtcPusher(string signalingServerUrl, string deviceCode)
        {
            _sig = new SignalingClient(signalingServerUrl, deviceCode, "host");
            _otp.SetFixed(DotDeskSettingsStore.Load().FixedPassword);

            _sig.OnLog += msg => Log(msg);
            _sig.OnStateChanged += state =>
            {
                // 把信令连接状态同步给主界面，服务器断开时显示断网页。
                if (state == SignalingState.Connecting)
                    OnConnectionStatus?.Invoke("连接服务器中...");
                else if (state == SignalingState.Connected)
                    OnConnectionStatus?.Invoke("等待控制端连接...");
                else if (state is SignalingState.Disconnected or SignalingState.Reconnecting)
                    OnConnectionStatus?.Invoke("连接失败: 信令服务器不可达，正在自动重试");
            };
            _sig.OnPeerJoined += OnPeerJoined;
            _sig.OnAuth += OnAuthReceived;
            _sig.OnAnswer += OnAnswerReceived;
            _sig.OnIceCandidate += OnRemoteIce;

            _sig.OnPeerLeftGraceful += () =>
            {
                Log("控制端主动断开");
                ResetPc();
                OnDisconnected?.Invoke();
            };

            _sig.OnPeerLeftAbnormal += () =>
            {
                Log("控制端掉线");
                ResetPc();
                OnDisconnected?.Invoke();
            };

            InputHandler.OnRequestKeyFrame += HandleRequestKeyFrame;
            InputHandler.OnCursorChanged += HandleCursorChanged;
        }

        public async Task StartAsync(int width, int height, int fps = 30)
        {
            if (_disposed) return;

            _width = width;
            _height = height;
            _fps = fps;

            _disconnecting = false;
            _authPassed = false;
            _mediaReady = false;
            _connectedReported = false;
            _forceNextKeyFrame = true;

            _sig.AutoReconnect = true;

            await _sig.ConnectAsync();

            Log("被控端已启动，等待控制端连接...");
        }

        public void Disconnect()
        {
            if (_disconnecting) return;

            _disconnecting = true;
            _authPassed = false;

            try { _sig.Disconnect(); } catch { }

            ResetPc();
        }

        public void PushFrame(byte[] bgra, int width, int height)
        {
            if (_disposed) return;
            if (!_authPassed) return;
            if (!IsConnected || _pc == null) return;
            if (bgra == null || bgra.Length == 0) return;
            if (!ShouldAcceptRawFrame())
            {
                _droppedRawFrames++;
                if (_droppedRawFrames == 1 || _droppedRawFrames % 60 == 0)
                    Log($"网络/编码节流：丢弃采集帧 {_droppedRawFrames} 个");
                return;
            }

            try
            {
                EnsureEncoder(width, height);
                MaybeForcePeriodicKeyFrame();

                if (!Monitor.TryEnter(_encoderLock))
                {
                    _droppedRawFrames++;
                    if (_droppedRawFrames == 1 || _droppedRawFrames % 60 == 0)
                        Log($"编码器忙：丢弃采集帧 {_droppedRawFrames} 个");
                    return;
                }

                try
                {
                    _encoder?.Encode(bgra);
                }
                finally
                {
                    Monitor.Exit(_encoderLock);
                }
            }
            catch (Exception ex)
            {
                Log($"编码失败: {ex.Message}");
            }
        }

        private void EnsureEncoder(int width, int height)
        {
            int evenWidth = width & ~1;
            int evenHeight = height & ~1;

            lock (_encoderLock)
            {
                if (_encoder != null &&
                    _encoder.Width == evenWidth &&
                    _encoder.Height == evenHeight)
                {
                    return;
                }

                Log($"创建编码器: {evenWidth}x{evenHeight}@{_fps}");

                try
                {
                    _encoder?.Dispose();
                    _encoder = null;
                }
                catch { }

                _encoder = new H264Encoder(evenWidth, evenHeight, _fps, bitrate: _allowRelay ? 380_000 : 650_000);
                if (_forceNextKeyFrame)
                {
                    _encoder.ForceKeyFrame();
                    _forceNextKeyFrame = false;
                }

                _encoder.OnEncoded += (nal, isKey, pts) =>
                {
                    try
                    {
                        if (_disposed) return;
                        if (!_authPassed) return;
                        var pc = _pc;
                        if (pc == null) return;
                        if (!_mediaReady && pc.connectionState != RTCPeerConnectionState.connected) return;
                        if (!ShouldSendEncodedFrame(nal.Length, isKey))
                        {
                            _droppedEncodedFrames++;
                            if (_droppedEncodedFrames == 1 || _droppedEncodedFrames % 90 == 0)
                                Log($"发送队列节流：丢弃非关键帧 {_droppedEncodedFrames} 个");
                            return;
                        }

                        pc.SendVideo((uint)(pts * 90), nal);
                        _sentVideoFrames++;
                        if (_sentVideoFrames <= 3 || _sentVideoFrames % 90 == 0)
                            Log($"发送视频帧: {nal.Length} bytes key={isKey}");
                    }
                    catch (Exception ex)
                    {
                        Log($"发送视频失败: {ex.Message}");
                    }
                };
            }
        }

        private void OnAuthReceived(string password)
        {
            if (_disposed || _disconnecting) return;

            if (_otp.Verify(password))
            {
                _authPassed = true;

                Log("密码验证成功");

                OnAuthSuccess?.Invoke();
                _sig.SendAuthResult(true, Environment.MachineName);
            }
            else
            {
                _authPassed = false;

                Log("密码验证失败");

                OnAuthFailed?.Invoke();
                _sig.SendAuthResult(false, Environment.MachineName);

                ResetPc();
            }
        }

        private async void OnPeerJoined()
        {
            if (_disposed || _disconnecting) return;

            try
            {
                _authPassed = false;

                Log("控制端上线，创建 PeerConnection...");

                await CreatePcAsync(allowRelay: false);
            }
            catch (Exception ex)
            {
                Log($"创建 PeerConnection 失败: {ex.Message}");
                ResetPc();
            }
        }

        private void OnAnswerReceived(string sdp)
        {
            if (_disposed || _disconnecting) return;
            if (_pc == null) return;

            Log(_allowRelay ? "收到 TURN Answer" : "收到 Answer");
            AppLogger.Log("Pusher", $"=== Answer SDP ===\n{sdp}\n==================");

            string cleanSdp = Regex.Replace(sdp, @"a=fmtp:[^\n]+\n?", "");
            if (!_allowRelay)
                cleanSdp = IceCandidateTools.StripRelayCandidates(cleanSdp);

            AppLogger.Log("Pusher", $"=== Clean Answer SDP ===\n{cleanSdp}\n=======================");

            var result = _pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = cleanSdp
            });

            AppLogger.Log("Pusher", $"setRemoteDescription: {result}");

            if (result != SetDescriptionResultEnum.OK)
            {
                Log($"setRemoteDescription 失败: {result}");
                ResetPc();
                return;
            }

            _remoteDescSet = true;

            lock (_pendingCandidates)
            {
                AppLogger.Log("Pusher", $"刷新缓存 ICE: {_pendingCandidates.Count} 个");

                foreach (var c in _pendingCandidates)
                {
                    try
                    {
                        AppLogger.Log("Pusher", $"刷新缓存远端 ICE: {IceCandidateTools.Describe(c.candidate)}");
                        _pc?.addIceCandidate(c);
                    }
                    catch (Exception ex)
                    {
                        Log($"刷新缓存 ICE 失败: {ex.Message}");
                    }
                }

                _pendingCandidates.Clear();
            }
        }

        private void OnRemoteIce(IceCandidate ice)
        {
            if (_disposed || _disconnecting) return;
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
                    AppLogger.Log("Pusher", $"直接添加远端 ICE: {IceCandidateTools.Describe(ice.Candidate)}");
                    _pc.addIceCandidate(init);
                }
                catch (Exception ex)
                {
                    Log($"添加 ICE 失败: {ex.Message}");
                }
            }
            else
            {
                AppLogger.Log("Pusher", $"缓存远端 ICE: {IceCandidateTools.Describe(ice.Candidate)}");

                lock (_pendingCandidates)
                    _pendingCandidates.Add(init);
            }
        }

        private async Task CreatePcAsync(bool allowRelay)
        {
            _allowRelay = allowRelay;
            ResetPc();
            if (!allowRelay)
            {
                _p2pFailed = false;
                _turnStarted = false;
                _isRetryingTurn = false;
            }
            int attemptId = ++_connectionAttemptId;

            Log(allowRelay
                ? "创建 TURN PeerConnection：使用 STUN + TURN UDP/TCP"
                : "开始 P2P 打洞：仅使用 host/srflx 候选");
            OnConnectionStatus?.Invoke(allowRelay ? "正在通过 TURN 中继连接..." : "正在 P2P 打洞...");

            _pc = new RTCPeerConnection(new RTCConfiguration
            {
                iceServers = CreateIceServers(allowRelay)
            });

            var videoTrack = new MediaStreamTrack(
                new List<VideoFormat>
                {
                    new VideoFormat(VideoCodecsEnum.H264, 96)
                },
                MediaStreamStatusEnum.SendOnly);

            _pc.addTrack(videoTrack);

            _pc.OnVideoFormatsNegotiated += formats =>
            {
                foreach (var f in formats)
                {
                    AppLogger.Log(
                        "Pusher",
                        $"协商格式: {f.FormatName} pt={f.FormatID} params={f.Parameters}");
                }
            };

            // 被控端创建 DataChannel，控制端通过 ondatachannel 接收
            _dataChannel = await _pc.createDataChannel("input");

            _dataChannel.onopen += () =>
            {
                Log("DataChannel 已开启");
            };

            _dataChannel.onclose += () =>
            {
                Log("DataChannel 已关闭");
            };

            _dataChannel.onmessage += (dc, proto, data) =>
            {
                try
                {
                    string json = Encoding.UTF8.GetString(data);
                    InputHandler.Handle(json);
                }
                catch (Exception ex)
                {
                    Log($"处理输入失败: {ex.Message}");
                }
            };

            _pc.onicecandidate += c =>
            {
                if (c == null)
                {
                    Log("ICE Gathering: complete candidate");
                    return;
                }

                if (_disposed || _disconnecting) return;

                string cand = c.candidate ?? "";

                if (ShouldDropLocalCandidate(cand))
                {
                    Log($"跳过 ICE: {IceCandidateTools.Describe(cand)}");
                    return;
                }

                try
                {
                    var info = IceCandidateTools.Parse(cand);
                    if (allowRelay && info?.IsRelay == true)
                        Log($"发送 relay candidate: {IceCandidateTools.Describe(cand)}");
                    else
                        Log($"发送 ICE: {IceCandidateTools.Describe(cand)}");

                    _sig.SendIce(new IceCandidate
                    {
                        Candidate = cand,
                        SdpMid = c.sdpMid,
                        SdpMLineIndex = c.sdpMLineIndex,
                    });
                }
                catch (Exception ex)
                {
                    Log($"发送 ICE 失败: {ex.Message}");
                }
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
                    if (!_allowRelay &&
                        (state == RTCPeerConnectionState.failed ||
                         state == RTCPeerConnectionState.disconnected))
                        _ = StartTurnRelayAfterP2PFailureAsync(attemptId, state.ToString());

                    ScheduleDisconnectIfStillDown(attemptId);
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
                    if (state == RTCIceConnectionState.failed)
                    {
                        Log(_allowRelay
                            ? "TURN 中继连接失败"
                            : "P2P直连失败：准备自动切换 TURN 中继");
                    }
                    if (!_allowRelay)
                        _ = StartTurnRelayAfterP2PFailureAsync(attemptId, state.ToString());

                    ScheduleDisconnectIfStillDown(attemptId);
                }
            };

            var offer = _pc.createOffer();
            await _pc.setLocalDescription(offer);

            _pc.onicegatheringstatechange += state =>
            {
                Log($"ICE Gathering: {state}");
            };

            if (_disposed || _disconnecting || _pc == null)
            {
                Log("连接已关闭，不发送 Offer");
                return;
            }

            string finalSdp = _pc.localDescription.sdp.ToString() ?? offer.sdp;
            if (!allowRelay)
                finalSdp = IceCandidateTools.StripRelayCandidates(finalSdp);
            if (allowRelay && !finalSdp.Contains("a=x-dotdesk-relay:1", StringComparison.OrdinalIgnoreCase))
                finalSdp += "a=x-dotdesk-relay:1\r\n";

            AppLogger.Log("Pusher", $"=== Offer SDP ===\n{finalSdp}\n=================");

            _sig.SendOffer(finalSdp);
            Log(allowRelay
                ? "TURN Offer 已发送，后续 ICE 使用 trickle 实时发送"
                : "Offer 已发送，后续 ICE 使用 trickle 实时发送");
        }

        private void HandleRequestKeyFrame()
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                long minTicks = Stopwatch.Frequency * 2500L / 1000L;
                long last = Interlocked.Read(ref _lastKeyFrameRequestTick);
                if (last != 0 && now - last < minTicks)
                {
                    Log("关键帧请求过于频繁，已限流");
                    return;
                }

                Interlocked.Exchange(ref _lastKeyFrameRequestTick, now);

                lock (_encoderLock)
                {
                    if (_encoder == null)
                    {
                        _forceNextKeyFrame = true;
                        return;
                    }

                    _encoder.ForceKeyFrame();
                    Interlocked.Exchange(ref _lastPeriodicKeyFrameTick, now);
                }
            }
            catch { }
        }

        private string? _lastCursorKind;
        private long _lastCursorTick;

        private void HandleCursorChanged(string cursorKind)
        {
            try
            {
                if (_dataChannel?.readyState != RTCDataChannelState.open)
                    return;

                long now = Stopwatch.GetTimestamp();
                if (cursorKind == _lastCursorKind &&
                    now - _lastCursorTick < Stopwatch.Frequency / 5)
                    return;

                _lastCursorKind = cursorKind;
                _lastCursorTick = now;
                _dataChannel.send($"{{\"type\":\"cursor\",\"cursor\":\"{cursorKind}\"}}");
            }
            catch { }
        }

        private void ResetPc()
        {
            _mediaReady = false;
            _connectedReported = false;
            _remoteDescSet = false;
            _sentVideoFrames = 0;
            _droppedRawFrames = 0;
            _droppedEncodedFrames = 0;
            _lastRawFrameTick = 0;
            _lastKeyFrameRequestTick = 0;
            _lastPeriodicKeyFrameTick = 0;
            _sendBudgetWindowTick = 0;
            _sendBudgetBytes = 0;
            _forceNextKeyFrame = true;

            lock (_pendingCandidates)
            {
                _pendingCandidates.Clear();
            }

            try
            {
                _dataChannel?.close();
                _dataChannel = null;
            }
            catch { }

            try
            {
                _pc?.Close("reset");
                _pc?.Dispose();
                _pc = null;
            }
            catch { }

            lock (_encoderLock)
            {
                try
                {
                    _encoder?.Dispose();
                    _encoder = null;
                }
                catch { }
            }
        }

        private void Log(string msg)
        {
            AppLogger.Log("Pusher", msg);
            OnLog?.Invoke($"[Pusher] {msg}");
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

            Task.Delay(300).ContinueWith(_ =>
            {
                HandleRequestKeyFrame();
            });
        }

        private bool ShouldAcceptRawFrame()
        {
            int targetFps = _allowRelay ? 8 : Math.Min(_fps, 10);
            if (targetFps <= 0) targetFps = 10;

            long now = Stopwatch.GetTimestamp();
            long minInterval = Stopwatch.Frequency / targetFps;
            long last = Interlocked.Read(ref _lastRawFrameTick);
            if (last != 0 && now - last < minInterval)
                return false;

            Interlocked.Exchange(ref _lastRawFrameTick, now);
            return true;
        }

        private bool ShouldSendEncodedFrame(int byteCount, bool isKeyFrame)
        {
            // 软背压：没有 SIPSorcery 视频 bufferedAmount，只能在应用层做字节预算。
            // 网络卡时只丢 P 帧，关键帧必须放行，否则接收端会一直等关键帧导致画面停住。
            int maxBytesPerSecond = _allowRelay ? 180_000 : 320_000;
            long now = Stopwatch.GetTimestamp();

            lock (_sendBudgetLock)
            {
                if (_sendBudgetWindowTick == 0 ||
                    now - _sendBudgetWindowTick >= Stopwatch.Frequency)
                {
                    _sendBudgetWindowTick = now;
                    _sendBudgetBytes = 0;
                }

                if (isKeyFrame)
                {
                    // 关键帧是解码恢复点：放行并重置预算窗口，避免被前一秒的 P 帧预算拖死。
                    _sendBudgetWindowTick = now;
                    _sendBudgetBytes = Math.Min(byteCount, maxBytesPerSecond / 2);
                    return true;
                }

                if (!isKeyFrame && _sendBudgetBytes + byteCount > maxBytesPerSecond)
                    return false;

                _sendBudgetBytes += byteCount;
                return true;
            }
        }

        private void MaybeForcePeriodicKeyFrame()
        {
            long now = Stopwatch.GetTimestamp();
            long intervalTicks = Stopwatch.Frequency * (_allowRelay ? 5 : 4);
            long last = Interlocked.Read(ref _lastPeriodicKeyFrameTick);
            if (last != 0 && now - last < intervalTicks)
                return;

            if (Interlocked.CompareExchange(ref _lastPeriodicKeyFrameTick, now, last) != last)
                return;

            lock (_encoderLock)
            {
                if (_encoder == null)
                    _forceNextKeyFrame = true;
                else
                    _encoder.ForceKeyFrame();
            }

            Log("周期关键帧：用于修复网络丢包后的花屏");
        }

        private void ScheduleDisconnectIfStillDown(int attemptId)
        {
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (_disposed || _disconnecting || _pc == null) return;
                if (attemptId != _connectionAttemptId) return;
                if (_pc.connectionState == RTCPeerConnectionState.connected) return;

                if (_pc.iceConnectionState == RTCIceConnectionState.connected)
                {
                    return;
                }

                if (!_allowRelay)
                {
                    Log(_p2pFailed
                        ? (_turnStarted ? "P2P 已失败，TURN 中继重试已启动" : "P2P 已失败，等待 TURN 中继重试")
                        : "P2P 暂时不可用，继续等待 ICE 恢复");
                    return;
                }

                _mediaReady = false;
                _connectedReported = false;
                OnDisconnected?.Invoke();
            });
        }

        private async Task StartTurnRelayAfterP2PFailureAsync(int attemptId, string reason)
        {
            await _turnFallbackLock.WaitAsync();
            try
            {
                if (_allowRelay)
                {
                    Log("已经处于 TURN 流程，不重复切换");
                    return;
                }

                if (_isRetryingTurn)
                {
                    Log("TURN fallback 已在执行中，不重复启动");
                    return;
                }

                _p2pFailed = true;
                _turnStarted = true;
                _isRetryingTurn = true;

                Log($"TURN fallback 开始：P2P 状态 {reason}");
                OnConnectionStatus?.Invoke("P2P失败，正在切换TURN中继...");

                // 给 ICE disconnected 一个很短的恢复窗口；failed 基本不可恢复，马上重建体验更好。
                await Task.Delay(reason.Equals("failed", StringComparison.OrdinalIgnoreCase) ? 500 : 1500);

                if (_disposed || _disconnecting)
                {
                    Log("TURN 中继切换取消：连接已关闭");
                    return;
                }

                if (_pc?.iceConnectionState == RTCIceConnectionState.connected ||
                    _pc?.connectionState == RTCPeerConnectionState.connected)
                {
                    Log("P2P 已恢复，不切换 TURN");
                    return;
                }

                Log("关闭旧 P2P PeerConnection");
                Log("开始 TURN 中继连接");
                await CreatePcAsync(allowRelay: true);
            }
            catch (Exception ex)
            {
                Log("启动 TURN 中继失败: " + ex);
            }
            finally
            {
                _isRetryingTurn = false;
                _turnFallbackLock.Release();
            }
        }

        private static List<RTCIceServer> CreateIceServers(bool allowRelay)
        {
            var servers = new List<RTCIceServer>
            {
                new RTCIceServer { urls = "stun:159.75.93.74:3478" },
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
                // 可选：如果 coturn 配置了 TLS 证书，启用 5349。
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
            _disconnecting = true;
            _authPassed = false;

            InputHandler.OnRequestKeyFrame -= HandleRequestKeyFrame;
            InputHandler.OnCursorChanged -= HandleCursorChanged;

            try { _sig.Disconnect(); } catch { }

            ResetPc();

            try { _sig.Dispose(); } catch { }
        }
    }
}
