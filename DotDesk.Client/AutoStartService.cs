using System;
using System.Threading.Tasks;
using DotDesk.Core;
using DotDesk.Core.Models;
using DotDesk.Client.Network;

namespace DotDesk.Client
{
    /// <summary>
    /// 被控端自动启动服务
    /// 程序启动时自动连接信令服务器，等待控制端连接
    /// </summary>
    public sealed class AutoStartService : IDisposable
    {
        // ── 事件 ─────────────────────────────────────────────────────
        public event Action<string>? OnLog;
        public event Action? OnConnected;      // 控制端已连接
        public event Action? OnDisconnected;   // 控制端断开
        public event Action<string>? OnStatusChanged;  // 状态文字
        public event Action<float>? OnFpsUpdate;      // 推流帧率

        // ── 属性 ─────────────────────────────────────────────────────
        public string DeviceCode { get; } = DotDesk.Core.Models.DeviceCode.GetFormatted();
        public bool IsP2PConnected => _pusher?.IsConnected ?? false;

        /// <summary>当前一次性密码（显示在被控端界面）</summary>
        public string Password => _pusher?.Password ?? "------";

        /// <summary>刷新生成新密码</summary>
        public string RefreshPassword() => _pusher?.RefreshPassword() ?? "------";

        // ── 内部 ─────────────────────────────────────────────────────
        private WebRtcPusher? _pusher;
        private CaptureAndPushPipeline? _pipeline;
        private readonly string _serverUrl;
        private bool _disposed;

        /// <param name="serverUrl">信令服务器地址 ws://your-server:5000</param>
        public AutoStartService(string serverUrl)
        {
            _serverUrl = serverUrl;
        }

        // ── 启动 ─────────────────────────────────────────────────────

        /// <param name="screenWidth">主屏幕宽度（由 App 层传入）</param>
        /// <param name="screenHeight">主屏幕高度（由 App 层传入）</param>
        /// <param name="fps">推流帧率</param>
        public async Task StartAsync(int screenWidth, int screenHeight, int fps = 30)
        {
            AppLogger.Log("AutoStart", $"设备码: {DeviceCode}");
            AppLogger.Log("AutoStart", $"连接服务器: {_serverUrl}");
            SetStatus("连接服务器中...");

            try
            {
                var code = DotDesk.Core.Models.DeviceCode.Get();
                _pusher = new WebRtcPusher(_serverUrl, code);

                _pusher.OnLog += msg => OnLog?.Invoke(msg);
                _pusher.OnConnectionStatus += status => SetStatus(status);

                _pusher.OnConnected += () =>
                {
                    AppLogger.Log("AutoStart", "控制端已连接，开始推流");
                    SetStatus("控制端已连接");
                    OnConnected?.Invoke();
                    StartCapture(screenWidth, screenHeight, fps);
                };

                _pusher.OnDisconnected += () =>
                {
                    AppLogger.Log("AutoStart", "控制端断开，停止推流");
                    SetStatus("等待控制端连接...");
                    OnDisconnected?.Invoke();
                    StopCapture();
                };

                await _pusher.StartAsync(screenWidth, screenHeight, fps);

                SetStatus("等待控制端连接...");
                AppLogger.Log("AutoStart", "信令已连接，等待控制端");
            }
            catch (Exception ex)
            {
                AppLogger.Log("AutoStart", $"启动失败: {ex.Message}");
                SetStatus($"连接失败: {ex.Message}");
            }
        }

        // ── 停止 ─────────────────────────────────────────────────────

        public void Stop()
        {
            StopCapture();
            _pusher?.Dispose();
            _pusher = null;
        }

        // ── 截图推流管线 ──────────────────────────────────────────────

        private void StartCapture(int w, int h, int fps)
        {
            if (_pipeline != null) return;

            _pipeline = new CaptureAndPushPipeline(_pusher!);
            _pipeline.OnLog += msg => OnLog?.Invoke(msg);
            _pipeline.OnFpsUpdate += fps =>
            {
                OnFpsUpdate?.Invoke(fps);
                SetStatus($"推流中 {fps:F1} fps");
            };
            _pipeline.Start(fps: fps);
        }

        private void StopCapture()
        {
            _pipeline?.Stop();
            _pipeline?.Dispose();
            _pipeline = null;
        }

        private void SetStatus(string status) =>
            OnStatusChanged?.Invoke(status);

        // ── IDisposable ───────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCapture();
            _pusher?.Dispose();
        }
    }
}
