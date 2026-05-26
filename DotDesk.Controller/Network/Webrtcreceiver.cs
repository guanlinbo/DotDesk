// WebRtcReceiver.cs  —  控制端 WebRTC 接收核心（libdatachannel 版本）
//
// ┌─ 传输层 ──────────────────────────────────────────────────────────────┐
// │  底层：DataChannelDotnet（libdatachannel C++ 封装）                   │
// │  DTLS：OpenSSL 原生，比 SIPSorcery BouncyCastle 快 3~5 倍            │
// └───────────────────────────────────────────────────────────────────────┘
//
// ┌─ 视频接收：应用层 RTP 解包 + XOR FEC 恢复 ────────────────────────────┐
// │  datachannel.dll 1.3.1 不导出 rtcSetH264Depacketizer，               │
// │  Track OnMessage 回调收到的是原始 RTP 帧字节（非重组后的 NAL）        │
// │  解包流程：                                                           │
// │    RTP字节 → 剥 RTP头 → 识别 PT                                      │
// │      PT=96  → H264RtpDepacketizer（FU-A重组）→ H264Decoder           │
// │      PT=127 → XOR FEC 恢复逻辑 → H264RtpDepacketizer → H264Decoder  │
// └───────────────────────────────────────────────────────────────────────┘
//
// ┌─ 连接策略：被动响应 Offer ─────────────────────────────────────────────┐
// │  • 收到 Offer 时判断是否含 relay candidate / x-dotdesk-relay:1 标记  │
// │  • P2P Offer → 用 STUN 创建 Answer（不带 relay candidate）           │
// │  • TURN Offer → 用 STUN+TURN 创建 Answer（带 relay candidate）       │
// │  • 连接成功后启动首帧关键帧请求循环 + Watchdog 定期检测断流           │
// └───────────────────────────────────────────────────────────────────────┘

using DotDesk.Core.Logging;
using DotDesk.Core.Network;
using DotDesk.Core.Protocol;
using DotDesk.Core.WebRtc;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DotDesk.Controller.Network
{
    public sealed class WebRtcReceiver : IDisposable
    {
        // ── 公开事件 ──────────────────────────────────────────────────────────
        public event Action? OnPeerJoined2;
        public event Action? OnAuthSuccess;
        public event Action? OnAuthFailed;
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action<string>? OnConnectionFailed;
        public event Action<string>? OnConnectionStatus;
        public event Action<string>? OnLog;
        public event Action<byte[], int, int>? OnVideoFrame;

        // OnVideoFrameWithTimestamp：hostCaptureMonoMs + frameId + controller receive/decode mono ms。
        public event Action<byte[], int, int, long, long, long, long>? OnVideoFrameWithTimestamp;

        public event Action<string>? OnRemoteCursorChanged;

        // ── 时间同步（Ping/Pong RTT 测量）────────────────────────────────────
        // 控制端发 Ping（含本地时间），被控端回 Pong（含收发时间戳）。
        // hostToControllerOffsetMs = estimatedControllerNowMs - hostNowMs.
        // 用于把 host monotonic 时间转换到 controller monotonic 时间轴。
        public long HostToControllerOffsetMs { get; private set; }
        public long TimeSyncRttMs { get; private set; } = -1;

        public bool IsConnected => _connected;
        public string RemoteDeviceName { get; private set; } = "远程设备";

        // ── 私有字段 ──────────────────────────────────────────────────────────
        private readonly SignalingClient _sig;

        private int _pc = -1;
        private int _dc = -1;
        private int _vtr = -1;

        private H264Decoder? _decoder;

        private bool _disposed, _authPassed, _disconnecting, _connected;
        private bool _allowRelay, _firstFrameReceived, _firstPayloadReceived;
        private string? _pendingOfferSdp;
        private int _connectionGeneration;
        private int _receivedPayloadCount, _decodedFrameCount;
        private long _lastRtpCaptureTimestampMs; // 被控端采集时间戳（ms），0=未从RTP扩展头读取
        private long _lastRtpFrameId;
        private readonly ConcurrentDictionary<long, long> _frameMetaById = new();
        private readonly ConcurrentDictionary<long, long> _frameReceiveById = new();
        private int _rtpFramesInWindow, _nalFramesInWindow, _decodedFramesInWindow, _uiFramesInWindow;
        private int _lastRtpFps, _lastDecodedFps, _lastUiFps;
        private int _uiQueueLength;

        private long _lastPayloadAtTick;
        private long _lastFrameAtTick;
        private long _lastUiFrameAtTick;
        private long _rtpZeroSinceTick;
        private long _decodedZeroSinceTick;
        private long _connectedAtTick;
        private long _lastDecoderRecoveryRequestAtTick;
        private int _firstRtpLogged;
        private int _firstDecodedLogged;

        // GC 保护
        private DescriptionCallback? _cbLocalDescription;
        private CandidateCallback? _cbLocalCandidate;
        private StateChangeCallback? _cbStateChange;
        private GatheringStateCallback? _cbGatheringState;
        private DataChannelCallback? _cbDataChannel;
        private OpenCallback? _cbDcOpen;
        private ClosedCallback? _cbDcClose;
        private MessageCallback? _cbDcMessage;
        private OpenCallback? _cbTrackOpen;
        private MessageCallback? _cbTrackMessage;

        // ── 构造 ──────────────────────────────────────────────────────────────
        public WebRtcReceiver(string signalingServerUrl, string targetDeviceCode)
        {
            Rtc.InitLogger(4, msg => AppLogger.Log("RTC", msg)); // RTC_LOG_INFO

            _sig = new SignalingClient(signalingServerUrl, targetDeviceCode, "guest");
            _sig.OnLog += msg => Log(msg);
            _sig.OnPeerJoined += () => { Log("被控端在线，等待 Offer..."); OnPeerJoined2?.Invoke(); };
            _sig.OnAuthResultInfo += OnAuthResultReceived;
            _sig.OnOffer += OnOfferReceived;
            _sig.OnIceCandidate += OnRemoteIce;
            _sig.OnPeerLeftGraceful += () => { Log("被控端主动断开"); Disconnect(); };
            _sig.OnPeerLeftAbnormal += () => { Log("被控端掉线"); Disconnect(); };
        }

        // ── 生命周期 ──────────────────────────────────────────────────────────
        public async Task ConnectAsync()
        {
            _authPassed = false; _disconnecting = false; _connected = false;
            _firstFrameReceived = false; _pendingOfferSdp = null;
            _sig.AutoReconnect = false;
            await _sig.ConnectAsync();
            Log("等待被控端...");
        }

        public void SendPassword(string password) { _sig.SendAuth(password); Log("已发送密码验证"); }
        public void SendInput(string json) => TrySendText(json);
        public void SendProtocolMessage(string json) => TrySendText(json);
        public void ReportUiFrameRendered()
        {
            Interlocked.Increment(ref _uiFramesInWindow);
            Interlocked.Exchange(ref _lastUiFrameAtTick, DateTime.UtcNow.Ticks);
        }
        public void ReportUiQueueLength(int queueLength) =>
            Interlocked.Exchange(ref _uiQueueLength, Math.Max(0, queueLength));

        public void Disconnect()
        {
            if (_disconnecting) return;
            _disconnecting = true; _authPassed = false;
            try { _sig.SendBye(); } catch { }
            try { _sig.Disconnect(); } catch { }
            CleanUp();
            OnDisconnected?.Invoke();
        }

        // ── 认证 ──────────────────────────────────────────────────────────────
        private void OnAuthResultReceived(bool ok, string? deviceName)
        {
            if (_disconnecting) return;
            if (ok)
            {
                RemoteDeviceName = string.IsNullOrWhiteSpace(deviceName) ? "远程设备" : deviceName.Trim();
                _authPassed = true;
                Log($"密码验证通过，被控端电脑名: {RemoteDeviceName}");
                OnAuthSuccess?.Invoke();
                var pending = _pendingOfferSdp; _pendingOfferSdp = null;
                if (!string.IsNullOrWhiteSpace(pending))
                { Log("认证通过后处理缓存 Offer"); var _unusedOffer = ProcessOfferAsync(pending); }
            }
            else
            {
                _authPassed = false; Log("密码错误"); OnAuthFailed?.Invoke(); Disconnect();
            }
        }

        // ── Offer 处理 ────────────────────────────────────────────────────────
        private async void OnOfferReceived(string sdp)
        {
            if (_disconnecting) { Log("正在断开，忽略 Offer"); return; }
            if (!_authPassed) { _pendingOfferSdp = sdp; Log("未认证，缓存 Offer"); return; }
            var _unusedOffer2 = ProcessOfferAsync(sdp);
        }

        private async Task ProcessOfferAsync(string sdp)
        {
            Log($"收到 Offer，开始处理... sdpLength={sdp.Length}");
            try
            {
                bool relay = IceCandidateTools.HasRelayCandidate(sdp)
                    || sdp.Contains("a=x-dotdesk-relay:1", StringComparison.OrdinalIgnoreCase);
                Log(relay ? "Offer 标记为 TURN relay 流程" : "Offer 标记为 P2P 流程");
                await CreatePcAndAnswerAsync(sdp, relay);
            }
            catch (Exception ex) { Log($"处理 Offer 失败: {ex}"); Disconnect(); }
        }

        private async Task CreatePcAndAnswerAsync(string offerSdp, bool allowRelay)
        {
            CleanUp();
            int generation = ++_connectionGeneration;
            _allowRelay = allowRelay;
            Interlocked.Exchange(ref _lastPayloadAtTick, 0);
            Interlocked.Exchange(ref _lastFrameAtTick, 0);
            Interlocked.Exchange(ref _lastUiFrameAtTick, 0);
            Interlocked.Exchange(ref _rtpZeroSinceTick, 0);
            Interlocked.Exchange(ref _decodedZeroSinceTick, 0);
            Interlocked.Exchange(ref _connectedAtTick, 0);
            Interlocked.Exchange(ref _firstRtpLogged, 0);
            Interlocked.Exchange(ref _firstDecodedLogged, 0);
            Interlocked.Exchange(ref _lastDecoderRecoveryRequestAtTick, 0);
            _receivedPayloadCount = _decodedFrameCount = 0;
            Interlocked.Exchange(ref _rtpFramesInWindow, 0);
            Interlocked.Exchange(ref _nalFramesInWindow, 0);
            Interlocked.Exchange(ref _decodedFramesInWindow, 0);
            Interlocked.Exchange(ref _uiFramesInWindow, 0);
            _lastRtpFps = _lastDecodedFps = _lastUiFps = 0;
            Interlocked.Exchange(ref _uiQueueLength, 0);
            _firstFrameReceived = false;
            _firstPayloadReceived = false;

            Log(allowRelay ? "收到 TURN 中继 Offer，仅使用 TURN 创建 Answer"
                           : "收到 P2P Offer，仅使用 STUN 创建 Answer");
            OnConnectionStatus?.Invoke(allowRelay ? "正在通过 TURN 中继连接..." : "正在 P2P 打洞...");

            // 解码器
            _decoder = new H264Decoder();
            _decoder.OnFrame += (bgr, w, h) =>
            {
                _firstFrameReceived = true;
                Interlocked.Exchange(ref _lastFrameAtTick, DateTime.UtcNow.Ticks);
                long connectedAt = Interlocked.Read(ref _connectedAtTick);
                if (connectedAt != 0 && Interlocked.CompareExchange(ref _firstDecodedLogged, 1, 0) == 0)
                    Log($"first decoded frame after connected={(Stopwatch.GetTimestamp() - connectedAt) * 1000 / Stopwatch.Frequency}ms");
                int n = ++_decodedFrameCount;
                Interlocked.Increment(ref _decodedFramesInWindow);
                if (n <= 5 || n % 60 == 0) AppLogger.Log("Receiver", $"解码成功: {w}x{h} frame={n}");
                OnVideoFrame?.Invoke(bgr, w, h);
                long frameId = Interlocked.Read(ref _lastRtpFrameId);
                long captureTs = _lastRtpCaptureTimestampMs > 0
                    ? _lastRtpCaptureTimestampMs
                    : MonoNowMs();
                if (frameId > 0 && _frameMetaById.TryGetValue(frameId, out long metaCaptureTs))
                    captureTs = metaCaptureTs;
                long controllerReceiveMonoMs = frameId > 0 && _frameReceiveById.TryGetValue(frameId, out long receiveMs)
                    ? receiveMs
                    : MonoNowMs();
                long controllerDecodeMonoMs = MonoNowMs();
                OnVideoFrameWithTimestamp?.Invoke(
                    bgr,
                    w,
                    h,
                    captureTs,
                    frameId,
                    controllerReceiveMonoMs,
                    controllerDecodeMonoMs);
            };

            string[] iceServers = BuildIceServers(allowRelay);
            Log($"ICE config relayOnly={allowRelay} servers={string.Join(", ", iceServers)}");
            _pc = Rtc.CreatePeerConnection(iceServers, enableIceTcp: false, mtu: 1300,
                relayOnly: allowRelay);
            if (_pc < 0)
            {
                Log($"CreatePeerConnection 失败: {_pc}");
                return;
            }

            // 回调
            _cbLocalDescription = (pc, sdp2, type, _) =>
            {
                if (_disconnecting) return;
                string finalSdp = sdp2;
                if (!allowRelay) finalSdp = IceCandidateTools.StripRelayCandidates(finalSdp);
                if (allowRelay && !finalSdp.Contains("a=x-dotdesk-relay:1"))
                    finalSdp += "a=x-dotdesk-relay:1\r\n";
                if (type == "answer") { _sig.SendAnswer(finalSdp); Log("Answer 已发送"); }
            };

            _cbLocalCandidate = (pc, cand, mid, _) =>
            {
                if (_disconnecting || ShouldDropLocalCandidate(cand)) return;
                AppLogger.Log("Receiver", $"本地ICE: {IceCandidateTools.Describe(cand)}");
                _sig.SendIce(new IceCandidate { Candidate = cand, SdpMid = mid, SdpMLineIndex = 0 });
            };

            _cbStateChange = (pc, state, _) =>
            {
                Log($"P2P: {state}");
                if (state == RtcState.Connected) ReportConnected(generation);
                else if (state is RtcState.Failed or RtcState.Disconnected or RtcState.Closed)
                    ScheduleDisconnectIfDown(generation, state == RtcState.Failed
                        ? "P2P直连失败：当前两端网络无法直接打通" : null);
            };

            _cbGatheringState = (pc, state, _) => Log($"ICE Gathering: {state}");

            // DataChannel（接收被控端创建的 DataChannel）
            _cbDataChannel = (pc, dc, _) =>
            {
                _dc = dc;
                Log($"收到 DataChannel");
                _cbDcOpen = (id, _) => Log("DataChannel 已开启");
                _cbDcClose = (id, _) => Log("DataChannel 已关闭");
                _cbDcMessage = (id, msgPtr, size, _) =>
                {
                    try
                    {
                        string json = size < 0
                            ? Marshal.PtrToStringUTF8(msgPtr) ?? ""
                            : Encoding.UTF8.GetString(GetBytes(msgPtr, size));
                        var msg = DotDeskMessageCodec.Parse(json);
                        if (msg.MessageType == DotDeskMessageType.CursorChanged)
                            OnRemoteCursorChanged?.Invoke(msg.Cursor ?? "arrow");
                        else if (msg.MessageType == DotDeskMessageType.ConnectionStatus)
                        {
                            var text = string.IsNullOrWhiteSpace(msg.Text) ? msg.Status ?? "" : msg.Text;
                            Log($"远端状态: {text}"); OnConnectionStatus?.Invoke(text);
                        }
                        else if (msg.MessageType == DotDeskMessageType.VideoFrameMeta)
                        {
                            if (msg.FrameId > 0 && msg.HostCaptureMonoMs > 0)
                            {
                                _frameMetaById[msg.FrameId] = msg.HostCaptureMonoMs;
                                TrimFrameMetadata();
                            }
                        }
                        else if (msg.MessageType == DotDeskMessageType.Ping)
                        {
                            // 被控端发 Ping（罕见），立即回 Pong
                            TrySendText(DotDeskMessageCodec.Pong(
                                msg.ControllerSendMonoMs,
                                msg.HostReceiveMonoMs,
                                MonoNowMs(),
                                MonoNowMs()));
                        }
                        else if (msg.MessageType == DotDeskMessageType.Pong)
                        {
                            // 控制端发 Ping，被控端回 Pong，用于跨机器时钟同步
                            long ctrlSend = msg.ControllerSendMonoMs;
                            long hostReceive = msg.HostReceiveMonoMs;
                            long hostSend = msg.HostSendMonoMs;
                            long ctrlNow = MonoNowMs();
                            long rtt = ctrlNow - ctrlSend;
                            long hostToControllerOffset =
                                ((ctrlSend - hostReceive) + (ctrlNow - hostSend)) / 2;
                            TimeSyncRttMs = rtt;
                            HostToControllerOffsetMs = hostToControllerOffset;
                            AppLogger.Log("Latency",
                                $"[TimeSync] hostToControllerOffsetMs={hostToControllerOffset}ms rtt={rtt}ms " +
                                $"controllerSendMonoMs={ctrlSend} hostReceiveMonoMs={hostReceive} " +
                                $"hostSendMonoMs={hostSend} controllerReceiveMonoMs={ctrlNow}");
                        }
                    }
                    catch (Exception ex) { Log($"处理 DataChannel 消息失败: {ex.Message}"); }
                };
                Rtc.SetOpenCallback(dc, _cbDcOpen);
                Rtc.SetClosedCallback(dc, _cbDcClose);
                Rtc.SetMessageCallback(dc, _cbDcMessage);
            };

            Rtc.SetLocalDescriptionCallback(_pc, _cbLocalDescription);
            Rtc.SetLocalCandidateCallback(_pc, _cbLocalCandidate);
            Rtc.SetStateChangeCallback(_pc, _cbStateChange);
            Rtc.SetGatheringStateChangeCallback(_pc, _cbGatheringState);
            Rtc.SetDataChannelCallback(_pc, _cbDataChannel);

            // 添加 RecvOnly Video Track（触发 SDP 协商包含 video m-line）
            string trackSdp =
                "m=video 9 UDP/TLS/RTP/SAVP 96\r\n" +
                "c=IN IP4 0.0.0.0\r\n" +
                "a=mid:0\r\n" +
                "a=recvonly\r\n" +
                "a=rtpmap:96 H264/90000\r\n" +
                "a=fmtp:96 profile-level-id=42e01f;packetization-mode=1\r\n" +
                "a=rtcp-mux\r\n" +
                "a=rtcp-fb:96 transport-cc\r\n";
            _vtr = Rtc.AddTrack(_pc, trackSdp);

            if (_vtr >= 0)
            {
                // datachannel.dll 1.3.1 不导出 rtcSetH264Depacketizer，
                // 改用应用层 H264RtpDepacketizer 处理收到的原始 RTP 包（与 SIPSorcery 版本相同逻辑）。
                // Track OnMessage 收到的是标准 RTP 帧字节，需要先剥离 RTP 头再做 FU-A 重组。
                var depacketizer = new H264RtpDepacketizer();

                _cbTrackOpen = (tr, _) => Log("Video Track 已开启");
                _cbTrackMessage = (tr, msgPtr, size, _) =>
                {
                    if (size <= 12) return;
                    try
                    {
                        byte[] rtpPacket = GetBytes(msgPtr, size);
                        Interlocked.Exchange(ref _lastPayloadAtTick, DateTime.UtcNow.Ticks);
                        long connectedAt = Interlocked.Read(ref _connectedAtTick);
                        if (connectedAt != 0 && Interlocked.CompareExchange(ref _firstRtpLogged, 1, 0) == 0)
                            Log($"first RTP after connected={(Stopwatch.GetTimestamp() - connectedAt) * 1000 / Stopwatch.Frequency}ms");
                        _firstPayloadReceived = true;
                        int idx = ++_receivedPayloadCount;

                        int cc = rtpPacket[0] & 0x0F;
                        int headerLen = 12 + cc * 4;
                        bool extension = (rtpPacket[0] & 0x10) != 0;
                        if (extension && rtpPacket.Length >= headerLen + 4)
                        {
                            long controllerReceiveMonoMs = MonoNowMs();
                            int extWords = (rtpPacket[headerLen + 2] << 8) | rtpPacket[headerLen + 3];
                            int extLen = extWords * 4;
                            if (TryReadFrameMetadataExtension(rtpPacket, headerLen, extLen, out long captureTs, out long frameId))
                            {
                                if (captureTs > 0) Interlocked.Exchange(ref _lastRtpCaptureTimestampMs, captureTs);
                                if (frameId > 0)
                                {
                                    Interlocked.Exchange(ref _lastRtpFrameId, frameId);
                                    if (captureTs > 0)
                                    {
                                        _frameMetaById[frameId] = captureTs;
                                        _frameReceiveById[frameId] = controllerReceiveMonoMs;
                                        TrimFrameMetadata();
                                    }
                                }
                            }
                            headerLen += 4 + extLen;
                        }
                        if (rtpPacket.Length <= headerLen) return;

                        byte pt = (byte)(rtpPacket[1] & 0x7F);
                        ushort seq = (ushort)((rtpPacket[2] << 8) | rtpPacket[3]);

                        // PT=127 → XOR FEC 纠错包（Pusher 的 FlushFecGroup 发出）
                        // 不送解码器，只用于恢复同组丢失的数据包
                        if (pt == 127)
                        {
                            TryApplyFec(rtpPacket, headerLen, seq, depacketizer);
                            return;
                        }

                        // PT=96 → H264 RTP 数据包，记录到 FEC 循环缓冲
                        // 记录是为了：当同组 FEC 包到达时，能用其他包 XOR 恢复丢失包
                        RecordFecData(seq, rtpPacket, headerLen);

                        byte[] payload = new byte[rtpPacket.Length - headerLen];
                        Buffer.BlockCopy(rtpPacket, headerLen, payload, 0, payload.Length);

                        if (idx <= 5 || idx % 120 == 0)
                            AppLogger.Log("Receiver", $"收到 RTP/H264: {payload.Length}B seq={seq}");

                        Interlocked.Increment(ref _rtpFramesInWindow);
                        foreach (var nal in depacketizer.Depacketize(payload))
                        {
                            if (nal == null || nal.Length == 0) continue;
                            Interlocked.Increment(ref _nalFramesInWindow);
                            _decoder?.Decode(nal);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"解码失败: {ex.Message}");
                        RequestDecoderRecovery("解码异常，请求关键帧", DateTime.UtcNow,
                            TimeSpan.FromSeconds(3), decoderError: true);
                    }
                };
                Rtc.SetTrackOpenCallback(_vtr, _cbTrackOpen);
                Rtc.SetTrackMessageCallback(_vtr, _cbTrackMessage);
            }

            // setRemoteDescription（offer）→ 触发 answer 生成
            string cleanSdp = Regex.Replace(offerSdp, @"a=fmtp:[^\n]+\n?", "");
            if (!allowRelay) cleanSdp = IceCandidateTools.StripRelayCandidates(cleanSdp);
            int ret = Rtc.SetRemoteDescription(_pc, cleanSdp, "offer");
            Log($"setRemoteDescription: {(ret >= 0 ? "OK" : ret.ToString())}");
            if (ret < 0) { Log("setRemoteDescription 失败"); return; }

            StartKeyFrameRequestsUntilFirstFrame(generation);
            StartPingLoop(generation);
            StartFrameWatchdog(generation);
            StartReceiverStats(generation);
            Log("Answer 协商中...");
        }

        // ── ICE 管理 ──────────────────────────────────────────────────────────
        private void OnRemoteIce(IceCandidate ice)
        {
            if (_disconnecting || _pc < 0) return;
            if (ShouldDropRemoteCandidate(ice.Candidate)) return;
            Rtc.AddRemoteCandidate(_pc, ice.Candidate, ice.SdpMid ?? "0");
            Log($"添加远端 ICE: {IceCandidateTools.Describe(ice.Candidate)}");
        }

        // ── 连接管理 ──────────────────────────────────────────────────────────
        private void ReportConnected(int generation)
        {
            if (_disposed || _disconnecting) return;
            _connected = true;
            Interlocked.Exchange(ref _connectedAtTick, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _firstRtpLogged, 0);
            Interlocked.Exchange(ref _firstDecodedLogged, 0);
            Log(_allowRelay ? "最终连接路径: TURN relay" : "最终连接路径: P2P");
            OnConnectionStatus?.Invoke(_allowRelay ? "中继连接" : "P2P成功");
            OnConnected?.Invoke();
        }

        private void ScheduleDisconnectIfDown(int generation, string? reason = null)
        {
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (_disposed || _disconnecting || _pc < 0) return;
                if (generation != _connectionGeneration) return;
                if (_connected) return;
                _connected = false;
                if (!_allowRelay) { OnConnectionStatus?.Invoke("P2P失败，等待中继重试..."); return; }
                if (!string.IsNullOrEmpty(reason)) { OnConnectionFailed?.Invoke(reason!); }
                OnDisconnected?.Invoke();
            });
        }

        // ── 首帧关键帧请求 ────────────────────────────────────────────────────
        // ── 首帧关键帧请求循环 ───────────────────────────────────────────────────
        // 连接建立后，解码器需要收到 IDR 帧才能开始解码（intra-refresh 也算）。
        // 被控端连接后会自动发一次，但可能在 DTLS 握手完成前就发了，接收端还没准备好。
        // 所以控制端主动轮询请求，直到第一帧画面出现为止。
        // relay 下每次请求间隔更长（2s vs 1s），因为 relay 单程延迟 60~200ms，
        // 太密集的请求会导致被控端产生 IDR burst，反而加重拥塞。
        private void StartKeyFrameRequestsUntilFirstFrame(int generation)
        {
            int maxRetries = _allowRelay ? 4 : 8;
            int baseDelayMs = _allowRelay ? 2000 : 1000;
            Task.Run(async () =>
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    if (_disposed || _disconnecting || generation != _connectionGeneration) return;
                    long payloadTick = Interlocked.Read(ref _lastPayloadAtTick);
                    long frameTick = Interlocked.Read(ref _lastFrameAtTick);
                    if (_firstPayloadReceived && payloadTick != 0)
                        return;
                    if (_firstFrameReceived && frameTick != 0
                        && DateTime.UtcNow - new DateTime(frameTick, DateTimeKind.Utc) < TimeSpan.FromSeconds(2))
                        return;
                    TrySendText(DotDeskMessageCodec.RequestKeyFrame());
                    if (i == 0) Log("等待视频帧，已请求关键帧");
                    else if (i == 2 || i == 5) Log("仍未收到可解码视频帧，继续请求关键帧");
                    await Task.Delay(baseDelayMs + i * 300);
                }
            });
        }

        private void StartReceiverStats(int generation)
        {
            Task.Run(async () =>
            {
                while (!_disposed && !_disconnecting && generation == _connectionGeneration)
                {
                    await Task.Delay(1000);
                    if (_disposed || _disconnecting || generation != _connectionGeneration) return;
                    DateTime now = DateTime.UtcNow;
                    int rtpFps = Interlocked.Exchange(ref _rtpFramesInWindow, 0);
                    int nalFps = Interlocked.Exchange(ref _nalFramesInWindow, 0);
                    int decodedFps = Interlocked.Exchange(ref _decodedFramesInWindow, 0);
                    int uiFps = Interlocked.Exchange(ref _uiFramesInWindow, 0);
                    _lastRtpFps = rtpFps;
                    _lastDecodedFps = decodedFps;
                    _lastUiFps = uiFps;
                    UpdateZeroSince(ref _rtpZeroSinceTick, rtpFps, now);
                    UpdateZeroSince(ref _decodedZeroSinceTick, decodedFps, now);
                    int queue = Volatile.Read(ref _uiQueueLength);
                    int payloadAge = AgeMs(now, Interlocked.Read(ref _lastPayloadAtTick));
                    int decodedAge = AgeMs(now, Interlocked.Read(ref _lastFrameAtTick));
                    int uiAge = AgeMs(now, Interlocked.Read(ref _lastUiFrameAtTick));
                    Log($"[Stats] rtpFps={rtpFps} nalFps={nalFps} decodedFps={decodedFps} uiFps={uiFps} queue={queue} age payload={payloadAge}ms decoded={decodedAge}ms ui={uiAge}ms");
                }
            });
        }

        private static int AgeMs(DateTime now, long utcTicks)
        {
            if (utcTicks == 0) return -1;
            double ms = (now - new DateTime(utcTicks, DateTimeKind.Utc)).TotalMilliseconds;
            if (ms < 0) return 0;
            return ms > int.MaxValue ? int.MaxValue : (int)ms;
        }

        private static void UpdateZeroSince(ref long zeroSinceTick, int fps, DateTime now)
        {
            if (fps > 0)
            {
                Interlocked.Exchange(ref zeroSinceTick, 0);
                return;
            }

            Interlocked.CompareExchange(ref zeroSinceTick, now.Ticks, 0);
        }

        private static bool TryReadFrameMetadataExtension(
            byte[] rtpPacket,
            int extensionHeaderOffset,
            int extensionLength,
            out long captureMonoMs,
            out long frameId)
        {
            captureMonoMs = 0;
            frameId = 0;
            if (extensionLength <= 0 || rtpPacket.Length < extensionHeaderOffset + 4 + extensionLength)
                return false;
            if (rtpPacket[extensionHeaderOffset] != 0xBE || rtpPacket[extensionHeaderOffset + 1] != 0xDE)
                return false;

            int pos = extensionHeaderOffset + 4;
            int end = pos + extensionLength;
            while (pos < end)
            {
                byte header = rtpPacket[pos++];
                if (header == 0) continue;
                int id = header >> 4;
                int len = (header & 0x0F) + 1;
                if (id == 15 || pos + len > end) break;

                if (len == 8 && (id == 1 || id == 2))
                {
                    long value = 0;
                    for (int i = 0; i < 8; i++)
                        value = (value << 8) | rtpPacket[pos + i];
                    if (id == 1) captureMonoMs = value;
                    else frameId = value;
                }
                pos += len;
            }
            return captureMonoMs > 0 || frameId > 0;
        }

        private void TrimFrameMetadata()
        {
            if (_frameMetaById.Count <= 512 && _frameReceiveById.Count <= 512) return;
            foreach (var key in _frameMetaById.Keys)
            {
                if (_frameMetaById.Count <= 256) break;
                _frameMetaById.TryRemove(key, out _);
            }
            foreach (var key in _frameReceiveById.Keys)
            {
                if (_frameReceiveById.Count <= 256) break;
                _frameReceiveById.TryRemove(key, out _);
            }
        }

        private static long MonoNowMs() =>
            Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

        private bool IsStreamHealthy(DateTime now)
        {
            long payloadTick = Interlocked.Read(ref _lastPayloadAtTick);
            long decodedTick = Interlocked.Read(ref _lastFrameAtTick);
            long uiTick = Interlocked.Read(ref _lastUiFrameAtTick);
            if (payloadTick == 0 || decodedTick == 0) return false;

            bool rtpFresh = now - new DateTime(payloadTick, DateTimeKind.Utc) < TimeSpan.FromSeconds(3);
            bool decodedFresh = now - new DateTime(decodedTick, DateTimeKind.Utc) < TimeSpan.FromSeconds(3);
            bool uiFresh = uiTick == 0 || now - new DateTime(uiTick, DateTimeKind.Utc) < TimeSpan.FromSeconds(3);
            return rtpFresh && decodedFresh && uiFresh
                && Volatile.Read(ref _lastRtpFps) > 0
                && Volatile.Read(ref _lastDecodedFps) > 0;
        }

        private bool HasZeroFpsFor(ref long zeroSinceTick, DateTime now, TimeSpan duration)
        {
            long tick = Interlocked.Read(ref zeroSinceTick);
            return tick != 0 && now - new DateTime(tick, DateTimeKind.Utc) >= duration;
        }

        // ── Watchdog ──────────────────────────────────────────────────────────
        // ── Watchdog：定期检测视频流是否正常 ───────────────────────────────────
        // 原因：relay 模式下 SCTP/DataChannel 可能 connected，但 RTP/SRTP 通道静默失效
        // 检测两个维度：
        //   • payloadAge > 6s：RTP 包长时间没到达 → 网络中断或被控端停止发送
        //   • frameAge > 10/15s：包有来但解码器没有产出帧 → FU-A 重组失败 / 解码器卡死
        // 处理：发送 RequestKeyFrame → 被控端重新发起 intra-refresh 帧，让解码器重建
        // ── Ping/Pong 时间同步循环 ───────────────────────────────────────────────
        // 每 5 秒发一次 Ping，收到 Pong 后更新 TimeSyncRttMs / HostToControllerOffsetMs
        // 用于校正被控端 captureTimestampMs 与控制端时钟的差值（两台机器时钟不同步）
        private void StartPingLoop(int generation)
        {
            Task.Run(async () =>
            {
                await Task.Delay(2000); // 连接稳定后再开始
                while (!_disposed && !_disconnecting && generation == _connectionGeneration)
                {
                    if (_connected && _dc >= 0)
                    {
                        try
                        {
                            long sendMs = MonoNowMs();
                            TrySendText(DotDeskMessageCodec.Ping(sendMs));
                        }
                        catch { }
                    }
                    await Task.Delay(5000);
                }
            });
        }

        private void StartFrameWatchdog(int generation)
        {
            Task.Run(async () =>
            {
                while (!_disposed && !_disconnecting && generation == _connectionGeneration)
                {
                    await Task.Delay(2000); // 2s 检查一次（原 5s 太慢，画面卡住要等很久）
                    if (_disposed || _disconnecting || generation != _connectionGeneration) return;
                    if (!_connected) continue;
                    DateTime now = DateTime.UtcNow;
                    long payloadTick = Interlocked.Read(ref _lastPayloadAtTick);
                    long frameTick = Interlocked.Read(ref _lastFrameAtTick);
                    TimeSpan payloadAge = payloadTick == 0 ? TimeSpan.MaxValue
                        : now - new DateTime(payloadTick, DateTimeKind.Utc);
                    TimeSpan frameAge = frameTick == 0 ? TimeSpan.MaxValue
                        : now - new DateTime(frameTick, DateTimeKind.Utc);

                    // RTP 包停止到达：网络断开或被控端异常
                    if (payloadAge > TimeSpan.FromSeconds(4)
                        && HasZeroFpsFor(ref _rtpZeroSinceTick, now, TimeSpan.FromSeconds(3)))
                    { RequestDecoderRecovery("视频数据长时间未到达", now, TimeSpan.FromSeconds(8)); continue; }

                    // 包有来但解码帧停止：intra-refresh 场景下丢包导致解码器等 IDR
                    // 3s 无新解码帧立即请求关键帧（比原来的 5s 快）
                    if (frameAge > TimeSpan.FromSeconds(3)
                        && HasZeroFpsFor(ref _decodedZeroSinceTick, now, TimeSpan.FromSeconds(2)))
                        RequestDecoderRecovery("视频解码停止刷新，请求关键帧", now,
                            TimeSpan.FromSeconds(5)); // 冷却缩短到 5s（原 10s）
                }
            });
        }

        private void RequestDecoderRecovery(string msg, DateTime now, TimeSpan cooldown, bool decoderError = false)
        {
            if (!decoderError && IsStreamHealthy(now))
            {
                Log("[IDR] viewer-request ignored: stream healthy");
                return;
            }
            long recoveryTick = Interlocked.Read(ref _lastDecoderRecoveryRequestAtTick);
            if (recoveryTick != 0 && now - new DateTime(recoveryTick, DateTimeKind.Utc) < cooldown) return;
            Interlocked.Exchange(ref _lastDecoderRecoveryRequestAtTick, now.Ticks);
            Log(msg); TrySendText(DotDeskMessageCodec.RequestKeyFrame());
        }

        // ── DataChannel 发送 ──────────────────────────────────────────────────
        private void TrySendText(string json)
        {
            try { if (_dc >= 0) Rtc.SendMessage(_dc, json); }
            catch (Exception ex) { Log($"发送消息失败: {ex.Message}"); }
        }

        // ── ICE 服务器 ────────────────────────────────────────────────────────
        private string[] BuildIceServers(bool allowRelay)
        {
            if (allowRelay)
            {
                return new[]
                {
                    // DataChannelDotnet 1.3.1 uses libjuice for ICE; this build does not support
                    // TURN TCP/TLS. Keep relay fallback on UDP TURN only.
                    "turn://dotdesk:DotDesk2025@159.75.93.74:3478?transport=udp",
                };
            }

            var list = new System.Collections.Generic.List<string>
            {
                "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302",
                "stun:stun.qq.com:3478", "stun:stun.miwifi.com:3478",
            };
            return list.ToArray();
        }

        private bool ShouldDropLocalCandidate(string? cand)
        {
            var info = IceCandidateTools.Parse(cand);
            return info != null && !_allowRelay && info.IsRelay;
        }

        private bool ShouldDropRemoteCandidate(string? cand)
        {
            var info = IceCandidateTools.Parse(cand);
            return info != null && !_allowRelay && info.IsRelay;
        }

        // ── 清理 ──────────────────────────────────────────────────────────────
        // ── XOR FEC 接收端（对应 Pusher 的 SendWithFec）─────────────────────────
        // Pusher 每 4 个数据包发 1 个 FEC 包（PT=127），FEC 包 payload = XOR(4包)
        // 接收端：
        //   1. 每收到 PT=96 数据包 → 记录到循环缓冲（最近64个seq）
        //   2. 收到 PT=127 FEC 包  → 检查对应组（groupStart ~ groupStart+N-1）
        //      • 组内全部到达 → 无需处理
        //      • 组内恰好缺1包 → recovered = XOR(FEC, 其他包) 还原丢失包
        //      • 组内缺2+包   → XOR 无法恢复，等上层 IDR 重建
        // 优点：丢1包不重传，直接本地恢复，节省 1 RTT（relay 下 RTT ≈ 60~200ms）

        private const int FecBufSize = 64;  // 循环缓冲：最近 64 个 seq 的包
        private readonly byte[]?[] _fecBuf = new byte[FecBufSize][];
        private readonly ushort[] _fecSeqs = new ushort[FecBufSize];
        private readonly int[] _fecHdrs = new int[FecBufSize]; // headerLen

        private void RecordFecData(ushort seq, byte[] pkt, int headerLen)
        {
            int slot = seq % FecBufSize;
            _fecSeqs[slot] = seq;
            _fecBuf[slot] = pkt;
            _fecHdrs[slot] = headerLen;
        }

        private void TryApplyFec(byte[] fecPkt, int fecHdrLen, ushort fecSeq,
            H264RtpDepacketizer depacketizer)
        {
            // FEC 元数据（fecHdrLen 之后 4 字节）
            if (fecPkt.Length < fecHdrLen + 4) return;
            ushort groupStart = (ushort)((fecPkt[fecHdrLen] << 8) | fecPkt[fecHdrLen + 1]);
            int groupSize = fecPkt[fecHdrLen + 2];
            int xorDataOff = fecHdrLen + 4;
            int xorDataLen = fecPkt.Length - xorDataOff;
            if (xorDataLen <= 0 || groupSize <= 0) return;

            // 检查组内哪个包丢失
            int missingIdx = -1;
            ushort missingSeq = 0;
            for (int i = 0; i < groupSize; i++)
            {
                ushort seq = (ushort)(groupStart + i);
                int slot = seq % FecBufSize;
                if (_fecSeqs[slot] != seq || _fecBuf[slot] == null)
                {
                    if (missingIdx >= 0) return; // 丢了超过1包，XOR无法恢复
                    missingIdx = i;
                    missingSeq = seq;
                }
            }
            if (missingIdx < 0) return; // 没丢包，不需要恢复

            // 用 XOR 恢复：recovered = XOR(fec_data) XOR XOR(all_other_packets)
            byte[] recovered = new byte[xorDataLen];
            Buffer.BlockCopy(fecPkt, xorDataOff, recovered, 0, xorDataLen);

            for (int i = 0; i < groupSize; i++)
            {
                if (i == missingIdx) continue;
                ushort seq = (ushort)(groupStart + i);
                int slot = seq % FecBufSize;
                byte[]? pkt = _fecBuf[slot];
                if (pkt == null) return;
                // XOR 整个包（含 RTP 头）
                int len = Math.Min(pkt.Length, recovered.Length);
                for (int b = 0; b < len; b++) recovered[b] ^= pkt[b];
            }

            // recovered 现在是丢失包的原始 RTP 内容（含头）
            // 解析恢复包的 RTP 头，提取 payload
            if (recovered.Length < 12) return;
            int rcc = recovered[0] & 0x0F;
            int rHdr = 12 + rcc * 4;
            bool rExt = (recovered[0] & 0x10) != 0;
            if (rExt && recovered.Length >= rHdr + 4)
            {
                int extLen = ((recovered[rHdr + 2] << 8) | recovered[rHdr + 3]) * 4;
                rHdr += 4 + extLen;
            }
            if (recovered.Length <= rHdr) return;

            byte[] payload = new byte[recovered.Length - rHdr];
            Buffer.BlockCopy(recovered, rHdr, payload, 0, payload.Length);

            AppLogger.Log("Receiver", $"FEC 恢复成功: seq={missingSeq} size={payload.Length}B");

            // 记录恢复的包
            RecordFecData(missingSeq, recovered, rHdr);

            // 送 depacketizer
            try
            {
                foreach (var nal in depacketizer.Depacketize(payload))
                {
                    if (nal == null || nal.Length == 0) continue;
                    _decoder?.Decode(nal);
                }
            }
            catch (Exception ex) { Log($"FEC 恢复包解码失败: {ex.Message}"); }
        }

        private void CleanUp()
        {
            _connected = false;
            if (_vtr >= 0) { try { Rtc.DeleteTrack(_vtr); } catch { } _vtr = -1; }
            if (_dc >= 0) { try { Rtc.DeleteDataChannel(_dc); } catch { } _dc = -1; }
            if (_pc >= 0) { try { Rtc.DeletePeerConnection(_pc); } catch { } _pc = -1; }
            try { _decoder?.Dispose(); _decoder = null; } catch { }
            _firstFrameReceived = false;
            _firstPayloadReceived = false;
            Interlocked.Exchange(ref _lastPayloadAtTick, 0);
            Interlocked.Exchange(ref _lastFrameAtTick, 0);
            Interlocked.Exchange(ref _lastUiFrameAtTick, 0);
            Interlocked.Exchange(ref _rtpZeroSinceTick, 0);
            Interlocked.Exchange(ref _decodedZeroSinceTick, 0);
            Interlocked.Exchange(ref _connectedAtTick, 0);
            Interlocked.Exchange(ref _firstRtpLogged, 0);
            Interlocked.Exchange(ref _firstDecodedLogged, 0);
            Interlocked.Exchange(ref _lastDecoderRecoveryRequestAtTick, 0);
            _receivedPayloadCount = _decodedFrameCount = 0;
            Interlocked.Exchange(ref _rtpFramesInWindow, 0);
            Interlocked.Exchange(ref _nalFramesInWindow, 0);
            Interlocked.Exchange(ref _decodedFramesInWindow, 0);
            Interlocked.Exchange(ref _uiFramesInWindow, 0);
            _lastRtpFps = _lastDecodedFps = _lastUiFps = 0;
            Interlocked.Exchange(ref _uiQueueLength, 0);
        }

        private static byte[] GetBytes(IntPtr ptr, int size)
        {
            var buf = new byte[size];
            Marshal.Copy(ptr, buf, 0, size);
            return buf;
        }

        private void Log(string msg)
        {
            AppLogger.Log("Receiver", msg);
            OnLog?.Invoke($"[Receiver] {msg}");
        }

        // ── Dispose ───────────────────────────────────────────────────────────
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
