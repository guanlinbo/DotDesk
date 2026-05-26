using DesktopDuplication;
using System;
using System.Diagnostics;
using System.Threading;
using DotDesk.Client.Native;
using DotDesk.Core.Logging;

namespace DotDesk.Client.Network
{
    public sealed class CaptureAndPushPipeline : IDisposable
    {
        private readonly WebRtcPusher _pusher;
        private Thread? _thread;
        private volatile bool _running;
        private bool _disposed;
        private int _accessDeniedCount;
        private int _captureAdapterIndex = 0;
        private int _captureOutputIndex = 0;
        private byte[]? _lastPushFrame;
        private int _lastPushWidth;
        private int _lastPushHeight;
        private long _lastPushCaptureTimestampMs;
        private long _lastPushFrameId;
        private long _lastCaptureColorProbeTick;
        private long _lastCaptureFreshnessLogTick;
        private long _lastSuccessfulCaptureTick;
        private long _lastDxgiGdiCompareTick;
        private readonly bool _forceGdiCapture =
            string.Equals(Environment.GetEnvironmentVariable("DOTDESK_CAPTURE_MODE"), "gdi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("DOTDESK_FORCE_GDI_CAPTURE"), "1", StringComparison.OrdinalIgnoreCase);
        private readonly object _forceLock = new();
        private string? _forceCaptureReason;

        public event Action<string>? OnLog;
        public event Action<float>? OnFpsUpdate;
        public event Action<byte[], int, int>? OnFrameCaptured;

        public CaptureAndPushPipeline(WebRtcPusher pusher)
        {
            _pusher = pusher;
            _pusher.OnForceCaptureOnce += RequestForceCaptureOnce;
        }

        public void Start(int fps = 15)
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(() => CaptureLoop(fps))
            {
                IsBackground = true,
                Name = "CaptureAndPush"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(2000);
        }

        private void CaptureLoop(int fps)
        {
            long interval = Stopwatch.Frequency / fps;

            while (_running)
            {
                DesktopCapture? capture = null;
                try
                {
                    // ── 初始化捕获器 ──────────────────────────────────
                    // 每次重建前重新探测最佳显示器（ToDesk 关闭后物理显示器索引可能变化）
                    (_captureAdapterIndex, _captureOutputIndex) = DesktopCapture.FindBestDisplay();
                    capture = new DesktopCapture(_captureAdapterIndex, _captureOutputIndex);
                    Log($"截图分辨率: {capture.Width}x{capture.Height}");
                    if (_forceGdiCapture)
                        LogCapture("GDI compatible capture mode enabled");

                    // 预热：轻推鼠标触发 DWM 刷新
                    Win32.GetCursorPos(out var pos);
                    Win32.SetCursorPos(pos.X + 1, pos.Y);
                    Thread.Sleep(80);
                    Win32.SetCursorPos(pos.X, pos.Y);

                    long next = Stopwatch.GetTimestamp();
                    long statTick = next;
                    int frmCount = 0;

                    // ── 捕获循环 ──────────────────────────────────────
                    while (_running)
                    {
                        if (TryConsumeForceCaptureReason(out var forceReason))
                        {
                            PushForcedFrame(capture, forceReason);
                            next = Stopwatch.GetTimestamp() + interval;
                            continue;
                        }

                        // 精确帧率节拍
                        long now = Stopwatch.GetTimestamp();
                        if (now < next)
                        {
                            double ms = (next - now) * 1000.0 / Stopwatch.Frequency;
                            if (ms > 1.5) Thread.Sleep((int)(ms - 1));
                            while (Stopwatch.GetTimestamp() < next) { }
                        }
                        next += interval;

                        // 截图（AccessLost 会抛异常，跳出内层循环重建捕获器）
                        using var frame = _forceGdiCapture
                            ? capture.CaptureCurrentFrameGdi()
                            : capture.TryCapture(20);
                        if (frame == null)
                        {
                            if (_pusher.ConsumeImmediateFrameRequest() && _lastPushFrame != null)
                            {
                                // DXGI 没有桌面变化时不会吐新帧；关键帧请求到来时重推上一帧，
                                // 让编码器立刻输出 IDR，避免接收端一直等关键帧。
                                LogCapture($"force capture once reason=viewer-request");
                                _pusher.PushFrame(_lastPushFrame, _lastPushWidth, _lastPushHeight, force: true, _lastPushCaptureTimestampMs, _lastPushFrameId);
                            }
                            continue;
                        }

                        if (!_forceGdiCapture)
                            CompareDxgiWithGdi(capture, frame);

                        PushCapturedFrame(frame, force: false, source: _forceGdiCapture ? "gdi-compatible" : "dxgi");

                        // 帧率统计
                        frmCount++;
                        double elapsed = (Stopwatch.GetTimestamp() - statTick)
                                         / (double)Stopwatch.Frequency;
                        if (elapsed >= 1.0)
                        {
                            OnFpsUpdate?.Invoke(frmCount / (float)elapsed);
                            frmCount = 0;
                            statTick = Stopwatch.GetTimestamp();
                        }
                    }
                }
                catch (Exception ex) when (_running)
                {
                    // E_ACCESSDENIED (0x80070005)：屏幕被锁定/UAC弹窗/桌面切换
                    //   → DXGI Desktop Duplication 访问被拒，重建捕获器无效
                    //   → 必须等待桌面恢复可访问（通常几秒内），延长等待时间
                    // DXGI_ERROR_ACCESS_LOST (0x887A0026)：GPU/显示器驱动重置
                    //   → 重建捕获器即可恢复
                    bool isAccessDenied = ex.HResult == unchecked((int)0x80070005);
                    int waitMs = isAccessDenied ? (_accessDeniedCount < 10 ? 500 : 1000) : 300;

                    string reason = isAccessDenied ? "屏幕锁定/UAC弹窗（E_ACCESSDENIED）" : ex.Message;
                    // AccessDenied 持续时打印频率限制（避免每 300ms 刷一行日志）
                    if (!isAccessDenied || _accessDeniedCount++ % 5 == 0)
                    {
                        Log($"捕获异常（将重建）: {reason}");
                        AppLogger.Log("Pipeline", $"捕获异常（将重建）: {reason}");
                    }
                    Thread.Sleep(waitMs);
                }
                finally
                {
                    capture?.Dispose();
                }
            }
        }

        private void Log(string msg) => OnLog?.Invoke($"[Pipeline] {msg}");
        private void LogCapture(string msg)
        {
            AppLogger.Log("Capture", msg);
            OnLog?.Invoke($"[Capture] {msg}");
        }

        private void RequestForceCaptureOnce(string reason)
        {
            lock (_forceLock)
                _forceCaptureReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
        }

        private bool TryConsumeForceCaptureReason(out string reason)
        {
            lock (_forceLock)
            {
                reason = _forceCaptureReason ?? "";
                _forceCaptureReason = null;
                return reason.Length > 0;
            }
        }

        private void PushForcedFrame(DesktopCapture capture, string reason)
        {
            LogCapture($"force capture once reason={reason}");
            var sw = Stopwatch.StartNew();
            if (_lastPushFrame != null)
            {
                LogCapture($"first frame captured cost={sw.ElapsedMilliseconds}ms source=lastCapturedFrame");
                if (IsFirstFrameReason(reason))
                {
                    _pusher.MarkFirstFrameCaptureDone();
                    _pusher.PushFirstFrameImmediately(_lastPushFrame, _lastPushWidth, _lastPushHeight, _lastPushCaptureTimestampMs, _lastPushFrameId);
                }
                else
                {
                    _pusher.PushFrame(_lastPushFrame, _lastPushWidth, _lastPushHeight, force: true, _lastPushCaptureTimestampMs, _lastPushFrameId);
                }
                return;
            }

            CapturedFrame? frame = null;
            try
            {
                frame = capture.TryCapture(1);
                if (frame == null)
                {
                    Win32.GetCursorPos(out var pos);
                    Win32.SetCursorPos(pos.X + 1, pos.Y);
                    Thread.Sleep(20);
                    Win32.SetCursorPos(pos.X, pos.Y);
                    frame = capture.TryCapture(120);
                }

                if (frame == null)
                {
                    LogCapture($"first frame capture timeout cost={sw.ElapsedMilliseconds}ms");
                    return;
                }

                int maxStreamWidth = _pusher.RecommendedMaxStreamWidth;
                byte[] pushData = PrepareStreamFrame(
                    frame.Data,
                    frame.Width,
                    frame.Height,
                    maxStreamWidth,
                    out int pw,
                    out int ph);

                _lastPushFrame = pushData;
                _lastPushWidth = pw;
                _lastPushHeight = ph;
                _lastPushCaptureTimestampMs = MonoNowMs();
                long frameId = _pusher.NextFrameId();
                _lastPushFrameId = frameId;
                if (pw >= 200 && ph >= 20)
                    OverlayTimestamp(pushData, pw, ph, frameId, _lastPushCaptureTimestampMs);
                Interlocked.Exchange(ref _lastSuccessfulCaptureTick, Stopwatch.GetTimestamp());
                LogCaptureFreshness(frame.Timestamp, force: true);
                LogCaptureColorProbe(pushData, pw, ph, "stream-bgra");
                LogCapture($"first frame captured cost={sw.ElapsedMilliseconds}ms");
                if (IsFirstFrameReason(reason))
                {
                    _pusher.MarkFirstFrameCaptureDone();
                    _pusher.PushFirstFrameImmediately(pushData, pw, ph, _lastPushCaptureTimestampMs, frameId);
                }
                else
                {
                    _pusher.PushFrame(pushData, pw, ph, force: true, _lastPushCaptureTimestampMs, frameId);
                }
                OnFrameCaptured?.Invoke(frame.Data, frame.Width, frame.Height);
            }
            finally
            {
                frame?.Dispose();
            }
        }

        private static bool IsFirstFrameReason(string reason) =>
            string.Equals(reason, "first-frame", StringComparison.OrdinalIgnoreCase);

        private void PushCapturedFrame(CapturedFrame frame, bool force, string source)
        {
            // 推流前降到更适合公网/TURN 的尺寸，降低 RTP 丢包导致的花屏。
            int maxStreamWidth = _pusher.RecommendedMaxStreamWidth;
            byte[] pushData = PrepareStreamFrame(
                frame.Data,
                frame.Width,
                frame.Height,
                maxStreamWidth,
                out int pw,
                out int ph);

            long captureTimestampMs = MonoNowMs();
            long frameId = _pusher.NextFrameId();
            LogCaptureFreshness(frame.Timestamp);

            // 水印：叠加 frameId + 被控端时间，用于肉眼验证真实端到端延迟。
            if (pw >= 200 && ph >= 20)
                OverlayTimestamp(pushData, pw, ph, frameId, captureTimestampMs);

            _pusher.PushFrame(pushData, pw, ph, force, captureTimestampMs, frameId);
            LogCaptureColorProbe(pushData, pw, ph,
                source is "gdi-fallback" or "gdi-compatible" ? "gdi-bgra" : "stream-bgra");
            _lastPushFrame = pushData;
            _lastPushWidth = pw;
            _lastPushHeight = ph;
            _lastPushCaptureTimestampMs = captureTimestampMs;
            _lastPushFrameId = frameId;
            Interlocked.Exchange(ref _lastSuccessfulCaptureTick, Stopwatch.GetTimestamp());

            // 本地预览
            OnFrameCaptured?.Invoke(frame.Data, frame.Width, frame.Height);
        }

        private void LogCaptureColorProbe(byte[] bgra, int width, int height, string stage)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastCaptureColorProbeTick != 0
                && now - _lastCaptureColorProbeTick < Stopwatch.Frequency)
                return;
            _lastCaptureColorProbeTick = now;
            if (bgra.Length < width * height * 4 || width <= 0 || height <= 0) return;
            int x = width / 2;
            int y = height / 2;
            int i = (y * width + x) * 4;
            AppLogger.Log("Color", $"capture {stage} center BGRA b={bgra[i]} g={bgra[i + 1]} r={bgra[i + 2]} a={bgra[i + 3]} size={width}x{height}");
        }

        private void LogCaptureFreshness(long presentTimestamp, bool force = false)
        {
            if (presentTimestamp <= 0)
                return;

            long now = Stopwatch.GetTimestamp();
            if (!force
                && _lastCaptureFreshnessLogTick != 0
                && now - _lastCaptureFreshnessLogTick < Stopwatch.Frequency)
                return;

            _lastCaptureFreshnessLogTick = now;
            long ageMs = Math.Max(0, (now - presentTimestamp) * 1000 / Stopwatch.Frequency);
            AppLogger.Log("Capture", $"dxgi present age={ageMs}ms");
        }

        private void CompareDxgiWithGdi(DesktopCapture capture, CapturedFrame dxgiFrame)
        {
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _lastDxgiGdiCompareTick);
            if (last != 0 && now - last < Stopwatch.Frequency)
                return;
            Interlocked.Exchange(ref _lastDxgiGdiCompareTick, now);

            using var gdiFrame = capture.CaptureCurrentFrameGdi();
            if (gdiFrame == null || gdiFrame.Width != dxgiFrame.Width || gdiFrame.Height != dxgiFrame.Height)
                return;

            long sum = 0;
            int samples = 0;
            for (int gy = 1; gy <= 4; gy++)
            {
                int y = dxgiFrame.Height * gy / 5;
                for (int gx = 1; gx <= 4; gx++)
                {
                    int x = dxgiFrame.Width * gx / 5;
                    int i = (y * dxgiFrame.Width + x) * 4;
                    int db = dxgiFrame.Data[i] - gdiFrame.Data[i];
                    int dg = dxgiFrame.Data[i + 1] - gdiFrame.Data[i + 1];
                    int dr = dxgiFrame.Data[i + 2] - gdiFrame.Data[i + 2];
                    sum += Math.Abs(db) + Math.Abs(dg) + Math.Abs(dr);
                    samples += 3;
                }
            }

            int center = ((dxgiFrame.Height / 2) * dxgiFrame.Width + dxgiFrame.Width / 2) * 4;
            long avgDiff = samples > 0 ? sum / samples : 0;
            AppLogger.Log("Capture",
                $"dxgi/gdi compare avgDiff={avgDiff} " +
                $"dxgiCenter={dxgiFrame.Data[center]},{dxgiFrame.Data[center + 1]},{dxgiFrame.Data[center + 2]} " +
                $"gdiCenter={gdiFrame.Data[center]},{gdiFrame.Data[center + 1]},{gdiFrame.Data[center + 2]}");
        }

        private static byte[] PrepareStreamFrame(byte[] bgra, int width, int height, int maxStreamWidth, out int ostW, out int ostH)
        {
            ostW = width;
            ostH = height;

            if (maxStreamWidth <= 0) maxStreamWidth = 960;

            if (width > maxStreamWidth)
            {
                double scale = maxStreamWidth / (double)width;
                ostW = maxStreamWidth;
                ostH = Math.Max(2, (int)Math.Round(height * scale));
            }

            ostW &= ~1;
            ostH &= ~1;

            if (ostW == width && ostH == height)
                return bgra;

            var resized = new byte[ostW * ostH * 4];

            for (int y = 0; y < ostH; y++)
            {
                int srcY = y * height / ostH;
                for (int x = 0; x < ostW; x++)
                {
                    int srcX = x * width / ostW;
                    int src = (srcY * width + srcX) * 4;
                    int dst = (y * ostW + x) * 4;

                    resized[dst] = bgra[src];
                    resized[dst + 1] = bgra[src + 1];
                    resized[dst + 2] = bgra[src + 2];
                    resized[dst + 3] = bgra[src + 3];
                }
            }

            return resized;
        }

        private static long MonoNowMs() =>
            Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pusher.OnForceCaptureOnce -= RequestForceCaptureOnce;
            Stop();
        }

        // ── 水印：叠加时间戳到 BGRA 帧左上角 ────────────────────────────────
        // 纯 C# 实现，不依赖 GDI/WinForms/FFmpeg
        // 字体：5×7 像素点阵，每字符 6 像素宽（含间距），背景黑色半透明
        private static void OverlayTimestamp(byte[] bgra, int width, int height, long frameId, long monoMs)
        {
            string flash = (monoMs / 500) % 2 == 0 ? "WHITE" : "BLACK";
            string text = $"{frameId:000000} {monoMs} {flash}";

            const int charW = 6;    // 每字符宽度（含右间距）
            const int charH = 9;    // 字符高度
            const int padX = 4;    // 左边距
            const int padY = 4;    // 上边距
            int textW = text.Length * charW;
            int boxW = textW + padX * 2;
            int boxH = charH + padY * 2;

            if (boxW > width || boxH > height) return;

            // 画背景（半透明黑色）
            for (int y = 0; y < boxH; y++)
            {
                for (int x = 0; x < boxW; x++)
                {
                    int idx = (y * width + x) * 4;
                    bgra[idx] = (byte)(bgra[idx] / 2);       // B
                    bgra[idx + 1] = (byte)(bgra[idx + 1] / 2);       // G
                    bgra[idx + 2] = (byte)(bgra[idx + 2] / 2);       // R
                    // A 不变
                }
            }

            // 画字符
            for (int ci = 0; ci < text.Length; ci++)
            {
                char c = text[ci];
                int cx = padX + ci * charW;
                int cy = padY;
                DrawChar5x7(bgra, width, height, c, cx, cy);
            }
        }

        // 5×7 点阵字体，支持 0-9、:、. 和空格；水印保持数字化，避免字体依赖。
        private static readonly byte[,] _font5x7 = new byte[,]
        {
            // 0
            {0,1,1,1,0, 1,0,0,0,1, 1,0,0,1,1, 1,0,1,0,1, 1,1,0,0,1, 1,0,0,0,1, 0,1,1,1,0},
            // 1
            {0,0,1,0,0, 0,1,1,0,0, 0,0,1,0,0, 0,0,1,0,0, 0,0,1,0,0, 0,0,1,0,0, 0,1,1,1,0},
            // 2
            {0,1,1,1,0, 1,0,0,0,1, 0,0,0,0,1, 0,0,0,1,0, 0,0,1,0,0, 0,1,0,0,0, 1,1,1,1,1},
            // 3
            {1,1,1,1,0, 0,0,0,0,1, 0,0,0,0,1, 0,1,1,1,0, 0,0,0,0,1, 0,0,0,0,1, 1,1,1,1,0},
            // 4
            {0,0,0,1,0, 0,0,1,1,0, 0,1,0,1,0, 1,0,0,1,0, 1,1,1,1,1, 0,0,0,1,0, 0,0,0,1,0},
            // 5
            {1,1,1,1,1, 1,0,0,0,0, 1,0,0,0,0, 1,1,1,1,0, 0,0,0,0,1, 0,0,0,0,1, 1,1,1,1,0},
            // 6
            {0,1,1,1,0, 1,0,0,0,0, 1,0,0,0,0, 1,1,1,1,0, 1,0,0,0,1, 1,0,0,0,1, 0,1,1,1,0},
            // 7
            {1,1,1,1,1, 0,0,0,0,1, 0,0,0,1,0, 0,0,1,0,0, 0,1,0,0,0, 0,1,0,0,0, 0,1,0,0,0},
            // 8
            {0,1,1,1,0, 1,0,0,0,1, 1,0,0,0,1, 0,1,1,1,0, 1,0,0,0,1, 1,0,0,0,1, 0,1,1,1,0},
            // 9
            {0,1,1,1,0, 1,0,0,0,1, 1,0,0,0,1, 0,1,1,1,1, 0,0,0,0,1, 0,0,0,0,1, 0,1,1,1,0},
            // : (index 10)
            {0,0,0,0,0, 0,0,1,0,0, 0,0,1,0,0, 0,0,0,0,0, 0,0,1,0,0, 0,0,1,0,0, 0,0,0,0,0},
            // . (index 11)
            {0,0,0,0,0, 0,0,0,0,0, 0,0,0,0,0, 0,0,0,0,0, 0,0,0,0,0, 0,0,1,0,0, 0,0,1,0,0},
        };

        private static void DrawChar5x7(byte[] bgra, int width, int height, char c, int startX, int startY)
        {
            if (TryGetGlyph(c, out var glyph))
            {
                DrawGlyph(bgra, width, height, glyph, startX, startY);
                return;
            }

            int fontIdx = c switch
            {
                >= '0' and <= '9' => c - '0',
                ':' => 10,
                '.' => 11,
                ' ' => -2,
                _ => -1
            };
            if (fontIdx == -2) return;
            if (fontIdx < 0) return;

            for (int row = 0; row < 7; row++)
            {
                int py = startY + row;
                if (py >= height) break;
                for (int col = 0; col < 5; col++)
                {
                    int px = startX + col;
                    if (px >= width) break;
                    if (_font5x7[fontIdx, row * 5 + col] == 0) continue;
                    int idx = (py * width + px) * 4;
                    // 白色字体
                    bgra[idx] = 255; // B
                    bgra[idx + 1] = 255; // G
                    bgra[idx + 2] = 255; // R
                }
            }
        }

        private static bool TryGetGlyph(char c, out string[] glyph)
        {
            glyph = c switch
            {
                'A' => new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
                'B' => new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
                'C' => new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
                'E' => new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
                'H' => new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
                'I' => new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" },
                'K' => new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
                'L' => new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
                'T' => new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
                'W' => new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" },
                '-' => new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
                _ => Array.Empty<string>()
            };
            return glyph.Length > 0;
        }

        private static void DrawGlyph(byte[] bgra, int width, int height, string[] glyph, int startX, int startY)
        {
            for (int row = 0; row < glyph.Length; row++)
            {
                int py = startY + row;
                if (py >= height) break;
                for (int col = 0; col < glyph[row].Length; col++)
                {
                    int px = startX + col;
                    if (px >= width) break;
                    if (glyph[row][col] != '1') continue;
                    int idx = (py * width + px) * 4;
                    bgra[idx] = 255;
                    bgra[idx + 1] = 255;
                    bgra[idx + 2] = 255;
                }
            }
        }
    }
}
