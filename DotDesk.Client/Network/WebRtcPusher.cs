// WebRtcPusher.cs  —  被控端 WebRTC 推流核心（libdatachannel 版本）
//
// ┌─ 传输层 ──────────────────────────────────────────────────────────────┐
// │  底层库：DataChannelDotnet（封装 libdatachannel C++）                 │
// │  DTLS：OpenSSL（原生，比原 SIPSorcery BouncyCastle 快 3~5 倍）       │
// │  ICE：libjuice（原生，支持 STUN/TURN UDP）                           │
// └───────────────────────────────────────────────────────────────────────┘
//
// ┌─ 连接策略：并行双路 + 快速决策 ───────────────────────────────────────┐
// │  1. peer-joined 时立即发 P2P Offer（仅 host/srflx candidate）        │
// │  2. 同时启动 1500ms 计时器；计时结束 P2P 未通 → 立刻切 TURN relay   │
// │  3. 关闭 TCP candidate（对称 NAT 下无效，只增协商延迟）              │
// │  4. ICE Failed/Disconnected 也会触发 TURN fallback，但加了去抖       │
// └───────────────────────────────────────────────────────────────────────┘
//
// ┌─ 视频发送：应用层 RFC 6184 FU-A ─────────────────────────────────────┐
// │  • 不用 rtcSetH264Packetizer（libjuice TURN buffer 硬限 4096B，       │
// │    整帧 NAL 超限会被 libjuice 静默丢弃）                              │
// │  • 在 C# 层手动做 FU-A 分片：每包 RTP payload ≤ 1100B                │
// │  • 默认不附加 FEC：TURN relay 下优先降低包量和排队时延               │
// └───────────────────────────────────────────────────────────────────────┘
//
// ┌─ QoS：基于发送队列深度的动态码率/帧率控制 ───────────────────────────┐
// │  queue=0      → 正常（P2P:900kbps/15fps，relay:550kbps/8fps）        │
// │  queue≥2(mild)→ 轻拥塞：降码率/降帧率                               │
// │  queue≥4(cng) → 严重拥塞：大幅降码率/帧率，丢弃积压旧帧             │
// │  relay 模式始终 queueLimit=1：永远只保最新帧，丢旧帧保实时性         │
// └───────────────────────────────────────────────────────────────────────┘

using DotDesk.Client.Encoder;
using DotDesk.Client.Input;
using DotDesk.Core.Config;
using DotDesk.Core.Logging;
using DotDesk.Core.Network;
using DotDesk.Core.Protocol;
using DotDesk.Core.Security;
using DotDesk.Core.WebRtc;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotDesk.Client.Network
{
    public sealed class WebRtcPusher : IDisposable
    {
        // ── 公开事件 ──────────────────────────────────────────────────────────
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action? OnAuthSuccess;
        public event Action? OnAuthFailed;
        public event Action<string>? OnConnectionStatus;
        public event Action<string>? OnLog;
        public event Action<string>? OnForceCaptureOnce;

        public bool IsConnected => _connected;
        public bool IsSignalingConnected => _sig.IsConnected;
        public string Password => _otp.Current;
        public string RefreshPassword() => _otp.Refresh();
        public string SetFixedPassword(string? pw) { _otp.SetFixed(pw); return _otp.Current; }

        public int RecommendedMaxStreamWidth => 1280;

        // ── 私有字段 ──────────────────────────────────────────────────────────
        private readonly SignalingClient _sig;
        private readonly OneTimePassword _otp = new();
        private readonly object _pcLock = new();
        private readonly object _encoderLock = new();
        private readonly SemaphoreSlim _sendSignal = new(0);
        private readonly ConcurrentQueue<EncodedVideoFrame> _sendQueue = new();
        private readonly SemaphoreSlim _turnFallbackLock = new(1, 1);
        private readonly object _rtpSendLock = new();

        // libdatachannel 句柄（int，-1 = 未初始化）
        private int _pc = -1;
        private int _dc = -1;   // DataChannel
        private int _vtr = -1;   // Video Track

        // 编码器
        private IVideoEncoder? _encoder;
        private CancellationTokenSource? _sendLoopCts;

        // 状态
        private bool _disposed;
        private bool _connected;
        private bool _authPassed;
        private bool _disconnecting;
        private bool _allowRelay;
        private bool _p2pFailed;
        private bool _isRetryingTurn;
        private bool _forceNextKeyFrame = true;
        private int _connectionAttemptId;
        private int _queuedEncodedFrames;
        private int _sentVideoFrames;
        private int _droppedEncodedFrames;
        private int _droppedRawFrames;
        private int _localOfferSent;

        // 编码器参数缓存
        private int _width, _height, _fps;
        private int _encoderFps, _encoderBitrate, _encoderGop;
        private VideoConnectionMode _encoderConnectionMode;
        private int _targetVideoFps, _targetSendBytesPerSecond, _targetEncoderBitrate;
        private int _targetQueueLimit, _targetKeyFrameIntervalSeconds;
        private int _pendingEncoderWidth, _pendingEncoderHeight;
        private int _sendBudgetBytes;
        private int _framesSinceLastIdr;
        private int _immediateFrameRequests;
        private int _firstVideoSentLogged;
        private int _firstFrameEncodedLogged;
        private int _firstFrameFastPathActive;
        private int _startupDuplicateKeyFramesDropped;

        private long _lastRawFrameTick;
        private long _lastKeyFrameRequestTick;
        private long _lastPeriodicKeyFrameTick;
        private long _lastForcedIdrTick;
        private long _lastFirstFrameIdrTick;
        private long _lastRealIFrameSentTick;
        private long _lastVideoSentTick;
        private long _connectedTick;
        private long _lastEncoderRecreateTick;
        private long _lastSlowSendTick;
        private long _lastRatePolicyLogTick;
        private long _sendBudgetWindowTick;
        private long _lastSentVideoPtsMs = -1;
        private long _pendingEncoderResizeTick;
        private long _firstFrameCaptureDoneTick;
        private long _firstFrameEncoderReadyTick;
        private long _firstFrameEncodedTick;
        private long _firstFrameSentTick;
        private long _lastIdrSkipLogTick;
        private long _nextFrameId;
        private int _lastLoggedIdrIntervalSeconds;
        private int _lastLoggedViewerRequestCooldownSeconds;
        private VideoConnectionMode _lastLoggedIdrMode;

        // RTP 时间戳基准（连接建立时固定）
        private ulong _rtpBaseUs;

        // GC 保护（防止委托被回收）
        private DescriptionCallback? _cbLocalDescription;
        private CandidateCallback? _cbLocalCandidate;
        private StateChangeCallback? _cbStateChange;
        private GatheringStateCallback? _cbGatheringState;
        private DataChannelCallback? _cbDataChannel;
        private OpenCallback? _cbDcOpen;
        private ClosedCallback? _cbDcClose;
        private MessageCallback? _cbDcMessage;

        private readonly ConcurrentDictionary<long, long> _frameIdsByCaptureTs = new();

        private sealed record EncodedVideoFrame(byte[] Nal, bool IsKeyFrame, long CaptureTimestampMs, long FrameId, string KeyFrameReason);

        public long NextFrameId() => Interlocked.Increment(ref _nextFrameId);

        // ── 构造 ──────────────────────────────────────────────────────────────
        public WebRtcPusher(string signalingServerUrl, string deviceCode)
        {
            Rtc.InitLogger(4, msg => AppLogger.Log("RTC", msg)); // RTC_LOG_INFO
            AppLogger.Log("Pusher", "WebRTC transport: DataChannelDotnet/libdatachannel, h264Packetizer=app-fua, fec=off");

            _sig = new SignalingClient(signalingServerUrl, deviceCode, "host");
            _otp.SetFixed(DotDeskSettingsStore.Load().FixedPassword);

            _sig.OnLog += msg => Log(msg);
            _sig.OnStateChanged += state =>
            {
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
            _sig.OnPeerLeftGraceful += () => { Log("控制端主动断开"); ResetPc(); OnDisconnected?.Invoke(); };
            _sig.OnPeerLeftAbnormal += () => { Log("控制端掉线"); ResetPc(); OnDisconnected?.Invoke(); };

            InputHandler.OnRequestKeyFrame += HandleRequestKeyFrame;
            InputHandler.OnCursorChanged += HandleCursorChanged;
        }

        // ── 生命周期 ──────────────────────────────────────────────────────────
        public async Task StartAsync(int width, int height, int fps = 30)
        {
            if (_disposed) return;
            _width = width; _height = height; _fps = fps;
            UpdateDynamicVideoPolicy(forceLog: true);
            _disconnecting = false; _authPassed = false;
            _connected = false; _forceNextKeyFrame = true;
            _sig.AutoReconnect = true;
            await _sig.ConnectAsync();
            if (!_sig.IsConnected)
                throw new InvalidOperationException("无法连接信令服务器");
            Log("被控端已启动，等待控制端连接...");
        }

        public void Disconnect()
        {
            if (_disconnecting) return;
            _disconnecting = true; _authPassed = false;
            try { _sig.Disconnect(); } catch { }
            ResetPc();
        }

        public bool ConsumeImmediateFrameRequest() =>
            Interlocked.Exchange(ref _immediateFrameRequests, 0) > 0;

        // ── 推帧入口 ──────────────────────────────────────────────────────────
        public void PushFrame(byte[] bgra, int width, int height) =>
            PushFrame(bgra, width, height, force: false);

        public void MarkFirstFrameCaptureDone()
        {
            long connectedTick = Interlocked.Read(ref _connectedTick);
            if (connectedTick == 0) return;
            long now = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref _firstFrameCaptureDoneTick, now);
            Log($"[FirstFrame] capture done at {(now - connectedTick) * 1000 / Stopwatch.Frequency}ms");
        }

        public void PushFirstFrameImmediately(byte[] bgra, int width, int height, long captureTimestampMs = 0, long frameId = 0)
        {
            if (_disposed || !_authPassed || !_connected || _pc < 0) return;
            if (bgra is null || bgra.Length == 0) return;

            try
            {
                UpdateDynamicVideoPolicy();
                if (!EnsureEncoder(width, height)) return;
                if (captureTimestampMs <= 0) captureTimestampMs = MonoNowMs();
                if (frameId <= 0) frameId = NextFrameId();

                long connectedTick = Interlocked.Read(ref _connectedTick);
                long readyTick = Stopwatch.GetTimestamp();
                Interlocked.Exchange(ref _firstFrameEncoderReadyTick, readyTick);
                if (connectedTick != 0)
                    Log($"[FirstFrame] encoder ready at {(readyTick - connectedTick) * 1000 / Stopwatch.Frequency}ms");

                lock (_encoderLock)
                {
                    Interlocked.Exchange(ref _firstFrameFastPathActive, 1);
                    try
                    {
                        long firstFrameIdr = Interlocked.Read(ref _lastFirstFrameIdrTick);
                        long forceCheckTick = Stopwatch.GetTimestamp();
                        if (firstFrameIdr == 0 || forceCheckTick - firstFrameIdr > Stopwatch.Frequency * 5)
                            ForceEncoderIdr("首帧立即发送", "first-frame-fast", resetPeriodic: true, logForce: true);
                        var firstFrameDeadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 750 / 1000;
                        int attempt = 0;
                        while (Stopwatch.GetTimestamp() < firstFrameDeadline)
                        {
                            attempt++;
                            long beginTick = Stopwatch.GetTimestamp();
                            if (connectedTick != 0)
                                Log($"[FirstFrame] encode begin at {(beginTick - connectedTick) * 1000 / Stopwatch.Frequency}ms");

                            LogLatency(captureTimestampMs, "capture->encode", MonoNowMs() - captureTimestampMs);
                            RememberFrameMetadata(captureTimestampMs, frameId);
                            var result = _encoder?.Encode(bgra, captureTimestampMs);
                            if (result != null && result.Packets.Count > 0)
                                break;

                            if (attempt <= 5 || attempt % 10 == 0)
                                Log($"[FirstFrame] encode returned no packet, retry={attempt + 1}");
                            Thread.Sleep(attempt < 5 ? 2 : 5);
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _firstFrameFastPathActive, 0);
                    }
                }
            }
            catch (Exception ex) { Log($"首帧立即发送失败: {ex.Message}"); }
        }

        public void PushFrame(byte[] bgra, int width, int height, bool force)
            => PushFrame(bgra, width, height, force, MonoNowMs());

        public void PushFrame(byte[] bgra, int width, int height, bool force, long captureTimestampMs)
            => PushFrame(bgra, width, height, force, captureTimestampMs, 0);

        public void PushFrame(byte[] bgra, int width, int height, bool force, long captureTimestampMs, long frameId)
        {
            if (_disposed || !_authPassed || !_connected || _pc < 0) return;
            if (bgra is null || bgra.Length == 0) return;
            UpdateDynamicVideoPolicy();
            if (!force && !ShouldAcceptRawFrame()) { _droppedRawFrames++; return; }
            if (captureTimestampMs <= 0) captureTimestampMs = MonoNowMs();
            if (frameId <= 0) frameId = NextFrameId();

            try
            {
                if (!EnsureEncoder(width, height)) return;
                    if (!Monitor.TryEnter(_encoderLock)) { _droppedRawFrames++; return; }
                try
                {
                    if (force)
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (!IsRecentForcedIdr(now, 1))
                            ForceEncoderIdr("控制端请求立即刷新画面", "decoder-error");
                    }
                    RememberFrameMetadata(captureTimestampMs, frameId);
                    _encoder?.Encode(bgra, captureTimestampMs);
                    if (!force) Interlocked.Exchange(ref _immediateFrameRequests, 0);
                }
                finally { Monitor.Exit(_encoderLock); }
            }
            catch (Exception ex) { Log($"编码失败: {ex.Message}"); }
        }

        // ── 信令回调 ──────────────────────────────────────────────────────────
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

        // ── 并行双路连接策略 ─────────────────────────────────────────────────
        // 参考 ToDesk/RustDesk 策略：
        //   1. 立即发 P2P Offer（仅 host/srflx，不含 relay）
        //   2. STUN 探测完成后等 1500ms 宽限期，P2P 仍未建立 → 立刻启动 TURN
        //   3. 关闭 TCP candidate（对称 NAT 下无效，只增延迟）
        //   4. P2P 一旦成功，TURN 不再发起
        // ── 并行双路连接：P2P 先行，STUN 结束后 1500ms 内未通则切 TURN ──────────
        // 传统做法：等 ICE Failed（约 40s）再切 TURN → 用户等待 40s 才出画面
        // 当前做法：STUN 探测通常 100~300ms 完成，之后再等 1500ms 宽限期
        //           → 最差情况 1.8s 就能切 TURN，比 40s 快 20 倍
        // 参考：ToDesk/RustDesk 均采用类似并行策略
        private const int StunFallbackMs = 1500; // STUN 完成后切 TURN 的最大等待（ms）

        private async void OnPeerJoined()
        {
            if (_disposed || _disconnecting) return;
            try
            {
                _authPassed = false;
                _p2pFailed = false; _isRetryingTurn = false;
                Log("控制端上线，创建 PeerConnection...");
                // P2P 先行
                await CreatePcAsync(allowRelay: false);
                // 并行计时：STUN 结束后若 P2P 未通则快速切 TURN
                int capturedAttemptId = _connectionAttemptId;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(StunFallbackMs);
                    if (_disposed || _disconnecting || _connected) return;
                    if (_allowRelay || _isRetryingTurn) return;
                    if (capturedAttemptId != _connectionAttemptId) return;
                    // P2P 候选已全部尝试（ICE Gathering Complete），
                    // 等待宽限期结束后仍未连通 → 判定 P2P 不可用
                    // 常见原因：双方均处于对称 NAT（运营商 CGNAT/4G），UDP 打洞失败
                    Log($"[快速决策] {StunFallbackMs}ms 内 P2P 未建立，提前启动 TURN relay");
                    await StartTurnRelayAsync(capturedAttemptId, "EarlyFallback");
                });
            }
            catch (Exception ex) { Log($"创建 PeerConnection 失败: {ex.Message}"); ResetPc(); }
        }

        private void OnAnswerReceived(string sdp)
        {
            if (_disposed || _disconnecting || _pc < 0) return;
            Log(_allowRelay ? "收到 TURN Answer" : "收到 Answer");
            string cleanSdp = System.Text.RegularExpressions.Regex.Replace(sdp, @"a=fmtp:[^\n]+\n?", "");
            if (!_allowRelay) cleanSdp = IceCandidateTools.StripRelayCandidates(cleanSdp);
            int ret = Rtc.SetRemoteDescription(_pc, cleanSdp, "answer");
            if (ret < 0) { Log($"setRemoteDescription 失败: {ret}"); ResetPc(); }
        }

        private void OnRemoteIce(IceCandidate ice)
        {
            if (_disposed || _disconnecting || _pc < 0) return;
            if (ShouldDropRemoteCandidate(ice.Candidate)) return;
            Rtc.AddRemoteCandidate(_pc, ice.Candidate, ice.SdpMid ?? "0");
        }

        // ── PeerConnection 建立 ───────────────────────────────────────────────
        private async Task CreatePcAsync(bool allowRelay)
        {
            _allowRelay = allowRelay;
            ResetPc();
            if (!allowRelay) { _p2pFailed = false; _isRetryingTurn = false; }
            int attemptId = ++_connectionAttemptId;
            Interlocked.Exchange(ref _localOfferSent, 0);
            _rtpBaseUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency);
            InitRtpState();

            Log(allowRelay ? "创建 TURN PeerConnection" : "开始 P2P 打洞");
            OnConnectionStatus?.Invoke(allowRelay ? "正在通过 TURN 中继连接..." : "正在 P2P 打洞...");

            string[] iceServers = BuildIceServers(allowRelay);
            Log($"ICE config relayOnly={allowRelay} servers={string.Join(", ", iceServers)}");
            _pc = Rtc.CreatePeerConnection(iceServers, enableIceTcp: false, mtu: 1300,
                relayOnly: allowRelay, disableAutoNegotiation: true);
            if (_pc < 0)
            {
                Log($"CreatePeerConnection 失败: {_pc}");
                ResetPc();
                return;
            }

            // 注册回调（持有强引用防 GC）
            _cbLocalDescription = (pc, sdp, type, _) =>
            {
                SendLocalDescription(sdp, type);
            };

            _cbLocalCandidate = (pc, cand, mid, _) =>
            {
                if (_disposed || _disconnecting) return;
                if (ShouldDropLocalCandidate(cand)) return;
                _sig.SendIce(new IceCandidate { Candidate = cand, SdpMid = mid, SdpMLineIndex = 0 });
                Log($"发送 ICE: {IceCandidateTools.Describe(cand)}");
            };

            _cbStateChange = (pc, state, userPtr) =>
            {
                Log($"P2P: {state}");
                if (state == RtcState.Connected)
                    ReportConnected();
                else if (state is RtcState.Failed or RtcState.Disconnected or RtcState.Closed)
                {
                    if (!_allowRelay && state is RtcState.Failed or RtcState.Disconnected)
                    {
                        var _unused = StartTurnRelayAsync(attemptId, state.ToString());
                    }
                    ScheduleDisconnectIfDown(attemptId);
                }
            };

            _cbGatheringState = (pc, state, _) => Log($"ICE Gathering: {state}");

            _cbDataChannel = (pc, dc, _) =>
            {
                _dc = dc;
                Log($"收到 DataChannel");
                _cbDcOpen = (id, _) =>
                {
                    Log("DataChannel 已开启");
                    TrySendText(DotDeskMessageCodec.ConnectionStatus("dataChannelOpen", "控制通道已开启"));
                };
                _cbDcClose = (id, _) => Log("DataChannel 已关闭");
                _cbDcMessage = (id, msgPtr, size, _) =>
                {
                    try
                    {
                        long hostReceiveMonoMs = MonoNowMs();
                        string json = size < 0
                            ? Marshal.PtrToStringUTF8(msgPtr) ?? ""
                            : Encoding.UTF8.GetString(GetBytes(msgPtr, size));
                        var msg = DotDeskMessageCodec.Parse(json);
                        if (msg.MessageType == DotDeskMessageType.Ping)
                            ReplyTimeSyncPing(msg, hostReceiveMonoMs);
                        else
                            InputHandler.Handle(json);
                    }
                    catch (Exception ex) { Log($"处理输入失败: {ex.Message}"); }
                };
                Rtc.SetOpenCallback(dc, _cbDcOpen);
                Rtc.SetClosedCallback(dc, _cbDcClose);
                Rtc.SetMessageCallback(dc, _cbDcMessage);
            };

            Rtc.SetLocalCandidateCallback(_pc, _cbLocalCandidate);
            Rtc.SetStateChangeCallback(_pc, _cbStateChange);
            Rtc.SetGatheringStateChangeCallback(_pc, _cbGatheringState);
            Rtc.SetDataChannelCallback(_pc, _cbDataChannel);
            Rtc.SetLocalDescriptionCallback(_pc, _cbLocalDescription);

            // 先创建 DataChannel，再添加 Video Track。libdatachannel 会在第一个媒体对象
            // 加入后进入 have-local-offer，因此必须让 DataChannel 先进入 SDP。
            _dc = Rtc.CreateDataChannel(_pc, "input");
            if (_dc < 0) { Log($"CreateDataChannel 失败: {_dc}"); ResetPc(); return; }
            _cbDcOpen = (id, _) =>
            {
                Log("DataChannel 已开启");
                TrySendText(DotDeskMessageCodec.ConnectionStatus("dataChannelOpen", "控制通道已开启"));
            };
            _cbDcClose = (id, _) => Log("DataChannel 已关闭");
            _cbDcMessage = (id, msgPtr, size, _) =>
            {
                try
                {
                    long hostReceiveMonoMs = MonoNowMs();
                    string json = size < 0
                        ? Marshal.PtrToStringUTF8(msgPtr) ?? ""
                        : Encoding.UTF8.GetString(GetBytes(msgPtr, size));
                    var msg = DotDeskMessageCodec.Parse(json);
                    if (msg.MessageType == DotDeskMessageType.Ping)
                        ReplyTimeSyncPing(msg, hostReceiveMonoMs);
                    else
                        InputHandler.Handle(json);
                }
                catch (Exception ex) { Log($"处理输入失败: {ex.Message}"); }
            };
            Rtc.SetOpenCallback(_dc, _cbDcOpen);
            Rtc.SetClosedCallback(_dc, _cbDcClose);
            Rtc.SetMessageCallback(_dc, _cbDcMessage);

            // 添加 Video Track（SendOnly）
            // 用 SDP 媒体描述字符串方式添加，最灵活
            string trackSdp =
                "m=video 9 UDP/TLS/RTP/SAVP 96\r\n" +
                "c=IN IP4 0.0.0.0\r\n" +
                "a=mid:0\r\n" +
                "a=sendonly\r\n" +
                "a=rtpmap:96 H264/90000\r\n" +
                "a=fmtp:96 profile-level-id=42e01f;packetization-mode=1\r\n" +
                "a=rtcp-mux\r\n" +
                "a=rtcp-fb:96 transport-cc\r\n";
            _vtr = Rtc.AddTrack(_pc, trackSdp);
            if (_vtr < 0) { Log($"AddTrack 失败: {_vtr}"); ResetPc(); return; }

            // rtcSetH264Packetizer 在 datachannel.dll 1.3.1 中存在，但 rtcSendMessage 发到
            // Video Track 时 libjuice 的 TURN buffer 只有 4096B，整帧超限全部丢弃。
            // 解决方案：应用层 RFC 6184 FU-A 分片，每包 ≤ 1100B，再逐包 rtcSendMessage，
            // 同时手动构造 RTP 头（12字节），让接收端的 H264RtpDepacketizer 能正确重组。
            // 这样完全不依赖 native packetizer，也不受 buffer 限制。
            Log("应用层 RTP/FU-A 分片已启用，maxFragment=1100B fec=off lowLatency=on");

            // 注册 Track 发送完成回调（用于慢发检测）
            Rtc.SetTrackOpenCallback(_vtr, (tr, _) => Log("Video Track 已开启"));

            int offerRet = Rtc.SetLocalDescription(_pc, "offer");
            if (offerRet < 0)
            {
                Log($"SetLocalDescription(offer) ignored/failed: {offerRet}");
            }

            SendCurrentLocalDescriptionIfAny();

            StartVideoSendLoop(attemptId);
            Log("Offer 协商中...");
        }

        // ── 视频发送循环 ──────────────────────────────────────────────────────
        private void StartVideoSendLoop(int attemptId)
        {
            StopVideoSendLoop();
            _sendLoopCts = new CancellationTokenSource();
            var token = _sendLoopCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_disposed && !_disconnecting)
                {
                    try { await _sendSignal.WaitAsync(token); }
                    catch (OperationCanceledException) { break; }

                    if (attemptId != _connectionAttemptId) break;

                    while (_sendQueue.TryDequeue(out var frame))
                    {
                        Interlocked.Decrement(ref _queuedEncodedFrames);
                        if (token.IsCancellationRequested || _disposed || _disconnecting) return;
                        int tr = _vtr;
                        if (tr < 0 || !_connected) continue;

                        try { SendEncodedFrameNow(frame, firstFrameFastPath: false); }
                        catch (Exception ex) { Log($"发送视频异常: {ex.Message}"); return; }
                    }
                }
            }, token);
        }

        // ── 帧年龄阈值（超过此值直接丢弃，不允许把旧画面发到接收端）────────────
        // relay 模式：150ms（单程延迟已 ~50ms，再发 150ms 前的帧意义不大）
        // P2P 模式：300ms（P2P 延迟低，可以宽松一些）
        private const int DropFrameAgeRelayMs = 150;
        private const int DropFrameAgeP2pMs = 300;

        // native 发送缓冲上限：超过此值说明 TURN/SRTP 层积压，停止发新帧
        // libdatachannel 的 libjuice TURN buffer 约 256KB
        private const int NativeBufferMaxBytes = 64_000; // 64KB，约 58 个 1100B RTP 包

        private void SendEncodedFrameNow(EncodedVideoFrame frame, bool firstFrameFastPath)
        {
            int tr = _vtr;
            if (tr < 0 || !_connected) return;
            if (ShouldDropDuplicateStartupKeyFrame(frame))
                return;

            // ── 检查1：帧年龄（C# 层帧队列积压导致的旧帧）────────────────────
            // CaptureTimestampMs 是采集时的 MonoNowMs，和当前比较得到帧龄
            if (!firstFrameFastPath && frame.CaptureTimestampMs > 0)
            {
                long frameAgeMs = MonoNowMs() - frame.CaptureTimestampMs;
                int maxAgeMs = _allowRelay ? DropFrameAgeRelayMs : DropFrameAgeP2pMs;
                if (frameAgeMs > maxAgeMs && !frame.IsKeyFrame)
                {
                    // 过期 P 帧：直接丢弃，避免播放旧画面
                    AppLogger.Log("Pusher", $"[DropOld] 丢弃过期帧 age={frameAgeMs}ms > {maxAgeMs}ms key={frame.IsKeyFrame} size={frame.Nal.Length}B");
                    return; // Decrement 已在出队时完成（565行），直接 return
                }
                if (frameAgeMs > maxAgeMs && frame.IsKeyFrame)
                {
                    // IDR 帧不丢，但记录警告
                    AppLogger.Log("Pusher", $"[DropOld] 过期 IDR 帧仍发送 age={frameAgeMs}ms（IDR 不可丢）");
                }
            }

            // ── 检查2：native 发送缓冲（TURN/SRTP 层积压）───────────────────
            // rtcGetBufferedAmount 返回当前 libdatachannel native 层待发送字节数
            // 若积压超阈值，P 帧直接跳过；IDR 帧强制发（接收端需要关键帧恢复画面）
            if (!firstFrameFastPath && !frame.IsKeyFrame && tr >= 0)
            {
                int buffered = Rtc.GetBufferedAmount(tr);
                if (buffered > NativeBufferMaxBytes)
                {
                    AppLogger.Log("Pusher",
                        $"[NativeBuf] native 缓冲积压 {buffered}B > {NativeBufferMaxBytes}B，丢弃 P 帧 size={frame.Nal.Length}B");
                    return;
                }
                // 每 5 秒打印一次 bufferedAmount（正常情况下应接近 0）
                LogNativeBuffer(tr, buffered);
            }

            // 计算 RTP 时间戳（90kHz）
            ulong nowUs = (ulong)(Stopwatch.GetTimestamp() * 1_000_000L / Stopwatch.Frequency);
            uint rtpTs = (uint)((nowUs - _rtpBaseUs) * 90_000 / 1_000_000);

            var sendWatch = Stopwatch.StartNew();
            int sent90;
            lock (_rtpSendLock)
                TrySendText(DotDeskMessageCodec.VideoFrameMeta(frame.FrameId, frame.CaptureTimestampMs));
                sent90 = SendFrameAsFuA(tr, frame.Nal, rtpTs, frame.IsKeyFrame, frame.CaptureTimestampMs, frame.FrameId);
            sendWatch.Stop();

            if (sendWatch.ElapsedMilliseconds >= 30)
                Interlocked.Exchange(ref _lastSlowSendTick, Stopwatch.GetTimestamp());

            int sent = ++_sentVideoFrames;
            long nowTick = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref _lastVideoSentTick, nowTick);
            if (frame.IsKeyFrame && sent90 >= 0)
                Interlocked.Exchange(ref _lastRealIFrameSentTick, nowTick);
            if (sent90 < 0)
                Log($"发送视频失败: ret={sent90}");
            else if (firstFrameFastPath || frame.IsKeyFrame || sent <= 3 || sent % 90 == 0)
                Log($"发送视频帧: {frame.Nal.Length}B key={frame.IsKeyFrame} reason={NormalizeKeyFrameReason(frame)} pkts={sent90} send={sendWatch.ElapsedMilliseconds}ms queue={Volatile.Read(ref _queuedEncodedFrames)}");

            long connectedTick = Interlocked.Read(ref _connectedTick);
            if (connectedTick != 0 && firstFrameFastPath)
            {
                long sentTick = Stopwatch.GetTimestamp();
                Interlocked.Exchange(ref _firstFrameSentTick, sentTick);
                Log($"[FirstFrame] sent at {(sentTick - connectedTick) * 1000 / Stopwatch.Frequency}ms");
                Log($"[FirstFrame] total after connected={(sentTick - connectedTick) * 1000 / Stopwatch.Frequency}ms");
            }
            if (frame.CaptureTimestampMs > 0)
                LogLatency(frame.CaptureTimestampMs, "capture->send", MonoNowMs() - frame.CaptureTimestampMs);

            if (connectedTick != 0 && Interlocked.CompareExchange(ref _firstVideoSentLogged, 1, 0) == 0)
            {
                long elapsedMs = (Stopwatch.GetTimestamp() - connectedTick) * 1000 / Stopwatch.Frequency;
                Log($"first video sent after connected={elapsedMs}ms");
            }
        }

        // ── 编码器管理 ────────────────────────────────────────────────────────
        private bool EnsureEncoder(int width, int height)
        {
            int evenW = width & ~1, evenH = height & ~1;
            lock (_encoderLock)
            {
                UpdateDynamicVideoPolicy();
                int bitrate = _targetEncoderBitrate > 0 ? _targetEncoderBitrate
                    : _allowRelay ? 750_000 : 650_000;
                int fps = _targetVideoFps > 0 ? _targetVideoFps : _fps;
                var mode = _allowRelay ? VideoConnectionMode.Relay : VideoConnectionMode.P2P;
                int gop = VideoEncoderPolicy.CalculateGopSize(fps, mode);

                if (_encoder != null && _encoder.Info.Width == evenW && _encoder.Info.Height == evenH)
                {
                    UpdateEncoderOptions(fps, bitrate, gop, mode);
                    return true;
                }

                if (_encoder != null && !IsResolutionStableForRecreate(evenW, evenH))
                    return false;

                Log($"创建编码器: {evenW}x{evenH}@{fps}fps bitrate={bitrate / 1000}kbps gop={gop} mode={mode}");
                try { _encoder?.Dispose(); _encoder = null; } catch { }

                _encoder = EncoderFactory.Create(new VideoEncoderOptions(
                    evenW, evenH, VideoPixelFormat.Bgra, fps, bitrate, gop,
                    LowLatencyMode: true, mode, VideoCodecFormat.H264));

                _encoderFps = fps; _encoderBitrate = bitrate;
                _encoderGop = gop; _encoderConnectionMode = mode;
                _lastEncoderRecreateTick = Stopwatch.GetTimestamp();
                _pendingEncoderWidth = _pendingEncoderHeight = 0;

                if (_forceNextKeyFrame)
                {
                    ForceEncoderIdr("新连接/编码器启动", "first-frame");
                    _forceNextKeyFrame = false;
                }
                Interlocked.Exchange(ref _lastPeriodicKeyFrameTick, Stopwatch.GetTimestamp());

                _encoder.OnEncoded += pkt =>
                {
                    long connectedTick = Interlocked.Read(ref _connectedTick);
                    if (connectedTick != 0 && Interlocked.CompareExchange(ref _firstFrameEncodedLogged, 1, 0) == 0)
                    {
                        AppLogger.Log("Encoder", $"first frame encoded cost={pkt.EncodeElapsedMs}ms size={pkt.EncodedSize}");
                        long encodedTick = Stopwatch.GetTimestamp();
                        Interlocked.Exchange(ref _firstFrameEncodedTick, encodedTick);
                        Log($"[FirstFrame] encoded at {(encodedTick - connectedTick) * 1000 / Stopwatch.Frequency}ms");
                    }

                    if (Volatile.Read(ref _firstFrameFastPathActive) == 1)
                    {
                        SendEncodedFrameNow(new EncodedVideoFrame(
                            pkt.Data,
                            pkt.IsKeyFrame,
                            pkt.PresentationTimeMs,
                            ResolveFrameId(pkt.PresentationTimeMs),
                            pkt.KeyFrameReason),
                            firstFrameFastPath: true);
                        return;
                    }

                    QueueEncodedFrame(pkt.Data, pkt.IsKeyFrame, pkt.PresentationTimeMs, pkt.KeyFrameReason);
                };

                return true;
            }
        }

        private void UpdateEncoderOptions(int fps, int bitrate, int gop, VideoConnectionMode mode)
        {
            if (_encoder == null) return;
            bool changed = _encoderFps != fps || _encoderBitrate != bitrate
                        || _encoderGop != gop || _encoderConnectionMode != mode;
            if (!changed) return;
            _encoder.UpdateOptions(new VideoEncoderUpdateOptions(fps, bitrate, gop,
                ConnectionMode: mode, LowLatencyMode: true));
            _encoderFps = fps; _encoderBitrate = bitrate;
            _encoderGop = gop; _encoderConnectionMode = mode;
        }

        private bool IsResolutionStableForRecreate(int w, int h)
        {
            long now = Stopwatch.GetTimestamp();
            long debounceTicks = Stopwatch.Frequency * 800 / 1000;
            if (Interlocked.Read(ref _lastEncoderRecreateTick) != 0
                && now - _lastEncoderRecreateTick < debounceTicks) return false;
            if (_pendingEncoderWidth != w || _pendingEncoderHeight != h)
            {
                _pendingEncoderWidth = w; _pendingEncoderHeight = h;
                _pendingEncoderResizeTick = now;
                return false;
            }
            return _pendingEncoderResizeTick != 0
                && now - _pendingEncoderResizeTick >= debounceTicks;
        }

        // ── 帧队列 ────────────────────────────────────────────────────────────
        private readonly object _queueLock = new();

        private void QueueEncodedFrame(byte[] nal, bool isKeyFrame, long captureTimestampMs, string keyFrameReason)
        {
            if (_disposed || !_authPassed || _pc < 0 || !_connected) return;
            UpdateDynamicVideoPolicy();
            var frame = new EncodedVideoFrame(nal, isKeyFrame, captureTimestampMs, ResolveFrameId(captureTimestampMs), keyFrameReason);
            if (ShouldDropDuplicateStartupKeyFrame(frame))
                return;
            if (!ShouldSendEncodedFrame(nal.Length, isKeyFrame))
            {
                _droppedEncodedFrames++;
                return;
            }

            lock (_queueLock)
            {
                int maxQueued = _allowRelay ? 1 : isKeyFrame ? 2 : Math.Max(1, _targetQueueLimit);
                if (Volatile.Read(ref _queuedEncodedFrames) >= maxQueued)
                {
                    if (_allowRelay)
                    {
                        // 【策略：relay 模式永远丢旧帧保新帧】
                        // relay 路径延迟 60~200ms，积压的旧帧送到接收端时已经过时
                        // 与其发一帧 300ms 前的画面，不如跳过，等下一帧最新画面
                        while (_sendQueue.TryDequeue(out _))
                            Interlocked.Decrement(ref _queuedEncodedFrames);
                    }
                    else if (!isKeyFrame)
                    {
                        _droppedEncodedFrames++;
                        return;
                    }
                    else
                    {
                        while (_sendQueue.TryDequeue(out _))
                            Interlocked.Decrement(ref _queuedEncodedFrames);
                    }
                }
                _sendQueue.Enqueue(frame);
                Interlocked.Increment(ref _queuedEncodedFrames);
            }
            try { _sendSignal.Release(); } catch { }
        }

        private bool ShouldDropDuplicateStartupKeyFrame(EncodedVideoFrame frame)
        {
            if (!frame.IsKeyFrame)
                return false;

            long recentRealI = Interlocked.Read(ref _lastRealIFrameSentTick);
            if (recentRealI == 0)
                return false;

            long now = Stopwatch.GetTimestamp();
            long connectedTick = Interlocked.Read(ref _connectedTick);
            bool startupWindow = connectedTick != 0 && now - connectedTick < Stopwatch.Frequency * 10;
            bool recentKey = now - recentRealI < Stopwatch.Frequency * 10;
            if (!startupWindow || !recentKey)
                return false;

            Interlocked.Increment(ref _droppedEncodedFrames);
            int dropped = Interlocked.Increment(ref _startupDuplicateKeyFramesDropped);
            if (dropped <= 3)
                Log("[IDR] startup duplicate I-frame dropped");
            return true;
        }

        private static string NormalizeKeyFrameReason(EncodedVideoFrame frame)
        {
            if (!frame.IsKeyFrame) return "-";
            return string.IsNullOrWhiteSpace(frame.KeyFrameReason) ? "unknown" : frame.KeyFrameReason;
        }

        // ── IDR 管理 ──────────────────────────────────────────────────────────
        private void ForceEncoderIdr(string reason, string logReason,
            bool resetPeriodic = true, bool logForce = true)
        {
            if (_encoder == null) { _forceNextKeyFrame = true; return; }
            _encoder.ForceIdr(logReason);
            long now = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref _lastForcedIdrTick, now);
            if (string.Equals(logReason, "first-frame", StringComparison.OrdinalIgnoreCase))
                Interlocked.Exchange(ref _lastFirstFrameIdrTick, now);
            if (logForce) Log($"[IDR] force reason={logReason}");
            if (resetPeriodic) Interlocked.Exchange(ref _lastPeriodicKeyFrameTick, now);
        }

        private void HandleRequestKeyFrame()
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                long firstFrameIdr = Interlocked.Read(ref _lastFirstFrameIdrTick);
                if (firstFrameIdr != 0 && now - firstFrameIdr < Stopwatch.Frequency * 5)
                {
                    Log("[IDR] skipped reason=cooldown");
                    return;
                }
                long recentForced = Interlocked.Read(ref _lastForcedIdrTick);
                long cooldown = Stopwatch.Frequency * (_allowRelay ? 10 : 5);
                if (recentForced != 0 && now - recentForced < cooldown)
                {
                    Log("[IDR] skipped reason=cooldown");
                    return;
                }

                long recentRealI = Interlocked.Read(ref _lastRealIFrameSentTick);
                if (recentRealI != 0 && now - recentRealI < Stopwatch.Frequency * 10)
                {
                    Log("[IDR] skipped reason=cooldown");
                    return;
                }

                long last = Interlocked.Read(ref _lastKeyFrameRequestTick);
                if (last != 0 && now - last < cooldown)
                {
                    Log("[IDR] skipped reason=cooldown");
                    return;
                }
                Interlocked.Exchange(ref _lastKeyFrameRequestTick, now);
                lock (_encoderLock)
                {
                    ForceEncoderIdr("控制端请求关键帧", "decoder-error");
                    Interlocked.Exchange(ref _immediateFrameRequests, 1);
                    RequestForceCaptureOnce("viewer-request");
                }
            }
            catch { }
        }

        // ── 发送预算（令牌桶限速）────────────────────────────────────────────────
        // 防止编码器在某帧突发大量输出时（如场景切换）撑爆发送链路。
        // 以 1 秒为窗口，累计已发字节 ≤ maxBps 才允许本帧入队。
        // IDR 帧豁免（重置窗口）：确保关键帧无论多大都能发出，接收端能解码。
        // IDR 后前 3 帧 P 帧也豁免：这 3 帧是关键帧的参考链，丢了解码端会花屏。
        private readonly object _sendBudgetLock = new();

        private bool ShouldSendEncodedFrame(int byteCount, bool isKeyFrame)
        {
            UpdateDynamicVideoPolicy();
            int maxBps = _targetSendBytesPerSecond > 0 ? _targetSendBytesPerSecond
                : _allowRelay ? 180_000 : 320_000;
            long now = Stopwatch.GetTimestamp();
            lock (_sendBudgetLock)
            {
                if (_sendBudgetWindowTick == 0 || now - _sendBudgetWindowTick >= Stopwatch.Frequency)
                { _sendBudgetWindowTick = now; _sendBudgetBytes = 0; }
                if (isKeyFrame)
                { _sendBudgetWindowTick = now; _sendBudgetBytes = 0; _framesSinceLastIdr = 0; return true; }
                int protectedFramesAfterKey = _allowRelay ? 1 : 3;
                if (_framesSinceLastIdr < protectedFramesAfterKey) { _framesSinceLastIdr++; _sendBudgetBytes += byteCount; return true; }
                _framesSinceLastIdr++;
                if (_sendBudgetBytes + byteCount > maxBps) return false;
                _sendBudgetBytes += byteCount;
                return true;
            }
        }

        // ── 动态 QoS 策略（基于发送队列深度）────────────────────────────────────
        // 触发时机：PushFrame / QueueEncodedFrame 每次调用时检查，内部有2s节流
        //
        // 判断维度：发送队列深度（queuedEncodedFrames）
        //   queue=0        → 网络跟得上，正常参数
        //   queue≥2(mild)  → 轻拥塞，网络开始消化不了当前码率
        //   queue≥4(cong)  → 严重拥塞，必须大幅降级
        //
        // 策略设计原则（远程桌面场景）：
        //   • 永远丢旧帧保新帧（_allowRelay 模式 queueLimit=1 恒定）
        //   • 先降 FPS（减少数据量入口），再降码率（减少单帧体积）
        //   • relay 路径带宽受限（4Mbps 服务器），参数更保守
        //   • 不缓冲不平滑，实时性 > 流畅性
        private void UpdateDynamicVideoPolicy(bool forceLog = false)
        {
            int queued = Math.Max(0, Volatile.Read(ref _queuedEncodedFrames));
            bool congested = queued >= 4, mild = queued >= 2;
            int fps, sendBytes, bitrate, queueLimit, keyFrameSec;
            if (_allowRelay)
            {
                // TURN relay 路径：正常档使用 relay-clear，优先桌面文字清晰度；拥塞时立即降档。
                // queueLimit 始终=1：relay 延迟已有 60~100ms，绝对不允许额外积压
                if (congested) { fps = 4; sendBytes = 80_000; bitrate = 320_000; queueLimit = 1; keyFrameSec = 30; }
                else if (mild) { fps = 5; sendBytes = 105_000; bitrate = 420_000; queueLimit = 1; keyFrameSec = 30; }
                else { fps = 8; sendBytes = 190_000; bitrate = 1_300_000; queueLimit = 1; keyFrameSec = 30; }
            }
            else
            {
                // P2P 直连：带宽宽裕，允许更高码率和帧率
                // queueLimit 随拥塞程度收紧，最多保 6 帧缓冲
                if (congested) { fps = 6; sendBytes = 220_000; bitrate = 420_000; queueLimit = 3; keyFrameSec = 20; }
                else if (mild) { fps = 10; sendBytes = 340_000; bitrate = 650_000; queueLimit = 5; keyFrameSec = 16; }
                else { fps = 12; sendBytes = 260_000; bitrate = 1_800_000; queueLimit = 6; keyFrameSec = 15; }
            }
            bool changed = fps != _targetVideoFps || sendBytes != _targetSendBytesPerSecond
                || bitrate != _targetEncoderBitrate || queueLimit != _targetQueueLimit
                || keyFrameSec != _targetKeyFrameIntervalSeconds;
            _targetVideoFps = fps; _targetSendBytesPerSecond = sendBytes;
            _targetEncoderBitrate = bitrate; _targetQueueLimit = queueLimit;
            _targetKeyFrameIntervalSeconds = keyFrameSec;
            long now = Stopwatch.GetTimestamp();
            if ((forceLog || changed) && now - Interlocked.Read(ref _lastRatePolicyLogTick) > Stopwatch.Frequency * 2
                && Interlocked.Exchange(ref _lastRatePolicyLogTick, now) != now)
                Log($"[QoS] fps={fps} bitrate={bitrate / 1000}kbps budget={sendBytes / 1000}KB/s queued={queued}");
        }

        // ── 帧率控制 ──────────────────────────────────────────────────────────
        private bool ShouldAcceptRawFrame()
        {
            int targetFps = _targetVideoFps > 0 ? _targetVideoFps : (_allowRelay ? 8 : Math.Min(_fps, 10));
            if (targetFps <= 0) targetFps = 10;
            long now = Stopwatch.GetTimestamp();
            long minInterval = Stopwatch.Frequency / targetFps;
            long last = Interlocked.Read(ref _lastRawFrameTick);
            if (last != 0 && now - last < minInterval) return false;
            Interlocked.Exchange(ref _lastRawFrameTick, now);
            return true;
        }

        // ── DataChannel 发送 ──────────────────────────────────────────────────
        private string? _lastCursorKind;
        private long _lastCursorTick;

        private void HandleCursorChanged(string cursorKind)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                if (cursorKind == _lastCursorKind && now - _lastCursorTick < Stopwatch.Frequency / 5) return;
                _lastCursorKind = cursorKind; _lastCursorTick = now;
                TrySendText(DotDeskMessageCodec.CursorChanged(cursorKind));
            }
            catch { }
        }

        private void TrySendText(string json)
        {
            try { if (_dc >= 0) Rtc.SendMessage(_dc, json); }
            catch (Exception ex) { Log($"发送协议消息失败: {ex.Message}"); }
        }

        private void ReplyTimeSyncPing(DotDeskMessage msg, long hostReceiveMonoMs)
        {
            long hostSendMonoMs = MonoNowMs();
            TrySendText(DotDeskMessageCodec.Pong(
                msg.ControllerSendMonoMs,
                hostReceiveMonoMs,
                hostSendMonoMs));
        }

        // ── ICE 服务器配置 ────────────────────────────────────────────────────
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
                "stun:159.75.93.74:3478",
                "stun:stun.l.google.com:19302",
                "stun:stun1.l.google.com:19302",
                "stun:stun.qq.com:3478",
                "stun:stun.miwifi.com:3478",
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

        // ── 连接管理 ──────────────────────────────────────────────────────────
        private void ReportConnected()
        {
            if (_disposed || _disconnecting) return;
            _connected = true;
            Interlocked.Exchange(ref _connectedTick, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _firstVideoSentLogged, 0);
            Interlocked.Exchange(ref _firstFrameEncodedLogged, 0);
            Interlocked.Exchange(ref _firstFrameFastPathActive, 0);
            _firstFrameCaptureDoneTick = _firstFrameEncoderReadyTick = _firstFrameEncodedTick = _firstFrameSentTick = 0;
            Log(_allowRelay ? "最终连接路径: TURN relay" : "最终连接路径: P2P");
            OnConnectionStatus?.Invoke(_allowRelay ? "中继连接" : "P2P成功");
            TrySendText(DotDeskMessageCodec.ConnectionStatus(
                _allowRelay ? "relayConnected" : "p2pConnected",
                _allowRelay ? "TURN 中继已连接" : "P2P 已连接"));
            OnConnected?.Invoke();
            Task.Delay(50).ContinueWith(_ => RequestForceCaptureOnce("first-frame"));
        }

        private void ScheduleDisconnectIfDown(int attemptId)
        {
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (_disposed || _disconnecting || _pc < 0) return;
                if (attemptId != _connectionAttemptId) return;
                if (_connected) return;
                if (!_allowRelay) { Log("P2P 已失败，等待 TURN 中继重试"); return; }
                _connected = false;
                OnDisconnected?.Invoke();
            });
        }

        private async Task StartTurnRelayAsync(int attemptId, string reason)
        {
            await _turnFallbackLock.WaitAsync();
            try
            {
                if (_allowRelay || _isRetryingTurn) return;
                _p2pFailed = true; _isRetryingTurn = true;
                Log($"TURN fallback 开始: P2P 状态={reason}");
                OnConnectionStatus?.Invoke("P2P失败，正在切换TURN中继...");
                // EarlyFallback：调用前已等过 StunFallbackMs，立刻重建 PC
                // ICE Failed：等 300ms 让 libjuice 完成清理再重建
                // ICE Disconnected：等 800ms，可能是短暂网络抖动，多等一会
                int delayMs = reason == "EarlyFallback" ? 0
                            : reason == "Failed" ? 300 : 800;
                if (delayMs > 0) await Task.Delay(delayMs);
                if (_disposed || _disconnecting) return;
                if (_connected) return;
                await CreatePcAsync(allowRelay: true);
            }
            catch (Exception ex) { Log($"启动 TURN 中继失败: {ex}"); }
            finally { _isRetryingTurn = false; _turnFallbackLock.Release(); }
        }

        private void ResetPc()
        {
            _connected = false;
            StopVideoSendLoop();
            while (_sendQueue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _queuedEncodedFrames, 0);
            _sentVideoFrames = _droppedRawFrames = _droppedEncodedFrames = 0;
            _sendBudgetBytes = 0; _framesSinceLastIdr = 0;
            _firstVideoSentLogged = _firstFrameEncodedLogged = 0;
            _firstFrameFastPathActive = 0;
            _lastRawFrameTick = _lastKeyFrameRequestTick = _lastPeriodicKeyFrameTick = 0;
            _lastForcedIdrTick = _lastFirstFrameIdrTick = 0;
            _lastRealIFrameSentTick = _lastVideoSentTick = 0;
            _lastEncoderRecreateTick = _lastSlowSendTick = 0;
            _connectedTick = 0;
            _firstFrameCaptureDoneTick = _firstFrameEncoderReadyTick = _firstFrameEncodedTick = _firstFrameSentTick = 0;
            _localOfferSent = 0;
            _lastSentVideoPtsMs = -1;
            _forceNextKeyFrame = true;

            if (_vtr >= 0) { try { Rtc.DeleteTrack(_vtr); } catch { } _vtr = -1; }
            if (_dc >= 0) { try { Rtc.DeleteDataChannel(_dc); } catch { } _dc = -1; }
            if (_pc >= 0) { try { Rtc.DeletePeerConnection(_pc); } catch { } _pc = -1; }

            lock (_encoderLock)
            {
                try { _encoder?.Dispose(); _encoder = null; } catch { }
            }
        }

        private void StopVideoSendLoop()
        {
            try { _sendLoopCts?.Cancel(); _sendLoopCts?.Dispose(); }
            catch { }
            finally { _sendLoopCts = null; }
        }

        // ── 辅助 ──────────────────────────────────────────────────────────────
        // native 缓冲日志（5s 节流）
        private long _lastNativeBufLogTick;
        private void LogNativeBuffer(int tr, int buffered)
        {
            long now = Stopwatch.GetTimestamp();
            if (now - _lastNativeBufLogTick < Stopwatch.Frequency * 5) return;
            _lastNativeBufLogTick = now;
            // 也可顺便读 DataChannel 的 bufferedAmount 做对比
            int dcBuf = _dc >= 0 ? Rtc.GetBufferedAmount(_dc) : -1;
            AppLogger.Log("Pusher",
                $"[NativeBuf] track={buffered}B dc={dcBuf}B queue={Volatile.Read(ref _queuedEncodedFrames)}");
        }

        private bool IsRecentSlowSend(long now) =>
            Interlocked.Read(ref _lastSlowSendTick) != 0
            && now - _lastSlowSendTick < Stopwatch.Frequency * 5;

        private bool IsRecentEncoderRecreate(long now) =>
            Interlocked.Read(ref _lastEncoderRecreateTick) != 0
            && now - _lastEncoderRecreateTick < Stopwatch.Frequency * 5;

        private bool IsRecentForcedIdr(long now, int sec) =>
            Interlocked.Read(ref _lastForcedIdrTick) != 0
            && now - _lastForcedIdrTick < Stopwatch.Frequency * sec;

        private void RequestForceCaptureOnce(string reason)
        {
            try { OnForceCaptureOnce?.Invoke(reason); }
            catch { }
        }

        private void SendCurrentLocalDescriptionIfAny()
        {
            if (_pc < 0) return;
            string? sdp = Rtc.GetLocalDescription(_pc);
            string? type = Rtc.GetLocalDescriptionType(_pc);
            if (string.IsNullOrWhiteSpace(sdp) || string.IsNullOrWhiteSpace(type))
                return;
            SendLocalDescription(sdp, type);
        }

        private void SendLocalDescription(string sdp, string type)
        {
            if (_disposed || _disconnecting) return;
            bool hasVideo = sdp.Contains("m=video", StringComparison.OrdinalIgnoreCase);
            bool hasData = sdp.Contains("m=application", StringComparison.OrdinalIgnoreCase);
            Log($"LocalDescription type={type} sdpLength={sdp.Length} video={hasVideo} data={hasData}");
            if (type == "offer")
            {
                if (!hasVideo || !hasData)
                {
                    Log("LocalDescription offer incomplete, wait for video+data");
                    return;
                }

                if (Interlocked.CompareExchange(ref _localOfferSent, 1, 0) != 0)
                {
                    Log("LocalDescription offer duplicate ignored");
                    return;
                }
            }

            string finalSdp = sdp;
            if (!_allowRelay) finalSdp = IceCandidateTools.StripRelayCandidates(finalSdp);
            if (_allowRelay && !finalSdp.Contains("a=x-dotdesk-relay:1"))
                finalSdp += "a=x-dotdesk-relay:1\r\n";
            if (type == "offer") { _sig.SendOffer(finalSdp); Log("Offer 已发送"); }
            else if (type == "answer") { _sig.SendAnswer(finalSdp); Log("Answer 已发送"); }
        }

        private static byte[] GetBytes(IntPtr ptr, int size)
        {
            var buf = new byte[size];
            Marshal.Copy(ptr, buf, 0, size);
            return buf;
        }

        // ── 应用层 RFC 6184 RTP/FU-A 分片 ─────────────────────────────────────
        //
        // 为什么不用 rtcSetH264Packetizer（native）：
        //   libdatachannel 1.3.1 的 datachannel.dll 虽然导出了该函数，但通过 Track
        //   发送时 libjuice 底层 TURN send buffer 硬限 4096B，整帧 NAL（5~80KB）
        //   超限后被 libjuice 静默丢弃，接收端永远收不到。
        //
        // FU-A 分片（RFC 6184 §5.8）：
        //   每包 RTP payload ≤ 1100B = RTP头12B + FU indicator1B + FU header1B + 数据
        //   1100B + DTLS开销约30B + TURN header4B ≈ 1134B < libjuice buffer 4096B
        //   接收端 H264RtpDepacketizer 按 FU-A 重组后送解码器，与 SIPSorcery 版本相同
        //
        // FEC：
        //   接收端仍支持 PT=127 XOR FEC，但发送端默认关闭。当前 TURN relay 的主要
        //   瓶颈是包量和排队时延，额外 20% FEC 包会让鼠标/画面反馈更慢。

        private const int RtpMaxPayload = 1100;
        private const byte RtpPT = 96;
        private const byte FecPT = 127;  // FEC 包的 RTP payload type
        private const bool EnableRelayFec = false;
        private const int FecGroupSize = 4;    // 每4个数据包产生1个FEC包
        private const uint RtpClockRate = 90_000;
        private uint _rtpSsrc;
        private ushort _rtpSeq;

        // FEC 状态：积累当前组的 XOR
        private readonly byte[] _fecXor = new byte[12 + RtpMaxPayload + 4]; // 足够大
        private int _fecXorLen;
        private int _fecGroupCount;  // 当前组已累积包数
        private ushort _fecGroupStart; // 当前组第一包 seq

        private void InitRtpState()
        {
            var rng = new Random();
            _rtpSsrc = (uint)rng.Next(1, int.MaxValue);
            _rtpSeq = (ushort)rng.Next(0, 65535);
            _fecGroupCount = 0;
            _fecXorLen = 0;
        }

        /// <summary>
        /// 把一帧 Annex-B H264 NAL 按 RFC 6184 FU-A 分片后逐包发送。
        /// relay 模式下自动附加 XOR FEC 包。
        /// </summary>
        private int SendFrameAsFuA(int tr, byte[] nal, uint rtpTimestamp, bool isKeyFrame, long captureTimestampMs, long frameId)
        {
            if (nal == null || nal.Length == 0) return -1;

            // 跳过 Annex-B 起始码
            int offset = 0;
            if (nal.Length >= 4 && nal[0] == 0 && nal[1] == 0 && nal[2] == 0 && nal[3] == 1)
                offset = 4;
            else if (nal.Length >= 3 && nal[0] == 0 && nal[1] == 0 && nal[2] == 1)
                offset = 3;
            if (offset >= nal.Length) return -1;

            byte nalHeader = nal[offset];
            int nalLen = nal.Length - offset;
            int dataStart = offset + 1;

            // 单 NAL 包
            if (nalLen <= RtpMaxPayload)
            {
                int headerLen = captureTimestampMs > 0 ? 36 : 12;
                byte[] pkt = new byte[headerLen + nalLen];
                WriteRtpHeader(pkt, rtpTimestamp, marker: true, hasExtension: captureTimestampMs > 0);
                if (captureTimestampMs > 0) WriteFrameMetadataExtension(pkt, captureTimestampMs, frameId);
                Buffer.BlockCopy(nal, offset, pkt, headerLen, nalLen);
                SendWithFec(tr, pkt);
                return 1;
            }

            // FU-A 分片
            byte fuIndicator = (byte)((nalHeader & 0xE0) | 28);
            byte fuNalType = (byte)(nalHeader & 0x1F);
            int pos = dataStart;
            bool first = true;
            int count = 0;

            while (pos < nal.Length)
            {
                int fragDataLen = Math.Min(nal.Length - pos, RtpMaxPayload - 2);
                bool last = (pos + fragDataLen >= nal.Length);

                byte fuHeader = fuNalType;
                if (first) fuHeader |= 0x80;
                if (last) fuHeader |= 0x40;

                int headerLen = captureTimestampMs > 0 ? 36 : 12;
                byte[] pkt = new byte[headerLen + 2 + fragDataLen];
                WriteRtpHeader(pkt, rtpTimestamp, marker: last, hasExtension: captureTimestampMs > 0);
                if (captureTimestampMs > 0) WriteFrameMetadataExtension(pkt, captureTimestampMs, frameId);
                pkt[headerLen] = fuIndicator;
                pkt[headerLen + 1] = fuHeader;
                Buffer.BlockCopy(nal, pos, pkt, headerLen + 2, fragDataLen);

                int r = SendWithFec(tr, pkt);
                if (r < 0) return r;

                count++;
                pos += fragDataLen;
                first = false;
            }
            return count;
        }

        /// <summary>
        /// 发送单个 RTP 包；FEC 默认关闭以降低 relay 包量和排队时延。
        /// </summary>
        private int SendWithFec(int tr, byte[] pkt)
        {
            int r = Rtc.SendMessage(tr, pkt);
            _rtpSeq++;
            if (r < 0) return r;

            // 非 relay 模式不做 FEC（P2P 丢包率低，overhead 不值得）
            if (!_allowRelay || !EnableRelayFec) return r;

            // 积累 XOR：按字节 XOR 整个 RTP 包（含头）
            if (_fecGroupCount == 0)
            {
                _fecGroupStart = (ushort)(_rtpSeq - 1); // 组起始 seq
                int len = pkt.Length;
                if (len > _fecXor.Length) len = _fecXor.Length;
                Buffer.BlockCopy(pkt, 0, _fecXor, 0, len);
                _fecXorLen = len;
            }
            else
            {
                int len = Math.Min(pkt.Length, _fecXorLen);
                for (int i = 0; i < len; i++) _fecXor[i] ^= pkt[i];
                // 如果新包比 FEC 缓冲长，XOR 补充部分
                if (pkt.Length > _fecXorLen)
                {
                    for (int i = _fecXorLen; i < pkt.Length && i < _fecXor.Length; i++)
                        _fecXor[i] = pkt[i];
                    _fecXorLen = pkt.Length;
                }
            }
            _fecGroupCount++;

            // 满组：发出 FEC 包
            if (_fecGroupCount >= FecGroupSize)
            {
                FlushFecGroup(tr);
            }
            return r;
        }

        private void FlushFecGroup(int tr)
        {
            if (_fecGroupCount == 0 || _fecXorLen < 12) { _fecGroupCount = 0; return; }

            // FEC 包：RTP 头（PT=127）+ 4字节元数据 + XOR 数据
            // 元数据: groupStart(2B) + groupSize(1B) + pad(1B)
            byte[] fec = new byte[12 + 4 + _fecXorLen];
            // RTP 头：PT=FecPT, seq=当前seq, ts=0（FEC 时间戳无意义）
            fec[0] = 0x80;
            fec[1] = FecPT; // 不设 marker
            fec[2] = (byte)(_rtpSeq >> 8);
            fec[3] = (byte)(_rtpSeq & 0xFF);
            fec[4] = 0; fec[5] = 0; fec[6] = 0; fec[7] = 0; // ts=0
            fec[8] = (byte)(_rtpSsrc >> 24); fec[9] = (byte)(_rtpSsrc >> 16);
            fec[10] = (byte)(_rtpSsrc >> 8); fec[11] = (byte)(_rtpSsrc & 0xFF);
            // 元数据
            fec[12] = (byte)(_fecGroupStart >> 8);
            fec[13] = (byte)(_fecGroupStart & 0xFF);
            fec[14] = (byte)_fecGroupCount;
            fec[15] = 0;
            // XOR 数据
            Buffer.BlockCopy(_fecXor, 0, fec, 16, _fecXorLen);

            Rtc.SendMessage(tr, fec);
            _rtpSeq++;
            _fecGroupCount = 0;
            _fecXorLen = 0;
        }

        private void WriteRtpHeader(byte[] pkt, uint ts, bool marker, bool hasExtension = false)
        {
            pkt[0] = (byte)(0x80 | (hasExtension ? 0x10 : 0x00));
            pkt[1] = (byte)(RtpPT | (marker ? 0x80 : 0x00));
            pkt[2] = (byte)(_rtpSeq >> 8);
            pkt[3] = (byte)(_rtpSeq & 0xFF);
            pkt[4] = (byte)(ts >> 24); pkt[5] = (byte)(ts >> 16);
            pkt[6] = (byte)(ts >> 8); pkt[7] = (byte)(ts & 0xFF);
            pkt[8] = (byte)(_rtpSsrc >> 24); pkt[9] = (byte)(_rtpSsrc >> 16);
            pkt[10] = (byte)(_rtpSsrc >> 8); pkt[11] = (byte)(_rtpSsrc & 0xFF);
        }

        private static void WriteFrameMetadataExtension(byte[] pkt, long captureTimestampMs, long frameId)
        {
            // RTP one-byte header extension, profile 0xBEDE, 5 words (20 bytes).
            // id=1: host capture monotonic ms, id=2: frameId.
            pkt[12] = 0xBE; pkt[13] = 0xDE;
            pkt[14] = 0; pkt[15] = 5;
            pkt[16] = 0x17; // id=1, len=7 => 8-byte timestamp
            for (int i = 0; i < 8; i++)
                pkt[17 + i] = (byte)(captureTimestampMs >> (56 - i * 8));
            pkt[25] = 0x27; // id=2, len=7 => 8-byte frameId
            for (int i = 0; i < 8; i++)
                pkt[26 + i] = (byte)(frameId >> (56 - i * 8));
            pkt[34] = pkt[35] = 0;
        }

        private long _lastLatencyLogTick;
        private void RememberFrameMetadata(long captureTimestampMs, long frameId)
        {
            if (captureTimestampMs <= 0 || frameId <= 0) return;
            _frameIdsByCaptureTs[captureTimestampMs] = frameId;
            if (_frameIdsByCaptureTs.Count > 512)
            {
                foreach (var key in _frameIdsByCaptureTs.Keys)
                {
                    if (_frameIdsByCaptureTs.Count <= 256) break;
                    _frameIdsByCaptureTs.TryRemove(key, out _);
                }
            }
        }

        private long ResolveFrameId(long captureTimestampMs)
        {
            if (captureTimestampMs <= 0) return 0;
            return _frameIdsByCaptureTs.TryRemove(captureTimestampMs, out long frameId) ? frameId : 0;
        }

        private void LogLatency(long captureTimestampMs, string stage, long elapsedMs)
        {
            long now = Stopwatch.GetTimestamp();
            if (now - Interlocked.Read(ref _lastLatencyLogTick) < Stopwatch.Frequency) return;
            Interlocked.Exchange(ref _lastLatencyLogTick, now);
            AppLogger.Log("Latency", $"[Latency] {stage}={elapsedMs}ms captureTs={captureTimestampMs}");
        }

        private static long MonoNowMs() =>
            Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;


        private void Log(string msg)
        {
            AppLogger.Log("Pusher", msg);
            OnLog?.Invoke($"[Pusher] {msg}");
        }

        // ── Dispose ───────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _disconnecting = true; _authPassed = false;
            InputHandler.OnRequestKeyFrame -= HandleRequestKeyFrame;
            InputHandler.OnCursorChanged -= HandleCursorChanged;
            try { _sig.Disconnect(); } catch { }
            ResetPc();
            try { _sig.Dispose(); } catch { }
        }
    }
}
