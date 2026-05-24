using AntdUI;
using AntButton = AntdUI.Button;
using WinMessage = System.Windows.Forms.Message;

namespace DotDesk.App
{
    public partial class MainForm : BorderlessForm
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private HomePage? _homePage;

        // 鏂扮殑鏂綉椤甸潰锛氭敞鎰忚繖閲岀敤鐨勬槸 NetworkOfflinePage
        // 浠ュ悗鏂綉 UI 閮藉幓鏀?NetworkOfflinePage.cs锛屼笉瑕佸啀鏀?MainForm 閲岀殑鏃т唬鐮?
        private NetworkOfflinePage? _networkOverlay;
        private System.Windows.Forms.Timer? _offlineRetryTimer;
        private bool _offlineRetrying;

        private RoundTabLine? _homeTabLine;
        private RoundTabLine? _settingsTabLine;

        private enum Tab
        {
            Home,
            Settings
        }

        private Tab _currentTab = Tab.Home;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        /// <summary>
        /// 椤堕儴 Tab 涓嬮潰鐨勫皬钃濇潯銆?
        /// 涓嶇敤鏅€?Panel锛屾槸鍥犱负鏅€?Panel 鍦嗚涓嶅ソ鎺у埗銆?
        /// </summary>
        private class RoundTabLine : System.Windows.Forms.Control
        {
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public Color LineColor { get; set; } = Color.FromArgb(37, 99, 235);

            public RoundTabLine()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor,
                    true
                );

                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                int lineHeight = 3;
                int y = (Height - lineHeight) / 2;

                using var brush = new SolidBrush(LineColor);
                using var path = CreateRoundRectPath(
                    new RectangleF(0, y, Width, lineHeight),
                    lineHeight / 2f
                );

                e.Graphics.FillPath(brush, path);
            }

            private static System.Drawing.Drawing2D.GraphicsPath CreateRoundRectPath(RectangleF rect, float radius)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();

                float d = radius * 2;

                if (d > rect.Width) d = rect.Width;
                if (d > rect.Height) d = rect.Height;

                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                return path;
            }
        }

        public MainForm()
        {
            InitializeComponent();

            InitChrome();

            // 棣栭〉
            _homePage = new HomePage
            {
                Dock = DockStyle.Fill
            };

            // HomePage 鍙礋璐ｅ憡璇?MainForm锛氱幇鍦ㄦ柇缃戜簡 / 鎭㈠浜?
            _homePage.NetworkOfflineChanged += ToggleNetworkOverlay;

            contentPanel.Controls.Clear();
            contentPanel.Controls.Add(_homePage);

            // 鍒涘缓鏂扮殑鏂綉椤甸潰锛岀洊鍦ㄤ富绐楀彛涓?
            CreateNetworkOverlay();
        }

        /// <summary>
        /// 鍒濆鍖栫獥鍙ｅ澹筹細鏍囬鏍忋€乀ab銆佺獥鍙ｆ寜閽€?
        /// </summary>
        private void InitChrome()
        {
            Size = new Size(1180, 680);
            MinimumSize = new Size(1180, 680);
            BackColor = Color.FromArgb(246, 249, 255);

            Radius = 18;
            Shadow = 18;
            ShadowColor = Color.FromArgb(90, 15, 23, 42);
            ShadowPierce = false;
            UseDwm = true;
            Resizable = true;

            titleBar.BackColor = Color.White;
            titleBar.MouseDown += DragWindow;

            appLogoLabel.Text = "D";
            appLogoLabel.ForeColor = Color.FromArgb(37, 99, 235);
            appLogoLabel.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
            appLogoLabel.MouseDown += DragWindow;

            contentPanel.BorderWidth = 0;

            // 鍙充笂瑙掔獥鍙ｆ寜閽紝鍏ㄩ儴浣跨敤 SVG锛屼笉鐢ㄦ枃瀛?
            StyleWindowButton(menuButton, "menu");
            StyleWindowButton(minimizeWindowButton, "minimize");
            StyleWindowButton(maximizeWindowButton, "maximize");
            StyleWindowButton(closeWindowButton, "close", true);

            CreateTabLines();

            homeTabButton.Click += homeTabButton_Click;
            settingsTabButton.Click += settingsTabButton_Click;

            // 榧犳爣绉诲埌鍝釜 Tab锛屽摢涓?Tab 涓存椂鍙樿摑
            homeTabButton.MouseEnter += (_, _) => ApplyTabVisual(Tab.Home);
            homeTabButton.MouseLeave += (_, _) => ApplyTabVisual(_currentTab);

            settingsTabButton.MouseEnter += (_, _) => ApplyTabVisual(Tab.Settings);
            settingsTabButton.MouseLeave += (_, _) => ApplyTabVisual(_currentTab);

            SetTabActive(Tab.Home);
        }

        /// <summary>
        /// 鍒涘缓椤堕儴 Tab 涓嬫柟鐨勫皬钃濇潯銆?
        /// </summary>
        private void CreateTabLines()
        {
            _homeTabLine = new RoundTabLine
            {
                Size = new Size(40, 6),
                LineColor = Color.FromArgb(37, 99, 235),
                Visible = false
            };

            _settingsTabLine = new RoundTabLine
            {
                Size = new Size(40, 6),
                LineColor = Color.FromArgb(37, 99, 235),
                Visible = false
            };

            titleBar.Controls.Add(_homeTabLine);
            titleBar.Controls.Add(_settingsTabLine);
        }

        /// <summary>
        /// 鏍规嵁鏂囧瓧瀹藉害鑷姩璋冩暣灏忚摑鏉￠暱搴︺€?
        /// 鎯宠灏忚摑鏉℃洿闀匡紝灏辨敼 +25 杩欎釜鏁般€?
        /// </summary>
        private void LayoutTabLines()
        {
            if (_homeTabLine == null || _settingsTabLine == null) return;

            int GetLineWidth(AntButton button)
            {
                using var g = button.CreateGraphics();
                var textSize = g.MeasureString(button.Text, button.Font);

                return Math.Max(34, (int)textSize.Width + 25);
            }

            _homeTabLine.Size = new Size(GetLineWidth(homeTabButton), 6);
            _settingsTabLine.Size = new Size(GetLineWidth(settingsTabButton), 6);

            _homeTabLine.Location = new Point(
                homeTabButton.Left + (homeTabButton.Width - _homeTabLine.Width) / 2,
                homeTabButton.Bottom - 5
            );

            _settingsTabLine.Location = new Point(
                settingsTabButton.Left + (settingsTabButton.Width - _settingsTabLine.Width) / 2,
                settingsTabButton.Bottom - 5
            );

            _homeTabLine.BringToFront();
            _settingsTabLine.BringToFront();

            _homeTabLine.Invalidate();
            _settingsTabLine.Invalidate();
        }

        private void SetTabActive(Tab tab)
        {
            _currentTab = tab;
            ApplyTabVisual(tab);
        }

        /// <summary>
        /// 鍒囨崲 Tab 鐨勮瑙夌姸鎬併€?
        /// 褰撳墠鎴栬€呴紶鏍囨偓鍋滅殑 Tab 鏄摑鑹诧紝鍙︿竴涓槸鐏拌壊銆?
        /// </summary>
        private void ApplyTabVisual(Tab visualTab)
        {
            bool homeBlue = visualTab == Tab.Home;
            bool settingsBlue = visualTab == Tab.Settings;

            StyleTopButton(homeTabButton, "主页", "home", homeBlue);
            StyleTopButton(settingsTabButton, "设置", "settings", settingsBlue);

            if (_homeTabLine != null)
            {
                _homeTabLine.Visible = homeBlue;
                _homeTabLine.BringToFront();
            }

            if (_settingsTabLine != null)
            {
                _settingsTabLine.Visible = settingsBlue;
                _settingsTabLine.BringToFront();
            }

            LayoutTabLines();
        }

        private static void StyleTopButton(AntButton button, string text, string iconName, bool blue)
        {
            Color blueColor = Color.FromArgb(37, 99, 235);
            Color grayColor = Color.FromArgb(100, 116, 139);

            Color currentColor = blue ? blueColor : grayColor;

            button.Text = text;
            button.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);

            button.ForeColor = currentColor;
            button.ForeHover = blueColor;

            button.OriginalBackColor = Color.White;
            button.BackColor = Color.White;
            button.BackHover = Color.White;
            button.BackActive = Color.White;

            button.Radius = 0;
            button.BorderWidth = 0;
            button.DefaultBorderColor = Color.Transparent;
            button.Ghost = false;
            button.WaveSize = 0;

            button.Padding = new Padding(8, 0, 8, 0);
            button.IconSize = new Size(16, 16);

            button.IconSvg = GetTabIconSvg(iconName, currentColor);
            button.IconHoverSvg = GetTabIconSvg(iconName, blueColor);
        }

        private static string GetTabIconSvg(string iconName, Color color)
        {
            string fill = ColorTranslator.ToHtml(color);

            return iconName switch
            {
                "home" =>
                    $"<svg viewBox=\"0 0 1024 1024\">" +
                    $"<path fill=\"{fill}\" d=\"M946.5 505L534.6 93.4a31.93 31.93 0 0 0-45.2 0L77.5 505c-12 12-18.8 28.3-18.8 45.3 0 35.3 28.7 64 64 64h43.4V908c0 17.7 14.3 32 32 32H448V716h128v224h265.9c17.7 0 32-14.3 32-32V614.3h43.4c17 0 33.3-6.7 45.3-18.8 24.9-25 24.9-65.5-.1-90.5z\"/>" +
                    $"</svg>",

                "settings" =>
                    $"<svg viewBox=\"0 0 1024 1024\">" +
                    $"<path fill=\"{fill}\" d=\"M924.8 625.7l-65.5-56c3.1-19 4.7-38.4 4.7-57.8s-1.6-38.8-4.7-57.8l65.5-56a32.03 32.03 0 0 0 9.3-35.2l-.9-2.6a442.81 442.81 0 0 0-79.7-137.9l-1.8-2.1a32.12 32.12 0 0 0-35.1-9.5l-81.3 28.9c-30-24.6-63.5-44-99.7-57.6l-15.7-85a32.05 32.05 0 0 0-25.8-25.7l-2.7-.5c-52.1-9.4-106.9-9.4-159 0l-2.7.5a32.05 32.05 0 0 0-25.8 25.7l-15.8 85.4a351.86 351.86 0 0 0-99 57.4l-81.9-29.1a32 32 0 0 0-35.1 9.5l-1.8 2.1a446.02 446.02 0 0 0-79.7 137.9l-.9 2.6c-4.5 12.5-.8 26.5 9.3 35.2l66.3 56.6c-3.1 18.8-4.6 38-4.6 57.1 0 19.2 1.5 38.4 4.6 57.1L99 625.5a32.03 32.03 0 0 0-9.3 35.2l.9 2.6c18.1 50.4 44.9 96.9 79.7 137.9l1.8 2.1a32.12 32.12 0 0 0 35.1 9.5l81.9-29.1c29.8 24.5 63.1 43.9 99 57.4l15.8 85.4a32.05 32.05 0 0 0 25.8 25.7l2.7.5a449.4 449.4 0 0 0 159 0l2.7-.5a32.05 32.05 0 0 0 25.8-25.7l15.7-85a351.86 351.86 0 0 0 99.7-57.6l81.3 28.9a32 32 0 0 0 35.1-9.5l1.8-2.1c34.8-41.1 61.6-87.5 79.7-137.9l.9-2.6c4.5-12.3.8-26.3-9.3-35zM512 668c-85.7 0-156-70.3-156-156s70.3-156 156-156 156 70.3 156 156-70.3 156-156 156z\"/>" +
                    $"</svg>",

                _ => ""
            };
        }

        private static string GetWindowIconSvg(string iconName, Color color)
        {
            string stroke = ColorTranslator.ToHtml(color);

            return iconName switch
            {
                "menu" =>
                    $"<svg viewBox=\"0 0 1024 1024\">" +
                    $"<path d=\"M224 320H800M224 512H800M224 704H800\" stroke=\"{stroke}\" stroke-width=\"78\" stroke-linecap=\"round\" fill=\"none\"/>" +
                    $"</svg>",

                "minimize" =>
                    $"<svg viewBox=\"0 0 1024 1024\">" +
                    $"<path d=\"M256 512H768\" stroke=\"{stroke}\" stroke-width=\"78\" stroke-linecap=\"round\" fill=\"none\"/>" +
                    $"</svg>",

                "maximize" =>
                    $"<svg viewBox=\"0 0 1024 1024\">" +
                    $"<rect x=\"256\" y=\"256\" width=\"512\" height=\"512\" rx=\"42\" ry=\"42\" stroke=\"{stroke}\" stroke-width=\"72\" fill=\"none\"/>" +
                    $"</svg>",

                "close" =>
                    $"<svg viewBox=\"0 0 1024 1024\">" +
                    $"<path d=\"M304 304L720 720M720 304L304 720\" stroke=\"{stroke}\" stroke-width=\"78\" stroke-linecap=\"round\" fill=\"none\"/>" +
                    $"</svg>",

                _ => ""
            };
        }

        private static void StyleWindowButton(AntButton button, string iconName, bool isClose = false)
        {
            Color normalColor = Color.FromArgb(30, 41, 59);
            Color hoverColor = isClose ? Color.White : Color.FromArgb(15, 23, 42);

            button.Text = "";
            button.Font = new Font("Segoe UI", 12F, FontStyle.Regular);

            button.ForeColor = normalColor;
            button.ForeHover = hoverColor;

            button.IconSize = new Size(17, 17);
            button.IconSvg = GetWindowIconSvg(iconName, normalColor);
            button.IconHoverSvg = GetWindowIconSvg(iconName, hoverColor);

            button.OriginalBackColor = Color.White;
            button.BackColor = Color.White;

            button.BackHover = isClose
                ? Color.FromArgb(239, 68, 68)
                : Color.FromArgb(241, 245, 249);

            button.BackActive = isClose
                ? Color.FromArgb(220, 38, 38)
                : Color.FromArgb(226, 232, 240);

            button.Radius = 8;
            button.BorderWidth = 0;
            button.WaveSize = 0;
            button.Ghost = false;
            button.DefaultBorderColor = Color.Transparent;
            button.Padding = new Padding(0);
        }

        private void homeTabButton_Click(object? sender, EventArgs e)
        {
            if (_currentTab == Tab.Home) return;

            SetTabActive(Tab.Home);

            contentPanel.Controls.Clear();

            if (_homePage == null)
            {
                _homePage = new HomePage
                {
                    Dock = DockStyle.Fill
                };

                _homePage.NetworkOfflineChanged += ToggleNetworkOverlay;
            }

            contentPanel.Controls.Add(_homePage);
        }

        private void settingsTabButton_Click(object? sender, EventArgs e)
        {
            if (_currentTab == Tab.Settings) return;

            SetTabActive(Tab.Settings);

            contentPanel.Controls.Clear();

            // 杩欓噷鍏堟斁涓€涓畝鍗曞崰浣嶃€?
            // 浠ュ悗浣犲啓 SettingsPage 鍚庯紝鎶婅繖閲屾浛鎹㈡垚 SettingsPage 鍗冲彲銆?
            var placeholder = new System.Windows.Forms.Label
            {
                Dock = DockStyle.Fill,
                Text = "设置页面开发中",
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.FromArgb(246, 249, 255),
                TextAlign = ContentAlignment.MiddleCenter
            };

            contentPanel.Controls.Add(placeholder);
        }

        /// <summary>
        /// 鍒涘缓鏂綉椤甸潰銆?
        /// 杩欓噷宸茬粡涓嶇敤鏃х殑 AntPanel 鍗＄墖浜嗭紝鐩存帴浣跨敤 NetworkOfflinePage銆?
        /// </summary>
        private void CreateNetworkOverlay()
        {
            _networkOverlay = new NetworkOfflinePage
            {
                // 启动时先盖住主页，等服务器检测成功后 HomePage 再通知隐藏。
                // 这样没开服务器/断网时，用户第一眼看到的就是断网页。
                Visible = true
            };

            _networkOverlay.RetryClicked += async () =>
            {
                if (_homePage != null)
                {
                    await TryReconnectFromOfflineAsync();
                }
            };

            _networkOverlay.DiagnoseClicked += (_, _) =>
            {
                MessageBox.Show(this, "这里可以接入网络诊断功能。", "网络诊断",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            Controls.Add(_networkOverlay);
            _networkOverlay.BringToFront();
            StartOfflineRetryTimer();
        }

        /// <summary>
        /// HomePage 妫€娴嬪埌缃戠粶寮傚父鏃讹紝浼氳皟鐢ㄨ繖涓柟娉曘€?
        /// </summary>
        private void ToggleNetworkOverlay(bool visible)
        {
            if (_networkOverlay == null) return;

            _networkOverlay.Visible = visible;

            if (visible)
            {
                _networkOverlay.BringToFront();
                StartOfflineRetryTimer();
            }
            else
            {
                StopOfflineRetryTimer();
                contentPanel.BringToFront();
                titleBar.BringToFront();
            }
        }

        private void StartOfflineRetryTimer()
        {
            if (_offlineRetryTimer != null)
            {
                _offlineRetryTimer.Start();
                return;
            }

            _offlineRetryTimer = new System.Windows.Forms.Timer
            {
                Interval = 3000
            };

            _offlineRetryTimer.Tick += async (_, _) => await TryReconnectFromOfflineAsync();
            _offlineRetryTimer.Start();
        }

        private void StopOfflineRetryTimer()
        {
            _offlineRetryTimer?.Stop();
            _offlineRetrying = false;
        }

        private async Task TryReconnectFromOfflineAsync()
        {
            if (_offlineRetrying || _homePage == null || _networkOverlay?.Visible != true)
                return;

            _offlineRetrying = true;
            try
            {
                await _homePage.RetryNetworkAsync();
            }
            finally
            {
                _offlineRetrying = false;
            }
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

            LayoutTabLines();
        }

        protected override void WndProc(ref WinMessage m)
        {
            const int wmNcHitTest = 0x0084;
            const int htClient = 1;
            const int htCaption = 2;

            base.WndProc(ref m);

            if (m.Msg != wmNcHitTest || (int)m.Result != htClient)
                return;

            var screenPoint = new Point(
                unchecked((short)(long)m.LParam),
                unchecked((short)((long)m.LParam >> 16))
            );

            var point = PointToClient(screenPoint);

            // 鍙湁鏍囬鏍忎腑闂寸┖鐧藉尯鍩熷彲浠ユ嫋鍔ㄧ獥鍙ｃ€?
            // 閬垮厤鎸夐挳銆乀ab 鍖哄煙琚綋鎴愭嫋鍔ㄥ尯銆?
            if (point.Y <= titleBar.Height && point.X > 320 && point.X < ClientSize.Width - 190)
            {
                m.Result = htCaption;
            }
        }
    }
}