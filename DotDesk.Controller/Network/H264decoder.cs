using System;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using DotDesk.Core.Logging;

namespace DotDesk.Controller.Network
{
    public sealed class H264Decoder : IDisposable
    {
        public event Action<byte[], int, int>? OnFrame;

        private readonly CodecContext _ctx;
        private readonly Frame _yuvFrame;
        private readonly Frame _bgrFrame;
        private VideoFrameConverter _sws;
        private bool _disposed;
        private int _lastW, _lastH;

        public H264Decoder()
        {
            var codec = Codec.FindDecoderById(AVCodecID.H264);
            _ctx = new CodecContext(codec);
            _ctx.Open(codec);

            _yuvFrame = new Frame();
            _bgrFrame = new Frame();
            _sws = new VideoFrameConverter();
        }

        public unsafe void Decode(byte[] nal)
        {
            if (_disposed || nal == null || nal.Length == 0) return;

            // 确保有 Annex B 起始码
            byte[] data;
            if (HasStartCode(nal))
                data = nal;
            else
            {
                data = new byte[4 + nal.Length];
                data[0] = 0; data[1] = 0; data[2] = 0; data[3] = 1;
                Buffer.BlockCopy(nal, 0, data, 4, nal.Length);
            }

            fixed (byte* ptr = data)
            {
                var pkt = ffmpeg.av_packet_alloc();
                try
                {
                    ffmpeg.av_new_packet(pkt, data.Length);
                    Buffer.MemoryCopy(ptr, pkt->data, data.Length, data.Length);
                    pkt->size = data.Length;

                    int ret = ffmpeg.avcodec_send_packet(_ctx, pkt);
                    if (ret < 0)
                    {
                        // AVERROR_INVALIDDATA: 等待关键帧，静默忽略
                        if (ret != -1094995529)
                            AppLogger.Log("Decoder", $"send_packet 失败: {ret}");
                        return;
                    }
                }
                finally { ffmpeg.av_packet_free(&pkt); }
            }

            DrainFrames();
        }

        private void DrainFrames()
        {
            while (true)
            {
                var result = _ctx.ReceiveFrame(_yuvFrame);
                if (result == CodecResult.Again || result == CodecResult.EOF) break;
                if (result != CodecResult.Success)
                {
                    AppLogger.Log("Decoder", $"ReceiveFrame 失败: {result}");
                    break;
                }

                ConvertAndCallback();
                _yuvFrame.Unref();
            }
        }

        private unsafe void ConvertAndCallback()
        {
            int w = _ctx.Width;
            int h = _ctx.Height;
            if (w <= 0 || h <= 0) return;

            if (_lastW != w || _lastH != h)
            {
                AppLogger.Log("Decoder", $"分辨率变化: {_lastW}x{_lastH} → {w}x{h}");

                // 重建 BGR 帧
                ffmpeg.av_frame_unref(_bgrFrame);
                _bgrFrame.Width = w;
                _bgrFrame.Height = h;
                _bgrFrame.Format = (int)AVPixelFormat.Bgr24;
                int ret = ffmpeg.av_frame_get_buffer(_bgrFrame, 1);
                if (ret < 0)
                {
                    AppLogger.Log("Decoder", $"BGR帧分配失败: {ret}");
                    return;
                }

                // 重建 sws 上下文（清除旧分辨率缓存）
                _sws.Dispose();
                _sws = new VideoFrameConverter();

                _lastW = w;
                _lastH = h;
                AppLogger.Log("Decoder", $"BGR帧已分配: {w}x{h} stride={_bgrFrame.Linesize[0]}");
            }

            ffmpeg.av_frame_make_writable(_bgrFrame);
            _sws.ConvertFrame(_yuvFrame, _bgrFrame, SWS.Bilinear);

            int stride = _bgrFrame.Linesize[0];
            if (stride <= 0) return;

            int rowBytes = w * 3;
            var output = new byte[rowBytes * h];
            byte* src = (byte*)_bgrFrame.Data[0];

            fixed (byte* dst = output)
            {
                for (int row = 0; row < h; row++)
                    Buffer.MemoryCopy(
                        src + (long)row * stride,
                        dst + (long)row * rowBytes,
                        rowBytes, rowBytes);
            }

            OnFrame?.Invoke(output, w, h);
        }

        private static bool HasStartCode(byte[] data) =>
            (data.Length >= 4 &&
             data[0] == 0 && data[1] == 0 &&
             data[2] == 0 && data[3] == 1)
            ||
            (data.Length >= 3 &&
             data[0] == 0 && data[1] == 0 &&
             data[2] == 1);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sws.Dispose();
            _bgrFrame.Dispose();
            _yuvFrame.Dispose();
            _ctx.Dispose();
        }
    }
}
