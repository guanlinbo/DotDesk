using System;
using System.Runtime.InteropServices;

namespace DotDesk.Controller.Network
{
    /// <summary>
    /// BGR24 帧数据处理
    /// 不依赖 WinForms，只做数据转换
    /// UI 层自己决定如何显示（PictureBox / DirectX 等）
    /// </summary>
    public sealed class VideoRenderer : IDisposable
    {
        /// <summary>每帧 BGR24 数据回调，UI 层订阅此事件显示画面</summary>
        public event Action<byte[], int, int>? OnFrame;

        private bool _disposed;

        /// <summary>渲染一帧 BGR24 数据（可从任意线程调用）</summary>
        public void Render(byte[] bgr, int width, int height)
        {
            if (_disposed || width <= 0 || height <= 0) return;
            OnFrame?.Invoke(bgr, width, height);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}