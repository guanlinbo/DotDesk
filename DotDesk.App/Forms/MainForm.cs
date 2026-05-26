using AntdUI;
using System.Runtime.CompilerServices;
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

        private DotDesk.App.RoundTabLine? _homeTabLine;
        private DotDesk.App.RoundTabLine? _settingsTabLine;

        
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
            DotDeskUi.ApplyFixedMainWindow(this);

            titleBar.BackColor = DotDeskUi.AppBackground;
            titleBar.MouseDown += DragWindow;

            appLogoLabel.Text = "D";
            appLogoLabel.ForeColor = Color.FromArgb(37, 99, 235);
            appLogoLabel.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
            appLogoLabel.MouseDown += DragWindow;

            contentPanel.BorderWidth = 0;

            // 鍙充笂瑙掔獥鍙ｆ寜閽紝鍏ㄩ儴浣跨敤 SVG锛屼笉鐢ㄦ枃瀛?
            DotDeskUi.StyleWindowButton(menuButton, "menu");
            DotDeskUi.StyleWindowButton(minimizeWindowButton, "minimize");
            DotDeskUi.StyleWindowButton(maximizeWindowButton, "maximize");
            DotDeskUi.StyleWindowButton(closeWindowButton, "close", true);

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
            _homeTabLine = new DotDesk.App.RoundTabLine
            {
                Size = new Size(40, 6),
                LineColor = Color.FromArgb(37, 99, 235),
                Visible = false
            };

            _settingsTabLine = new DotDesk.App.RoundTabLine
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

            DotDeskUi.StyleTopButton(homeTabButton, "主页", "home", homeBlue);
            DotDeskUi.StyleTopButton(settingsTabButton, "设置", "settings", settingsBlue);


            //homeTabButton.Back = DotDeskUi.AppBackground;
            homeTabButton.BackColor = DotDeskUi.AppBackground;

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
            // 主界面固定尺寸，保留按钮位置但不执行最大化。
            WindowState = FormWindowState.Normal;
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
