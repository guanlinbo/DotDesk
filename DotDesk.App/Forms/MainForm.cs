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

        // 新的断网页面：注意这里用的是 NetworkOfflinePage
        // 以后断网 UI 都去改 NetworkOfflinePage.cs，不要再改 MainForm 里的旧代码
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
        /// 顶部 Tab 下面的小蓝条。
        /// 不用普通 Panel，是因为普通 Panel 圆角不好控制。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            InitChrome();

            // 首页
            _homePage = new HomePage
            {
                Dock = DockStyle.Fill
            };

            // HomePage 只负责告诉 MainForm：现在断网了 / 恢复了
            _homePage.NetworkOfflineChanged += ToggleNetworkOverlay;

            contentPanel.Controls.Clear();
            contentPanel.Controls.Add(_homePage);

            // 创建新的断网页面，盖在主窗口上
            CreateNetworkOverlay();
        }

        /// <summary>
        /// 初始化窗口外壳：标题栏、Tab、窗口按钮。
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

            // 右上角窗口按钮，全部使用 SVG，不用文字
            DotDeskUi.StyleWindowButton(menuButton, "menu");
            DotDeskUi.StyleWindowButton(minimizeWindowButton, "minimize");
            DotDeskUi.StyleWindowButton(maximizeWindowButton, "maximize");
            DotDeskUi.StyleWindowButton(closeWindowButton, "close", true);

            CreateTabLines();

            homeTabButton.Click += homeTabButton_Click;
            settingsTabButton.Click += settingsTabButton_Click;

            // 鼠标移到哪个 Tab，哪个 Tab 临时变蓝
            homeTabButton.MouseEnter += (_, _) => ApplyTabVisual(Tab.Home);
            homeTabButton.MouseLeave += (_, _) => ApplyTabVisual(_currentTab);

            settingsTabButton.MouseEnter += (_, _) => ApplyTabVisual(Tab.Settings);
            settingsTabButton.MouseLeave += (_, _) => ApplyTabVisual(_currentTab);

            SetTabActive(Tab.Home);
        }

        /// <summary>
        /// 创建顶部 Tab 下方的小蓝条。
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
        /// 根据文字宽度自动调整小蓝条长度。
        /// 想让小蓝条更长，就改 +25 这个数。
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
        /// 切换 Tab 的视觉状态。
        /// 当前或者鼠标悬停的 Tab 是蓝色，另一个是灰色。
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

            // 这里先放一个简单占位。
            // 以后你写 SettingsPage 后，把这里替换成 SettingsPage 即可。
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
        /// 创建断网页面。
        /// 这里已经不用旧的 AntPanel 卡片了，直接使用 NetworkOfflinePage。
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
        /// HomePage 检测到网络异常时，会调用这个方法。
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

            // 只有标题栏中间空白区域可以拖动窗口。
            // 避免按钮、Tab 区域被当成拖动区。
            if (point.Y <= titleBar.Height && point.X > 320 && point.X < ClientSize.Width - 190)
            {
                m.Result = htCaption;
            }
        }
    }
}