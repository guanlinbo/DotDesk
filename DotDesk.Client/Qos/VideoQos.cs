// VideoQos.cs  —  DotDesk 动态 QoS 模块
// 实现 6 档精确控制策略 + 交互/静态双模式
// 命名空间：DotDesk.Client.Qos

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using DotDesk.Core.Logging;

namespace DotDesk.Client.Qos
{
    // ═══════════════════════════════════════════════════════════════
    //  EncoderSettings — 编码参数快照
    // ═══════════════════════════════════════════════════════════════
    public sealed record EncoderSettings(
        int Bitrate,      // bps
        int Fps,
        float Scale,        // 1.0 = 全分辨率
        int GopSize,
        int MaxQueueDepth // 发送队列上限
    )
    {
        public static EncoderSettings Default => new(
            Bitrate: 4_000_000, Fps: 30, Scale: 1.0f, GopSize: 90, MaxQueueDepth: 4
        );

        public override string ToString() =>
            $"bitrate={Bitrate / 1000}kbps fps={Fps} scale={Scale:P0} " +
            $"gop={GopSize} queue≤{MaxQueueDepth}";
    }

    // ═══════════════════════════════════════════════════════════════
    //  NetworkQuality — 5 档网络质量
    // ═══════════════════════════════════════════════════════════════
    public enum NetworkQuality
    {
        Excellent,  // RTT<60  loss<2%  queue≤1
        Good,       // RTT<120 loss<5%
        Poor,       // RTT>150 loss>5%  queue>3
        Bad,        // RTT>250 loss>10%
        Unknown
    }

    // ═══════════════════════════════════════════════════════════════
    //  NetworkQualityEstimator — RTT / 丢包 / 队列 统计
    // ═══════════════════════════════════════════════════════════════
    public sealed class NetworkQualityEstimator
    {
        private const int WinSize = 20;

        private readonly Queue<float> _rttSamples = new();
        private readonly Queue<float> _lossSamples = new();
        private readonly Queue<long> _frameTicks = new();
        private readonly object _lock = new();

        public float RttMs { get; private set; }
        public float LossRate { get; private set; }
        public float ActualFps { get; private set; }
        public int QueueDepth { get; set; }

        public NetworkQuality Quality
        {
            get
            {
                float rtt = RttMs;
                float loss = LossRate;
                int q = QueueDepth;

                if (rtt > 250 || loss > 0.10f) return NetworkQuality.Bad;
                if (rtt > 150 || loss > 0.05f || q > 3) return NetworkQuality.Poor;
                if (rtt < 60 && loss < 0.02f && q <= 1) return NetworkQuality.Excellent;
                if (rtt <= 120 && loss <= 0.05f) return NetworkQuality.Good;
                return NetworkQuality.Good;
            }
        }

        public void RecordRtt(float rttMs)
        {
            lock (_lock) { Push(_rttSamples, rttMs); }
            RttMs = Avg(_rttSamples);
        }

        public void RecordLoss(int sent, int lost)
        {
            if (sent <= 0) return;
            float r = (float)lost / sent;
            lock (_lock) { Push(_lossSamples, r); }
            LossRate = Avg(_lossSamples);
        }

        public void RecordFrame()
        {
            long now = Stopwatch.GetTimestamp();
            lock (_lock)
            {
                Push(_frameTicks, now);
                if (_frameTicks.Count >= 2)
                {
                    long[] a = _frameTicks.ToArray();
                    double span = (a[^1] - a[0]) / (double)Stopwatch.Frequency;
                    ActualFps = span > 0 ? (float)((a.Length - 1) / span) : 0;
                }
            }
        }

        private static void Push<T>(Queue<T> q, T v)
        {
            q.Enqueue(v);
            while (q.Count > WinSize) q.Dequeue();
        }

        private static float Avg(Queue<float> q)
        {
            if (q.Count == 0) return 0;
            float s = 0; foreach (float v in q) s += v;
            return s / q.Count;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  InputActivityTracker — 鼠标键盘活动检测
    // ═══════════════════════════════════════════════════════════════
    public sealed class InputActivityTracker
    {
        // 500ms 内有输入 → 交互模式
        private static readonly long InteractiveTicks =
            (long)(Stopwatch.Frequency * 0.5);
        // 超过 2s 无输入 → 静态模式
        private static readonly long IdleTicks =
            (long)(Stopwatch.Frequency * 2.0);

        private long _lastInputTick;

        /// <summary>500ms 内有输入 → 交互模式</summary>
        public bool IsInteractive =>
            _lastInputTick != 0 &&
            Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastInputTick) < InteractiveTicks;

        /// <summary>2s 以上无输入 → 静态办公模式</summary>
        public bool IsIdle =>
            _lastInputTick == 0 ||
            Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastInputTick) >= IdleTicks;

        public void RecordInput() =>
            Interlocked.Exchange(ref _lastInputTick, Stopwatch.GetTimestamp());

        public void RecordMouseMove() => RecordInput();
        public void RecordMouseClick() => RecordInput();
        public void RecordKeyPress() => RecordInput();

        public double IdleSeconds =>
            _lastInputTick == 0 ? double.MaxValue
            : (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastInputTick))
              / (double)Stopwatch.Frequency;
    }

    // ═══════════════════════════════════════════════════════════════
    //  FrameScheduler — 发送队列，积压时丢旧保新
    // ═══════════════════════════════════════════════════════════════
    public sealed class FrameScheduler
    {
        public sealed class Frame
        {
            public byte[] Bgra { get; init; } = Array.Empty<byte>();
            public int Width { get; init; }
            public int Height { get; init; }
            public bool ForceKey { get; init; }
            public long Tick { get; init; } = Stopwatch.GetTimestamp();
        }

        private readonly ConcurrentQueue<Frame> _q = new();
        private int _count;
        private volatile int _maxDepth = 4;

        public int Count => _count;
        public int MaxDepth => _maxDepth;
        public int DroppedTotal { get; private set; }

        public void SetMaxDepth(int d) => _maxDepth = Math.Clamp(d, 1, 8);

        public void Enqueue(Frame f)
        {
            if (f.ForceKey)
            {
                // 关键帧：原子清空旧帧
                int n = 0;
                while (_q.TryDequeue(out _)) { Interlocked.Decrement(ref _count); n++; }
                if (n > 0) AppLogger.Log("Scheduler", $"关键帧到达，清空 {n} 帧积压");
            }
            else
            {
                // 超出上限：丢最旧帧
                while (_count >= _maxDepth)
                {
                    if (_q.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref _count);
                        DroppedTotal++;
                    }
                }
            }
            _q.Enqueue(f);
            Interlocked.Increment(ref _count);
        }

        public bool TryDequeue(out Frame? f)
        {
            if (_q.TryDequeue(out f)) { Interlocked.Decrement(ref _count); return true; }
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  VideoQosController — 核心调度，每 1s 执行一次
    // ═══════════════════════════════════════════════════════════════
    public sealed class VideoQosController
    {
        // ── 绝对边界 ──────────────────────────────────────────────
        private const int BitrateMin = 800_000;
        private const int BitrateMax = 8_000_000;
        private const int FpsMin = 5;
        private const int FpsMax = 60;
        private const float ScaleMin = 0.50f;
        private const float ScaleMax = 1.00f;
        private const long TickIntervalMs = 1000;

        // ── 依赖 ──────────────────────────────────────────────────
        private readonly NetworkQualityEstimator _net;
        private readonly InputActivityTracker _input;
        private readonly FrameScheduler _sched;

        // ── 可变状态 ──────────────────────────────────────────────
        private EncoderSettings _cur;
        private long _lastTickMs;
        private int _poorStreak;   // 连续"差"计数（Poor/Bad）
        private int _goodStreak;   // 连续"优"计数（Excellent）
        private float _curScale;     // 当前分辨率缩放

        public event Action<EncoderSettings>? OnSettingsChanged;
        public EncoderSettings Current => _cur;

        public VideoQosController(
            NetworkQualityEstimator net,
            InputActivityTracker input,
            FrameScheduler sched,
            EncoderSettings? initial = null)
        {
            _net = net;
            _input = input;
            _sched = sched;
            _cur = initial ?? EncoderSettings.Default;
            _curScale = _cur.Scale;
        }

        // 每帧编码后调用（内部节流 1s）
        public void Tick()
        {
            long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
            if (nowMs - _lastTickMs < TickIntervalMs) return;
            _lastTickMs = nowMs;
            RunAdjust();
        }

        private void RunAdjust()
        {
            _net.QueueDepth = _sched.Count;

            NetworkQuality qual = _net.Quality;
            bool interactive = _input.IsInteractive;
            bool idle = _input.IsIdle;

            // ── 连续计数 ──────────────────────────────────────────
            bool isPoor = qual is NetworkQuality.Poor or NetworkQuality.Bad;
            if (isPoor) { _poorStreak++; _goodStreak = 0; }
            else if (qual == NetworkQuality.Excellent)
            { _goodStreak++; _poorStreak = 0; }
            else { if (_poorStreak > 0) _poorStreak--; if (_goodStreak > 0) _goodStreak--; }

            // ── 计算目标值 ────────────────────────────────────────
            int fps = _cur.Fps;
            int bitrate = _cur.Bitrate;
            float scale = _curScale;
            int maxQueue;

            // 优先处理交互模式（覆盖网络策略的队列和延迟要求）
            if (interactive)
                maxQueue = 1;
            else if (qual == NetworkQuality.Bad)
                maxQueue = 1;
            else if (qual == NetworkQuality.Poor)
                maxQueue = 1;
            else if (idle)
                maxQueue = 3;
            else
                maxQueue = 4;

            // ── 按网络质量分档 ────────────────────────────────────
            if (qual == NetworkQuality.Excellent)
            {
                // 规则 1：优秀 — bitrate +10%, fps 上限 30/60, scale → 1.0
                bitrate = (int)Math.Min(bitrate * 1.10, BitrateMax);
                fps = interactive ? Math.Min(fps + 3, 60) : Math.Min(fps + 3, 30);
                // 连续 3 次 Excellent 才恢复 scale（避免抖动）
                if (_goodStreak >= 3) scale = ScaleMax;
            }
            else if (qual == NetworkQuality.Good)
            {
                // 规则 2：一般 — bitrate 微降或保持, fps 24~30
                bitrate = Math.Max((int)(bitrate * 0.98), BitrateMin);
                fps = Math.Clamp(fps, 24, 30);
                // scale 不变
            }
            else if (qual == NetworkQuality.Poor)
            {
                // 规则 3：差 — bitrate -20%, fps 15~20
                bitrate = Math.Max((int)(bitrate * 0.80), BitrateMin);
                fps = Math.Clamp(fps - 2, 15, 20);
                // 连续 3 次 Poor 才降 scale
                if (_poorStreak >= 3) scale = Math.Max(scale - 0.25f, 0.75f);
            }
            else // Bad
            {
                // 规则 4：很差 — bitrate 800k~1500k, fps 10~15, scale 0.5~0.75
                bitrate = Math.Clamp((int)(bitrate * 0.70), BitrateMin, 1_500_000);
                fps = Math.Clamp(fps - 5, 10, 15);
                scale = Math.Max(scale - 0.25f, ScaleMin);
            }

            // ── 覆盖：交互模式（规则 5）─────────────────────────
            if (interactive)
            {
                // 低延迟优先，可临时降画质
                fps = Math.Max(fps, 20);         // 保证操作帧率
                bitrate = Math.Max(bitrate, 800_000); // 最低 800kbps 保操作响应
                // 不限制上调码率（网络允许就保清晰）
            }

            // ── 覆盖：静态办公模式（规则 6）─────────────────────
            if (idle && !interactive)
            {
                // 降 fps 换单帧质量，文字清晰优先
                fps = Math.Clamp(fps, 10, 15);
                // 同码率下 fps 低 → 每帧 bit 更多 → 更清晰，无需额外调整
            }

            // ── 边界裁剪 ──────────────────────────────────────────
            fps = Math.Clamp(fps, FpsMin, FpsMax);
            bitrate = Math.Clamp(bitrate, BitrateMin, BitrateMax);
            scale = Math.Clamp(scale, ScaleMin, ScaleMax);
            _curScale = scale;

            // GOP：交互小 GOP（恢复快），静态大 GOP（节省码率）
            int gop = interactive ? Math.Max(fps, 15)
                    : idle ? Math.Min(fps * 5, 150)
                    : Math.Clamp(fps * 3, 30, 120);

            var next = new EncoderSettings(bitrate, fps, scale, gop, maxQueue);
            _sched.SetMaxDepth(maxQueue);

            if (next == _cur) return;

            string mode = interactive ? "interactive" : idle ? "idle" : "normal";
            string reason = $"net={qual} mode={mode} " +
                            $"rtt={_net.RttMs:F0}ms loss={_net.LossRate:P1} " +
                            $"queue={_sched.Count} poor×{_poorStreak} good×{_goodStreak}";
            AppLogger.Log("QoS", $"{_cur} → {next} [{reason}]");

            _cur = next;
            OnSettingsChanged?.Invoke(next);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  QosPipeline — 统一入口，调用方只持有这一个对象
    // ═══════════════════════════════════════════════════════════════
    public sealed class QosPipeline : IDisposable
    {
        public NetworkQualityEstimator Net { get; }
        public InputActivityTracker Input { get; }
        public FrameScheduler Scheduler { get; }
        public VideoQosController Ctrl { get; }

        public EncoderSettings Current => Ctrl.Current;

        public event Action<EncoderSettings>? OnSettingsChanged
        {
            add => Ctrl.OnSettingsChanged += value;
            remove => Ctrl.OnSettingsChanged -= value;
        }

        public QosPipeline(EncoderSettings? initial = null)
        {
            Net = new NetworkQualityEstimator();
            Input = new InputActivityTracker();
            Scheduler = new FrameScheduler();
            Ctrl = new VideoQosController(Net, Input, Scheduler, initial);
        }

        // ── 采集线程 ─────────────────────────────────────────────
        public void SubmitFrame(byte[] bgra, int w, int h, bool forceKey = false)
        {
            Net.RecordFrame();
            Scheduler.Enqueue(new FrameScheduler.Frame
            { Bgra = bgra, Width = w, Height = h, ForceKey = forceKey });
        }

        // ── 编码线程 ─────────────────────────────────────────────
        public void OnFrameEncoded() => Ctrl.Tick();

        // ── 网络层 ───────────────────────────────────────────────
        public void OnRttMeasured(float rttMs) => Net.RecordRtt(rttMs);
        public void OnPacketLoss(int sent, int lost) => Net.RecordLoss(sent, lost);

        // ── 输入事件 ─────────────────────────────────────────────
        public void OnMouseMove() => Input.RecordMouseMove();
        public void OnMouseClick() => Input.RecordMouseClick();
        public void OnKeyPress() => Input.RecordKeyPress();

        public void Dispose() { }
    }
}