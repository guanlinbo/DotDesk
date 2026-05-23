using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using DotDesk.Controller.Network;

namespace DotDesk.App
{
    public partial class RemoteDesktopControl : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly WebRtcReceiver _receiver;
        private readonly PictureBox _pic;
        private readonly Label _statusLabel;
        private readonly Action _receiverDisconnectedHandler;
        private bool _firstFrameRendered;
        private bool _closing;
        private bool _closeConfirmed;

        public RemoteDesktopControl(
            WebRtcReceiver receiver,
            string deviceCode,
            string? remoteDeviceName,
            int? serverLatencyMs)
        {
            InitializeComponent();

            _receiver = receiver;

            Text = $"DotDesk - {deviceCode}";
            remoteNameLabel.Text = string.IsNullOrWhiteSpace(remoteDeviceName)
                ? deviceCode
                : remoteDeviceName.Trim();
            latencyLabel.Text = serverLatencyMs.HasValue ? $"{serverLatencyMs.Value}ms" : "--ms";
            InitRemoteTopBar();

            // 创建显示控件
            _pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Cross,
            };

            // 开启双缓冲
            typeof(PictureBox)
                .GetProperty(
                    "DoubleBuffered",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_pic, true);

            desktopSurfacePanel.Controls.Add(_pic);

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

            // 点击获取键盘焦点
            _pic.Click += (_, _) => _pic.Focus();
            _pic.TabStop = true;
        }

        private void InitRemoteTopBar()
        {
            remoteTopBar.Back = Color.FromArgb(197, 215, 255);
            avatarLabel.BackColor = Color.FromArgb(231, 242, 255);
            avatarLabel.ForeColor = Color.FromArgb(37, 99, 235);
            remoteNameLabel.BackColor = Color.Transparent;
            remoteNameLabel.ForeColor = Color.FromArgb(30, 41, 59);
            remoteNameLabel.Font = new Font("Segoe UI", 9F);
            signalBarsLabel.BackColor = Color.Transparent;
            signalBarsLabel.ForeColor = Color.FromArgb(22, 163, 74);
            signalBarsLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            latencyLabel.BackColor = Color.Transparent;
            latencyLabel.ForeColor = Color.FromArgb(22, 163, 74);
            latencyLabel.Font = new Font("Segoe UI", 9F);
            securityBadgeLabel.BackColor = Color.Transparent;
            securityBadgeLabel.ForeColor = Color.FromArgb(234, 179, 8);
            qualityBadgeLabel.BackColor = Color.FromArgb(239, 253, 244);
            qualityBadgeLabel.ForeColor = Color.FromArgb(22, 163, 74);
            qualityBadgeLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
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
                _statusLabel.Visible = false;
                _pic.BringToFront();
            }
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
            _receiver.OnVideoFrame -= RenderFrame;
            _receiver.OnDisconnected -= _receiverDisconnectedHandler;

            Image? old = _pic.Image;
            _pic.Image = null;
            old?.Dispose();

            base.OnFormClosing(e);
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
