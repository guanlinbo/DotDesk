using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntdUI;
using DotDesk.Client;
using DotDesk.Controller.Network;
using DotDesk.Core;
using DotDesk.Core.Models;
using DotDesk.Core.Network;

namespace DotDesk.App
{
    public partial class HomePage : UserControl
    {
        // ── 业务对象 ─────────────────────────────────────────────────
        private AutoStartService? _autoStart;
        private WebRtcReceiver? _receiver;
        private bool _connecting;
        private int? _lastServerLatencyMs;
        private static bool _autoStartInitialized;  // 静态标志，全局只初始化一次
        private System.Windows.Forms.Panel? _loadingOverlay;
        private System.Windows.Forms.Label? _loadingTitle;
        private System.Windows.Forms.Label? _loadingSubtitle;
        private ProgressBar? _loadingProgress;
        private System.Windows.Forms.Panel? _controlledNoticePanel;
        private System.Windows.Forms.Label? _controlledNoticeTitle;
        private System.Windows.Forms.Label? _controlledNoticeSubtitle;

        // ── 服务器地址 ───────────────────────────────────────────────
        private const string SERVER_WS = "ws://185.200.65.254:5000";
        private const string SERVER_HTTP = "http://185.200.65.254:5000";

        // ─────────────────────────────────────────────────────────────
        public HomePage()
        {
            InitializeComponent();
            InitUI();

            HandleDestroyed += HomePage_HandleDestroyed;
            HandleCreated += async (_, _) => await InitAutoStartAsync();
        }

        // ── UI 初始化 ─────────────────────────────────────────────────

        private void InitUI()
        {
            Size = new Size(900, 514);
            LayoutCompactHome();
            BackColor = Color.FromArgb(246, 249, 255);

            sidebarPanel.Back = Color.White;
            mainContentPanel.Back = Color.FromArgb(246, 249, 255);

            StyleCard(heroCardPanel, Color.FromArgb(30, 94, 255));
            StyleCard(deviceInfoCardPanel, Color.White);
            StyleCard(securityCardPanel, Color.FromArgb(238, 245, 255));
            StyleCard(connectCardPanel, Color.White);
            StyleCard(fileTransferCardPanel, Color.White);
            StyleCard(terminalCardPanel, Color.White);
            StyleCard(recentCardPanel, Color.White);
            StyleCard(settingsTipPanel, Color.FromArgb(235, 243, 255));

            heroTitleLabel.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            heroTitleLabel.Text = "你的桌面";
            heroTitleLabel.ForeColor = Color.White;
            heroTitleLabel.BackColor = Color.Transparent;
            heroSubtitleLabel.Font = new Font("Microsoft YaHei UI", 10F);
            heroSubtitleLabel.Text = "随时随地，安全访问";
            heroSubtitleLabel.ForeColor = Color.White;
            heroSubtitleLabel.BackColor = Color.Transparent;
            onlineDotLabel.BackColor = Color.FromArgb(37, 211, 102);
            onlineStatusLabel.Font = new Font("Microsoft YaHei UI", 10F);
            onlineStatusLabel.Text = "在线";
            onlineStatusLabel.ForeColor = Color.White;
            onlineStatusLabel.BackColor = Color.Transparent;

            lblIdTitle.Text = "ID";
            lblIdTitle.Font = new Font("Segoe UI", 10F);
            lblIdTitle.AutoSize = true;
            lblIdTitle.BackColor = Color.Transparent;
            lblIdTitle.ForeColor = Color.FromArgb(55, 65, 81);

            lblId.Font = new Font("Segoe UI", 15F, FontStyle.Regular);
            lblId.AutoSize = true;
            lblId.BackColor = Color.Transparent;
            lblId.ForeColor = Color.FromArgb(0, 96, 220);
            lblId.Text = DeviceCode.GetFormatted();
            lblId.MouseEnter += (_, _) => lblId.ForeColor = Color.FromArgb(22, 119, 255);
            lblId.MouseLeave += (_, _) => lblId.ForeColor = Color.FromArgb(0, 96, 220);

            lblPwdTitle.Text = "一次性密码";
            lblPwdTitle.Font = new Font("Microsoft YaHei UI", 10F);
            lblPwdTitle.AutoSize = true;
            lblPwdTitle.BackColor = Color.Transparent;
            lblPwdTitle.ForeColor = Color.FromArgb(75, 85, 99);

            lblPwd.Font = new Font("Segoe UI", 19F, FontStyle.Regular);
            lblPwd.AutoSize = true;
            lblPwd.BackColor = Color.Transparent;
            lblPwd.ForeColor = Color.FromArgb(0, 96, 220);
            lblPwd.Text = "------";
            lblPwd.MouseEnter += (_, _) => lblPwd.ForeColor = Color.FromArgb(22, 119, 255);
            lblPwd.MouseLeave += (_, _) => lblPwd.ForeColor = Color.FromArgb(0, 96, 220);

            connectionTitleLabel.BackColor = Color.Transparent;
            connectionTitleLabel.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            connectionTitleLabel.ForeColor = Color.FromArgb(17, 24, 39);
            connectionTitleLabel.Text = "连接远程桌面";
            connectionHelpLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            connectionHelpLabel.ForeColor = Color.FromArgb(107, 114, 128);
            connectionHelpLabel.BackColor = Color.Transparent;

            connectButton.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            connectButton.Text = "连接";
            connectButton.ForeColor = Color.White;
            connectButton.OriginalBackColor = Color.FromArgb(0, 96, 220);
            connectMenuButton.Font = new Font("Segoe UI", 18F);
            connectMenuButton.Text = "v";
            connectMenuButton.ForeColor = Color.FromArgb(15, 23, 42);

            remoteIdInput.Text = "";
            remoteIdInput.PlaceholderText = "输入对方 ID";
            remoteIdInput.Font = new Font("Microsoft YaHei UI", 15F);

            logInput.Text = "";
            logInput.Multiline = true;
            logInput.Font = new Font("Consolas", 9.5F);
            logInput.Visible = false;

            StyleNavButton(remoteControlNavButton, active: true);
            remoteControlNavButton.Text = "远程控制";
            StyleNavButton(deviceListNavButton, active: false);
            deviceListNavButton.Text = "设备列表";
            StyleNavButton(recentNavButton, active: false);
            recentNavButton.Text = "最近连接";
            StyleNavButton(favoritesNavButton, active: false);
            favoritesNavButton.Text = "收藏夹";
            StyleNavButton(inviteHelpNavButton, active: false);
            inviteHelpNavButton.Text = "邀请与帮助";

            copyIdButton.Click += (_, _) => Clipboard.SetText(DeviceCode.GetFormatted());
            refreshPasswordButton.Click += (_, _) =>
            {
                if (_autoStart == null) return;
                lblPwd.Text = _autoStart.RefreshPassword();
            };

            StyleFeatureCard(fileTransferIconLabel, fileTransferTitleLabel, fileTransferSubtitleLabel, Color.FromArgb(99, 102, 241));
            fileTransferIconLabel.Text = "F";
            fileTransferTitleLabel.Text = "文件传输";
            fileTransferSubtitleLabel.Text = "安全快速传输文件";
            StyleFeatureCard(terminalIconLabel, terminalTitleLabel, terminalSubtitleLabel, Color.FromArgb(16, 185, 129));
            terminalIconLabel.Text = ">";
            terminalTitleLabel.Text = "远程终端";
            terminalSubtitleLabel.Text = "访问远程命令行";
            recentTitleLabel.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            recentTitleLabel.Text = "最近连接";
            recentTitleLabel.BackColor = Color.Transparent;
            BuildRecentListPreview();
            settingsTipLabel.BackColor = Color.Transparent;
            settingsTipLabel.Text = "想要无人值守访问？试试设置访问密码";
            settingsTipLinkLabel.BackColor = Color.Transparent;
            settingsTipLinkLabel.Text = "前往设置 >";
            settingsTipLinkLabel.ForeColor = Color.FromArgb(0, 96, 220);

            securityIconLabel.BackColor = Color.FromArgb(52, 211, 153);
            securityIconLabel.ForeColor = Color.White;
            securityIconLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            securityIconLabel.Text = "✓";
            securityTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            securityTitleLabel.Text = "安全连接已启用";
            securityTitleLabel.BackColor = Color.Transparent;
            securitySubtitleLabel.ForeColor = Color.FromArgb(75, 85, 99);
            securitySubtitleLabel.Text = "端到端加密保护中";
            securitySubtitleLabel.BackColor = Color.Transparent;
            securityArrowLabel.Text = ">";
            securityArrowLabel.BackColor = Color.Transparent;

            CreateLoadingOverlay();
            CreateControlledNotice();
        }

        private void LayoutCompactHome()
        {
            sidebarPanel.Width = 260;
            mainContentPanel.Location = new Point(260, 0);
            mainContentPanel.Size = new Size(640, 514);

            heroCardPanel.Location = new Point(20, 18);
            heroCardPanel.Size = new Size(220, 104);
            heroTitleLabel.Location = new Point(20, 18);
            heroTitleLabel.Size = new Size(140, 28);
            heroSubtitleLabel.Location = new Point(20, 46);
            heroSubtitleLabel.Size = new Size(170, 22);
            onlineDotLabel.Location = new Point(20, 78);
            onlineStatusLabel.Location = new Point(42, 72);
            onlineStatusLabel.Size = new Size(150, 24);

            deviceInfoCardPanel.Location = new Point(20, 144);
            deviceInfoCardPanel.Size = new Size(220, 128);
            lblIdTitle.Location = new Point(18, 14);
            lblId.Location = new Point(18, 38);
            lblId.Size = new Size(150, 30);
            copyIdButton.Location = new Point(176, 40);
            lblPwdTitle.Location = new Point(18, 72);
            lblPwd.Location = new Point(18, 94);
            lblPwd.Size = new Size(120, 28);
            refreshPasswordButton.Location = new Point(148, 94);
            showPasswordButton.Location = new Point(180, 94);

            remoteControlNavButton.Location = new Point(20, 292);
            deviceListNavButton.Location = new Point(20, 332);
            recentNavButton.Location = new Point(20, 372);
            favoritesNavButton.Location = new Point(20, 412);
            inviteHelpNavButton.Location = new Point(20, 452);
            foreach (var button in new[] { remoteControlNavButton, deviceListNavButton, recentNavButton, favoritesNavButton, inviteHelpNavButton })
                button.Size = new Size(220, 34);

            securityCardPanel.Location = new Point(20, 486);
            securityCardPanel.Size = new Size(220, 56);
            securityIconLabel.Location = new Point(14, 13);
            securityIconLabel.Size = new Size(30, 30);
            securityTitleLabel.Location = new Point(56, 8);
            securityTitleLabel.Size = new Size(126, 22);
            securitySubtitleLabel.Visible = true;
            securitySubtitleLabel.Location = new Point(56, 30);
            securitySubtitleLabel.Size = new Size(126, 20);
            securityArrowLabel.Location = new Point(190, 15);

            connectCardPanel.Location = new Point(24, 26);
            connectCardPanel.Size = new Size(592, 178);
            connectionTitleLabel.Location = new Point(28, 22);
            connectionTitleLabel.Size = new Size(180, 34);
            connectionHelpLabel.Location = new Point(192, 28);
            remoteIdInput.Location = new Point(28, 66);
            remoteIdInput.Size = new Size(360, 48);
            connectButton.Location = new Point(398, 66);
            connectButton.Size = new Size(92, 48);
            connectMenuButton.Location = new Point(502, 66);
            connectMenuButton.Size = new Size(52, 48);
            fileTransferCardPanel.Location = new Point(28, 130);
            fileTransferCardPanel.Size = new Size(250, 38);
            terminalCardPanel.Location = new Point(304, 130);
            terminalCardPanel.Size = new Size(250, 38);
            fileTransferIconLabel.Location = new Point(12, 7);
            fileTransferIconLabel.Size = new Size(26, 24);
            fileTransferTitleLabel.Location = new Point(52, 4);
            fileTransferSubtitleLabel.Location = new Point(52, 22);
            fileTransferSubtitleLabel.Visible = true;
            terminalIconLabel.Location = new Point(12, 7);
            terminalIconLabel.Size = new Size(26, 24);
            terminalTitleLabel.Location = new Point(52, 4);
            terminalSubtitleLabel.Location = new Point(52, 22);
            terminalSubtitleLabel.Visible = true;

            recentCardPanel.Location = new Point(24, 222);
            recentCardPanel.Size = new Size(592, 222);
            recentTitleLabel.Location = new Point(22, 16);
            logInput.Location = new Point(22, 50);
            logInput.Size = new Size(548, 148);

            settingsTipPanel.Location = new Point(24, 462);
            settingsTipPanel.Size = new Size(592, 40);
            settingsTipLabel.Location = new Point(18, 10);
            settingsTipLabel.Size = new Size(390, 22);
            settingsTipLinkLabel.Location = new Point(476, 10);
            settingsTipLinkLabel.Size = new Size(94, 22);
            BuildRecentListPreview();
        }

        private static void StyleCard(AntdUI.Panel panel, Color backColor)
        {
            panel.Back = backColor;
            panel.BackColor = backColor;
        }

        private static void StyleNavButton(AntdUI.Button button, bool active)
        {
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Font = new Font("Microsoft YaHei UI", 10.5F, active ? FontStyle.Bold : FontStyle.Regular);
            button.ForeColor = active ? Color.FromArgb(0, 96, 220) : Color.FromArgb(55, 65, 81);
            button.OriginalBackColor = active ? Color.FromArgb(234, 242, 255) : Color.White;
        }

        private static void StyleFeatureCard(AntdUI.Label icon, AntdUI.Label title, AntdUI.Label subtitle, Color iconColor)
        {
            icon.BackColor = iconColor;
            icon.ForeColor = Color.White;
            icon.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            title.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            title.BackColor = Color.Transparent;
            subtitle.ForeColor = Color.FromArgb(107, 114, 128);
            subtitle.BackColor = Color.Transparent;
        }

        private void BuildRecentListPreview()
        {
            for (int i = recentCardPanel.Controls.Count - 1; i >= 0; i--)
            {
                if (Equals(recentCardPanel.Controls[i].Tag, "recent-preview"))
                    recentCardPanel.Controls.RemoveAt(i);
            }

            AddRecentRow("办公电脑", "192.168.1.10", "今天 14:30", "W", Color.FromArgb(37, 99, 235), 54);
            AddRecentRow("设计师电脑", "192.168.1.20", "昨天 09:12", "D", Color.FromArgb(16, 185, 129), 104);
            AddRecentRow("服务器-测试环境", "10.0.0.88", "3 天前", "S", Color.FromArgb(124, 58, 237), 154);
        }

        private void AddRecentRow(string name, string address, string time, string icon, Color iconColor, int y)
        {
            var row = new System.Windows.Forms.Panel
            {
                Tag = "recent-preview",
                BackColor = Color.White,
                Location = new Point(22, y),
                Size = new Size(Math.Max(520, recentCardPanel.Width - 44), 42)
            };

            var iconLabel = new System.Windows.Forms.Label
            {
                BackColor = iconColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Text = icon,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(12, 7),
                Size = new Size(28, 28)
            };

            var nameLabel = new System.Windows.Forms.Label
            {
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(17, 24, 39),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                Text = name,
                Location = new Point(54, 4),
                Size = new Size(210, 20)
            };

            var addressLabel = new System.Windows.Forms.Label
            {
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(107, 114, 128),
                Font = new Font("Segoe UI", 9F),
                Text = address,
                Location = new Point(54, 22),
                Size = new Size(180, 18)
            };

            var timeLabel = new System.Windows.Forms.Label
            {
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(107, 114, 128),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Text = time,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(row.Width - 150, 12),
                Size = new Size(104, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var moreLabel = new System.Windows.Forms.Label
            {
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(75, 85, 99),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Text = "...",
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(row.Width - 38, 8),
                Size = new Size(24, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            row.Controls.Add(iconLabel);
            row.Controls.Add(nameLabel);
            row.Controls.Add(addressLabel);
            row.Controls.Add(timeLabel);
            row.Controls.Add(moreLabel);
            recentCardPanel.Controls.Add(row);
            row.BringToFront();
        }

        // ── 被控端自动启动 ────────────────────────────────────────────

        private async Task InitAutoStartAsync()
        {
            if (_autoStartInitialized) return;
            _autoStartInitialized = true;

            var screen = Screen.PrimaryScreen!;
            _autoStart = new AutoStartService(SERVER_WS);

            _autoStart.OnLog += msg => AppendLog(msg);
            _autoStart.OnStatusChanged += status => SetStatus(status);
            _autoStart.OnFpsUpdate += fps => SetStatus($"推流中 {fps:F1} fps");

            _autoStart.OnConnected += () => BeginInvoke(() =>
            {
                SetStatus("控制端已连接");
                ShowControlledNotice();
                UpdatePassword();  // 连接成功后刷新密码
            });

            _autoStart.OnDisconnected += () => BeginInvoke(() =>
            {
                SetStatus("等待控制端连接...");
                HideControlledNotice();
                UpdatePassword();
            });

            await _autoStart.StartAsync(
                screen.Bounds.Width,
                screen.Bounds.Height,
                fps: 15);

            // 启动后显示密码
            BeginInvoke(() => UpdatePassword());
        }

        // ── 连接按钮（控制端）────────────────────────────────────────

        private async void connectButton_Click(object sender, EventArgs e)
        {
            if (_connecting) return;

            var code = DeviceCode.Normalize(remoteIdInput.Text);
            if (code.Length != 9)
            {
                AppendLog("⚠️ 请输入9位设备码");
                return;
            }

            if (code == DeviceCode.Get())
            {
                AppendLog("⚠️ 不能远程连接本机，请输入另一台设备的ID");
                MessageBox.Show(FindForm(), "不能远程连接本机，请输入另一台设备的ID。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var passwordDialog = new PasswordDialog();
            if (passwordDialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            var password = passwordDialog.Password;

            _connecting = true;
            connectButton.Enabled = false;
            AppendLog($"查询设备 {code}...");

            // Step1：查询设备是否在线
            var status = await DeviceChecker.CheckAsync(SERVER_HTTP, code);
            AppLogger.Log("HomePage", $"查询结果: Online={status.Online} Error={status.Error}");
            _lastServerLatencyMs = status.ServerLatencyMs;

            if (!status.Online)
            {
                AppendLog($"❌ 设备不在线（{status.Error ?? "无响应"}）");
                Reset();
                return;
            }

            AppendLog("✅ 设备在线，验证密码...");

            // Step2：创建接收器
            _receiver = new WebRtcReceiver(SERVER_WS, code);
            _receiver.OnLog += msg => AppendLog(msg);
            _receiver.OnConnectionStatus += status => BeginInvoke(() =>
            {
                AppendLog($"连接状态：{status}");
                ShowLoading(status, status.Contains("中继") ? "P2P失败，正在使用TURN兜底..." : "正在尝试直连...");
            });
            _receiver.OnConnectionFailed += msg => BeginInvoke(() =>
            {
                AppendLog($"❌ {msg}");
                MessageBox.Show(FindForm(),
                    $"{msg}\r\n\r\n当前已禁用服务器中继，只允许直连 P2P。",
                    "连接失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            });

            // Step3：密码验证结果
            _receiver.OnAuthSuccess += () => BeginInvoke(() =>
            {
                AppendLog("✅ 密码验证通过，建立连接...");
                ShowLoading("密码验证成功", "正在建立远程桌面连接...");
            });

            _receiver.OnAuthFailed += () => BeginInvoke(() =>
            {
                AppendLog("❌ 密码错误，连接已拒绝");
                HideLoading();
                CleanUpAsync();
                Reset();
            });

            // Step4：P2P 建立成功
            _receiver.OnConnected += () => BeginInvoke(() =>
            {
                AppendLog("✅ 连接成功，打开远程桌面");
                HideLoading();
                connectButton.Enabled = true;
                _connecting = false;
                ShowRemoteDesktop(code);
            });

            _receiver.OnDisconnected += () => BeginInvoke(() =>
            {
                AppendLog("⚠️ 连接已断开");
                HideLoading();
                CleanUpAsync();
                Reset();
            });

            // Step5：连接信令，连上后发送密码
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
                AppendLog($"❌ 连接失败：{ex.Message}");
                HideLoading();
                CleanUpAsync();
                Reset();
            }
        }

        // ── 弹出远程桌面 ─────────────────────────────────────────────

        private void ShowRemoteDesktop(string deviceCode)
        {
            var remoteName = _receiver?.RemoteDeviceName;
            var form = new RemoteDesktopControl(_receiver!, deviceCode, remoteName, _lastServerLatencyMs);
            form.FormClosed += (_, _) =>
            {
                AppendLog("远程桌面已关闭");
                CleanUpAsync();
                Reset();
            };
            form.Show();
        }

        // ── 辅助方法 ─────────────────────────────────────────────────

        private void UpdatePassword()
        {
            if (_autoStart == null) return;
            lblPwd.Text = _autoStart.Password;
        }

        private void SetStatus(string status)
        {
            if (InvokeRequired)
                BeginInvoke(() => onlineStatusLabel.Text = status);
            else
                onlineStatusLabel.Text = status;
        }

        private void CleanUp()
        {
            var receiver = Interlocked.Exchange(ref _receiver, null);
            if (receiver == null) return;

            receiver.Disconnect();
            receiver.Dispose();
        }

        private void CleanUpAsync()
        {
            var receiver = Interlocked.Exchange(ref _receiver, null);
            if (receiver == null) return;

            _ = Task.Run(() =>
            {
                try { receiver.Disconnect(); } catch { }
                try { receiver.Dispose(); } catch { }
            });
        }

        private void Reset()
        {
            _connecting = false;
            connectButton.Enabled = true;
            HideLoading();
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

        private void LayoutLoadingOverlay()
        {
            if (_loadingTitle == null || _loadingSubtitle == null || _loadingProgress == null) return;

            _loadingTitle.Width = mainContentPanel.Width;
            _loadingSubtitle.Width = mainContentPanel.Width;
            _loadingTitle.Location = new Point(0, Math.Max(120, mainContentPanel.Height / 2 - 76));
            _loadingSubtitle.Location = new Point(0, _loadingTitle.Bottom + 8);
            _loadingProgress.Location = new Point(
                Math.Max(24, (mainContentPanel.Width - _loadingProgress.Width) / 2),
                _loadingSubtitle.Bottom + 24);
        }

        private void CreateControlledNotice()
        {
            _controlledNoticePanel = new System.Windows.Forms.Panel
            {
                BackColor = Color.FromArgb(235, 245, 255),
                Location = new Point(24, 6),
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
            if (_controlledNoticePanel == null) return;

            _controlledNoticePanel.Visible = true;
            _controlledNoticePanel.BringToFront();
            AppendLog("⚠️ 当前设备正在被远程控制");
        }

        private void HideControlledNotice()
        {
            if (_controlledNoticePanel == null) return;

            _controlledNoticePanel.Visible = false;
        }

        private void ShowLoading(string title, string subtitle)
        {
            if (_loadingOverlay == null || _loadingTitle == null || _loadingSubtitle == null) return;

            _loadingTitle.Text = title;
            _loadingSubtitle.Text = subtitle;
            LayoutLoadingOverlay();
            _loadingOverlay.Visible = true;
            _loadingOverlay.BringToFront();
            _loadingProgress!.MarqueeAnimationSpeed = 28;
        }

        private void HideLoading()
        {
            if (_loadingOverlay == null) return;

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
            logInput.AppendText(line + "\r\n");
            logInput.ScrollToCaret();
        }

        private void HomePage_HandleDestroyed(object? sender, EventArgs e)
        {
            CleanUp();
            _autoStart?.Dispose();
        }

        private sealed class PasswordDialog : Form
        {
            private readonly TextBox _passwordInput;

            public string Password => _passwordInput.Text.Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();

            public PasswordDialog()
            {
                Text = "输入密码";
                ClientSize = new Size(360, 202);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                Font = new Font("Microsoft YaHei UI", 9F);
                BackColor = Color.White;

                var title = new System.Windows.Forms.Label
                {
                    AutoSize = false,
                    Text = "请输入对方的一次性密码",
                    Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Location = new Point(28, 24),
                    Size = new Size(304, 28)
                };

                var hint = new System.Windows.Forms.Label
                {
                    AutoSize = false,
                    Text = "密码为 6 位字母或数字，验证通过后会继续建立连接。",
                    ForeColor = Color.FromArgb(100, 116, 139),
                    Location = new Point(28, 58),
                    Size = new Size(304, 24)
                };

                _passwordInput = new TextBox
                {
                    Location = new Point(28, 94),
                    Size = new Size(304, 27),
                    MaxLength = 6,
                    PasswordChar = '*',
                    TextAlign = HorizontalAlignment.Center
                };

                var cancelButton = new System.Windows.Forms.Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(170, 148),
                    Size = new Size(76, 30)
                };

                var okButton = new System.Windows.Forms.Button
                {
                    Text = "连接",
                    DialogResult = DialogResult.OK,
                    Location = new Point(256, 148),
                    Size = new Size(76, 30)
                };

                _passwordInput.KeyPress += (_, e) =>
                {
                    if (!char.IsControl(e.KeyChar) && !char.IsLetterOrDigit(e.KeyChar))
                        e.Handled = true;
                };

                okButton.Click += (_, _) =>
                {
                    if (Password.Length == 6) return;

                    MessageBox.Show(this, "请输入6位字母或数字密码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _passwordInput.Focus();
                    _passwordInput.SelectAll();
                    DialogResult = DialogResult.None;
                };

                AcceptButton = okButton;
                CancelButton = cancelButton;
                Controls.Add(title);
                Controls.Add(hint);
                Controls.Add(_passwordInput);
                Controls.Add(cancelButton);
                Controls.Add(okButton);

                Shown += (_, _) => _passwordInput.Focus();
            }
        }
    }
}
