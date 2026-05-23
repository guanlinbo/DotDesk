namespace DotDesk.App
{
    partial class RemoteDesktopControl
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

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            remoteTopBar = new AntdUI.Panel();
            addButton = new AntdUI.Button();
            closeRemoteButton = new AntdUI.Button();
            speakerButton = new AntdUI.Button();
            qualityBadgeLabel = new AntdUI.Label();
            securityBadgeLabel = new AntdUI.Label();
            latencyLabel = new AntdUI.Label();
            signalBarsLabel = new AntdUI.Label();
            remoteNameLabel = new AntdUI.Label();
            avatarLabel = new AntdUI.Label();
            minimizeWindowButton = new AntdUI.Button();
            maximizeWindowButton = new AntdUI.Button();
            closeWindowButton = new AntdUI.Button();
            desktopSurfacePanel = new AntdUI.Panel();
            remoteTopBar.SuspendLayout();
            SuspendLayout();
            // 
            // remoteTopBar
            // 
            remoteTopBar.Controls.Add(addButton);
            remoteTopBar.Controls.Add(closeRemoteButton);
            remoteTopBar.Controls.Add(speakerButton);
            remoteTopBar.Controls.Add(qualityBadgeLabel);
            remoteTopBar.Controls.Add(securityBadgeLabel);
            remoteTopBar.Controls.Add(latencyLabel);
            remoteTopBar.Controls.Add(signalBarsLabel);
            remoteTopBar.Controls.Add(remoteNameLabel);
            remoteTopBar.Controls.Add(avatarLabel);
            remoteTopBar.Controls.Add(minimizeWindowButton);
            remoteTopBar.Controls.Add(maximizeWindowButton);
            remoteTopBar.Controls.Add(closeWindowButton);
            remoteTopBar.Dock = DockStyle.Top;
            remoteTopBar.Location = new Point(0, 0);
            remoteTopBar.Margin = new Padding(0);
            remoteTopBar.Name = "remoteTopBar";
            remoteTopBar.Size = new Size(1280, 34);
            remoteTopBar.TabIndex = 0;
            // 
            // addButton
            // 
            addButton.Location = new Point(372, 3);
            addButton.Name = "addButton";
            addButton.Size = new Size(34, 28);
            addButton.TabIndex = 8;
            addButton.Text = "+";
            // 
            // closeRemoteButton
            // 
            closeRemoteButton.Location = new Point(336, 3);
            closeRemoteButton.Name = "closeRemoteButton";
            closeRemoteButton.Size = new Size(28, 28);
            closeRemoteButton.TabIndex = 7;
            closeRemoteButton.Text = "x";
            closeRemoteButton.Click += closeRemoteButton_Click;
            // 
            // speakerButton
            // 
            speakerButton.Location = new Point(306, 3);
            speakerButton.Name = "speakerButton";
            speakerButton.Size = new Size(28, 28);
            speakerButton.TabIndex = 6;
            speakerButton.Text = "A";
            // 
            // qualityBadgeLabel
            // 
            qualityBadgeLabel.Location = new Point(274, 8);
            qualityBadgeLabel.Name = "qualityBadgeLabel";
            qualityBadgeLabel.Size = new Size(28, 18);
            qualityBadgeLabel.TabIndex = 5;
            qualityBadgeLabel.Text = "HD";
            qualityBadgeLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // securityBadgeLabel
            // 
            securityBadgeLabel.Location = new Point(248, 7);
            securityBadgeLabel.Name = "securityBadgeLabel";
            securityBadgeLabel.Size = new Size(22, 20);
            securityBadgeLabel.TabIndex = 4;
            securityBadgeLabel.Text = "S";
            securityBadgeLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // latencyLabel
            // 
            latencyLabel.Location = new Point(194, 7);
            latencyLabel.Name = "latencyLabel";
            latencyLabel.Size = new Size(52, 20);
            latencyLabel.TabIndex = 3;
            latencyLabel.Text = "--ms";
            // 
            // signalBarsLabel
            // 
            signalBarsLabel.Location = new Point(168, 7);
            signalBarsLabel.Name = "signalBarsLabel";
            signalBarsLabel.Size = new Size(24, 20);
            signalBarsLabel.TabIndex = 2;
            signalBarsLabel.Text = "|||";
            signalBarsLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // remoteNameLabel
            // 
            remoteNameLabel.Location = new Point(44, 6);
            remoteNameLabel.Name = "remoteNameLabel";
            remoteNameLabel.Size = new Size(118, 22);
            remoteNameLabel.TabIndex = 1;
            remoteNameLabel.Text = "remote";
            // 
            // avatarLabel
            // 
            avatarLabel.Location = new Point(14, 5);
            avatarLabel.Name = "avatarLabel";
            avatarLabel.Size = new Size(24, 24);
            avatarLabel.TabIndex = 0;
            avatarLabel.Text = "PC";
            avatarLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // minimizeWindowButton
            // 
            minimizeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            minimizeWindowButton.Location = new Point(1166, 3);
            minimizeWindowButton.Name = "minimizeWindowButton";
            minimizeWindowButton.Size = new Size(34, 28);
            minimizeWindowButton.TabIndex = 9;
            minimizeWindowButton.Text = "-";
            minimizeWindowButton.Click += minimizeWindowButton_Click;
            // 
            // maximizeWindowButton
            // 
            maximizeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            maximizeWindowButton.Location = new Point(1202, 3);
            maximizeWindowButton.Name = "maximizeWindowButton";
            maximizeWindowButton.Size = new Size(34, 28);
            maximizeWindowButton.TabIndex = 10;
            maximizeWindowButton.Text = "[]";
            maximizeWindowButton.Click += maximizeWindowButton_Click;
            // 
            // closeWindowButton
            // 
            closeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeWindowButton.Location = new Point(1238, 3);
            closeWindowButton.Name = "closeWindowButton";
            closeWindowButton.Size = new Size(34, 28);
            closeWindowButton.TabIndex = 11;
            closeWindowButton.Text = "x";
            closeWindowButton.Click += closeWindowButton_Click;
            // 
            // desktopSurfacePanel
            // 
            desktopSurfacePanel.Dock = DockStyle.Fill;
            desktopSurfacePanel.Location = new Point(0, 34);
            desktopSurfacePanel.Margin = new Padding(0);
            desktopSurfacePanel.Name = "desktopSurfacePanel";
            desktopSurfacePanel.Size = new Size(1280, 706);
            desktopSurfacePanel.TabIndex = 1;
            // 
            // RemoteDesktopControl
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.Black;
            ClientSize = new Size(1280, 740);
            Controls.Add(desktopSurfacePanel);
            Controls.Add(remoteTopBar);
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(640, 420);
            Name = "RemoteDesktopControl";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "RemoteDesktopControl";
            remoteTopBar.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Panel remoteTopBar;
        private AntdUI.Label avatarLabel;
        private AntdUI.Label remoteNameLabel;
        private AntdUI.Label signalBarsLabel;
        private AntdUI.Label latencyLabel;
        private AntdUI.Label securityBadgeLabel;
        private AntdUI.Label qualityBadgeLabel;
        private AntdUI.Button speakerButton;
        private AntdUI.Button closeRemoteButton;
        private AntdUI.Button addButton;
        private AntdUI.Button minimizeWindowButton;
        private AntdUI.Button maximizeWindowButton;
        private AntdUI.Button closeWindowButton;
        private AntdUI.Panel desktopSurfacePanel;
    }
}
