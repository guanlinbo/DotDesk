using System;
using System.Threading;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using DotDesk.Core;

namespace DotDesk.Client.Encoder
{
    public sealed class H264Encoder : IDisposable
    {
        public event Action<byte[], bool, long>? OnEncoded;

        public int Width { get; }
        public int Height { get; }
        public int Fps { get; }
        public int Bitrate { get; }

        private readonly CodecContext _ctx;
        private readonly Frame _srcFrame;
        private readonly Frame _dstFrame;
        private readonly Packet _pkt;
        private readonly VideoFrameConverter _sws;
        private readonly object _lock = new();  // 线程安全锁

        private long _pts;
        private long _encodedCount;
        private bool _disposed;

        public H264Encoder(int width, int height, int fps = 30,
                           int bitrate = 4_000_000, string preset = "ultrafast")
        {
            Width = width & ~1;
            Height = height & ~1;
            Fps = fps;
            Bitrate = bitrate;

            Log($"初始化编码器 {Width}x{Height} @{Fps}fps");

            int gopSize = Math.Max(fps * 2, 16);
            int minKeyInt = Math.Max(fps, 8);

            var codec = Codec.FindEncoderById(AVCodecID.H264);
            _ctx = new CodecContext(codec)
            {
                Width = Width,
                Height = Height,
                TimeBase = new AVRational(1, fps),
                Framerate = new AVRational(fps, 1),
                BitRate = bitrate,
                PixelFormat = AVPixelFormat.Yuv420p,
                // 远控公网/TURN 下依赖周期 IDR 修复丢包花屏，但不能太密。
                GopSize = gopSize,
                MaxBFrames = 0,
            };

            using var dict = new MediaDictionary();
            dict["preset"] = preset;
            dict["tune"] = "zerolatency";
            dict["profile"] = "baseline";
            dict["level"] = "3.1";
            dict["x264-params"] =
                $"repeat-headers=1:sliced-threads=1:slice-max-size=700:keyint={gopSize}:min-keyint={minKeyInt}:scenecut=0";
            _ctx.Open(codec, dict);
            Log("H264 编码器已打开");

            _srcFrame = Frame.CreateVideo(Width, Height, AVPixelFormat.Bgra);
            _dstFrame = Frame.CreateVideo(Width, Height, AVPixelFormat.Yuv420p);
            _pkt = new Packet();
            _sws = new VideoFrameConverter();

            Log("图像转换器初始化完成");
        }

        /// <summary>强制下一帧编码为关键帧（IDR帧）</summary>
        public void ForceKeyFrame()
        {
            if (_disposed) return;
            // 设置标志，下次 Encode 时强制输出关键帧
            _forceKeyFrame = true;
        }

        private volatile bool _forceKeyFrame = false;

        public unsafe void Encode(byte[] bgra)
        {
            if (_disposed) return;
            if (bgra == null || bgra.Length < Width * Height * 4) return;

            lock (_lock)
            {
                try
                {
                    fixed (byte* src = bgra)
                    {
                        int srcStride = Width * 4;
                        byte* dst = (byte*)_srcFrame.Data[0];
                        int dstStride = _srcFrame.Linesize[0];

                        for (int row = 0; row < Height; row++)
                            Buffer.MemoryCopy(
                                src + (long)row * srcStride,
                                dst + (long)row * dstStride,
                                dstStride, srcStride);
                    }

                    _sws.ConvertFrame(_srcFrame, _dstFrame);
                    _dstFrame.Pts = _pts++;

                    // 强制关键帧：直接操作底层 AVFrame 的 pict_type
                    // 强制关键帧：直接操作底层 AVFrame 的 pict_type
                    if (_forceKeyFrame)
                    {
                        ffmpeg.av_frame_make_writable(_dstFrame);
                        unsafe
                        {
                            var pf = (AVFrame*)_dstFrame.DangerousGetHandle();
                            pf->pict_type = AVPictureType.I;
                        }
                        _forceKeyFrame = false;
                    }
                    else
                    {
                        unsafe
                        {
                            var pf = (AVFrame*)_dstFrame.DangerousGetHandle();
                            pf->pict_type = AVPictureType.None;
                        }
                    }

                    _ctx.SendFrame(_dstFrame);
                    DrainPackets();
                }
                catch (Exception ex)
                {
                    Log($"编码异常: {ex.Message}");
                }
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
                    DrainPackets();
                    Log("编码器 Flush 完成");
                }
                catch (Exception ex)
                {
                    Log($"Flush 异常: {ex.Message}");
                }
            }
        }

        private void DrainPackets()
        {
            // 注意：调用前必须持有 _lock
            while (true)
            {
                var ret = _ctx.ReceivePacket(_pkt);
                if (ret == CodecResult.Again || ret == CodecResult.EOF) break;
                if (ret != CodecResult.Success)
                {
                    Log($"ReceivePacket 失败: {ret}");
                    break;
                }

                byte[] data = _pkt.Data.ToArray();
                bool isKey = (_pkt.Flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                long pts = _pkt.Pts >= 0 ? _pkt.Pts * 1000 / Fps : 0;

                _encodedCount++;
                if (isKey || _encodedCount <= 3 || _encodedCount % (Fps * 5) == 0)
                    Log($"输出 H264: {data.Length} bytes key={isKey}");

                OnEncoded?.Invoke(data, isKey, pts);
                _pkt.Unref();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Log("释放 H264 编码器");

            lock (_lock)
            {
                try { Flush(); } catch { }
                try { _pkt.Dispose(); } catch { }
                try { _dstFrame.Dispose(); } catch { }
                try { _srcFrame.Dispose(); } catch { }
                try { _sws.Dispose(); } catch { }
                try { _ctx.Dispose(); } catch { }
            }

            Log("H264 编码器已释放");
        }

        private void Log(string msg) => AppLogger.Log("H264Encoder", msg);
    }
}
