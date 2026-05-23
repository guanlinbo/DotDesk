using System;
using System.Runtime.InteropServices;

namespace DotDesk.Client.Input
{
    /// <summary>
    /// 鼠标键盘输入模拟（user32.dll SendInput）
    /// </summary>
    public static class InputSimulator
    {
        // ── P/Invoke ──────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        // ── 结构体 ───────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        // ── 常量 ─────────────────────────────────────────────────────

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        // 鼠标标志
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        // 键盘标志
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        // ── 鼠标操作 ─────────────────────────────────────────────────

        /// <summary>
        /// 移动鼠标到绝对位置
        /// x/y 是控制端画面上的归一化坐标（0.0~1.0）
        /// 自动映射到被控端实际屏幕分辨率
        /// </summary>
        public static void MouseMove(double normalizedX, double normalizedY)
        {
            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int screenH = GetSystemMetrics(SM_CYSCREEN);

            // MOUSEEVENTF_ABSOLUTE 坐标范围是 0~65535
            int absX = (int)(normalizedX * 65535);
            int absY = (int)(normalizedY * 65535);

            Send(new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                    }
                }
            });
        }

        /// <summary>鼠标左键按下/抬起</summary>
        public static void MouseLeft(bool down) =>
            Send(MouseInput(down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP));

        /// <summary>鼠标右键按下/抬起</summary>
        public static void MouseRight(bool down) =>
            Send(MouseInput(down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP));

        /// <summary>鼠标中键按下/抬起</summary>
        public static void MouseMiddle(bool down) =>
            Send(MouseInput(down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP));

        /// <summary>鼠标滚轮（delta 正数向上，负数向下）</summary>
        public static void MouseWheel(int delta) =>
            Send(new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        mouseData = (uint)delta,
                        dwFlags = MOUSEEVENTF_WHEEL,
                    }
                }
            });

        // ── 键盘操作 ─────────────────────────────────────────────────

        /// <summary>按键按下/抬起（Virtual Key Code）</summary>
        public static void KeyPress(ushort vkCode, bool down) =>
            Send(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vkCode,
                        dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                    }
                }
            });

        /// <summary>输入 Unicode 字符（用于中文等非ASCII字符）</summary>
        public static void KeyUnicode(char c, bool down) =>
            Send(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | (down ? 0u : KEYEVENTF_KEYUP),
                    }
                }
            });

        // ── 工具 ─────────────────────────────────────────────────────

        private static INPUT MouseInput(uint flags) => new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } }
        };

        private static void Send(INPUT input) =>
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}