using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DesktopDuplication
{
    public sealed class CapturedFrame : IDisposable
    {
        public byte[] Data { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public long Timestamp { get; }

        internal CapturedFrame(byte[] data, int width, int height, long timestamp)
        {
            Data = data;
            Width = width;
            Height = height;
            Stride = width * 4;
            Timestamp = timestamp;
        }

        public void Dispose() { }
    }

    public sealed class DesktopCapture : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly IDXGIOutputDuplication _duplication;

        private ID3D11Texture2D? _stagingTexture;
        private int _stagingW, _stagingH;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public DesktopCapture(int adapterIndex = 0, int outputIndex = 0)
        {
            // ── 1. D3D11 Device ──────────────────────────────────────
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
            };

            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out _device!,
                out _,
                out _context!).CheckError();

            // ── 2. Adapter / Output ───────────────────────────────────
            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            adapter.EnumOutputs((uint)outputIndex, out IDXGIOutput output).CheckError();
            using (output)
            {
                var desc = output.Description;
                Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

                // ── 3. OutputDuplication ─────────────────────────────
                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device);
            }

            EnsureStagingTexture(Width, Height);
        }

        // ── 捕获一帧 ─────────────────────────────────────────────────

        public CapturedFrame? TryCapture(int timeoutMs = 100)
        {
            var hr = _duplication.AcquireNextFrame(
                (uint)timeoutMs,
                out OutduplFrameInfo frameInfo,
                out IDXGIResource? desktopResource);

            if (hr == Vortice.DXGI.ResultCode.WaitTimeout)
                return null;

            hr.CheckError();

            // 即使 AccumulatedFrames == 0，也复制当前桌面纹理，保证连接后有首帧。
            using (desktopResource)
            {
                using var gpuTexture = desktopResource!.QueryInterface<ID3D11Texture2D>();
                var texDesc = gpuTexture.Description;

                int w = (int)texDesc.Width;
                int h = (int)texDesc.Height;

                EnsureStagingTexture(w, h);
                _context.CopyResource(_stagingTexture!, gpuTexture);

                var mapped = _context.Map(
                    _stagingTexture!,
                    0,
                    Vortice.Direct3D11.MapMode.Read,
                    Vortice.Direct3D11.MapFlags.None);

                try
                {
                    return CopyMappedData(mapped, w, h, frameInfo.LastPresentTime);
                }
                finally
                {
                    _context.Unmap(_stagingTexture!, 0);
                    _duplication.ReleaseFrame();
                }
            }
        }

        // ── 工具方法 ─────────────────────────────────────────────────

        private void EnsureStagingTexture(int w, int h)
        {
            if (_stagingTexture != null && _stagingW == w && _stagingH == h)
                return;

            _stagingTexture?.Dispose();

            // CS0117 修复: 字段名是 CPUAccessFlags（全大写 CPU），不是 CpuAccessFlags
            var desc = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,   // ← 全大写 CPU
                MiscFlags = ResourceOptionFlags.None,
            };

            _stagingTexture = _device.CreateTexture2D(desc);
            _stagingW = w;
            _stagingH = h;
        }

        private static unsafe CapturedFrame CopyMappedData(
            MappedSubresource mapped, int w, int h, long timestamp)
        {
            int dstStride = w * 4;
            var data = new byte[dstStride * h];

            byte* src = (byte*)mapped.DataPointer;
            fixed (byte* dst = data)
            {
                for (int row = 0; row < h; row++)
                {
                    Buffer.MemoryCopy(
                        src + (long)row * mapped.RowPitch,
                        dst + (long)row * dstStride,
                        dstStride,
                        dstStride);
                }
            }

            return new CapturedFrame(data, w, h, timestamp);
        }

        // ── IDisposable ──────────────────────────────────────────────

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stagingTexture?.Dispose();
            _duplication.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
//  保存截图为 PNG / BMP（仅需系统内置库，无额外依赖）
// ═══════════════════════════════════════════════════════════════════
namespace DesktopDuplication
{
    using System.IO;

    public static class FrameSaver
    {
        /// <summary>
        /// 将 CapturedFrame (BGRA) 保存为 PNG 文件。
        /// 使用纯托管代码写 PNG，无需 System.Drawing / SkiaSharp。
        /// </summary>
        public static void SavePng(CapturedFrame frame, string path)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            WritePng(bw, frame.Data, frame.Width, frame.Height);
        }

        /// <summary>
        /// 将 CapturedFrame (BGRA) 保存为 BMP 文件（无压缩，写法最简单）。
        /// 打开最快，适合调试确认画面是否正确。
        /// </summary>
        public static void SaveBmp(CapturedFrame frame, string path)
        {
            int w = frame.Width, h = frame.Height;
            // BMP 行需要 4 字节对齐（BGRA 恰好是 w*4，天然对齐）
            int rowBytes = w * 4;
            int imageSize = rowBytes * h;
            int fileSize = 54 + imageSize;

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // ── BITMAPFILEHEADER (14 bytes) ──
            bw.Write((ushort)0x4D42); // 'BM'
            bw.Write(fileSize);
            bw.Write(0);              // reserved
            bw.Write(54);             // pixel data offset

            // ── BITMAPINFOHEADER (40 bytes) ──
            bw.Write(40);             // header size
            bw.Write(w);
            bw.Write(-h);             // 负值 = 从上到下（top-down）
            bw.Write((ushort)1);      // color planes
            bw.Write((ushort)32);     // bits per pixel
            bw.Write(0);              // BI_RGB (no compression)
            bw.Write(imageSize);
            bw.Write(2835); bw.Write(2835); // 72 DPI
            bw.Write(0); bw.Write(0);       // color table

            // ── Pixel data (BGRA, top-down) ──
            bw.Write(frame.Data);
        }

        // ─── 纯托管 PNG 写入 ──────────────────────────────────────
        // 格式：PNG signature + IHDR + IDAT(deflate) + IEND
        // BGRA → RGBA 转换在此完成

        private static void WritePng(BinaryWriter bw, byte[] bgra, int w, int h)
        {
            // PNG signature
            bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            // IHDR
            WriteChunk(bw, "IHDR", ihdr =>
            {
                WriteUInt32BE(ihdr, (uint)w);
                WriteUInt32BE(ihdr, (uint)h);
                ihdr.WriteByte(8);  // bit depth
                ihdr.WriteByte(6);  // color type: RGBA
                ihdr.WriteByte(0);  // compression method
                ihdr.WriteByte(0);  // filter method
                ihdr.WriteByte(0);  // interlace method
            });

            // IDAT — raw image data with filter byte 0 per row, then deflate
            byte[] raw = BuildFilteredRows(bgra, w, h);
            byte[] compressed = DeflateCompress(raw);
            WriteChunk(bw, "IDAT", idat => idat.Write(compressed));

            // IEND
            WriteChunk(bw, "IEND", _ => { });
        }

        private static byte[] BuildFilteredRows(byte[] bgra, int w, int h)
        {
            // Each row: 1 filter byte (0=None) + w*4 RGBA bytes
            var ms = new MemoryStream(h * (1 + w * 4));
            for (int row = 0; row < h; row++)
            {
                ms.WriteByte(0); // filter type None
                int rowStart = row * w * 4;
                for (int col = 0; col < w; col++)
                {
                    int i = rowStart + col * 4;
                    ms.WriteByte(bgra[i + 2]); // R  (from B G R A)
                    ms.WriteByte(bgra[i + 1]); // G
                    ms.WriteByte(bgra[i + 0]); // B
                    ms.WriteByte(bgra[i + 3]); // A
                }
            }
            return ms.ToArray();
        }

        private static byte[] DeflateCompress(byte[] data)
        {
            // zlib = CMF + FLG + deflate stream + Adler32
            using var deflateMs = new MemoryStream();
            using (var ds = new System.IO.Compression.DeflateStream(
                deflateMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                ds.Write(data, 0, data.Length);
            }
            byte[] deflated = deflateMs.ToArray();

            // Adler32
            uint a = 1, b = 0;
            foreach (byte bt in data) { a = (a + bt) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;

            var result = new MemoryStream(2 + deflated.Length + 4);
            result.WriteByte(0x78); // CMF: deflate, window=32K
            result.WriteByte(0x01); // FLG: fastest (0x78*256+FLG divisible by 31 → 0x7801=30721, 30721%31=0 ✓)
            result.Write(deflated);
            result.WriteByte((byte)(adler >> 24));
            result.WriteByte((byte)(adler >> 16));
            result.WriteByte((byte)(adler >> 8));
            result.WriteByte((byte)adler);
            return result.ToArray();
        }

        private static void WriteChunk(BinaryWriter bw, string type, Action<MemoryStream> writeData)
        {
            var ms = new MemoryStream();
            writeData(ms);
            byte[] data = ms.ToArray();
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

            WriteUInt32BE(bw.BaseStream, (uint)data.Length);
            bw.BaseStream.Write(typeBytes);
            bw.BaseStream.Write(data);

            // CRC32 over type + data
            uint crc = Crc32(typeBytes, 0xFFFFFFFF);
            crc = Crc32(data, crc) ^ 0xFFFFFFFF;
            WriteUInt32BE(bw.BaseStream, crc);
        }

        private static void WriteUInt32BE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static uint Crc32(byte[] data, uint crc)
        {
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc;
        }
    }
}
