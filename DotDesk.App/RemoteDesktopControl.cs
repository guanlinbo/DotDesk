using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DotDesk.Controller.Network;

namespace DotDesk.App
{
    public partial class RemoteDesktopControl : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        private const int WsThickFrame = 0x00040000;
        private const int WsMaximizeBox = 0x00010000;
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint LlkhfExtended = 0x01;
        private const uint VkControl = 0x11;
        private const uint VkMenu = 0x12;
        private const uint VkDelete = 0x2E;
        private const uint VkTab = 0x09;
        private const uint VkEscape = 0x1B;
        private const uint VkF4 = 0x73;
        private const uint VkLWin = 0x5B;
        private const uint VkRWin = 0x5C;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private readonly WebRtcReceiver _receiver;
        private readonly PictureBox _pic;
        private readonly Label _statusLabel;
        private readonly Action _receiverDisconnectedHandler;
        private bool _firstFrameRendered;
        private bool _closing;
        private bool _closeConfirmed;
        private long _lastMouseMoveTick;
        private bool _remoteInputEnabled;
        private readonly LowLevelKeyboardProc _keyboardProc;
        private IntPtr _keyboardHook;
        private readonly HashSet<uint> _downKeys = new();

        public RemoteDesktopControl(
            WebRtcReceiver receiver,
            string deviceCode,
            string? remoteDeviceName,
            int? serverLatencyMs)
        {
            InitializeComponent();

            _receiver = receiver;
            _keyboardProc = KeyboardHookCallback;

            Text = $"DotDesk - {deviceCode}";
            remoteNameLabel.Text = string.IsNullOrWhiteSpace(remoteDeviceName)
                ? deviceCode
                : remoteDeviceName.Trim();
            latencyLabel.Text = serverLatencyMs.HasValue ? $"{serverLatencyMs.Value}ms" : "--ms";
            InitRemoteTopBar();
            KeyPreview = true;
            SizeChanged += (_, _) => LayoutRemoteTopBar();

            // 创建显示控件
            _pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Default,
            };

            // 开启双缓冲
            typeof(PictureBox)
                .GetProperty(
                    "DoubleBuffered",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_pic, true);

            desktopSurfacePanel.Controls.Add(_pic);
            HookRemoteInputEvents();

            _statusLabel = new Label
            {
                AutoSize = false,
                BackColor = Color.Black,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(210, 220, 230),
                Text = "等待视频帧...",
                TextAlign = ContentAlignment.MiddleCenter
            };
            desktopSurfacePanel.Controls.Add(_statusLabel);
            _statusLabel.BringToFront();

            // 视频帧回调
            _receiver.OnVideoFrame += RenderFrame;

            // 被控端断开
            _receiverDisconnectedHandler = () =>
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(() =>
                    {
                        if (_closing) return;

                        MessageBox.Show(
                            this,
                            "被控端已断开，远程控制窗口即将关闭。",
                            "连接已断开",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        _closeConfirmed = true;
                        Close();
                    }));
            };
            _receiver.OnDisconnected += _receiverDisconnectedHandler;
            _receiver.OnRemoteCursorChanged += ApplyRemoteCursor;

            // 点击获取键盘焦点
            _pic.Click += (_, _) => _pic.Focus();
            _pic.TabStop = true;
        }

        private void HookRemoteInputEvents()
        {
            // 控制端采集本地鼠标键盘，通过 DataChannel 发给被控端注入。
            _pic.MouseMove += (_, e) => SendMouseMove(e);
            _pic.MouseDown += (_, e) =>
            {
                _pic.Focus();
                SendMouseButton(e.Button, down: true);
            };
            _pic.MouseUp += (_, e) => SendMouseButton(e.Button, down: false);
            _pic.MouseWheel += (_, e) => SendInputJson($"{{\"type\":\"wheel\",\"delta\":{e.Delta}}}");
            _pic.MouseEnter += (_, _) => _pic.Focus();
            KeyDown += RemoteDesktopControl_KeyDown;
            KeyUp += RemoteDesktopControl_KeyUp;
        }

        private void InitRemoteTopBar()
        {
            BackColor = Color.Black;
            desktopSurfacePanel.Back = Color.Black;
            desktopSurfacePanel.BackColor = Color.Black;

            remoteTopBar.Height = 34;
            remoteTopBar.Back = Color.FromArgb(177, 199, 244);
            remoteTopBar.BackColor = Color.FromArgb(177, 199, 244);
            avatarLabel.BackColor = Color.FromArgb(231, 242, 255);
            avatarLabel.ForeColor = Color.FromArgb(37, 99, 235);
            avatarLabel.Text = "PC";
            avatarLabel.Font = new Font("Segoe UI", 7F);
            remoteNameLabel.BackColor = Color.Transparent;
            remoteNameLabel.ForeColor = Color.FromArgb(30, 41, 59);
            remoteNameLabel.Font = new Font("Segoe UI", 9F);
            signalBarsLabel.BackColor = Color.Transparent;
            signalBarsLabel.ForeColor = Color.FromArgb(22, 163, 74);
            signalBarsLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            signalBarsLabel.Text = "▮▮▮";
            latencyLabel.BackColor = Color.Transparent;
            latencyLabel.ForeColor = Color.FromArgb(22, 163, 74);
            latencyLabel.Font = new Font("Segoe UI", 9F);
            securityBadgeLabel.BackColor = Color.Transparent;
            securityBadgeLabel.ForeColor = Color.FromArgb(234, 179, 8);
            securityBadgeLabel.Text = "◆";
            qualityBadgeLabel.BackColor = Color.FromArgb(239, 253, 244);
            qualityBadgeLabel.ForeColor = Color.FromArgb(22, 163, 74);
            qualityBadgeLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            qualityBadgeLabel.Text = "HD";
            speakerButton.Text = "♪";
            closeRemoteButton.Text = "×";
            addButton.Text = "+";
            minimizeWindowButton.Text = "-";
            maximizeWindowButton.Text = "□";
            closeWindowButton.Text = "×";
            speakerButton.OriginalBackColor = Color.Transparent;
            closeRemoteButton.OriginalBackColor = Color.Transparent;
            addButton.OriginalBackColor = Color.Transparent;
            minimizeWindowButton.OriginalBackColor = Color.Transparent;
            maximizeWindowButton.OriginalBackColor = Color.Transparent;
            closeWindowButton.OriginalBackColor = Color.Transparent;
            minimizeWindowButton.ForeColor = Color.FromArgb(30, 41, 59);
            maximizeWindowButton.ForeColor = Color.FromArgb(30, 41, 59);
            closeWindowButton.ForeColor = Color.FromArgb(30, 41, 59);

            remoteTopBar.MouseDown += DragWindow;
            avatarLabel.MouseDown += DragWindow;
            remoteNameLabel.MouseDown += DragWindow;
            signalBarsLabel.MouseDown += DragWindow;
            latencyLabel.MouseDown += DragWindow;
            securityBadgeLabel.MouseDown += DragWindow;
            qualityBadgeLabel.MouseDown += DragWindow;

            LayoutRemoteTopBar();
        }

        private void LayoutRemoteTopBar()
        {
            avatarLabel.Location = new Point(14, 5);
            avatarLabel.Size = new Size(24, 24);
            remoteNameLabel.Location = new Point(44, 6);
            remoteNameLabel.Size = new Size(118, 22);
            signalBarsLabel.Location = new Point(168, 7);
            signalBarsLabel.Size = new Size(24, 20);
            latencyLabel.Location = new Point(194, 7);
            latencyLabel.Size = new Size(52, 20);
            securityBadgeLabel.Location = new Point(248, 7);
            securityBadgeLabel.Size = new Size(22, 20);
            qualityBadgeLabel.Location = new Point(274, 8);
            qualityBadgeLabel.Size = new Size(28, 18);
            speakerButton.Location = new Point(306, 3);
            speakerButton.Size = new Size(28, 28);
            closeRemoteButton.Location = new Point(336, 3);
            closeRemoteButton.Size = new Size(28, 28);
            addButton.Location = new Point(372, 3);
            addButton.Size = new Size(34, 28);

            closeWindowButton.Location = new Point(ClientSize.Width - 42, 3);
            maximizeWindowButton.Location = new Point(ClientSize.Width - 78, 3);
            minimizeWindowButton.Location = new Point(ClientSize.Width - 114, 3);
        }

        private void DragWindow(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }

        private void closeRemoteButton_Click(object sender, EventArgs e)
        {
            ConfirmAndClose();
        }

        private void minimizeWindowButton_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void maximizeWindowButton_Click(object sender, EventArgs e)
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void closeWindowButton_Click(object sender, EventArgs e)
        {
            ConfirmAndClose();
        }

        private void ConfirmAndClose()
        {
            using var confirm = new EndRemoteDialog(remoteNameLabel.Text ?? "远程设备");
            if (confirm.ShowDialog(this) != DialogResult.OK)
                return;

            _closeConfirmed = true;
            Close();
        }

        // ─────────────────────────────────────────────
        // BGR24 -> Bitmap
        // ─────────────────────────────────────────────

        private unsafe void RenderFrame(
            byte[] bgr,
            int width,
            int height)
        {
            try
            {
                if (_closing)
                    return;

                if (width <= 0 || height <= 0)
                    return;

                if (bgr == null)
                    return;

                if (bgr.Length < width * height * 3)
                    return;

                // 创建 Bitmap
                Bitmap bmp = new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb);

                BitmapData bd = bmp.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    int rowBytes = width * 3;

                    fixed (byte* srcPtr = bgr)
                    {
                        byte* src = srcPtr;
                        byte* dst = (byte*)bd.Scan0;

                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(
                                src + y * rowBytes,
                                dst + y * bd.Stride,
                                bd.Stride,
                                rowBytes);
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }

                // UI 更新
                if (_pic.IsDisposed)
                {
                    bmp.Dispose();
                    return;
                }

                if (_pic.InvokeRequired)
                {
                    _pic.BeginInvoke(new Action(() =>
                    {
                        if (_closing)
                        {
                            bmp.Dispose();
                            return;
                        }

                        SetPicture(bmp);
                    }));
                }
                else
                {
                    SetPicture(bmp);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        // ─────────────────────────────────────────────
        // 设置 PictureBox 图片
        // ─────────────────────────────────────────────

        private void SetPicture(Bitmap bmp)
        {
            if (_pic.IsDisposed)
            {
                bmp.Dispose();
                return;
            }

            Image? old = _pic.Image;

            _pic.Image = bmp;

            old?.Dispose();

            if (!_firstFrameRendered)
            {
                _firstFrameRendered = true;
                _remoteInputEnabled = true;
                InstallKeyboardHook();
                _statusLabel.Visible = false;
                _pic.BringToFront();
                _pic.Focus();
            }
        }

        private void SendMouseMove(MouseEventArgs e)
        {
            if (!TryGetNormalizedPoint(e.Location, out var x, out var y))
                return;

            long now = Environment.TickCount64;
            if (now - _lastMouseMoveTick < 12)
                return;

            _lastMouseMoveTick = now;
            SendInputJson(FormattableString.Invariant(
                $"{{\"type\":\"mousemove\",\"x\":{x:0.######},\"y\":{y:0.######}}}"));
        }

        private void SendMouseButton(MouseButtons button, bool down)
        {
            int remoteButton = button switch
            {
                MouseButtons.Left => 0,
                MouseButtons.Middle => 1,
                MouseButtons.Right => 2,
                _ => -1
            };
            if (remoteButton < 0) return;

            SendInputJson($"{{\"type\":\"{(down ? "mousedown" : "mouseup")}\",\"button\":{remoteButton}}}");
        }

        private void RemoteDesktopControl_KeyDown(object? sender, KeyEventArgs e)
        {
            // 普通输入走 keyCode，兼容中文/英文输入框和常规快捷键。
            // 低级 Hook 只接管 Win/Alt 这类系统键，避免把普通打字吞掉。
            if (!_remoteInputEnabled || _closing || e.KeyCode == Keys.None || !IsRemoteKeyboardActive()) return;

            SendInputJson($"{{\"type\":\"keydown\",\"keyCode\":{(int)e.KeyCode}}}");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void RemoteDesktopControl_KeyUp(object? sender, KeyEventArgs e)
        {
            if (!_remoteInputEnabled || _closing || e.KeyCode == Keys.None || !IsRemoteKeyboardActive()) return;

            SendInputJson($"{{\"type\":\"keyup\",\"keyCode\":{(int)e.KeyCode}}}");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private bool TryGetNormalizedPoint(Point point, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (!_pic.ClientRectangle.Contains(point))
                return false;

            var rect = GetRenderedImageRectangle();
            if (rect.Width <= 0 || rect.Height <= 0)
                return false;
            if (!rect.Contains(point))
                return false;

            // 按 ToDesk 逻辑：只有真实远程画面区域接管鼠标，黑边不控制。
            x = Math.Clamp((point.X - rect.Left) / (double)rect.Width, 0, 1);
            y = Math.Clamp((point.Y - rect.Top) / (double)rect.Height, 0, 1);
            return true;
        }

        private Rectangle GetRenderedImageRectangle()
        {
            if (_pic.Image == null || _pic.Width <= 0 || _pic.Height <= 0)
                return _pic.ClientRectangle;

            double imageRatio = _pic.Image.Width / (double)_pic.Image.Height;
            double boxRatio = _pic.Width / (double)_pic.Height;

            if (boxRatio > imageRatio)
            {
                int height = _pic.Height;
                int width = Math.Max(1, (int)Math.Round(height * imageRatio));
                return new Rectangle((_pic.Width - width) / 2, 0, width, height);
            }

            int fullWidth = _pic.Width;
            int fullHeight = Math.Max(1, (int)Math.Round(fullWidth / imageRatio));
            return new Rectangle(0, (_pic.Height - fullHeight) / 2, fullWidth, fullHeight);
        }

        private void SendInputJson(string json)
        {
            if (!_remoteInputEnabled || _closing)
                return;

            _receiver.SendInput(json);
        }

        private void ApplyRemoteCursor(string cursorKind)
        {
            if (_pic.IsDisposed) return;

            void Apply()
            {
                _pic.Cursor = cursorKind switch
                {
                    "ibeam" => Cursors.IBeam,
                    "hand" => Cursors.Hand,
                    "sizewe" => Cursors.SizeWE,
                    "sizens" => Cursors.SizeNS,
                    "sizenwse" => Cursors.SizeNWSE,
                    "sizenesw" => Cursors.SizeNESW,
                    "sizeall" => Cursors.SizeAll,
                    "cross" => Cursors.Cross,
                    "wait" => Cursors.WaitCursor,
                    "no" => Cursors.No,
                    "uparrow" => Cursors.UpArrow,
                    _ => Cursors.Default
                };
            }

            if (_pic.InvokeRequired) _pic.BeginInvoke((Action)Apply);
            else Apply();
        }

        private void InstallKeyboardHook()
        {
            if (_keyboardHook != IntPtr.Zero)
                return;

            _keyboardHook = SetWindowsHookEx(
                WhKeyboardLl,
                _keyboardProc,
                IntPtr.Zero,
                0);
        }

        private void UninstallKeyboardHook()
        {
            if (_keyboardHook == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _downKeys.Clear();
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !_remoteInputEnabled || _closing || !IsRemoteKeyboardActive())
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            int message = wParam.ToInt32();
            bool keyDown = message is WmKeyDown or WmSysKeyDown;
            bool keyUp = message is WmKeyUp or WmSysKeyUp;
            if (!keyDown && !keyUp)
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == 0)
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            if (keyDown) _downKeys.Add(info.vkCode);
            else _downKeys.Remove(info.vkCode);

            if (keyDown &&
                info.vkCode == VkDelete &&
                _downKeys.Contains(VkControl) &&
                _downKeys.Contains(VkMenu))
            {
                SendInputJson("{\"type\":\"secureAttention\"}");
                return (IntPtr)1;
            }

            // 远控窗口激活后所有按键都发给被控端，包括 F1/F2/F5 等功能键。
            // 这里只发 keyCode，不走 scanCode，避免不同键盘布局下普通打字失效。
            SendInputJson($"{{\"type\":\"{(keyDown ? "keydown" : "keyup")}\",\"keyCode\":{info.vkCode}}}");
            return (IntPtr)1;
        }

        private bool ShouldInterceptSystemKey(uint vkCode, bool isSysMessage)
        {
            bool altDown = _downKeys.Contains(VkMenu);
            bool ctrlDown = _downKeys.Contains(VkControl);

            // 这些键如果不拦截，会优先被本机窗口或 Windows 自己处理。
            return vkCode == VkLWin ||
                   vkCode == VkRWin ||
                   isSysMessage ||
                   (altDown && (vkCode == VkTab || vkCode == VkF4)) ||
                   (ctrlDown && vkCode == VkEscape);
        }

        private bool IsRemoteKeyboardActive()
        {
            if (!Visible || WindowState == FormWindowState.Minimized)
                return false;

            if (_pic.IsDisposed || !_pic.Visible || _pic.Image == null)
                return false;

            // 键盘接管只在真实远程画面区域内生效；黑边、标题栏、窗口外都交还本机。
            // 鼠标移出远程画面后，Win/F1 等键交还给本机。
            var clientPoint = _pic.PointToClient(Cursor.Position);
            if (!_pic.ClientRectangle.Contains(clientPoint))
                return false;
            if (!GetRenderedImageRectangle().Contains(clientPoint))
                return false;

            var foreground = GetForegroundWindow();
            return foreground == Handle || ContainsFocus;
        }

        // ─────────────────────────────────────────────
        // 关闭清理
        // ─────────────────────────────────────────────

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closeConfirmed && e.CloseReason == CloseReason.UserClosing)
            {
                using var confirm = new EndRemoteDialog(remoteNameLabel.Text ?? "远程设备");
                if (confirm.ShowDialog(this) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }

                _closeConfirmed = true;
            }

            _closing = true;
            _remoteInputEnabled = false;
            UninstallKeyboardHook();
            KeyDown -= RemoteDesktopControl_KeyDown;
            KeyUp -= RemoteDesktopControl_KeyUp;
            _receiver.OnVideoFrame -= RenderFrame;
            _receiver.OnDisconnected -= _receiverDisconnectedHandler;
            _receiver.OnRemoteCursorChanged -= ApplyRemoteCursor;

            Image? old = _pic.Image;
            _pic.Image = null;
            old?.Dispose();

            base.OnFormClosing(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= WsThickFrame | WsMaximizeBox;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int wmNcHitTest = 0x0084;
            const int htClient = 1;
            const int htCaption = 2;
            const int htLeft = 10;
            const int htRight = 11;
            const int htTop = 12;
            const int htTopLeft = 13;
            const int htTopRight = 14;
            const int htBottom = 15;
            const int htBottomLeft = 16;
            const int htBottomRight = 17;

            base.WndProc(ref m);

            if (m.Msg != wmNcHitTest || (int)m.Result != htClient)
                return;

            var screenPoint = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            var point = PointToClient(screenPoint);
            const int grip = 8;

            bool left = point.X <= grip;
            bool right = point.X >= ClientSize.Width - grip;
            bool top = point.Y <= grip;
            bool bottom = point.Y >= ClientSize.Height - grip;

            if (top && left) m.Result = htTopLeft;
            else if (top && right) m.Result = htTopRight;
            else if (bottom && left) m.Result = htBottomLeft;
            else if (bottom && right) m.Result = htBottomRight;
            else if (left) m.Result = htLeft;
            else if (right) m.Result = htRight;
            else if (top) m.Result = htTop;
            else if (bottom) m.Result = htBottom;
            else if (point.Y <= remoteTopBar.Height) m.Result = htCaption;
        }

        private sealed class EndRemoteDialog : Form
        {
            public EndRemoteDialog(string remoteName)
            {
                Text = "结束远控";
                ClientSize = new Size(360, 174);
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                BackColor = Color.White;
                Font = new Font("Microsoft YaHei UI", 9F);

                var warning = new Label
                {
                    AutoSize = false,
                    Location = new Point(24, 20),
                    Size = new Size(24, 24),
                    Text = "!",
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(245, 158, 11),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold)
                };

                var title = new Label
                {
                    AutoSize = false,
                    Location = new Point(54, 19),
                    Size = new Size(220, 28),
                    Text = "结束远控",
                    ForeColor = Color.FromArgb(15, 23, 42),
                    Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold)
                };

                var close = new Button
                {
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(318, 18),
                    Size = new Size(24, 24),
                    Text = "×",
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(75, 85, 99)
                };
                close.FlatAppearance.BorderSize = 0;
                close.Click += (_, _) => DialogResult = DialogResult.Cancel;

                var body = new Label
                {
                    AutoSize = false,
                    Location = new Point(24, 70),
                    Size = new Size(312, 24),
                    Text = $"是否结束对 \"{remoteName}\" 的远程控制?",
                    ForeColor = Color.FromArgb(15, 23, 42)
                };

                var dontShow = new CheckBox
                {
                    Location = new Point(24, 124),
                    Size = new Size(92, 28),
                    Text = "不再提示"
                };

                var cancel = new Button
                {
                    Location = new Point(168, 120),
                    Size = new Size(88, 34),
                    Text = "取消",
                    DialogResult = DialogResult.Cancel
                };

                var ok = new Button
                {
                    Location = new Point(270, 120),
                    Size = new Size(72, 34),
                    Text = "确定",
                    DialogResult = DialogResult.OK,
                    BackColor = Color.FromArgb(0, 102, 255),
                    ForeColor = Color.White
                };

                Controls.Add(warning);
                Controls.Add(title);
                Controls.Add(close);
                Controls.Add(body);
                Controls.Add(dontShow);
                Controls.Add(cancel);
                Controls.Add(ok);
                AcceptButton = ok;
                CancelButton = cancel;
            }
        }
    }
}
