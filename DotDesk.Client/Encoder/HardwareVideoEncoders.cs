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
    public abstract class FfmpegHardwareH264Encoder : IHardwareVideoEncoder
    {
        public event Action<EncodedVideoPacket>? OnEncoded;

        public abstract HardwareVideoEncoderKind HardwareKind { get; }
        public abstract string Name { get; }
        protected abstract string CodecName { get; }
        protected abstract void FillEncoderOptions(MediaDictionary dict);

        public bool IsHardware => true;
        public VideoEncoderInfo Info => new(
            Name,
            true,
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
        private long _encodedCount;
        private long _lastForceKeyFrameLogTick;
        private long _lastStatsTick;
        private long _lastEncodeElapsedMs;
        private int _statsIFrames;
        private int _statsPFrames;
        private long _statsIBytes;
        private long _statsPBytes;
        private string _pendingIdrReason = "首帧";
        private volatile bool _forceKeyFrame = true;
        private bool _disposed;

        protected FfmpegHardwareH264Encoder(VideoEncoderOptions options)
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
                throw new NotSupportedException($"{Name} 当前仅接入 H264");
            if (_inputPixelFormat != VideoPixelFormat.Bgra)
                throw new NotSupportedException($"{Name} 当前阶段仅支持 CPU BGRA 输入");

            Log($"初始化硬件编码器: ffmpeg={CodecName} {Width}x{Height}@{_targetFps}fps " +
                $"bitrate={_targetBitrate / 1000}kbps mode={_connectionMode} gop={_gopSize} " +
                $"min-keyint={_minKeyInt} lowLatency={_lowLatencyMode}");

            var codec = Codec.FindEncoderByName(CodecName);
            if (codec == null)
                throw new NotSupportedException($"FFmpeg 不支持 {CodecName}");

            _ctx = new CodecContext(codec)
            {
                Width = Width,
                Height = Height,
                TimeBase = new AVRational(1, _targetFps),
                Framerate = new AVRational(_targetFps, 1),
                BitRate = _targetBitrate,
                PixelFormat = AVPixelFormat.Nv12,
                GopSize = _gopSize,
                MaxBFrames = 0,
            };

            using var dict = new MediaDictionary();
            FillCommonOptions(dict);
            FillEncoderOptions(dict);

            _ctx.Open(codec, dict);
            Log($"硬件编码器已打开: {CodecName} input=BGRA->NV12 bframes=0 gop={_gopSize}");

            _srcFrame = Frame.CreateVideo(Width, Height, AVPixelFormat.Bgra);
            _dstFrame = Frame.CreateVideo(Width, Height, AVPixelFormat.Nv12);
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
                    CopyBgraToSourceFrame(frame);
                    _sws.ConvertFrame(_srcFrame, _dstFrame);
                    _dstFrame.Pts = _pts++;
                    ApplyFrameType();
                    _ctx.SendFrame(_dstFrame);
                    DrainPackets(packets, sw);
                }
                catch (Exception ex)
                {
                    Log($"硬件编码异常: {ex.Message}");
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
                    Log($"更新硬件编码参数: mode={_connectionMode} fps={_targetFps} " +
                        $"bitrate={_targetBitrate / 1000}kbps gop={_gopSize} min-keyint={_minKeyInt}");
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
                    Log("硬件编码器 Flush 完成");
                }
                catch (Exception ex)
                {
                    Log($"硬件编码器 Flush 异常: {ex.Message}");
                }
            }
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Log($"释放硬件编码器: {Name}");

            lock (_lock)
            {
                try { _pkt.Dispose(); } catch { }
                try { _dstFrame.Dispose(); } catch { }
                try { _srcFrame.Dispose(); } catch { }
                try { _sws.Dispose(); } catch { }
                try { _ctx.Dispose(); } catch { }
            }

            Log($"硬件编码器已释放: {Name}");
        }

        protected virtual void FillCommonOptions(MediaDictionary dict)
        {
            dict["bf"] = "0";
            dict["g"] = _gopSize.ToString();
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
                pf->pict_type = AVPictureType.I;
                _forceKeyFrame = false;
                Log($"强制 IDR: {_pendingIdrReason}");
                _pendingIdrReason = string.Empty;
            }
            else
            {
                pf->pict_type = AVPictureType.None;
            }
        }

        private void DrainPackets(List<EncodedVideoPacket> packets, Stopwatch encodeWatch)
        {
            while (true)
            {
                var ret = _ctx.ReceivePacket(_pkt);
                if (ret == CodecResult.Again || ret == CodecResult.EOF) break;
                if (ret != CodecResult.Success)
                {
                    Log($"硬件编码 ReceivePacket 失败: {ret}");
                    break;
                }

                byte[] data = _pkt.Data.ToArray();
                bool isKey = (_pkt.Flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                long pts = _pkt.Pts >= 0 ? _pkt.Pts * 1000 / Math.Max(1, _targetFps) : 0;
                var frameType = isKey ? EncodedFrameType.I : EncodedFrameType.P;
                var packet = new EncodedVideoPacket(
                    data,
                    isKey,
                    frameType,
                    pts,
                    encodeWatch.ElapsedMilliseconds,
                    data.Length);

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
                Log($"输出 H264: {packet.EncodedSize} bytes frame={packet.FrameType} encode={packet.EncodeElapsedMs}ms");
            }

            long now = Stopwatch.GetTimestamp();
            if (now - _lastStatsTick < Stopwatch.Frequency)
                return;

            int iCount = Math.Max(1, _statsIFrames);
            int pCount = Math.Max(1, _statsPFrames);
            Log($"硬件编码统计: I={_statsIFrames}/s P={_statsPFrames}/s " +
                $"avgI={_statsIBytes / iCount}B avgP={_statsPBytes / pCount}B " +
                $"lastEncode={_lastEncodeElapsedMs}ms");

            _statsIFrames = 0;
            _statsPFrames = 0;
            _statsIBytes = 0;
            _statsPBytes = 0;
            _lastStatsTick = now;
        }

        protected void Log(string msg) => AppLogger.Log(Name, msg);
    }

    public sealed class NvencVideoEncoder : FfmpegHardwareH264Encoder
    {
        public NvencVideoEncoder(VideoEncoderOptions options) : base(options) { }
        public override HardwareVideoEncoderKind HardwareKind => HardwareVideoEncoderKind.Nvenc;
        public override string Name => "NVIDIA NVENC";
        protected override string CodecName => "h264_nvenc";

        protected override void FillEncoderOptions(MediaDictionary dict)
        {
            dict["preset"] = "p1";
            dict["tune"] = "ull";
            dict["rc"] = "cbr";
            dict["zerolatency"] = "1";
            dict["delay"] = "0";
            dict["forced-idr"] = "1";
            dict["no-scenecut"] = "1";
        }
    }

    public sealed class AmfVideoEncoder : FfmpegHardwareH264Encoder
    {
        public AmfVideoEncoder(VideoEncoderOptions options) : base(options) { }
        public override HardwareVideoEncoderKind HardwareKind => HardwareVideoEncoderKind.Amf;
        public override string Name => "AMD AMF";
        protected override string CodecName => "h264_amf";

        protected override void FillEncoderOptions(MediaDictionary dict)
        {
            dict["usage"] = "ultralowlatency";
            dict["quality"] = "speed";
            dict["rc"] = "cbr";
        }
    }

    public sealed class QsvVideoEncoder : FfmpegHardwareH264Encoder
    {
        public QsvVideoEncoder(VideoEncoderOptions options) : base(options) { }
        public override HardwareVideoEncoderKind HardwareKind => HardwareVideoEncoderKind.Qsv;
        public override string Name => "Intel Quick Sync/QSV";
        protected override string CodecName => "h264_qsv";

        protected override void FillEncoderOptions(MediaDictionary dict)
        {
            dict["preset"] = "veryfast";
            dict["look_ahead"] = "0";
        }
    }
}
