using DotDesk.Core.Logging;
using DotDesk.Core.Protocol;
using System;

namespace DotDesk.Client.Input
{
    /// <summary>
    /// 解析控制端通过 DataChannel 发来的协议消息，并调用 InputSimulator 注入输入。
    /// 后续剪贴板、文件传输、权限请求都继续走 DotDeskMessageCodec。
    /// </summary>
    public static class InputHandler
    {
        /// <summary>收到请求关键帧消息时触发。</summary>
        public static event Action? OnRequestKeyFrame;
        public static event Action<string>? OnCursorChanged;

        public static void Handle(string json)
        {
            try
            {
                var message = DotDeskMessageCodec.Parse(json);

                switch (message.MessageType)
                {
                    case DotDeskMessageType.MouseMove:
                        InputSimulator.MouseMove(message.X, message.Y);
                        OnCursorChanged?.Invoke(InputSimulator.GetCurrentCursorKind());
                        break;

                    case DotDeskMessageType.MouseDown:
                    case DotDeskMessageType.MouseUp:
                        var down = message.MessageType == DotDeskMessageType.MouseDown;
                        switch (message.Button)
                        {
                            case 0: InputSimulator.MouseLeft(down); break;
                            case 1: InputSimulator.MouseMiddle(down); break;
                            case 2: InputSimulator.MouseRight(down); break;
                        }
                        break;

                    case DotDeskMessageType.MouseWheel:
                        InputSimulator.MouseWheel(message.Delta);
                        break;

                    case DotDeskMessageType.KeyDown:
                    case DotDeskMessageType.KeyUp:
                        var isDown = message.MessageType == DotDeskMessageType.KeyDown;
                        if (message.ScanCode > 0)
                            InputSimulator.KeyScan((ushort)message.ScanCode, isDown, message.Extended);
                        else
                            InputSimulator.KeyPress((ushort)message.KeyCode, isDown);
                        break;

                    case DotDeskMessageType.SecureAttention:
                        // Ctrl+Alt+Del 是 Windows 安全注意序列，普通桌面进程无法用 SendInput 触发。
                        // ToDesk/RustDesk 这类软件通常依赖安装后的服务进程或系统组件完成。
                        AppLogger.Log("Input", "收到 Ctrl+Alt+Del 请求，但当前尚未实现服务级安全注意序列注入");
                        break;

                    case DotDeskMessageType.RequestKeyFrame:
                        OnRequestKeyFrame?.Invoke();
                        break;

                    case DotDeskMessageType.KeyPress:
                        // Unicode 字符输入。
                        if (message.CharCode > 0)
                        {
                            InputSimulator.KeyUnicode((char)message.CharCode, true);
                            InputSimulator.KeyUnicode((char)message.CharCode, false);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Input", $"指令解析失败: {ex.Message}  json={json}");
            }
        }
    }
}
