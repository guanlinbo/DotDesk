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
        private const int MaxStreamWidth = 960;

        private readonly WebRtcPusher _pusher;
        private Thread? _thread;
        private volatile bool _running;
        private bool _disposed;

        public event Action<string>? OnLog;
        public event Action<float>? OnFpsUpdate;
        public event Action<byte[], int, int>? OnFrameCaptured;

        public CaptureAndPushPipeline(WebRtcPusher pusher)
        {
            _pusher = pusher;
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
                    capture = new DesktopCapture(0, 0);
                    Log($"截图分辨率: {capture.Width}x{capture.Height}");

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
                        using var frame = capture.TryCapture(50);
                        if (frame == null) continue;

                        // 推流前降到更适合公网/TURN 的尺寸，降低 RTP 丢包导致的花屏。
                        byte[] pushData = PrepareStreamFrame(
                            frame.Data,
                            frame.Width,
                            frame.Height,
                            out int pw,
                            out int ph);

                        _pusher.PushFrame(pushData, pw, ph);

                        // 本地预览
                        OnFrameCaptured?.Invoke(frame.Data, frame.Width, frame.Height);

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
                    // AccessLost / 分辨率变化 → 重建捕获器
                    Log($"捕获异常（将重建）: {ex.Message}");
                    AppLogger.Log("Pipeline", $"捕获异常（将重建）: {ex.Message}");
                    Thread.Sleep(300);  // 等待系统稳定后重建
                }
                finally
                {
                    capture?.Dispose();
                }
            }
        }

        private void Log(string msg) => OnLog?.Invoke($"[Pipeline] {msg}");

        private static byte[] PrepareStreamFrame(byte[] bgra, int width, int height, out int ostW, out int ostH)
        {
            ostW = width;
            ostH = height;

            if (width > MaxStreamWidth)
            {
                double scale = MaxStreamWidth / (double)width;
                ostW = MaxStreamWidth;
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
