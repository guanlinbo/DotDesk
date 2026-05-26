namespace DotDesk.App
{
    partial class MainForm
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

        private void InitializeComponent()
        {
            titleBar = new AntdUI.PageHeader();
            closeWindowButton = new AntdUI.Button();
            maximizeWindowButton = new AntdUI.Button();
            minimizeWindowButton = new AntdUI.Button();
            menuButton = new AntdUI.Button();
            settingsTabButton = new AntdUI.Button();
            homeTabButton = new AntdUI.Button();
            appLogoLabel = new AntdUI.Label();
            contentPanel = new AntdUI.Panel();
            titleBar.SuspendLayout();
            SuspendLayout();
            // 
            // titleBar
            // 
            titleBar.Controls.Add(closeWindowButton);
            titleBar.Controls.Add(maximizeWindowButton);
            titleBar.Controls.Add(minimizeWindowButton);
            titleBar.Controls.Add(menuButton);
            titleBar.Controls.Add(settingsTabButton);
            titleBar.Controls.Add(homeTabButton);
            titleBar.Controls.Add(appLogoLabel);
            titleBar.Dock = DockStyle.Top;
            titleBar.Location = new Point(0, 0);
            titleBar.Margin = new Padding(0);
            titleBar.Name = "titleBar";
            titleBar.Size = new Size(950, 43);
            titleBar.TabIndex = 0;
            titleBar.Text = "";
            // 
            // closeWindowButton
            // 
            closeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeWindowButton.Location = new Point(902, 6);
            closeWindowButton.Name = "closeWindowButton";
            closeWindowButton.Size = new Size(34, 32);
            closeWindowButton.TabIndex = 6;
            closeWindowButton.Text = "x";
            closeWindowButton.Click += closeWindowButton_Click;
            // 
            // maximizeWindowButton
            // 
            maximizeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            maximizeWindowButton.Location = new Point(862, 6);
            maximizeWindowButton.Name = "maximizeWindowButton";
            maximizeWindowButton.Size = new Size(34, 32);
            maximizeWindowButton.TabIndex = 5;
            maximizeWindowButton.Text = "[]";
            maximizeWindowButton.Click += maximizeWindowButton_Click;
            // 
            // minimizeWindowButton
            // 
            minimizeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            minimizeWindowButton.Location = new Point(822, 6);
            minimizeWindowButton.Name = "minimizeWindowButton";
            minimizeWindowButton.Size = new Size(34, 32);
            minimizeWindowButton.TabIndex = 4;
            minimizeWindowButton.Text = "-";
            minimizeWindowButton.Click += minimizeWindowButton_Click;
            // 
            // menuButton
            // 
            menuButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            menuButton.Location = new Point(778, 6);
            menuButton.Name = "menuButton";
            menuButton.Size = new Size(34, 32);
            menuButton.TabIndex = 3;
            menuButton.Text = "=";
            // 
            // settingsTabButton
            // 
            settingsTabButton.Location = new Point(202, 2);
            settingsTabButton.Name = "settingsTabButton";
            settingsTabButton.Size = new Size(86, 42);
            settingsTabButton.TabIndex = 2;
            settingsTabButton.Text = "设置";
            // 
            // homeTabButton
            // 
            homeTabButton.Location = new Point(98, 2);
            homeTabButton.Name = "homeTabButton";
            homeTabButton.Size = new Size(86, 42);
            homeTabButton.TabIndex = 1;
            homeTabButton.Text = "主页";
            // 
            // appLogoLabel
            // 
            appLogoLabel.Location = new Point(24, 6);
            appLogoLabel.Name = "appLogoLabel";
            appLogoLabel.Size = new Size(34, 34);
            appLogoLabel.TabIndex = 0;
            appLogoLabel.Text = "D";
            appLogoLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // contentPanel
            // 
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Location = new Point(0, 43);
            contentPanel.Margin = new Padding(0);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new Size(950, 527);
            contentPanel.TabIndex = 1;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(950, 570);
            Controls.Add(contentPanel);
            Controls.Add(titleBar);
            MinimumSize = new Size(950, 570);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "DotDesk";
            titleBar.ResumeLayout(false);
            ResumeLayout(false);
        }

        private AntdUI.PageHeader titleBar;
        private AntdUI.Label appLogoLabel;
        private AntdUI.Button homeTabButton;
        private AntdUI.Button settingsTabButton;
        private AntdUI.Button menuButton;
        private AntdUI.Button minimizeWindowButton;
        private AntdUI.Button maximizeWindowButton;
        private AntdUI.Button closeWindowButton;
        private AntdUI.Panel contentPanel;
    }
}
