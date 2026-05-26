using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using DotDesk.Core.Logging;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;

namespace DotDesk.Client.Encoder
{
    public sealed class X264VideoEncoder : IVideoEncoder
    {
        public event Action<EncodedVideoPacket>? OnEncoded;

        public string Name => "x264 software encoder";
        public bool IsHardware => false;
        public VideoEncoderInfo Info => new(
            Name,
            false,
            _codecFormat,
            Width,
            Height,
            _targetFps,
            _targetBitrate,
            _gopSize,
            _minKeyInt,
            _inputPixelFormat,
            _connectionMode);

        public int Width { get; }
        public int Height { get; }

        private readonly CodecContext _ctx;
        private readonly Frame _srcFrame;
        private readonly Frame _dstFrame;
        private readonly Packet _pkt;
        private readonly VideoFrameConverter _sws;
        private readonly object _lock = new();

        private readonly VideoPixelFormat _inputPixelFormat;
        private readonly VideoCodecFormat _codecFormat;
        private VideoConnectionMode _connectionMode;
        private int _targetFps;
        private int _targetBitrate;
        private int _gopSize;
        private int _minKeyInt;
        private int _qualityLevel;
        private bool _lowLatencyMode;

        private long _pts;
        private long _currentInputTimestampMs;
        private long _encodedCount;
        private long _lastForceKeyFrameLogTick;
        private long _lastStatsTick;
        private long _lastEncodeElapsedMs;
        private int _statsIFrames;
        private int _statsPFrames;
        private long _statsIBytes;
        private long _statsPBytes;
        private string _pendingIdrReason = "首帧";
        private string _nextKeyFrameReason = "forced:first-frame";
        private volatile bool _forceKeyFrame = true;
        private bool _isFirstFrame = true;    // 首帧标记：第一次 Encode 需要特殊处理
        private bool _useIntraRefresh;           // 当前编码器是否使用 intra-refresh
        private bool _disposed;

        public X264VideoEncoder(VideoEncoderOptions options)
        {
            Width = options.Width & ~1;
            Height = options.Height & ~1;
            _inputPixelFormat = options.InputPixelFormat;
            _codecFormat = options.CodecFormat;
            _connectionMode = options.ConnectionMode;
            _targetFps = Math.Max(1, options.TargetFps);
            _targetBitrate = Math.Max(80_000, options.TargetBitrate);
            _gopSize = options.GopSize > 0
                ? options.GopSize
                : VideoEncoderPolicy.CalculateGopSize(_targetFps, _connectionMode);
            _minKeyInt = VideoEncoderPolicy.CalculateMinKeyFrameInterval(_targetFps, _connectionMode);
            _qualityLevel = options.QualityLevel;
            _lowLatencyMode = options.LowLatencyMode;

            if (_codecFormat != VideoCodecFormat.H264)
                throw new NotSupportedException("当前 x264 fallback 仅支持 H264");
            if (_inputPixelFormat != VideoPixelFormat.Bgra)
                throw new NotSupportedException("当前 x264 fallback 阶段仅支持 CPU BGRA 输入");

            Log($"初始化 x264: {Width}x{Height}@{_targetFps}fps " +
                $"bitrate={_targetBitrate / 1000}kbps mode={_connectionMode} " +
                $"gop={_gopSize} min-keyint={_minKeyInt} lowLatency={_lowLatencyMode}");

            var codec = Codec.FindEncoderById(AVCodecID.H264);
            _ctx = new CodecContext(codec)
            {
                Width = Width,
                Height = Height,
                TimeBase = new AVRational(1, _targetFps),
                Framerate = new AVRational(_targetFps, 1),
                BitRate = _targetBitrate,
                PixelFormat = AVPixelFormat.Yuv420p,
                GopSize = _gopSize,
                MaxBFrames = 0,
            };

            using var dict = new MediaDictionary();
            dict["preset"] = SelectPreset(_qualityLevel);
            dict["tune"] = "zerolatency";
            dict["profile"] = "baseline";
            dict["level"] = "3.1";
            int maxRateKbps = Math.Max(80, _targetBitrate / 1000);
            // vbv-bufsize = maxrate*3：给低帧率(8fps)场景足够的峰值缓冲，避免 IDR 被截断
            int vbvBufferKbits = Math.Max(300, maxRateKbps * 3);

            // intra-refresh：用渐进式帧内刷新代替周期性 IDR。
            // 传统 IDR 每次强制输出一个完整的关键帧，在 relay 下单帧可达 80-100KB，
            // 超出 MTU 后被路由器 IP 分片，分片丢失导致整帧作废、解码花屏。
            // intra-refresh 把帧内宏块分散到多个 P 帧中逐渐刷新，
            // 每帧大小和 P 帧相近（5-20KB），不产生大包，relay 丢包率大幅降低。
            // 代价：画面出错后恢复时间从 1 帧延长到 keyint 帧（约 10s@8fps），
            // 但对于 relay 带宽受限场景，连续性优于瞬时恢复。
            // intra-refresh 在 relay 模式下开启：
            // - 首帧 EAGAIN 问题已由 priming 循环（最多补喂2帧）解决
            // - intra-refresh 避免大 IDR 帧（80~100KB），relay 丢包时不会全帧作废
            // - P2P 模式不需要 intra-refresh（带宽充足，大 IDR 没问题）
            bool useIntraRefresh = _connectionMode == VideoConnectionMode.Relay;

            string intraParams = useIntraRefresh
                // intra-refresh=1：渐进式刷新，避免大 IDR 帧
                // keyint=gopSize：刷新周期，每 gopSize 帧完成一次全屏刷新
                // aq-mode=0：关闭自适应量化，减少首帧 IDR 复杂度
                // weightb=0：关闭加权预测，降低编码开销
                ? $"intra-refresh=1:keyint={_gopSize}:min-keyint={_gopSize}:aq-mode=0:weightb=0"
                : _connectionMode == VideoConnectionMode.Relay
                    ? "keyint=9999:min-keyint=9999:scenecut=0:aq-mode=0:weightb=0"
                    : $"keyint={_gopSize}:min-keyint={_minKeyInt}:scenecut=0";

            dict["x264-params"] =
                $"repeat-headers=1:sliced-threads=0:bframes=0:rc-lookahead=0:" +
                intraParams + ":" +
                $"vbv-maxrate={maxRateKbps}:vbv-bufsize={vbvBufferKbits}:nal-hrd=none";

            _ctx.Open(codec, dict);
            _useIntraRefresh = useIntraRefresh;
            Log($"x264 已打开: tune=zerolatency bframes=0 rc-lookahead=0 repeat-headers=1 " +
                $"intra-refresh={useIntraRefresh} vbv={maxRateKbps}/{vbvBufferKbits}kbit [v5]");

            _srcFrame = Frame.CreateVideo(Width, Height, AVPixelFormat.Bgra);
            _dstFrame = Frame.CreateVideo(Width, Height, AVPixelFormat.Yuv420p);
            _pkt = new Packet();
            _sws = new VideoFrameConverter();
            _lastStatsTick = Stopwatch.GetTimestamp();
        }

        public VideoEncodeResult Encode(ReadOnlyMemory<byte> frame, long timestampMs = 0)
        {
            if (_disposed) return new VideoEncodeResult(Array.Empty<EncodedVideoPacket>(), 0, 0, false);
            if (frame.Length < Width * Height * 4)
                return new VideoEncodeResult(Array.Empty<EncodedVideoPacket>(), 0, 0, false);

            lock (_lock)
            {
                var packets = new List<EncodedVideoPacket>(2);
                var sw = Stopwatch.StartNew();

                try
                {
                    _currentInputTimestampMs = timestampMs > 0
                        ? timestampMs
                        : MonoNowMs();
                    CopyBgraToSourceFrame(frame);
                    _sws.ConvertFrame(_srcFrame, _dstFrame);
                    _dstFrame.Pts = _pts++;

                    ApplyFrameType();

                    if (_isFirstFrame)
                    {
                        // ── 首帧特殊路径 ──────────────────────────────────────────────────
                        // 问题根因：intra-refresh=1 模式下，即使 pict_type=I，
                        //   x264 依然会将首帧放入编码器内部缓冲，
                        //   send_frame 后 receive_packet 返回 EAGAIN，必须额外处理。
                        // 解决：send_frame 后先 drain；若 drain 无包，
                        //   再补喂最多 2 次相同帧（priming），直到拿到 packet。
                        // 整个首帧 fast-path 控制在 50ms 以内。
                        AppLogger.Log("X264", $"send_frame first-frame pts={_dstFrame.Pts}");
                        _ctx.SendFrame(_dstFrame);
                        DrainPackets(packets, sw);

                        if (packets.Count == 0)
                        {
                            // 首帧 EAGAIN：补喂相同帧（encoder priming）
                            for (int prime = 0; prime < 2 && packets.Count == 0; prime++)
                            {
                                AppLogger.Log("X264", $"[FirstFrame] duplicated input frame for encoder priming (prime={prime + 1})");
                                _dstFrame.Pts = _pts++;
                                SetCurrentFramePictureType(AVPictureType.None);
                                _ctx.SendFrame(_dstFrame);
                                DrainPackets(packets, sw);
                            }
                        }

                        if (packets.Count > 0)
                        {
                            AppLogger.Log("X264", $"[FirstFrame] first packet emitted at {sw.ElapsedMilliseconds}ms size={packets[0].EncodedSize}B key={packets[0].IsKeyFrame}");
                            AppLogger.Log("Encoder", $"first frame encoded cost={sw.ElapsedMilliseconds}ms size={packets[0].EncodedSize}");
                        }
                        else
                        {
                            AppLogger.Log("X264", $"[FirstFrame] no packet after priming, reason=EAGAIN encoder_delay");
                        }

                        _isFirstFrame = false;
                    }
                    else
                    {
                        _ctx.SendFrame(_dstFrame);
                        DrainPackets(packets, sw);
                    }
                }
                catch (Exception ex)
                {
                    Log($"x264 编码异常: {ex.Message}");
                }
                finally
                {
                    sw.Stop();
                    _lastEncodeElapsedMs = sw.ElapsedMilliseconds;
                }

                int totalBytes = 0;
                bool hasKey = false;
                foreach (var packet in packets)
                {
                    totalBytes += packet.EncodedSize;
                    hasKey |= packet.IsKeyFrame;
                }

                return new VideoEncodeResult(packets, _lastEncodeElapsedMs, totalBytes, hasKey);
            }
        }

        public void UpdateOptions(VideoEncoderUpdateOptions options)
        {
            if (_disposed) return;

            lock (_lock)
            {
                bool changed = false;

                if (options.TargetBitrate is { } bitrate && bitrate > 0 && Math.Abs(bitrate - _targetBitrate) > 10_000)
                {
                    _targetBitrate = bitrate;
                    _ctx.BitRate = bitrate;
                    changed = true;
                }

                if (options.TargetFps is { } fps && fps > 0 && fps != _targetFps)
                {
                    _targetFps = fps;
                    _ctx.TimeBase = new AVRational(1, fps);
                    _ctx.Framerate = new AVRational(fps, 1);
                    changed = true;
                }

                if (options.ConnectionMode is { } mode && mode != _connectionMode)
                {
                    _connectionMode = mode;
                    changed = true;
                }

                if (options.GopSize is { } gop && gop > 0 && gop != _gopSize)
                {
                    _gopSize = gop;
                    _ctx.GopSize = gop;
                    changed = true;
                }

                if (options.QualityLevel is { } quality && quality != _qualityLevel)
                {
                    _qualityLevel = quality;
                    changed = true;
                }

                if (options.LowLatencyMode is { } lowLatency && lowLatency != _lowLatencyMode)
                {
                    _lowLatencyMode = lowLatency;
                    changed = true;
                }

                _minKeyInt = VideoEncoderPolicy.CalculateMinKeyFrameInterval(_targetFps, _connectionMode);

                if (changed)
                {
                    Log($"更新 x264 参数: mode={_connectionMode} fps={_targetFps} " +
                        $"bitrate={_targetBitrate / 1000}kbps gop={_gopSize} " +
                        $"min-keyint={_minKeyInt} lowLatency={_lowLatencyMode}");
                }
            }
        }

        public void ForceIdr(string reason)
        {
            if (_disposed) return;
            _pendingIdrReason = string.IsNullOrWhiteSpace(reason) ? "未指定原因" : reason;
            _forceKeyFrame = true;

            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _lastForceKeyFrameLogTick);
            if (last == 0 || now - last > Stopwatch.Frequency * 5)
            {
                Interlocked.Exchange(ref _lastForceKeyFrameLogTick, now);
                Log($"请求 IDR: {_pendingIdrReason}");
            }
        }

        public void Flush()
        {
            if (_disposed) return;
            lock (_lock)
            {
                try
                {
                    _ctx.SendFrame(null);
                    DrainPackets(new List<EncodedVideoPacket>(), Stopwatch.StartNew());
                    Log("x264 Flush 完成");
                }
                catch (Exception ex)
                {
                    Log($"x264 Flush 异常: {ex.Message}");
                }
            }
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Log("释放 x264 编码器");

            lock (_lock)
            {
                try { _pkt.Dispose(); } catch { }
                try { _dstFrame.Dispose(); } catch { }
                try { _srcFrame.Dispose(); } catch { }
                try { _sws.Dispose(); } catch { }
                try { _ctx.Dispose(); } catch { }
            }

            Log("x264 编码器已释放");
        }

        private unsafe void CopyBgraToSourceFrame(ReadOnlyMemory<byte> frame)
        {
            if (!MemoryMarshal.TryGetArray(frame, out ArraySegment<byte> segment) || segment.Array == null)
                segment = new ArraySegment<byte>(frame.ToArray());

            fixed (byte* srcBase = segment.Array)
            {
                byte* src = srcBase + segment.Offset;
                int srcStride = Width * 4;
                byte* dst = (byte*)_srcFrame.Data[0];
                int dstStride = _srcFrame.Linesize[0];

                for (int row = 0; row < Height; row++)
                {
                    Buffer.MemoryCopy(
                        src + (long)row * srcStride,
                        dst + (long)row * dstStride,
                        dstStride,
                        srcStride);
                }
            }
        }

        private unsafe void ApplyFrameType()
        {
            ffmpeg.av_frame_make_writable(_dstFrame);
            var pf = (AVFrame*)_dstFrame.DangerousGetHandle();

            if (_forceKeyFrame)
            {
                bool isIntraRefresh = _useIntraRefresh;
                bool isPeriodicRecovery = string.Equals(
                    _pendingIdrReason, "periodic-recovery",
                    StringComparison.OrdinalIgnoreCase);

                // 跳过 IDR 的条件：relay + intra-refresh + 周期恢复 + 非首帧
                // 首帧（_isFirstFrame=true）：无论 intra-refresh 状态都强制 pict_type=I
                // 原因：intra-refresh 模式下 x264 会忽略 pict_type=I，
                //       首帧的 priming 循环才是真正让编码器吐包的手段
                bool skipIdr = isIntraRefresh && isPeriodicRecovery && !_isFirstFrame;

                if (!skipIdr)
                {
                    pf->pict_type = AVPictureType.I;
                    _nextKeyFrameReason = $"forced:{NormalizeKeyFrameReason(_pendingIdrReason)}";
                    if (_isFirstFrame)
                    {
                        // 打印编码器延迟诊断信息
                        unsafe
                        {
                            var pCtx = (AVCodecContext*)_ctx.DangerousGetHandle();
                            AppLogger.Log("X264",
                                $"[X264] codec delay={pCtx->delay} has_b_frames={pCtx->has_b_frames} intra_refresh={isIntraRefresh}");
                        }
                    }
                    Log($"强制 IDR: {_pendingIdrReason}");
                }
                else
                {
                    pf->pict_type = AVPictureType.None;
                    Log($"跳过强制 IDR（intra-refresh 模式）: {_pendingIdrReason}");
                }
                _forceKeyFrame = false;
                _pendingIdrReason = string.Empty;
            }
            else
            {
                pf->pict_type = AVPictureType.None;
            }
        }

        private unsafe void SetCurrentFramePictureType(AVPictureType type)
        {
            ffmpeg.av_frame_make_writable(_dstFrame);
            var pf = (AVFrame*)_dstFrame.DangerousGetHandle();
            pf->pict_type = type;
        }

        private void DrainPackets(List<EncodedVideoPacket> packets, Stopwatch encodeWatch)
        {
            while (true)
            {
                var ret = _ctx.ReceivePacket(_pkt);
                if (ret == CodecResult.Again || ret == CodecResult.EOF) break;
                if (ret != CodecResult.Success)
                {
                    Log($"x264 ReceivePacket 失败: {ret}");
                    break;
                }

                byte[] data = _pkt.Data.ToArray();
                bool isKey = (_pkt.Flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                string keyReason = isKey ? ConsumeKeyFrameReason() : "";
                long pts = _currentInputTimestampMs > 0
                    ? _currentInputTimestampMs
                    : _pkt.Pts >= 0 ? _pkt.Pts * 1000 / Math.Max(1, _targetFps) : 0;
                var frameType = isKey ? EncodedFrameType.I : EncodedFrameType.P;
                var packet = new EncodedVideoPacket(
                    data,
                    isKey,
                    frameType,
                    pts,
                    encodeWatch.ElapsedMilliseconds,
                    data.Length,
                    keyReason);

                packets.Add(packet);
                OnEncoded?.Invoke(packet);
                UpdateStats(packet);

                _pkt.Unref();
            }
        }

        private void UpdateStats(EncodedVideoPacket packet)
        {
            _encodedCount++;
            if (packet.IsKeyFrame)
            {
                _statsIFrames++;
                _statsIBytes += packet.EncodedSize;
            }
            else
            {
                _statsPFrames++;
                _statsPBytes += packet.EncodedSize;
            }

            if (packet.IsKeyFrame || _encodedCount <= 3)
            {
                Log($"输出 H264: {packet.EncodedSize} bytes frame={packet.FrameType} " +
                    $"reason={NormalizeKeyFrameReason(packet.KeyFrameReason)} encode={packet.EncodeElapsedMs}ms");
            }

            long now = Stopwatch.GetTimestamp();
            if (now - _lastStatsTick < Stopwatch.Frequency)
                return;

            int iCount = Math.Max(1, _statsIFrames);
            int pCount = Math.Max(1, _statsPFrames);
            Log($"编码统计: I={_statsIFrames}/s P={_statsPFrames}/s " +
                $"avgI={_statsIBytes / iCount}B avgP={_statsPBytes / pCount}B " +
                $"lastEncode={_lastEncodeElapsedMs}ms");

            _statsIFrames = 0;
            _statsPFrames = 0;
            _statsIBytes = 0;
            _statsPBytes = 0;
            _lastStatsTick = now;
        }

        private static string SelectPreset(int qualityLevel)
        {
            return qualityLevel >= 8 ? "veryfast" : "ultrafast";
        }

        private void Log(string msg) => AppLogger.Log("X264VideoEncoder", msg);

        private static long MonoNowMs() =>
            Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
    }
}
