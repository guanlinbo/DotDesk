using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using DotDesk.Client;
using DotDesk.Controller.Network;
using DotDesk.Core.Config;
using DotDesk.Core.Logging;
using DotDesk.Core.Utils;

namespace DotDesk.App
{
    public partial class HomePage : UserControl
    {
        private const int SidebarWidth = DotDeskUi.MainSidebarWidth;
        private const int ContentTopOffset = 70;

        public event Action<bool>? NetworkOfflineChanged;

        // ── 业务对象 ─────────────────────────────────────────────────
        private AutoStartService? _autoStart;
        private WebRtcReceiver? _receiver;
        private bool _connecting;
        private int? _lastServerLatencyMs;
        private static bool _autoStartInitialized;

        // ── 加载遮罩 ─────────────────────────────────────────────────
        private System.Windows.Forms.Panel? _loadingOverlay;
        private System.Windows.Forms.Label? _loadingTitle;
        private System.Windows.Forms.Label? _loadingSubtitle;
        private ProgressBar? _loadingProgress;

        // ── 被控提示 ─────────────────────────────────────────────────
        private System.Windows.Forms.Panel? _controlledNoticePanel;
        private System.Windows.Forms.Label? _controlledNoticeTitle;
        private System.Windows.Forms.Label? _controlledNoticeSubtitle;

        // 左侧栏
        private DotDeskSidebar? _sidebar;

        // 右侧内容区
        private DotDeskHomeContent? _homeContent;
        private LatencyFlashWindow? _latencyFlashWindow;

        // 复制提示
        private AntdUI.Panel? _copyToastPanel;
        private System.Windows.Forms.Label? _copyToastLabel;
        private System.Windows.Forms.Timer? _copyToastTimer;

        // ── 服务器地址 ───────────────────────────────────────────────
        private const string SERVER_WS = "ws://159.75.93.74:5000";
        private const string SERVER_HTTP = "http://159.75.93.74:5000";

        public HomePage()
        {
            InitializeComponent();

            InitUI();
            CreateCopyToast();

            HandleDestroyed += HomePage_HandleDestroyed;
            HandleCreated += async (_, _) => await InitAutoStartAsync();
        }

        // ── UI 初始化 ─────────────────────────────────────────────────

        private void InitUI()
        {
            Size = DotDeskUi.HomePageSize;
            BackColor = DotDeskUi.AppBackground;

            LayoutCompactHome();
            InitSidebar();
            InitHomeContent();

            CreateLoadingOverlay();
            CreateControlledNotice();
        }

        /// <summary>
        /// 初始化左侧栏。
        /// Designer 里面的 sidebarPanel 保留，只清空旧控件。
        /// </summary>
        private void InitSidebar()
        {
            sidebarPanel.Controls.Clear();
            sidebarPanel.Back = DotDeskUi.SidebarBackground;
            sidebarPanel.BackColor = DotDeskUi.SidebarBackground;

            _sidebar = new DotDeskSidebar
            {
                Dock = DockStyle.Fill
            };

            sidebarPanel.Controls.Add(_sidebar);
            _sidebar.BringToFront();

            // ID 动态获取，不写死
            _sidebar.SetId(DeviceCode.GetFormatted());

            // 密码启动前先显示占位
            _sidebar.SetPassword("------");

            _sidebar.CopyIdClicked += () =>
            {
                var inviteText =
                    $"设备ID：{DeviceCode.GetFormatted()}\r\n一次性密码：{_sidebar?.PasswordText ?? "------"}";

                Clipboard.SetText(inviteText);

                AppendLog("已复制邀请信息");
                ShowCopyToast("已复制邀请信息");
            };

            _sidebar.RefreshPasswordClicked += () =>
            {
                if (_autoStart == null)
                {
                    _sidebar.SetPassword("------");
                    AppendLog("服务未启动，暂时无法刷新密码");
                    return;
                }

                var password = _autoStart.RefreshPassword();
                _sidebar.SetPassword(password);
                AppendLog("已刷新一次性密码");
            };

            _sidebar.ShowPasswordClicked += () =>
            {
                ShowFixedPasswordDialog();
            };

            _sidebar.RemoteControlClicked += () =>
            {
                AppendLog("切换到远程控制");
            };

            _sidebar.DeviceListClicked += () =>
            {
                AppendLog("切换到设备列表");
            };

            _sidebar.RecentClicked += () =>
            {
                AppendLog("切换到最近连接");
            };
        }

        /// <summary>
        /// 初始化右侧内容区。
        /// 右侧 UI 已经抽离到 DotDeskHomeContent。
        /// </summary>
        private void InitHomeContent()
        {
            mainContentPanel.Controls.Clear();
            mainContentPanel.Back = DotDeskUi.AppBackground;
            mainContentPanel.BackColor = DotDeskUi.AppBackground;

            _homeContent = new DotDeskHomeContent
            {
                Dock = DockStyle.Fill
            };

            mainContentPanel.Controls.Add(_homeContent);
            _homeContent.BringToFront();

            _homeContent.ConnectClicked += () =>
            {
                connectButton_Click(_homeContent, EventArgs.Empty);
            };

            _homeContent.InviteParsed += () =>
            {
                AppendLog("已识别邀请信息，自动连接...");
                BeginInvoke(() => connectButton_Click(_homeContent, EventArgs.Empty));
            };

            _homeContent.ConnectMenuClicked += () =>
            {
                AppendLog("点击连接菜单");
            };

            _homeContent.FileTransferClicked += () =>
            {
                AppendLog("点击文件传输");
            };

            _homeContent.TerminalClicked += () =>
            {
                AppendLog("点击远程终端");
            };

            _homeContent.SettingsClicked += () =>
            {
                ShowFixedPasswordDialog();
            };
        }

        /// <summary>
        /// 只负责左右区域大小，不再摆放右侧旧控件。
        /// </summary>
        private void LayoutCompactHome()
        {
            sidebarPanel.Width = SidebarWidth;
            sidebarPanel.Location = new Point(0, 0);
            sidebarPanel.Height = Height;
            sidebarPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;

            mainContentPanel.Location = new Point(SidebarWidth, 0);
            mainContentPanel.Size = new Size(
                Math.Max(100, Width - SidebarWidth),
                Height);

            mainContentPanel.Anchor =
                AnchorStyles.Left |
                AnchorStyles.Top |
                AnchorStyles.Right |
                AnchorStyles.Bottom;
        }

        // ── 被控端自动启动 ────────────────────────────────────────────

        private async Task InitAutoStartAsync()
        {
            if (_autoStartInitialized)
                return;

            _autoStartInitialized = true;

            var screen = Screen.PrimaryScreen!;

            SetStatus("检测服务器连接...");
            AppendLog("检测服务器连接...");

            var serverStatus = await DeviceChecker.CheckServerAsync(SERVER_HTTP);
            _lastServerLatencyMs = serverStatus.ServerLatencyMs;

            if (!serverStatus.Reachable)
            {
                _autoStartInitialized = false;

                var error = serverStatus.Error ?? "服务器不可达";
                SetStatus($"连接失败: {error}");
                AppendLog($"服务器连接失败: {error}");
                ShowOfflineOverlay(error);
                return;
            }

            HideOfflineOverlay();

            _autoStart = new AutoStartService(SERVER_WS);
            _autoStart.OnLog += msg => AppendLog(msg);
            _autoStart.OnStatusChanged += status => SetStatus(status);
            _autoStart.OnFpsUpdate += fps => SetStatus($"推流中 {fps:F1} fps");

            _autoStart.OnConnected += () => BeginInvoke(() =>
            {
                SetStatus("控制端已连接");
                ShowControlledNotice();
                ShowLatencyFlashWindow();
                UpdatePassword();
            });

            _autoStart.OnDisconnected += () => BeginInvoke(() =>
            {
                SetStatus("等待控制端连接...");
                HideControlledNotice();
                CloseLatencyFlashWindow();
                UpdatePassword();
            });

            try
            {
                await _autoStart.StartAsync(
                    screen.Bounds.Width,
                    screen.Bounds.Height,
                    fps: 15);
            }
            catch (Exception ex)
            {
                _autoStartInitialized = false;

                var error = $"无法连接信令服务器: {ex.Message}";
                SetStatus($"连接失败: {error}");
                AppendLog(error);
                ShowOfflineOverlay(error);
                return;
            }

            BeginInvoke(() => UpdatePassword());
        }

        // ── 连接按钮（控制端）────────────────────────────────────────

        private async void connectButton_Click(object sender, EventArgs e)
        {
            if (_connecting)
                return;

            var code = DeviceCode.Normalize(_homeContent?.RemoteIdText ?? "");

            if (code.Length != 9)
            {
                AppendLog("请输入 9 位设备码");
                return;
            }

            if (code == DeviceCode.Get())
            {
                AppendLog("不能远程连接本机，请输入另一台设备的 ID");

                MessageBox.Show(
                    FindForm(),
                    "不能远程连接本机，请输入另一台设备的ID。",
                    "提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            _connecting = true;
            _homeContent?.SetConnectEnabled(false);

            AppendLog("检测服务器连接...");

            var serverStatus = await DeviceChecker.CheckServerAsync(SERVER_HTTP);
            _lastServerLatencyMs = serverStatus.ServerLatencyMs;

            if (!serverStatus.Reachable)
            {
                var error = serverStatus.Error ?? "服务器不可达";

                AppendLog($"服务器连接失败: {error}");
                ShowOfflineOverlay(error);
                Reset();
                return;
            }

            HideOfflineOverlay();

            string? invitePassword = _homeContent?.PendingInvitePassword;
            string password;
            if (!string.IsNullOrWhiteSpace(invitePassword) && invitePassword.Length == 6)
            {
                password = _homeContent!.ConsumePendingInvitePassword()!;
                AppendLog("已从邀请信息读取密码");
            }
            else
            {
                using var passwordDialog = new PasswordDialog();

                if (passwordDialog.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    Reset();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(passwordDialog.InviteDeviceCode)
                    && passwordDialog.InviteDeviceCode != code)
                {
                    code = passwordDialog.InviteDeviceCode;
                    AppendLog("已从邀请信息切换目标设备");
                    if (code == DeviceCode.Get())
                    {
                        AppendLog("不能远程连接本机，请输入另一台设备的 ID");
                        Reset();
                        return;
                    }
                }

                password = passwordDialog.Password;
            }

            AppendLog($"查询设备 {code}...");

            var status = await DeviceChecker.CheckAsync(SERVER_HTTP, code);
            AppLogger.Log("HomePage", $"查询结果: Online={status.Online} Error={status.Error}");
            _lastServerLatencyMs = status.ServerLatencyMs;

            if (!status.Online)
            {
                AppendLog($"设备不在线（{status.Error ?? "无响应"}）");
                Reset();
                return;
            }

            AppendLog("设备在线，验证密码...");

            _receiver = new WebRtcReceiver(SERVER_WS, code);
            _receiver.OnLog += msg => AppendLog(msg);

            _receiver.OnConnectionStatus += statusText => BeginInvoke(() =>
            {
                AppendLog($"连接状态：{statusText}");

                ShowLoading(
                    statusText,
                    statusText.Contains("中继")
                        ? "P2P失败，正在使用TURN兜底..."
                        : "正在尝试直连...");
            });

            _receiver.OnConnectionFailed += msg => BeginInvoke(() =>
            {
                AppendLog(msg);
                HideLoading();
                CleanUpAsync();
                Reset();

                MessageBox.Show(
                    FindForm(),
                    $"{msg}\r\n\r\n已尝试 TURN 中继兜底，但中继连接未建立。DataChannelDotnet/libjuice 需要标准 TURN long-term credential；请确认服务器启用 lt-cred-mech、user=dotdesk:DotDesk2025，且 Allocate 响应包含 MESSAGE-INTEGRITY。",
                    "连接失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            });

            _receiver.OnAuthSuccess += () => BeginInvoke(() =>
            {
                AppendLog("密码验证通过，建立连接...");
                ShowLoading("密码验证成功", "正在建立远程桌面连接...");
            });

            _receiver.OnAuthFailed += () => BeginInvoke(() =>
            {
                AppendLog("密码错误，连接已拒绝");
                HideLoading();
                CleanUpAsync();
                Reset();
            });

            _receiver.OnConnected += () => BeginInvoke(() =>
            {
                AppendLog("连接成功，打开远程桌面");
                HideLoading();

                _homeContent?.SetConnectEnabled(true);
                _connecting = false;

                ShowRemoteDesktop(code);
            });

            _receiver.OnDisconnected += () => BeginInvoke(() =>
            {
                AppendLog("连接已断开");
                HideLoading();
                CleanUpAsync();
                Reset();
            });

            _receiver.OnPeerJoined2 += () => BeginInvoke(() =>
            {
                AppendLog("发送密码验证...");
                _receiver?.SendPassword(password);
            });

            try
            {
                await _receiver.ConnectAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"连接失败：{ex.Message}");
                HideLoading();
                CleanUpAsync();
                Reset();
            }
        }

        // ── 弹出远程桌面 ─────────────────────────────────────────────

        private void ShowRemoteDesktop(string deviceCode)
        {
            try
            {
                var remoteName = _receiver?.RemoteDeviceName;

                DotDeskSettingsStore.AddRecentConnection(
                    deviceCode,
                    remoteName,
                    DotDeskSettingsStore.FormatCode(deviceCode));

                _homeContent?.RefreshRecentList();

                var form = new RemoteDesktopControl(
                    _receiver!,
                    deviceCode,
                    remoteName,
                    _lastServerLatencyMs);

                form.FormClosed += (_, _) =>
                {
                    AppendLog("远程桌面已关闭");
                    CleanUpAsync();
                    Reset();
                };

                form.Show(this.FindForm());
                form.Activate();
                form.BringToFront();
            }
            catch (Exception ex)
            {
                AppendLog($"打开远程桌面失败：{ex.Message}");
                AppLogger.Log("HomePage", $"ShowRemoteDesktop failed: {ex}");
                MessageBox.Show(
                    FindForm(),
                    $"打开远程桌面失败：{ex.Message}",
                    "连接失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                CleanUpAsync();
                Reset();
            }
        }

        // ── 辅助方法 ─────────────────────────────────────────────────

        private void UpdatePassword()
        {
            if (_autoStart == null)
            {
                _sidebar?.SetPassword("------");
                return;
            }

            _sidebar?.SetPassword(_autoStart.Password);
        }

        private void SetStatus(string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetStatus(status));
                return;
            }

            // 左上蓝色卡片固定显示：
            // 你的桌面 / 随时随地，安全访问 / 在线
            // 不显示“等待控制端 / 控制端已连接 / 推流中”等状态。

            if (status.StartsWith("连接失败", StringComparison.Ordinal))
            {
                ShowOfflineOverlay(status);
            }
            else
            {
                HideOfflineOverlay();
            }
        }

        private void CleanUp()
        {
            CloseLatencyFlashWindow();
            var receiver = Interlocked.Exchange(ref _receiver, null);

            if (receiver == null)
                return;

            receiver.Disconnect();
            receiver.Dispose();
        }

        private void CleanUpAsync()
        {
            var receiver = Interlocked.Exchange(ref _receiver, null);

            if (receiver == null)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    receiver.Disconnect();
                }
                catch
                {
                }

                try
                {
                    receiver.Dispose();
                }
                catch
                {
                }
            });
        }

        private void Reset()
        {
            _connecting = false;
            _homeContent?.SetConnectEnabled(true);
            HideLoading();
        }

        private void ShowFixedPasswordDialog()
        {
            using var dialog = new FixedPasswordDialog(DotDeskSettingsStore.Load().FixedPassword);

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            var fixedPassword = DotDeskSettingsStore.UpdateFixedPassword(dialog.FixedPassword);
            var password = _autoStart?.SetFixedPassword(fixedPassword) ?? "------";

            _sidebar?.SetPassword(password);

            AppendLog(fixedPassword == null
                ? "已恢复随机临时密码"
                : "已设置固定访问密码");
        }

        private void CreateLoadingOverlay()
        {
            _loadingOverlay = new System.Windows.Forms.Panel
            {
                BackColor = Color.FromArgb(248, 250, 252),
                Dock = DockStyle.Fill,
                Visible = false
            };

            _loadingTitle = new System.Windows.Forms.Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(0, 196),
                Size = new Size(mainContentPanel.Width, 34),
                Text = "正在连接",
                TextAlign = ContentAlignment.MiddleCenter
            };

            _loadingSubtitle = new System.Windows.Forms.Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(0, 236),
                Size = new Size(mainContentPanel.Width, 28),
                Text = "正在建立远程桌面连接...",
                TextAlign = ContentAlignment.MiddleCenter
            };

            _loadingProgress = new ProgressBar
            {
                Location = new Point((mainContentPanel.Width - 220) / 2, 286),
                Size = new Size(220, 12),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28
            };

            _loadingOverlay.Controls.Add(_loadingTitle);
            _loadingOverlay.Controls.Add(_loadingSubtitle);
            _loadingOverlay.Controls.Add(_loadingProgress);

            mainContentPanel.Controls.Add(_loadingOverlay);
            _loadingOverlay.BringToFront();

            mainContentPanel.Resize += (_, _) => LayoutLoadingOverlay();
            LayoutLoadingOverlay();
        }

        private void ShowOfflineOverlay(string detail)
        {
            HideLoading();
            NetworkOfflineChanged?.Invoke(true);
        }

        private void HideOfflineOverlay()
        {
            NetworkOfflineChanged?.Invoke(false);
        }

        public async Task RetryNetworkAsync()
        {
            _autoStartInitialized = false;

            _autoStart?.Dispose();
            _autoStart = null;

            _sidebar?.SetPassword("------");

            await InitAutoStartAsync();
        }

        private async Task RetryAutoStartAsync()
        {
            await RetryNetworkAsync();
        }

        private void LayoutLoadingOverlay()
        {
            if (_loadingTitle == null ||
                _loadingSubtitle == null ||
                _loadingProgress == null)
            {
                return;
            }

            _loadingTitle.Width = mainContentPanel.Width;
            _loadingSubtitle.Width = mainContentPanel.Width;

            _loadingTitle.Location = new Point(
                0,
                Math.Max(120, mainContentPanel.Height / 2 - 76));

            _loadingSubtitle.Location = new Point(
                0,
                _loadingTitle.Bottom + 8);

            _loadingProgress.Location = new Point(
                Math.Max(24, (mainContentPanel.Width - _loadingProgress.Width) / 2),
                _loadingSubtitle.Bottom + 24);
        }

        private void CreateControlledNotice()
        {
            _controlledNoticePanel = new System.Windows.Forms.Panel
            {
                BackColor = Color.FromArgb(235, 245, 255),
                Location = new Point(24, ContentTopOffset + 6),
                Size = new Size(592, 48),
                Visible = false
            };

            var icon = new System.Windows.Forms.Label
            {
                AutoSize = false,
                BackColor = Color.FromArgb(0, 96, 220),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(14, 10),
                Size = new Size(28, 28),
                Text = "!",
                TextAlign = ContentAlignment.MiddleCenter
            };

            _controlledNoticeTitle = new System.Windows.Forms.Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(15, 23, 42),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                Location = new Point(54, 7),
                Size = new Size(340, 22),
                Text = "正在被远程控制"
            };

            _controlledNoticeSubtitle = new System.Windows.Forms.Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Location = new Point(54, 27),
                Size = new Size(360, 18),
                Text = "当前桌面正在共享，请勿输入敏感信息。"
            };

            var stopHint = new System.Windows.Forms.Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(0, 96, 220),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Location = new Point(430, 14),
                Size = new Size(136, 22),
                Text = "关闭程序可断开",
                TextAlign = ContentAlignment.MiddleRight
            };

            _controlledNoticePanel.Controls.Add(icon);
            _controlledNoticePanel.Controls.Add(_controlledNoticeTitle);
            _controlledNoticePanel.Controls.Add(_controlledNoticeSubtitle);
            _controlledNoticePanel.Controls.Add(stopHint);

            mainContentPanel.Controls.Add(_controlledNoticePanel);
            _controlledNoticePanel.BringToFront();
        }

        private void ShowControlledNotice()
        {
            if (_controlledNoticePanel == null)
                return;

            _controlledNoticePanel.Visible = true;
            _controlledNoticePanel.BringToFront();

            AppendLog("当前设备正在被远程控制");
        }

        private void HideControlledNotice()
        {
            if (_controlledNoticePanel == null)
                return;

            _controlledNoticePanel.Visible = false;
        }

        private void ShowLatencyFlashWindow()
        {
            if (_latencyFlashWindow is { IsDisposed: false })
                return;

            _latencyFlashWindow = new LatencyFlashWindow();
            _latencyFlashWindow.Show(FindForm());
        }

        private void CloseLatencyFlashWindow()
        {
            var window = _latencyFlashWindow;
            _latencyFlashWindow = null;
            if (window == null || window.IsDisposed)
                return;

            window.Close();
        }

        private void ShowLoading(string title, string subtitle)
        {
            if (_loadingOverlay == null ||
                _loadingTitle == null ||
                _loadingSubtitle == null ||
                _loadingProgress == null)
            {
                return;
            }

            _loadingTitle.Text = title;
            _loadingSubtitle.Text = subtitle;

            LayoutLoadingOverlay();

            _loadingOverlay.Visible = true;
            _loadingOverlay.BringToFront();

            _loadingProgress.MarqueeAnimationSpeed = 28;
        }

        private void HideLoading()
        {
            if (_loadingOverlay == null)
                return;

            if (_loadingProgress != null)
                _loadingProgress.MarqueeAnimationSpeed = 0;

            _loadingOverlay.Visible = false;
        }

        private void AppendLog(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";

            if (InvokeRequired)
                BeginInvoke(() => AppendLogUI(line));
            else
                AppendLogUI(line);
        }

        private void AppendLogUI(string line)
        {
            // 原来的 logInput 是 Designer 旧控件。
            // 右侧抽离后它不显示，但保留日志写入不会影响业务。
            if (logInput == null)
                return;

            logInput.AppendText(line + "\r\n");
            logInput.ScrollToCaret();
        }

        private void HomePage_HandleDestroyed(object? sender, EventArgs e)
        {
            CleanUp();
            _autoStart?.Dispose();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (sidebarPanel != null && mainContentPanel != null)
            {
                sidebarPanel.Width = SidebarWidth;
                sidebarPanel.Height = Height;

                mainContentPanel.Location = new Point(SidebarWidth, 0);
                mainContentPanel.Size = new Size(
                    Math.Max(100, Width - SidebarWidth),
                    Height);
            }

            LayoutLoadingOverlay();
            LayoutCopyToast();
        }

        private void CreateCopyToast()
        {
            _copyToastPanel = new AntdUI.Panel
            {
                Size = new Size(180, 48),
                Radius = 10,
                Back = Color.FromArgb(240, 253, 244),
                BackColor = Color.FromArgb(240, 253, 244),
                BorderWidth = 1,
                BorderColor = Color.FromArgb(134, 239, 172),
                Shadow = 0,
                ShadowOpacity = 0f,
                Visible = false
            };

            var iconLabel = new System.Windows.Forms.Label
            {
                Text = "✔",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(34, 197, 94),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(14, 12),
                Size = new Size(22, 22)
            };

            _copyToastLabel = new System.Windows.Forms.Label
            {
                Text = "已复制邀请信息",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 101, 52),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(46, 12),
                Size = new Size(120, 22)
            };

            _copyToastPanel.Controls.Add(iconLabel);
            _copyToastPanel.Controls.Add(_copyToastLabel);

            Controls.Add(_copyToastPanel);
            _copyToastPanel.BringToFront();

            _copyToastTimer = new System.Windows.Forms.Timer
            {
                Interval = 1600
            };

            _copyToastTimer.Tick += (_, _) =>
            {
                HideCopyToast();
            };

            LayoutCopyToast();
        }

        private void ShowCopyToast(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => ShowCopyToast(text));
                return;
            }

            if (_copyToastPanel == null || _copyToastLabel == null || _copyToastTimer == null)
                return;

            _copyToastLabel.Text = text;

            LayoutCopyToast();

            _copyToastTimer.Stop();
            _copyToastPanel.Visible = true;
            _copyToastPanel.BringToFront();

            Controls.SetChildIndex(_copyToastPanel, 0);

            _copyToastTimer.Start();
        }

        private void HideCopyToast()
        {
            if (_copyToastPanel == null || _copyToastTimer == null)
                return;

            _copyToastTimer.Stop();
            _copyToastPanel.Visible = false;
        }

        private void LayoutCopyToast()
        {
            if (_copyToastPanel == null)
                return;

            _copyToastPanel.Location = new Point(
                Math.Max(0, (Width - _copyToastPanel.Width) / 2),
                90);

            _copyToastPanel.BringToFront();
        }
    }
}
