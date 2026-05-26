using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DotDesk.App.Pages
{
    /// <summary>
    /// 右侧主页内容：
    /// 输入对方 ID、连接按钮、文件传输、远程终端、最近连接列表
    /// </summary>
    public class RemoteConnectPage : UserControl
    {
        private TextBox txtRemoteId;
        private Button btnConnect;
        private Button btnMore;

        public RemoteConnectPage()
        {
            // 右侧区域大小：
            // 总窗口 1000x620
            // 左侧菜单 72
            // 顶部栏 60
            // 所以右侧页面是 928x560
            Size = new Size(928, 560);
            BackColor = Color.FromArgb(246, 248, 252);
            DoubleBuffered = true;

            InitConnectCard();
            InitRecentCard();
            InitBottomTip();
        }

        private void InitConnectCard()
        {
            RoundedPanel card = new RoundedPanel
            {
                Location = new Point(24, 12),
                Size = new Size(832, 220),
                Radius = 14,
                BackColor = Color.White
            };
            Controls.Add(card);

            Label title = new Label
            {
                Text = "连接远程桌面",
                Location = new Point(28, 26),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 30, 45),
                BackColor = Color.Transparent
            };
            card.Controls.Add(title);

            Label help = new Label
            {
                Text = "ⓘ",
                Location = new Point(160, 27),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(145, 153, 168),
                BackColor = Color.Transparent
            };
            card.Controls.Add(help);

            txtRemoteId = new TextBox
            {
                Location = new Point(28, 72),
                Size = new Size(520, 44),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 13F),
                ForeColor = Color.FromArgb(45, 50, 65),
                BackColor = Color.White
            };

            txtRemoteId.PlaceholderText = "输入 对方 ID";
            card.Controls.Add(txtRemoteId);

            btnConnect = new Button
            {
                Text = "连接",
                Location = new Point(562, 72),
                Size = new Size(112, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 94, 255),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 112, 255);
            btnConnect.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 80, 220);
            btnConnect.Click += BtnConnect_Click;
            card.Controls.Add(btnConnect);

            btnMore = new Button
            {
                Text = "⌄",
                Location = new Point(688, 72),
                Size = new Size(56, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(65, 72, 90),
                Font = new Font("Segoe UI", 14F),
                Cursor = Cursors.Hand
            };
            btnMore.FlatAppearance.BorderColor = Color.FromArgb(226, 231, 240);
            btnMore.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 253);
            btnMore.FlatAppearance.MouseDownBackColor = Color.FromArgb(238, 243, 250);
            card.Controls.Add(btnMore);

            card.Controls.Add(CreateFeatureBox(
                new Point(28, 150),
                Color.FromArgb(120, 78, 255),
                "▣",
                "文件传输",
                "安全快速传输文件"
            ));

            card.Controls.Add(CreateFeatureBox(
                new Point(360, 150),
                Color.FromArgb(16, 185, 129),
                ">_",
                "远程终端",
                "访问远程命令行"
            ));
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            string remoteId = txtRemoteId.Text.Trim();

            if (string.IsNullOrWhiteSpace(remoteId))
            {
                MessageBox.Show(
                    "请输入对方 ID",
                    "提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            // 这里后面接你的远程连接逻辑
            // 例如：
            // RemoteClient.Connect(remoteId);

            MessageBox.Show(
                $"准备连接远程设备：{remoteId}",
                "DotDesk",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private Control CreateFeatureBox(
            Point location,
            Color iconColor,
            string iconText,
            string title,
            string desc)
        {
            RoundedPanel panel = new RoundedPanel
            {
                Location = location,
                Size = new Size(300, 64),
                Radius = 8,
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };

            panel.Paint += (s, e) =>
            {
                DrawBorder(e.Graphics, panel.ClientRectangle, Color.FromArgb(235, 238, 245), 8);
            };

            Label icon = new Label
            {
                Text = iconText,
                Location = new Point(24, 17),
                Size = new Size(32, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = iconColor
            };
            panel.Controls.Add(icon);

            Label lblTitle = new Label
            {
                Text = title,
                Location = new Point(72, 14),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 40, 55),
                BackColor = Color.Transparent
            };
            panel.Controls.Add(lblTitle);

            Label lblDesc = new Label
            {
                Text = desc,
                Location = new Point(72, 37),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(120, 128, 145),
                BackColor = Color.Transparent
            };
            panel.Controls.Add(lblDesc);

            return panel;
        }

        private void InitRecentCard()
        {
            RoundedPanel card = new RoundedPanel
            {
                Location = new Point(24, 248),
                Size = new Size(832, 250),
                Radius = 14,
                BackColor = Color.White
            };
            Controls.Add(card);

            Label title = new Label
            {
                Text = "最近连接",
                Location = new Point(24, 22),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 30, 45),
                BackColor = Color.Transparent
            };
            card.Controls.Add(title);

            Label more = new Label
            {
                Text = "查看更多  ›",
                Location = new Point(710, 24),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(22, 119, 255),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            card.Controls.Add(more);

            card.Controls.Add(CreateRecentItem(
                new Point(16, 62),
                Color.FromArgb(22, 119, 255),
                "⊞",
                "办公电脑 ★",
                "192.168.1.10",
                "今天 14:30"
            ));

            card.Controls.Add(CreateRecentItem(
                new Point(16, 122),
                Color.FromArgb(0, 190, 120),
                "D",
                "设计师电脑",
                "192.168.1.20",
                "昨天 09:12"
            ));

            card.Controls.Add(CreateRecentItem(
                new Point(16, 182),
                Color.FromArgb(130, 80, 255),
                "♟",
                "服务器-测试环境",
                "10.0.0.88",
                "3 天前"
            ));
        }

        private Control CreateRecentItem(
            Point location,
            Color iconColor,
            string iconText,
            string name,
            string ip,
            string time)
        {
            RoundedPanel item = new RoundedPanel
            {
                Location = location,
                Size = new Size(800, 56),
                Radius = 8,
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };

            item.Paint += (s, e) =>
            {
                DrawBorder(e.Graphics, item.ClientRectangle, Color.FromArgb(238, 241, 246), 8);
            };

            item.MouseEnter += (s, e) =>
            {
                item.BackColor = Color.FromArgb(248, 251, 255);
                item.Invalidate();
            };

            item.MouseLeave += (s, e) =>
            {
                item.BackColor = Color.White;
                item.Invalidate();
            };

            Label icon = new Label
            {
                Text = iconText,
                Location = new Point(20, 12),
                Size = new Size(32, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = iconColor
            };
            item.Controls.Add(icon);

            Label lblName = new Label
            {
                Text = name,
                Location = new Point(66, 8),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(45, 50, 65),
                BackColor = Color.Transparent
            };
            item.Controls.Add(lblName);

            Label lblIp = new Label
            {
                Text = ip,
                Location = new Point(66, 31),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(120, 128, 145),
                BackColor = Color.Transparent
            };
            item.Controls.Add(lblIp);

            Label lblTime = new Label
            {
                Text = time,
                Location = new Point(650, 20),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(120, 128, 145),
                BackColor = Color.Transparent
            };
            item.Controls.Add(lblTime);

            Label dots = new Label
            {
                Text = "⋮",
                Location = new Point(755, 14),
                AutoSize = true,
                Font = new Font("Segoe UI", 14F),
                ForeColor = Color.FromArgb(95, 103, 120),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            item.Controls.Add(dots);

            return item;
        }

        private void InitBottomTip()
        {
            RoundedPanel tipPanel = new RoundedPanel
            {
                Location = new Point(24, 512),
                Size = new Size(832, 36),
                Radius = 8,
                BackColor = Color.FromArgb(239, 246, 255)
            };
            Controls.Add(tipPanel);

            Label icon = new Label
            {
                Text = "♡",
                Location = new Point(22, 8),
                AutoSize = true,
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(22, 119, 255),
                BackColor = Color.Transparent
            };
            tipPanel.Controls.Add(icon);

            Label text = new Label
            {
                Text = "想要无人值守访问？试试设置访问密码",
                Location = new Point(58, 9),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(75, 95, 135),
                BackColor = Color.Transparent
            };
            tipPanel.Controls.Add(text);

            Label setting = new Label
            {
                Text = "前往设置  ›",
                Location = new Point(690, 9),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(22, 119, 255),
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            tipPanel.Controls.Add(setting);
        }

        private static void DrawBorder(Graphics g, Rectangle rect, Color color, int radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            rect.Width -= 1;
            rect.Height -= 1;

            using (GraphicsPath path = GetRoundPath(rect, radius))
            using (Pen pen = new Pen(color, 1))
            {
                g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath GetRoundPath(Rectangle rect, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        private class RoundedPanel : Panel
        {
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Radius { get; set; } = 10;

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (GraphicsPath path = GetRoundPath(ClientRectangle, Radius))
                {
                    Region = new Region(path);
                }
            }
        }
    }
}
