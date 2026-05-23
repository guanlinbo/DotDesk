using AntdUI;
using AntButton = AntdUI.Button;
using AntPanel = AntdUI.Panel;
using WinMessage = System.Windows.Forms.Message;

namespace DotDesk.App
{
    public partial class manUi : BorderlessForm
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private HomePage? _homePage;
        private System.Windows.Forms.Panel? _networkOverlay;
        private AntPanel? _networkCard;
        private AntButton? _networkRetryButton;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public manUi()
        {
            InitializeComponent();
            InitChrome();

            _homePage = new HomePage { Dock = DockStyle.Fill };
            _homePage.NetworkOfflineChanged += ToggleNetworkOverlay;
            contentPanel.Controls.Add(_homePage);

            CreateNetworkOverlay();
        }

        private void InitChrome()
        {
            Size = new Size(1000, 620);
            MinimumSize = new Size(1000, 620);
            Radius = 18;
            Shadow = 18;
            ShadowColor = Color.FromArgb(90, 15, 23, 42);
            ShadowPierce = false;
            UseDwm = true;
            Resizable = true;
            BackColor = Color.FromArgb(246, 249, 255);

            titleBar.BackColor = Color.White;
            titleBar.MouseDown += DragWindow;

            appLogoLabel.Text = "D";
            appLogoLabel.ForeColor = Color.FromArgb(37, 99, 235);
            appLogoLabel.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
            appLogoLabel.MouseDown += DragWindow;

            StyleTopButton(homeTabButton, "主页", true);
            StyleTopButton(settingsTabButton, "设置", false);
            StyleWindowButton(menuButton, "≡");
            StyleWindowButton(minimizeWindowButton, "-");
            StyleWindowButton(maximizeWindowButton, "□");
            StyleWindowButton(closeWindowButton, "×");
        }

        private static void StyleTopButton(AntButton button, string text, bool active)
        {
            button.Text = text;
            button.Font = new Font("Microsoft YaHei UI", 12F, active ? FontStyle.Bold : FontStyle.Regular);
            button.ForeColor = active ? Color.FromArgb(15, 23, 42) : Color.FromArgb(55, 65, 81);
            button.OriginalBackColor = Color.White;
            button.BackColor = Color.White;
            button.Radius = 10;
        }

        private static void StyleWindowButton(AntButton button, string text)
        {
            button.Text = text;
            button.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
            button.ForeColor = Color.FromArgb(15, 23, 42);
            button.OriginalBackColor = Color.White;
            button.BackColor = Color.White;
            button.Radius = 8;
        }

        private void CreateNetworkOverlay()
        {
            _networkOverlay = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 249, 255),
                Visible = false
            };

            _networkCard = new AntPanel
            {
                Size = new Size(500, 520),
                Back = Color.White,
                BackColor = Color.White,
                Radius = 18,
                Shadow = 14,
                ShadowColor = Color.FromArgb(148, 163, 184),
                ShadowOpacity = 0.22F,
                ShadowOffsetX = 0,
                ShadowOffsetY = 8
            };

            var wifiIcon = CreatePicture("wlcw.png", new Point(210, 52), new Size(80, 60));
            if (wifiIcon != null) _networkCard.Controls.Add(wifiIcon);

            var title = new System.Windows.Forms.Label
            {
                Text = "网络连接已断开",
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 160),
                Size = new Size(500, 36)
            };

            var subtitle = new System.Windows.Forms.Label
            {
                Text = "请检查网络连接，或稍后重试\r\n我们会自动尝试重新连接",
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 205),
                Size = new Size(500, 48)
            };

            _networkRetryButton = new AntButton
            {
                Text = "⟳  重新连接",
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                OriginalBackColor = Color.FromArgb(0, 96, 220),
                BackColor = Color.FromArgb(0, 96, 220),
                Radius = 10,
                Location = new Point(150, 280),
                Size = new Size(200, 38)
            };
            _networkRetryButton.Click += async (_, _) => await RetryFromOfflinePageAsync();

            var diagnoseButton = new AntButton
            {
                Text = "⌁  网络诊断",
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(71, 85, 105),
                OriginalBackColor = Color.White,
                BackColor = Color.White,
                Radius = 10,
                Location = new Point(170, 336),
                Size = new Size(160, 34)
            };

            var dividerLeft = CreateLine(new Point(70, 402), new Size(130, 1));
            var dividerRight = CreateLine(new Point(300, 402), new Size(130, 1));
            var reason = new System.Windows.Forms.Label
            {
                Text = "可能的原因",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(190, 390),
                Size = new Size(120, 24)
            };

            AddReason(_networkCard, "wifi.png", "网络未连接", 78, 425);
            AddReason(_networkCard, "connect.png", "服务器不可", 182, 425);
            AddReason(_networkCard, "security.png", "防火墙限制", 288, 425);
            AddReason(_networkCard, "device_list.png", "远程设备离线", 390, 425);

            var tip = new AntPanel
            {
                Location = new Point(56, 472),
                Size = new Size(388, 38),
                Back = Color.FromArgb(235, 243, 255),
                BackColor = Color.FromArgb(235, 243, 255),
                Radius = 12
            };
            var tipIcon = CreatePicture("help_small.png", new Point(12, 8), new Size(20, 20));
            if (tipIcon != null) tip.Controls.Add(tipIcon);
            tip.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "小贴士",
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 96, 220),
                BackColor = Color.Transparent,
                Location = new Point(40, 4),
                Size = new Size(80, 16)
            });
            tip.Controls.Add(new System.Windows.Forms.Label
            {
                Text = "请确保你的设备已连接到互联网，并且远程设备已开启",
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.Transparent,
                Location = new Point(40, 19),
                Size = new Size(330, 16)
            });

            _networkCard.Controls.Add(title);
            _networkCard.Controls.Add(subtitle);
            _networkCard.Controls.Add(_networkRetryButton);
            _networkCard.Controls.Add(diagnoseButton);
            _networkCard.Controls.Add(dividerLeft);
            _networkCard.Controls.Add(dividerRight);
            _networkCard.Controls.Add(reason);
            _networkCard.Controls.Add(tip);
            _networkOverlay.Controls.Add(_networkCard);

            Controls.Add(_networkOverlay);
            LayoutNetworkOverlay();
        }

        private static System.Windows.Forms.Panel CreateLine(Point location, Size size)
        {
            return new System.Windows.Forms.Panel
            {
                Location = location,
                Size = size,
                BackColor = Color.FromArgb(226, 232, 240)
            };
        }

        private static void AddReason(Control parent, string iconName, string text, int x, int y)
        {
            var icon = CreatePicture(iconName, new Point(x + 22, y), new Size(28, 28));
            if (icon != null) parent.Controls.Add(icon);

            parent.Controls.Add(new System.Windows.Forms.Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(30, 41, 59),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, y + 32),
                Size = new Size(72, 22)
            });
        }

        private static PictureBox? CreatePicture(string fileName, Point location, Size size)
        {
            var image = LoadImage(fileName);
            if (image == null) return null;

            return new PictureBox
            {
                Image = image,
                Location = location,
                Size = size,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
        }

        private static Image? LoadImage(string fileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
            return File.Exists(path) ? Image.FromFile(path) : null;
        }

        private void ToggleNetworkOverlay(bool visible)
        {
            if (_networkOverlay == null) return;

            _networkOverlay.Visible = visible;
            if (visible)
            {
                LayoutNetworkOverlay();
                _networkOverlay.BringToFront();
                ResetRetryButton();
            }
            else
            {
                ResetRetryButton();
                contentPanel.BringToFront();
                titleBar.BringToFront();
            }
        }

        private async Task RetryFromOfflinePageAsync()
        {
            if (_homePage == null || _networkRetryButton == null) return;

            _networkRetryButton.Enabled = false;
            _networkRetryButton.Text = "正在重连...";
            await _homePage.RetryNetworkAsync();

            if (_networkOverlay?.Visible == true)
                ResetRetryButton();
        }

        private void ResetRetryButton()
        {
            if (_networkRetryButton == null) return;

            _networkRetryButton.Enabled = true;
            _networkRetryButton.Text = "⟳  重新连接";
        }

        private void LayoutNetworkOverlay()
        {
            if (_networkCard == null) return;

            _networkCard.Location = new Point(
                Math.Max(0, (ClientSize.Width - _networkCard.Width) / 2),
                Math.Max(0, (ClientSize.Height - _networkCard.Height) / 2));
        }

        private void DragWindow(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
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
            Close();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutNetworkOverlay();
        }

        protected override void WndProc(ref WinMessage m)
        {
            const int wmNcHitTest = 0x0084;
            const int htClient = 1;
            const int htCaption = 2;

            base.WndProc(ref m);

            if (m.Msg != wmNcHitTest || (int)m.Result != htClient)
                return;

            var screenPoint = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            var point = PointToClient(screenPoint);
            if (point.Y <= titleBar.Height && point.X > 320 && point.X < ClientSize.Width - 190)
                m.Result = htCaption;
        }
    }
}
