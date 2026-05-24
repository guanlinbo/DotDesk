using AntdUI;
using FormsControl = System.Windows.Forms.Control;
using FormsLabel = System.Windows.Forms.Label;
using FormsPanel = System.Windows.Forms.Panel;
using Panel = System.Windows.Forms.Panel;
using System.ComponentModel;

namespace DotDesk.App
{
    /// <summary>
    /// 断网提示页。
    /// 
    /// 这个控件是盖在 MainForm 上面的遮罩页面。
    /// 它本身不负责判断网络是否断开，只负责显示 UI。
    /// 网络检测逻辑在 HomePage 里，HomePage 通过 NetworkOfflineChanged 通知 MainForm 显示/隐藏这个页面。
    /// </summary>
    public class NetworkOfflinePage : UserControl
    {
        /// <summary>
        /// 点击“重新连接”时触发。
        /// MainForm 会订阅这个事件，然后调用 HomePage.RetryNetworkAsync()。
        /// </summary>
        public event Func<Task>? RetryClicked;

        /// <summary>
        /// 点击“网络诊断”时触发。
        /// 以后可以接入真正的网络诊断功能。
        /// </summary>
        public event EventHandler? DiagnoseClicked;

        // 设计稿尺寸。所有控件位置都按这个区域来摆放。
        private const int DesignCardWidth = 520;
        private const int DesignCardHeight = 540;

        private Panel card = null!;
        private OfflineButton retryButton = null!;
        private OfflineButton diagnoseButton = null!;

        public NetworkOfflinePage()
        {
            Dock = DockStyle.Fill;

            // 目标图是白底，所以这里不要用灰色背景。
            BackColor = Color.White;

            BuildUI();

            // 父窗口大小变化时，让内容始终居中。
            Resize += (_, _) => LayoutCard();
        }

        /// <summary>
        /// 组装断网页面 UI。
        /// </summary>
        private void BuildUI()
        {
            card = new Panel
            {
                Size = new Size(DesignCardWidth, DesignCardHeight),
                BackColor = Color.White
            };

            Controls.Add(card);

            // 顶部 WiFi 断开图标。
            var topIcon = new WifiErrorIcon
            {
                Location = new Point(175, 16),
                Size = new Size(170, 130)
            };
            card.Controls.Add(topIcon);

            // 标题。
            var title = new FormsLabel
            {
                Text = "网络连接已断开",
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(17, 24, 39),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 160),
                Size = new Size(DesignCardWidth, 38)
            };
            card.Controls.Add(title);

            // 副标题。
            var subtitle = new FormsLabel
            {
                Text = "请检查网络连接，或稍后重试\r\n我们会自动尝试重新连接",
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(75, 85, 99),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 205),
                Size = new Size(DesignCardWidth, 48)
            };
            card.Controls.Add(subtitle);

            // 重新连接按钮。
            // 这里不用 AntdUI.Button，因为你截图里 AntButton 背景填充异常，只显示了边角。
            retryButton = new OfflineButton
            {
                Text = "⟳  重新连接",
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.White,
                NormalBackColor = Color.FromArgb(0, 96, 220),
                HoverBackColor = Color.FromArgb(0, 82, 190),
                PressBackColor = Color.FromArgb(0, 72, 170),
                BorderColor = Color.Transparent,
                BorderWidthValue = 0,
                RadiusValue = 8,
                Location = new Point(170, 270),
                Size = new Size(180, 38)
            };

            retryButton.Click += async (_, _) =>
            {
                if (RetryClicked == null) return;

                retryButton.Enabled = false;
                retryButton.Text = "正在重连...";

                try
                {
                    await RetryClicked.Invoke();
                }
                finally
                {
                    retryButton.Enabled = true;
                    retryButton.Text = "⟳  重新连接";
                }
            };

            card.Controls.Add(retryButton);

            // 网络诊断按钮。
            diagnoseButton = new OfflineButton
            {
                Text = "⌁  网络诊断",
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = Color.FromArgb(75, 85, 99),
                NormalBackColor = Color.White,
                HoverBackColor = Color.FromArgb(245, 247, 250),
                PressBackColor = Color.FromArgb(229, 231, 235),
                BorderColor = Color.FromArgb(209, 213, 219),
                BorderWidthValue = 1,
                RadiusValue = 8,
                Location = new Point(170, 320),
                Size = new Size(180, 36)
            };

            diagnoseButton.Click += (_, _) =>
            {
                DiagnoseClicked?.Invoke(this, EventArgs.Empty);
            };

            card.Controls.Add(diagnoseButton);

            AddReasonTitle();

            AddReasonItem(new Point(50, 418), "wifi", "网络未连接");
            AddReasonItem(new Point(170, 418), "server", "服务器不可达");
            AddReasonItem(new Point(290, 418), "shield", "防火墙限制");
            AddReasonItem(new Point(410, 418), "monitor", "远程设备离线");

            AddTipBox();

            LayoutCard();
        }

        /// <summary>
        /// “可能的原因”中间标题和左右分割线。
        /// </summary>
        private void AddReasonTitle()
        {
            var leftLine = new FormsPanel
            {
                BackColor = Color.FromArgb(229, 231, 235),
                Location = new Point(54, 390),
                Size = new Size(150, 1)
            };

            var rightLine = new FormsPanel
            {
                BackColor = Color.FromArgb(229, 231, 235),
                Location = new Point(314, 390),
                Size = new Size(150, 1)
            };

            var text = new FormsLabel
            {
                Text = "可能的原因",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(107, 114, 128),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(204, 378),
                Size = new Size(112, 24)
            };

            card.Controls.Add(leftLine);
            card.Controls.Add(rightLine);
            card.Controls.Add(text);
        }

        /// <summary>
        /// 添加一个可能原因：图标 + 文字。
        /// </summary>
        private void AddReasonItem(Point location, string iconType, string text)
        {
            var icon = new ReasonIcon
            {
                IconType = iconType,
                Location = new Point(location.X + 28, location.Y),
                Size = new Size(28, 28)
            };

            card.Controls.Add(icon);

            var label = new FormsLabel
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(55, 65, 81),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(location.X, location.Y + 34),
                Size = new Size(84, 24)
            };

            card.Controls.Add(label);
        }

        /// <summary>
        /// 底部小贴士区域。
        /// </summary>
        private void AddTipBox()
        {
            var tip = new RoundPanel
            {
                Location = new Point(34, 485),
                Size = new Size(452, 46),
                BackColor = Color.FromArgb(239, 246, 255),
                RadiusValue = 8
            };

            var icon = new FormsLabel
            {
                Text = "💡",
                Font = new Font("Segoe UI Emoji", 12F),
                BackColor = Color.Transparent,
                Location = new Point(14, 10),
                Size = new Size(24, 24)
            };

            var title = new FormsLabel
            {
                Text = "小贴士",
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(37, 99, 235),
                BackColor = Color.Transparent,
                Location = new Point(42, 6),
                Size = new Size(100, 18)
            };

            var desc = new FormsLabel
            {
                Text = "请确保你的设备已连接到互联网，并且远程设备已开启",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Color.FromArgb(75, 85, 99),
                BackColor = Color.Transparent,
                Location = new Point(42, 25),
                Size = new Size(380, 18)
            };

            tip.Controls.Add(icon);
            tip.Controls.Add(title);
            tip.Controls.Add(desc);

            card.Controls.Add(tip);
        }

        /// <summary>
        /// 页面居中。
        /// 
        /// 这里暂时不做 Scale 缩放。
        /// 之前用 Scale 后 AntdUI/Button 容易绘制异常，所以先用固定尺寸更稳。
        /// </summary>
        private void LayoutCard()
        {
            if (card == null) return;

            card.Size = new Size(DesignCardWidth, DesignCardHeight);

            card.Location = new Point(
                Math.Max(0, (Width - card.Width) / 2),
                Math.Max(0, (Height - card.Height) / 2)
            );
        }

        /// <summary>
        /// 自绘圆角按钮。
        /// 
        /// 为什么不用 AntdUI.Button：
        /// 你当前断网页里 AntButton 背景没有完整填充，只显示了两个蓝色角。
        /// 自绘按钮更稳定，也更容易控制样式。
        /// </summary>
        private class OfflineButton : FormsControl
        {
            private bool _hover;
            private bool _pressed;

            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color NormalBackColor { get; set; } = Color.White;
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color HoverBackColor { get; set; } = Color.FromArgb(245, 247, 250);
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color PressBackColor { get; set; } = Color.FromArgb(229, 231, 235);
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color BorderColor { get; set; } = Color.Transparent;
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int BorderWidthValue { get; set; } = 0;
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int RadiusValue { get; set; } = 8;

            public OfflineButton()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true
                );

                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hover = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = false;
                _pressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);

                if (e.Button == MouseButtons.Left)
                {
                    _pressed = true;
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _pressed = false;
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                var back = _pressed
                    ? PressBackColor
                    : _hover
                        ? HoverBackColor
                        : NormalBackColor;

                if (!Enabled)
                {
                    back = Color.FromArgb(203, 213, 225);
                }

                using var path = CreateRoundRectPath(
                    new RectangleF(0, 0, Width - 1, Height - 1),
                    RadiusValue
                );

                using var brush = new SolidBrush(back);
                g.FillPath(brush, path);

                if (BorderWidthValue > 0)
                {
                    using var pen = new Pen(BorderColor, BorderWidthValue);
                    g.DrawPath(pen, path);
                }

                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    ClientRectangle,
                    Enabled ? ForeColor : Color.White,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine
                );
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

        /// <summary>
        /// 圆角面板，用在底部小贴士区域。
        /// </summary>
        private class RoundPanel : FormsPanel
        {
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int RadiusValue { get; set; } = 8;

            public RoundPanel()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true
                );
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                using var path = CreateRoundRectPath(
                    new RectangleF(0, 0, Width - 1, Height - 1),
                    RadiusValue
                );

                using var brush = new SolidBrush(BackColor);
                g.FillPath(brush, path);
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

        /// <summary>
        /// 顶部 WiFi 断开图标。
        /// 这里用 GDI+ 画出来，不依赖图片资源。
        /// </summary>
        private class WifiErrorIcon : FormsControl
        {
            public WifiErrorIcon()
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

                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                var blue = Color.FromArgb(37, 99, 235);
                var lightBlue = Color.FromArgb(235, 242, 255);

                // 中间浅蓝圆形背景。
                using var bgBrush = new SolidBrush(lightBlue);
                g.FillEllipse(bgBrush, 28, 0, 120, 120);

                // 背景里的淡淡线条，模拟地球/网络连接感。
                using var mapPen = new Pen(Color.FromArgb(38, blue), 2);
                g.DrawEllipse(mapPen, 42, 14, 92, 92);
                g.DrawArc(mapPen, 36, 34, 104, 64, 200, 140);

                // WiFi 主体。
                using var wifiPen = new Pen(blue, 7)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round
                };

                g.DrawArc(wifiPen, 58, 40, 64, 46, 220, 100);
                g.DrawArc(wifiPen, 72, 58, 36, 26, 220, 100);

                using var dotBrush = new SolidBrush(blue);
                g.FillEllipse(dotBrush, 87, 86, 12, 12);

                // 红色错误圆点。
                using var redBrush = new SolidBrush(Color.FromArgb(239, 68, 68));
                g.FillEllipse(redBrush, 110, 78, 32, 32);

                using var xPen = new Pen(Color.White, 3)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round
                };

                g.DrawLine(xPen, 120, 88, 132, 100);
                g.DrawLine(xPen, 132, 88, 120, 100);

                // 两边的小云朵和小点，让画面不空。
                using var cloudBrush = new SolidBrush(Color.FromArgb(210, 224, 255));
                g.FillEllipse(cloudBrush, 8, 50, 28, 12);
                g.FillEllipse(cloudBrush, 126, 42, 42, 16);

                using var smallBrush = new SolidBrush(Color.FromArgb(170, 195, 245));
                g.FillEllipse(smallBrush, 18, 104, 4, 4);
                g.FillEllipse(smallBrush, 154, 102, 4, 4);
                g.FillEllipse(smallBrush, 58, 18, 4, 4);
                g.FillEllipse(smallBrush, 140, 12, 4, 4);
            }
        }

        /// <summary>
        /// 底部“可能的原因”图标。
        /// </summary>
        private class ReasonIcon : FormsControl
        {
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string IconType { get; set; } = "wifi";

            public ReasonIcon()
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

                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                var color = Color.FromArgb(31, 41, 55);

                using var pen = new Pen(color, 2)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round
                };

                using var brush = new SolidBrush(color);

                switch (IconType)
                {
                    case "wifi":
                        DrawWifiIcon(g, pen, brush);
                        break;

                    case "server":
                        DrawServerIcon(g, pen);
                        break;

                    case "shield":
                        DrawShieldIcon(g, pen);
                        break;

                    case "monitor":
                        DrawMonitorIcon(g, pen);
                        break;
                }
            }

            private static void DrawWifiIcon(Graphics g, Pen pen, Brush brush)
            {
                g.DrawArc(pen, 4, 8, 20, 16, 215, 110);
                g.DrawArc(pen, 8, 14, 12, 10, 215, 110);
                g.FillEllipse(brush, 12, 23, 4, 4);

                using var red = new SolidBrush(Color.FromArgb(239, 68, 68));
                g.FillEllipse(red, 20, 3, 8, 8);
            }

            private static void DrawServerIcon(Graphics g, Pen pen)
            {
                g.DrawEllipse(pen, 8, 2, 12, 8);
                g.DrawLine(pen, 14, 10, 14, 17);
                g.DrawRectangle(pen, 4, 17, 20, 8);
                g.DrawEllipse(pen, 7, 19, 3, 3);
                g.DrawEllipse(pen, 18, 19, 3, 3);
            }

            private static void DrawShieldIcon(Graphics g, Pen pen)
            {
                using var path = new System.Drawing.Drawing2D.GraphicsPath();

                path.AddLines(new[]
                {
                    new Point(14, 2),
                    new Point(24, 7),
                    new Point(22, 19),
                    new Point(14, 26),
                    new Point(6, 19),
                    new Point(4, 7),
                    new Point(14, 2)
                });

                g.DrawPath(pen, path);

                g.DrawLine(pen, 10, 14, 13, 17);
                g.DrawLine(pen, 13, 17, 19, 11);
            }

            private static void DrawMonitorIcon(Graphics g, Pen pen)
            {
                g.DrawRectangle(pen, 5, 5, 18, 14);
                g.DrawLine(pen, 14, 19, 14, 24);
                g.DrawLine(pen, 9, 24, 19, 24);
            }
        }
    }
}