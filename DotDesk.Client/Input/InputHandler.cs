using DotDesk.Core;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotDesk.Client.Input
{
    /// <summary>
    /// 解析控制端发来的 DataChannel JSON 指令，调用 InputSimulator
    ///
    /// 消息格式：
    ///   鼠标移动： { "type": "mousemove", "x": 0.5, "y": 0.3 }
    ///   鼠标按键： { "type": "mousedown"/"mouseup", "button": 0/1/2 }
    ///   滚轮：     { "type": "wheel", "delta": 120 }
    ///   键盘：     { "type": "keydown"/"keyup", "keyCode": 65 }
    /// </summary>
    public static class InputHandler
    {
        /// <summary>收到请求关键帧消息时触发</summary>
        public static event Action? OnRequestKeyFrame;
        public static event Action<string>? OnCursorChanged;

        public static void Handle(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node == null) return;

                var type = node["type"]?.GetValue<string>();

                switch (type)
                {
                    case "mousemove":
                        var x = node["x"]?.GetValue<double>() ?? 0;
                        var y = node["y"]?.GetValue<double>() ?? 0;
                        InputSimulator.MouseMove(x, y);
                        OnCursorChanged?.Invoke(InputSimulator.GetCurrentCursorKind());
                        break;

                    case "mousedown":
                    case "mouseup":
                        var btn = node["button"]?.GetValue<int>() ?? 0;
                        var down = type == "mousedown";
                        switch (btn)
                        {
                            case 0: InputSimulator.MouseLeft(down); break;
                            case 1: InputSimulator.MouseMiddle(down); break;
                            case 2: InputSimulator.MouseRight(down); break;
                        }
                        break;

                    case "wheel":
                        var delta = node["delta"]?.GetValue<int>() ?? 0;
                        InputSimulator.MouseWheel(delta);
                        break;

                    case "keydown":
                    case "keyup":
                        var isDown = type == "keydown";
                        var scanCode = node["scanCode"]?.GetValue<int>() ?? 0;
                        var extended = node["extended"]?.GetValue<bool>() ?? false;
                        if (scanCode > 0)
                        {
                            InputSimulator.KeyScan((ushort)scanCode, isDown, extended);
                        }
                        else
                        {
                            var keyCode = node["keyCode"]?.GetValue<int>() ?? 0;
                            InputSimulator.KeyPress((ushort)keyCode, isDown);
                        }
                        break;

                    case "secureAttention":
                        // Ctrl+Alt+Del 是 Windows 安全注意序列，普通桌面进程无法用 SendInput 触发。
                        // ToDesk/RustDesk 这类一般依赖安装后的服务进程/系统组件完成。
                        AppLogger.Log("Input", "收到 Ctrl+Alt+Del 请求，但当前未实现服务级安全注意序列注入");
                        break;

                    case "requestKeyFrame":
                        OnRequestKeyFrame?.Invoke();
                        break;

                    case "keypress":
                        // Unicode 字符输入
                        var charCode = node["charCode"]?.GetValue<int>() ?? 0;
                        if (charCode > 0)
                        {
                            InputSimulator.KeyUnicode((char)charCode, true);
                            InputSimulator.KeyUnicode((char)charCode, false);
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
