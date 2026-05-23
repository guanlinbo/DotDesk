using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopDuplication
{
    public delegate void EncodedDataCallback(ReadOnlySpan<byte> data, bool isKeyFrame, long timestamp);

    public interface IVideoEncoder : IDisposable
    {
        void Initialize(int width, int height, int fps, int bitrateBps);
        void Encode(ReadOnlySpan<byte> bgraData, int width, int height, int stride, long timestamp);
        event EncodedDataCallback? OnEncoded;
    }

    public sealed class CapturePipeline : IAsyncDisposable
    {
        private readonly IVideoEncoder _encoder;
        private readonly int _targetFps;
        private readonly int _adapterIndex;
        private readonly int _outputIndex;

        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        public CapturePipeline(IVideoEncoder encoder, int fps = 30, int adapterIndex = 0, int outputIndex = 0)
        {
            _encoder = encoder;
            _targetFps = fps;
            _adapterIndex = adapterIndex;
            _outputIndex = outputIndex;
        }

        public void Start()
        {
            if (_loopTask is { IsCompleted: false })
                throw new InvalidOperationException("Pipeline already running.");
            _cts = new CancellationTokenSource();
            _loopTask = Task.Factory.StartNew(
                () => CaptureLoop(_cts.Token),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            if (_loopTask != null)
                await _loopTask.ConfigureAwait(false);
        }

        private void CaptureLoop(CancellationToken ct)
        {
            using var capture = new DesktopCapture(_adapterIndex, _outputIndex);
            _encoder.Initialize(capture.Width, capture.Height, _targetFps, bitrateBps: 4_000_000);

            long frameIntervalTicks = Stopwatch.Frequency / _targetFps;
            long nextTick = Stopwatch.GetTimestamp();

            while (!ct.IsCancellationRequested)
            {
                long now = Stopwatch.GetTimestamp();
                if (now < nextTick)
                {
                    double remainMs = (nextTick - now) * 1000.0 / Stopwatch.Frequency;
                    if (remainMs > 1.5) Thread.Sleep((int)(remainMs - 1));
                    while (Stopwatch.GetTimestamp() < nextTick) { }
                }
                nextTick += frameIntervalTicks;

                using var frame = capture.TryCapture(timeoutMs: 50);
                if (frame == null) continue;
                _encoder.Encode(frame.Data, frame.Width, frame.Height, frame.Stride, frame.Timestamp);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _cts?.Dispose();
        }
    }

 

    // ── Stub 编码器（Pipeline 模式时使用）────────────────────────────
    internal sealed class StubEncoder : IVideoEncoder
    {
#pragma warning disable CS0067
        public event EncodedDataCallback? OnEncoded;
#pragma warning restore CS0067

        private int _frameCount;
        private long _startTick = Stopwatch.GetTimestamp();

        public void Initialize(int width, int height, int fps, int bitrateBps)
            => Console.WriteLine($"[Encoder] {width}x{height} {fps}fps {bitrateBps / 1000}kbps");

        public void Encode(ReadOnlySpan<byte> bgraData, int width, int height, int stride, long timestamp)
        {
            if (++_frameCount % 60 == 0)
            {
                double e = (Stopwatch.GetTimestamp() - _startTick) / (double)Stopwatch.Frequency;
                Console.WriteLine($"[Encoder] {_frameCount / e:F1} fps  #{_frameCount}  {bgraData.Length / 1024}KB");
            }
        }

        public void Dispose() { }
    }
}