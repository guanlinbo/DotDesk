namespace DotDesk.App
{
    partial class HomePage
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        private void InitializeComponent()
        {
            sidebarPanel = new AntdUI.Panel();
            securityCardPanel = new AntdUI.Panel();
            securityArrowLabel = new AntdUI.Label();
            securitySubtitleLabel = new AntdUI.Label();
            securityTitleLabel = new AntdUI.Label();
            securityIconLabel = new AntdUI.Label();
            recentNavButton = new AntdUI.Button();
            deviceListNavButton = new AntdUI.Button();
            remoteControlNavButton = new AntdUI.Button();
            deviceInfoCardPanel = new AntdUI.Panel();
            showPasswordButton = new AntdUI.Button();
            refreshPasswordButton = new AntdUI.Button();
            copyIdButton = new AntdUI.Button();
            lblPwd = new AntdUI.Label();
            lblPwdTitle = new AntdUI.Label();
            lblId = new AntdUI.Label();
            lblIdTitle = new AntdUI.Label();
            heroCardPanel = new AntdUI.Panel();
            onlineStatusLabel = new AntdUI.Label();
            onlineDotLabel = new AntdUI.Label();
            heroSubtitleLabel = new AntdUI.Label();
            heroTitleLabel = new AntdUI.Label();
            mainContentPanel = new AntdUI.Panel();
            settingsTipPanel = new AntdUI.Panel();
            settingsTipLinkLabel = new AntdUI.Label();
            settingsTipLabel = new AntdUI.Label();
            recentCardPanel = new AntdUI.Panel();
            logInput = new AntdUI.Input();
            recentTitleLabel = new AntdUI.Label();
            connectCardPanel = new AntdUI.Panel();
            terminalCardPanel = new AntdUI.Panel();
            terminalSubtitleLabel = new AntdUI.Label();
            terminalTitleLabel = new AntdUI.Label();
            terminalIconLabel = new AntdUI.Label();
            fileTransferCardPanel = new AntdUI.Panel();
            fileTransferSubtitleLabel = new AntdUI.Label();
            fileTransferTitleLabel = new AntdUI.Label();
            fileTransferIconLabel = new AntdUI.Label();
            connectMenuButton = new AntdUI.Button();
            connectButton = new AntdUI.Button();
            remoteIdInput = new AntdUI.Input();
            connectionHelpLabel = new AntdUI.Label();
            connectionTitleLabel = new AntdUI.Label();
            sidebarPanel.SuspendLayout();
            securityCardPanel.SuspendLayout();
            deviceInfoCardPanel.SuspendLayout();
            heroCardPanel.SuspendLayout();
            mainContentPanel.SuspendLayout();
            settingsTipPanel.SuspendLayout();
            recentCardPanel.SuspendLayout();
            connectCardPanel.SuspendLayout();
            terminalCardPanel.SuspendLayout();
            fileTransferCardPanel.SuspendLayout();
            SuspendLayout();
            // 
            // sidebarPanel
            // 
            sidebarPanel.Controls.Add(securityCardPanel);
            sidebarPanel.Controls.Add(recentNavButton);
            sidebarPanel.Controls.Add(deviceListNavButton);
            sidebarPanel.Controls.Add(remoteControlNavButton);
            sidebarPanel.Controls.Add(deviceInfoCardPanel);
            sidebarPanel.Controls.Add(heroCardPanel);
            sidebarPanel.Dock = DockStyle.Left;
            sidebarPanel.Location = new Point(0, 0);
            sidebarPanel.Margin = new Padding(0);
            sidebarPanel.Name = "sidebarPanel";
            sidebarPanel.Size = new Size(330, 560);
            sidebarPanel.TabIndex = 0;
            // 
            // securityCardPanel
            // 
            securityCardPanel.Controls.Add(securityArrowLabel);
            securityCardPanel.Controls.Add(securitySubtitleLabel);
            securityCardPanel.Controls.Add(securityTitleLabel);
            securityCardPanel.Controls.Add(securityIconLabel);
            securityCardPanel.Location = new Point(24, 604);
            securityCardPanel.Name = "securityCardPanel";
            securityCardPanel.Size = new Size(282, 86);
            securityCardPanel.TabIndex = 7;
            // 
            // securityArrowLabel
            // 
            securityArrowLabel.Location = new Point(248, 1);
            securityArrowLabel.Name = "securityArrowLabel";
            securityArrowLabel.Size = new Size(24, 28);
            securityArrowLabel.TabIndex = 3;
            securityArrowLabel.Text = "›";
            securityArrowLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // securitySubtitleLabel
            // 
            securitySubtitleLabel.Location = new Point(72, 48);
            securitySubtitleLabel.Name = "securitySubtitleLabel";
            securitySubtitleLabel.Size = new Size(168, 22);
            securitySubtitleLabel.TabIndex = 2;
            securitySubtitleLabel.Text = "端到端加密保护中";
            // 
            // securityTitleLabel
            // 
            securityTitleLabel.Location = new Point(72, 5);
            securityTitleLabel.Name = "securityTitleLabel";
            securityTitleLabel.Size = new Size(160, 26);
            securityTitleLabel.TabIndex = 1;
            securityTitleLabel.Text = "安全连接已启用";
            // 
            // securityIconLabel
            // 
            securityIconLabel.Location = new Point(22, -6);
            securityIconLabel.Name = "securityIconLabel";
            securityIconLabel.Size = new Size(38, 38);
            securityIconLabel.TabIndex = 0;
            securityIconLabel.Text = "✓";
            securityIconLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // recentNavButton
            // 
            recentNavButton.Location = new Point(24, 460);
            recentNavButton.Name = "recentNavButton";
            recentNavButton.Size = new Size(282, 44);
            recentNavButton.TabIndex = 4;
            recentNavButton.Text = "最近连接";
            // 
            // deviceListNavButton
            // 
            deviceListNavButton.Location = new Point(24, 414);
            deviceListNavButton.Name = "deviceListNavButton";
            deviceListNavButton.Size = new Size(282, 44);
            deviceListNavButton.TabIndex = 3;
            deviceListNavButton.Text = "设备列表";
            // 
            // remoteControlNavButton
            // 
            remoteControlNavButton.Location = new Point(24, 368);
            remoteControlNavButton.Name = "remoteControlNavButton";
            remoteControlNavButton.Size = new Size(282, 44);
            remoteControlNavButton.TabIndex = 2;
            remoteControlNavButton.Text = "远程控制";
            // 
            // deviceInfoCardPanel
            // 
            deviceInfoCardPanel.Controls.Add(showPasswordButton);
            deviceInfoCardPanel.Controls.Add(refreshPasswordButton);
            deviceInfoCardPanel.Controls.Add(copyIdButton);
            deviceInfoCardPanel.Controls.Add(lblPwd);
            deviceInfoCardPanel.Controls.Add(lblPwdTitle);
            deviceInfoCardPanel.Controls.Add(lblId);
            deviceInfoCardPanel.Controls.Add(lblIdTitle);
            deviceInfoCardPanel.Location = new Point(24, 172);
            deviceInfoCardPanel.Name = "deviceInfoCardPanel";
            deviceInfoCardPanel.Size = new Size(282, 142);
            deviceInfoCardPanel.TabIndex = 1;
            // 
            // showPasswordButton
            // 
            showPasswordButton.Location = new Point(238, 96);
            showPasswordButton.Name = "showPasswordButton";
            showPasswordButton.Size = new Size(28, 28);
            showPasswordButton.TabIndex = 6;
            showPasswordButton.Text = "👁";
            // 
            // refreshPasswordButton
            // 
            refreshPasswordButton.Location = new Point(198, 96);
            refreshPasswordButton.Name = "refreshPasswordButton";
            refreshPasswordButton.Size = new Size(28, 28);
            refreshPasswordButton.TabIndex = 5;
            refreshPasswordButton.Text = "↻";
            // 
            // copyIdButton
            // 
            copyIdButton.Location = new Point(234, 48);
            copyIdButton.Name = "copyIdButton";
            copyIdButton.Size = new Size(32, 32);
            copyIdButton.TabIndex = 4;
            copyIdButton.Text = "⧉";
            // 
            // lblPwd
            // 
            lblPwd.Location = new Point(22, 96);
            lblPwd.Name = "lblPwd";
            lblPwd.Size = new Size(160, 30);
            lblPwd.TabIndex = 3;
            lblPwd.Text = "------";
            // 
            // lblPwdTitle
            // 
            lblPwdTitle.Location = new Point(22, 74);
            lblPwdTitle.Name = "lblPwdTitle";
            lblPwdTitle.Size = new Size(110, 22);
            lblPwdTitle.TabIndex = 2;
            lblPwdTitle.Text = "一次性密码";
            // 
            // lblId
            // 
            lblId.Location = new Point(22, 42);
            lblId.Name = "lblId";
            lblId.Size = new Size(190, 34);
            lblId.TabIndex = 1;
            lblId.Text = "000 000 000";
            // 
            // lblIdTitle
            // 
            lblIdTitle.Location = new Point(22, 20);
            lblIdTitle.Name = "lblIdTitle";
            lblIdTitle.Size = new Size(48, 22);
            lblIdTitle.TabIndex = 0;
            lblIdTitle.Text = "ID";
            // 
            // heroCardPanel
            // 
            heroCardPanel.Controls.Add(onlineStatusLabel);
            heroCardPanel.Controls.Add(onlineDotLabel);
            heroCardPanel.Controls.Add(heroSubtitleLabel);
            heroCardPanel.Controls.Add(heroTitleLabel);
            heroCardPanel.Location = new Point(24, 20);
            heroCardPanel.Name = "heroCardPanel";
            heroCardPanel.Size = new Size(282, 128);
            heroCardPanel.TabIndex = 0;
            // 
            // onlineStatusLabel
            // 
            onlineStatusLabel.Location = new Point(44, 86);
            onlineStatusLabel.Name = "onlineStatusLabel";
            onlineStatusLabel.Size = new Size(80, 24);
            onlineStatusLabel.TabIndex = 3;
            onlineStatusLabel.Text = "在线";
            // 
            // onlineDotLabel
            // 
            onlineDotLabel.Location = new Point(24, 90);
            onlineDotLabel.Name = "onlineDotLabel";
            onlineDotLabel.Size = new Size(12, 12);
            onlineDotLabel.TabIndex = 2;
            onlineDotLabel.Text = "";
            // 
            // heroSubtitleLabel
            // 
            heroSubtitleLabel.Location = new Point(24, 54);
            heroSubtitleLabel.Name = "heroSubtitleLabel";
            heroSubtitleLabel.Size = new Size(180, 26);
            heroSubtitleLabel.TabIndex = 1;
            heroSubtitleLabel.Text = "随时随地，安全访问";
            // 
            // heroTitleLabel
            // 
            heroTitleLabel.Location = new Point(24, 24);
            heroTitleLabel.Name = "heroTitleLabel";
            heroTitleLabel.Size = new Size(150, 34);
            heroTitleLabel.TabIndex = 0;
            heroTitleLabel.Text = "你的桌面";
            // 
            // mainContentPanel
            // 
            mainContentPanel.Controls.Add(settingsTipPanel);
            mainContentPanel.Controls.Add(recentCardPanel);
            mainContentPanel.Controls.Add(connectCardPanel);
            mainContentPanel.Dock = DockStyle.Fill;
            mainContentPanel.Location = new Point(330, 0);
            mainContentPanel.Margin = new Padding(0);
            mainContentPanel.Name = "mainContentPanel";
            mainContentPanel.Size = new Size(670, 560);
            mainContentPanel.TabIndex = 1;
            // 
            // settingsTipPanel
            // 
            settingsTipPanel.Controls.Add(settingsTipLinkLabel);
            settingsTipPanel.Controls.Add(settingsTipLabel);
            settingsTipPanel.Location = new Point(32, 648);
            settingsTipPanel.Name = "settingsTipPanel";
            settingsTipPanel.Size = new Size(846, 52);
            settingsTipPanel.TabIndex = 2;
            // 
            // settingsTipLinkLabel
            // 
            settingsTipLinkLabel.Location = new Point(722, 13);
            settingsTipLinkLabel.Name = "settingsTipLinkLabel";
            settingsTipLinkLabel.Size = new Size(96, 26);
            settingsTipLinkLabel.TabIndex = 1;
            settingsTipLinkLabel.Text = "前往设置 ›";
            settingsTipLinkLabel.TextAlign = ContentAlignment.MiddleRight;
            // 
            // settingsTipLabel
            // 
            settingsTipLabel.Location = new Point(22, 13);
            settingsTipLabel.Name = "settingsTipLabel";
            settingsTipLabel.Size = new Size(440, 26);
            settingsTipLabel.TabIndex = 0;
            settingsTipLabel.Text = "💡 想要无人值守访问？试试设置访问密码";
            // 
            // recentCardPanel
            // 
            recentCardPanel.Controls.Add(logInput);
            recentCardPanel.Controls.Add(recentTitleLabel);
            recentCardPanel.Location = new Point(32, 348);
            recentCardPanel.Name = "recentCardPanel";
            recentCardPanel.Size = new Size(846, 278);
            recentCardPanel.TabIndex = 1;
            // 
            // logInput
            // 
            logInput.Location = new Point(22, 56);
            logInput.Multiline = true;
            logInput.Name = "logInput";
            logInput.Size = new Size(802, 198);
            logInput.TabIndex = 1;
            // 
            // recentTitleLabel
            // 
            recentTitleLabel.Location = new Point(22, 22);
            recentTitleLabel.Name = "recentTitleLabel";
            recentTitleLabel.Size = new Size(180, 30);
            recentTitleLabel.TabIndex = 0;
            recentTitleLabel.Text = "最近连接";
            // 
            // connectCardPanel
            // 
            connectCardPanel.Controls.Add(terminalCardPanel);
            connectCardPanel.Controls.Add(fileTransferCardPanel);
            connectCardPanel.Controls.Add(connectMenuButton);
            connectCardPanel.Controls.Add(connectButton);
            connectCardPanel.Controls.Add(remoteIdInput);
            connectCardPanel.Controls.Add(connectionHelpLabel);
            connectCardPanel.Controls.Add(connectionTitleLabel);
            connectCardPanel.Location = new Point(32, 40);
            connectCardPanel.Name = "connectCardPanel";
            connectCardPanel.Size = new Size(846, 276);
            connectCardPanel.TabIndex = 0;
            // 
            // terminalCardPanel
            // 
            terminalCardPanel.Controls.Add(terminalSubtitleLabel);
            terminalCardPanel.Controls.Add(terminalTitleLabel);
            terminalCardPanel.Controls.Add(terminalIconLabel);
            terminalCardPanel.Location = new Point(456, 176);
            terminalCardPanel.Name = "terminalCardPanel";
            terminalCardPanel.Size = new Size(368, 74);
            terminalCardPanel.TabIndex = 6;
            // 
            // terminalSubtitleLabel
            // 
            terminalSubtitleLabel.Location = new Point(92, 40);
            terminalSubtitleLabel.Name = "terminalSubtitleLabel";
            terminalSubtitleLabel.Size = new Size(180, 22);
            terminalSubtitleLabel.TabIndex = 2;
            terminalSubtitleLabel.Text = "访问远程命令行";
            // 
            // terminalTitleLabel
            // 
            terminalTitleLabel.Location = new Point(92, 16);
            terminalTitleLabel.Name = "terminalTitleLabel";
            terminalTitleLabel.Size = new Size(140, 24);
            terminalTitleLabel.TabIndex = 1;
            terminalTitleLabel.Text = "远程终端";
            // 
            // terminalIconLabel
            // 
            terminalIconLabel.Location = new Point(30, 18);
            terminalIconLabel.Name = "terminalIconLabel";
            terminalIconLabel.Size = new Size(42, 42);
            terminalIconLabel.TabIndex = 0;
            terminalIconLabel.Text = ">";
            terminalIconLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // fileTransferCardPanel
            // 
            fileTransferCardPanel.Controls.Add(fileTransferSubtitleLabel);
            fileTransferCardPanel.Controls.Add(fileTransferTitleLabel);
            fileTransferCardPanel.Controls.Add(fileTransferIconLabel);
            fileTransferCardPanel.Location = new Point(32, 176);
            fileTransferCardPanel.Name = "fileTransferCardPanel";
            fileTransferCardPanel.Size = new Size(368, 74);
            fileTransferCardPanel.TabIndex = 5;
            // 
            // fileTransferSubtitleLabel
            // 
            fileTransferSubtitleLabel.Location = new Point(92, 40);
            fileTransferSubtitleLabel.Name = "fileTransferSubtitleLabel";
            fileTransferSubtitleLabel.Size = new Size(180, 22);
            fileTransferSubtitleLabel.TabIndex = 2;
            fileTransferSubtitleLabel.Text = "安全快速传输文件";
            // 
            // fileTransferTitleLabel
            // 
            fileTransferTitleLabel.Location = new Point(92, 16);
            fileTransferTitleLabel.Name = "fileTransferTitleLabel";
            fileTransferTitleLabel.Size = new Size(140, 24);
            fileTransferTitleLabel.TabIndex = 1;
            fileTransferTitleLabel.Text = "文件传输";
            // 
            // fileTransferIconLabel
            // 
            fileTransferIconLabel.Location = new Point(30, 18);
            fileTransferIconLabel.Name = "fileTransferIconLabel";
            fileTransferIconLabel.Size = new Size(42, 42);
            fileTransferIconLabel.TabIndex = 0;
            fileTransferIconLabel.Text = "▣";
            fileTransferIconLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // connectMenuButton
            // 
            connectMenuButton.Location = new Point(760, 94);
            connectMenuButton.Name = "connectMenuButton";
            connectMenuButton.Size = new Size(64, 62);
            connectMenuButton.TabIndex = 4;
            connectMenuButton.Text = "⌄";
            // 
            // connectButton
            // 
            connectButton.Location = new Point(622, 94);
            connectButton.Name = "connectButton";
            connectButton.Size = new Size(124, 62);
            connectButton.TabIndex = 3;
            connectButton.Text = "连接";
            connectButton.Click += connectButton_Click;
            // 
            // remoteIdInput
            // 
            remoteIdInput.Location = new Point(32, 94);
            remoteIdInput.Name = "remoteIdInput";
            remoteIdInput.Size = new Size(574, 62);
            remoteIdInput.TabIndex = 2;
            // 
            // connectionHelpLabel
            // 
            connectionHelpLabel.Location = new Point(218, 44);
            connectionHelpLabel.Name = "connectionHelpLabel";
            connectionHelpLabel.Size = new Size(24, 24);
            connectionHelpLabel.TabIndex = 1;
            connectionHelpLabel.Text = "?";
            connectionHelpLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // connectionTitleLabel
            // 
            connectionTitleLabel.Location = new Point(32, 34);
            connectionTitleLabel.Name = "connectionTitleLabel";
            connectionTitleLabel.Size = new Size(186, 40);
            connectionTitleLabel.TabIndex = 0;
            connectionTitleLabel.Text = "连接远程桌面";
            // 
            // HomePage
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(mainContentPanel);
            Controls.Add(sidebarPanel);
            Margin = new Padding(0);
            Name = "HomePage";
            Size = new Size(1000, 560);
            sidebarPanel.ResumeLayout(false);
            securityCardPanel.ResumeLayout(false);
            deviceInfoCardPanel.ResumeLayout(false);
            heroCardPanel.ResumeLayout(false);
            mainContentPanel.ResumeLayout(false);
            settingsTipPanel.ResumeLayout(false);
            recentCardPanel.ResumeLayout(false);
            connectCardPanel.ResumeLayout(false);
            terminalCardPanel.ResumeLayout(false);
            fileTransferCardPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Panel sidebarPanel;
        private AntdUI.Panel heroCardPanel;
        private AntdUI.Label heroTitleLabel;
        private AntdUI.Label heroSubtitleLabel;
        private AntdUI.Label onlineDotLabel;
        private AntdUI.Label onlineStatusLabel;
        private AntdUI.Panel deviceInfoCardPanel;
        private AntdUI.Label lblIdTitle;
        private AntdUI.Label lblId;
        private AntdUI.Label lblPwdTitle;
        private AntdUI.Label lblPwd;
        private AntdUI.Button copyIdButton;
        private AntdUI.Button refreshPasswordButton;
        private AntdUI.Button showPasswordButton;
        private AntdUI.Button remoteControlNavButton;
        private AntdUI.Button deviceListNavButton;
        private AntdUI.Button recentNavButton;
        private AntdUI.Panel securityCardPanel;
        private AntdUI.Label securityIconLabel;
        private AntdUI.Label securityTitleLabel;
        private AntdUI.Label securitySubtitleLabel;
        private AntdUI.Label securityArrowLabel;
        private AntdUI.Panel mainContentPanel;
        private AntdUI.Panel connectCardPanel;
        private AntdUI.Label connectionTitleLabel;
        private AntdUI.Label connectionHelpLabel;
        private AntdUI.Input remoteIdInput;
        private AntdUI.Button connectButton;
        private AntdUI.Button connectMenuButton;
        private AntdUI.Panel fileTransferCardPanel;
        private AntdUI.Label fileTransferIconLabel;
        private AntdUI.Label fileTransferTitleLabel;
        private AntdUI.Label fileTransferSubtitleLabel;
        private AntdUI.Panel terminalCardPanel;
        private AntdUI.Label terminalIconLabel;
        private AntdUI.Label terminalTitleLabel;
        private AntdUI.Label terminalSubtitleLabel;
        private AntdUI.Panel recentCardPanel;
        private AntdUI.Label recentTitleLabel;
        private AntdUI.Input logInput;
        private AntdUI.Panel settingsTipPanel;
        private AntdUI.Label settingsTipLabel;
        private AntdUI.Label settingsTipLinkLabel;
    }
}
