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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int CURSOR_SHOWING = 0x00000001;
        private const int IDC_ARROW = 32512;
        private const int IDC_IBEAM = 32513;
        private const int IDC_WAIT = 32514;
        private const int IDC_CROSS = 32515;
        private const int IDC_UPARROW = 32516;
        private const int IDC_SIZE = 32640;
        private const int IDC_ICON = 32641;
        private const int IDC_SIZENWSE = 32642;
        private const int IDC_SIZENESW = 32643;
        private const int IDC_SIZEWE = 32644;
        private const int IDC_SIZENS = 32645;
        private const int IDC_SIZEALL = 32646;
        private const int IDC_NO = 32648;
        private const int IDC_HAND = 32649;
        private const int IDC_APPSTARTING = 32650;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

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
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

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

        public static string GetCurrentCursorKind()
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref info) || info.hCursor == IntPtr.Zero || (info.flags & CURSOR_SHOWING) == 0)
                return "arrow";

            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_IBEAM)) return "ibeam";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_HAND)) return "hand";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_SIZEWE)) return "sizewe";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_SIZENS)) return "sizens";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_SIZENWSE)) return "sizenwse";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_SIZENESW)) return "sizenesw";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_SIZEALL)) return "sizeall";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_CROSS)) return "cross";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_WAIT)) return "wait";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_APPSTARTING)) return "wait";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_NO)) return "no";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_UPARROW)) return "uparrow";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_SIZE)) return "sizeall";
            if (info.hCursor == LoadCursor(IntPtr.Zero, IDC_ICON)) return "arrow";

            return "arrow";
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

        public static void KeyScan(ushort scanCode, bool down, bool extended) =>
            Send(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = scanCode,
                        dwFlags = KEYEVENTF_SCANCODE
                            | (extended ? KEYEVENTF_EXTENDEDKEY : 0u)
                            | (down ? 0u : KEYEVENTF_KEYUP),
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
