using AntdUI;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using AntPanel = AntdUI.Panel;

namespace DotDesk.App
{
    /// <summary>
    /// DotDesk 左侧栏。
    /// 适配第二张图效果：
    /// 1. 顶部预留导航栏空间。
    /// 2. 蓝色桌面卡片、ID 卡片、菜单、安全连接卡片统一宽度。
    /// 3. ID 卡片无阴影。
    /// 4. 只保留：远程控制、设备列表、最近连接。
    /// 5. 修复底部安全连接卡片被裁剪。
    /// 6. 修复菜单选中背景过宽、背景断层问题。
    /// 7. 图标全部 GDI+ 绘制，不依赖图片。
    /// </summary>
    public partial class DotDeskSidebar : UserControl
    {
        private const int SidebarWidth = DotDeskUi.MainSidebarWidth;

        // 顶部导航栏下面开始
        private const int TopOffset = 60;

        // 左侧边距
        private const int LeftX = 16;

        // 左侧宽度 280，左右各 16，内容区 248
        private const int ItemWidth = 248;

        private readonly Color _blue = Color.FromArgb(0, 96, 220);
        private readonly Color _blueHover = Color.FromArgb(22, 119, 255);
        private readonly Color _textDark = Color.FromArgb(15, 23, 42);
        private readonly Color _textGray = Color.FromArgb(100, 116, 139);
        private readonly Color _navGray = Color.FromArgb(75, 85, 99);

        // 第二张图左侧背景更接近白色
        private readonly Color _sidebarBack = Color.White;

        private AntPanel? _safeCard;
        private System.Windows.Forms.Label? _idLabel;
        private System.Windows.Forms.Label? _passwordLabel;

        private DotDeskSvgIcon? _copyIcon;
        private System.Windows.Forms.Timer? _copySuccessTimer;

        public event Action? RemoteControlClicked;
        public event Action? DeviceListClicked;
        public event Action? RecentClicked;

        public event Action? CopyIdClicked;
        public event Action? RefreshPasswordClicked;
        public event Action? ShowPasswordClicked;

        public DotDeskSidebar()
        {
            Width = SidebarWidth;
            Dock = DockStyle.Fill;
            BackColor = _sidebarBack;
            DoubleBuffered = true;

            BuildUi();

            Resize += (_, _) => LayoutBottomCard();
        }

        private void BuildUi()
        {
            SuspendLayout();
            try
            {
                Controls.Clear();
                BackColor = _sidebarBack;

                Controls.Add(CreateDesktopCard());
                Controls.Add(CreateIdCard());

                int menuY = TopOffset + 280;

                Controls.Add(CreateMenuItem(
                    DotDeskIconType.Remote,
                    "远程控制",
                    LeftX,
                    menuY,
                    true,
                    () => RemoteControlClicked?.Invoke()));

                Controls.Add(CreateMenuItem(
                    DotDeskIconType.Device,
                    "设备列表",
                    LeftX,
                    menuY + 40,
                    false,
                    () => DeviceListClicked?.Invoke()));

                Controls.Add(CreateMenuItem(
                    DotDeskIconType.Clock,
                    "最近连接",
                    LeftX,
                    menuY + 80,
                    false,
                    () => RecentClicked?.Invoke()));

                _safeCard = CreateSafeCard();
                Controls.Add(_safeCard);

                LayoutBottomCard();
            }
            finally
            {
                ResumeLayout(false);
                Invalidate(true);
            }
        }

        private Control CreateDesktopCard()
        {
            var card = new GradientCard
            {
                Location = new Point(LeftX, TopOffset + 8),
                Size = new Size(ItemWidth, 118),
                Radius = 10,
                StartColor = Color.FromArgb(37, 99, 235),
                EndColor = Color.FromArgb(94, 76, 255)
            };

            card.Controls.Add(CreateLabel(
                "你的桌面",
                18,
                18,
                120,
                28,
                14f,
                FontStyle.Bold,
                Color.White));

            card.Controls.Add(CreateLabel(
                "随时随地，安全访问",
                18,
                45,
                160,
                22,
                9.5f,
                FontStyle.Regular,
                Color.White));

            card.Controls.Add(new StatusDot
            {
                Location = new Point(18, 88),
                Size = new Size(10, 10),
                BackColor = Color.Transparent
            });

            card.Controls.Add(CreateLabel(
                "在线",
                34,
                84,
                70,
                22,
                9.5f,
                FontStyle.Regular,
                Color.White));

            card.Controls.Add(new DotDeskSvgIcon
            {
                IconType = DotDeskIconType.MonitorLarge,
                IconColor = Color.FromArgb(190, 215, 255),
                Location = new Point(164, 48),
                Size = new Size(66, 52),
                BackColor = Color.Transparent
            });

            return card;
        }

        private AntPanel CreateIdCard()
        {
            var card = new AntPanel
            {
                Location = new Point(LeftX, TopOffset + 144),
                Size = new Size(ItemWidth, 124),
                Radius = 10,

                // ID 卡片纯白
                Back = Color.White,
                BackColor = Color.White,

                // 边框更淡
                BorderWidth = 1,
                BorderColor = Color.FromArgb(238, 242, 247),

                // 不要阴影
                Shadow = 0,
                ShadowOpacity = 0f,
                ShadowOffsetX = 0,
                ShadowOffsetY = 0
            };

            ApplyRoundRegion(card, 10);

            card.Controls.Add(CreateLabel(
                "ID",
                22,
                12,
                80,
                20,
                9.2f,
                FontStyle.Regular,
                Color.FromArgb(75, 85, 99)));

            _idLabel = CreateLabel(
                "------",
                22,
                36,
                170,
                28,
                15.2f,
                FontStyle.Regular,
                _blue);

            _idLabel.Font = new Font("Segoe UI", 15.2f, FontStyle.Regular);
            _idLabel.Cursor = Cursors.Hand;
            _idLabel.MouseEnter += (_, _) => _idLabel.ForeColor = _blueHover;
            _idLabel.MouseLeave += (_, _) => _idLabel.ForeColor = _blue;
            _idLabel.Click += (_, _) =>
            {
                CopyIdClicked?.Invoke();
                ShowCopySuccessIcon();
            };

            card.Controls.Add(_idLabel);

            _copyIcon = CreateIcon(
                DotDeskIconType.Copy,
                208,
                41,
                18,
                18,
                Color.FromArgb(75, 85, 99));

            _copyIcon.Click += (_, _) =>
            {
                CopyIdClicked?.Invoke();
                ShowCopySuccessIcon();
            };

            card.Controls.Add(_copyIcon);

            card.Controls.Add(CreateLabel(
                "› 一次性密码",
                22,
                68,
                120,
                18,
                9.2f,
                FontStyle.Regular,
                _textGray));

            _passwordLabel = CreateLabel(
                "------",
                22,
                88,
                130,
                26,
                14.5f,
                FontStyle.Regular,
                _blue);

            _passwordLabel.Font = new Font("Segoe UI", 14.5f, FontStyle.Regular);
            _passwordLabel.Cursor = Cursors.Hand;
            _passwordLabel.MouseEnter += (_, _) => _passwordLabel.ForeColor = _blueHover;
            _passwordLabel.MouseLeave += (_, _) => _passwordLabel.ForeColor = _blue;
            _passwordLabel.Click += (_, _) => ShowPasswordClicked?.Invoke();

            card.Controls.Add(_passwordLabel);

            var refreshIcon = CreateIcon(
                DotDeskIconType.Refresh,
                180,
                93,
                18,
                18,
                Color.FromArgb(75, 85, 99));

            refreshIcon.Click += (_, _) => RefreshPasswordClicked?.Invoke();
            card.Controls.Add(refreshIcon);

            var eyeIcon = CreateIcon(
                DotDeskIconType.Eye,
                212,
                93,
                18,
                18,
                Color.FromArgb(75, 85, 99));

            eyeIcon.Click += (_, _) => ShowPasswordClicked?.Invoke();
            card.Controls.Add(eyeIcon);

            return card;
        }

        private AntPanel CreateMenuItem(
            DotDeskIconType icon,
            string text,
            int x,
            int y,
            bool active,
            Action click)
        {
            var normalBack = _sidebarBack;
            var hoverBack = Color.FromArgb(245, 249, 255);
            var activeBack = Color.FromArgb(235, 244, 255);

            var normalColor = _navGray;
            var hoverColor = _blue;
            var activeColor = _blue;

            var item = new AntPanel
            {
                Location = new Point(x, y),
                Size = new Size(ItemWidth - 8, 36),
                Radius = 8,

                Back = active ? activeBack : normalBack,
                BackColor = active ? activeBack : normalBack,

                Cursor = Cursors.Hand,
                Shadow = 0,
                ShadowOpacity = 0f,
                BorderWidth = 0
            };

            ApplyRoundRegion(item, 8);

            var iconControl = CreateIcon(
                icon,
                16,
                9,
                18,
                18,
                active ? activeColor : normalColor);

            var textLabel = CreateLabel(
                text,
                52,
                7,
                120,
                22,
                9.2f,
                active ? FontStyle.Bold : FontStyle.Regular,
                active ? activeColor : normalColor);

            iconControl.Cursor = Cursors.Hand;
            textLabel.Cursor = Cursors.Hand;

            item.Controls.Add(iconControl);
            item.Controls.Add(textLabel);

            void SetHover()
            {
                if (active) return;

                item.Back = hoverBack;
                item.BackColor = hoverBack;
                iconControl.IconColor = hoverColor;
                textLabel.ForeColor = hoverColor;
            }

            void SetNormal()
            {
                if (active) return;

                item.Back = normalBack;
                item.BackColor = normalBack;
                iconControl.IconColor = normalColor;
                textLabel.ForeColor = normalColor;
            }

            item.MouseEnter += (_, _) => SetHover();
            iconControl.MouseEnter += (_, _) => SetHover();
            textLabel.MouseEnter += (_, _) => SetHover();

            item.MouseLeave += (_, _) =>
            {
                if (!item.ClientRectangle.Contains(item.PointToClient(Cursor.Position)))
                    SetNormal();
            };

            iconControl.MouseLeave += (_, _) =>
            {
                if (!item.ClientRectangle.Contains(item.PointToClient(Cursor.Position)))
                    SetNormal();
            };

            textLabel.MouseLeave += (_, _) =>
            {
                if (!item.ClientRectangle.Contains(item.PointToClient(Cursor.Position)))
                    SetNormal();
            };

            item.Click += (_, _) => click();
            iconControl.Click += (_, _) => click();
            textLabel.Click += (_, _) => click();

            return item;
        }

        private AntPanel CreateSafeCard()
        {
            var card = new AntPanel
            {
                Size = new Size(ItemWidth, 60),
                Radius = 10,

                Back = Color.FromArgb(235, 244, 255),
                BackColor = Color.FromArgb(235, 244, 255),

                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                Shadow = 0,
                ShadowOpacity = 0f,
                BorderWidth = 0
            };

            ApplyRoundRegion(card, 10);

            card.Controls.Add(CreateIcon(
                DotDeskIconType.Shield,
                16,
                18,
                24,
                24,
                Color.FromArgb(16, 185, 129)));

            card.Controls.Add(CreateLabel(
                "安全连接已启用",
                54,
                10,
                140,
                22,
                9.5f,
                FontStyle.Bold,
                _textDark));

            card.Controls.Add(CreateLabel(
                "端到端加密保护中",
                54,
                31,
                140,
                20,
                8.5f,
                FontStyle.Regular,
                _textGray));

            var arrow = CreateLabel(
                "›",
                220,
                17,
                20,
                24,
                15,
                FontStyle.Regular,
                _textDark);

            arrow.TextAlign = ContentAlignment.MiddleCenter;
            card.Controls.Add(arrow);

            return card;
        }

        private DotDeskSvgIcon CreateIcon(
            DotDeskIconType type,
            int x,
            int y,
            int width,
            int height,
            Color color)
        {
            return new DotDeskSvgIcon
            {
                IconType = type,
                IconColor = color,
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
        }

        private void LayoutBottomCard()
        {
            if (_safeCard == null) return;

            _safeCard.Location = new Point(
                LeftX,
                Math.Min(Height - _safeCard.Height - 14, TopOffset + 480));

            ApplyRoundRegion(_safeCard, 10);
        }

        private System.Windows.Forms.Label CreateLabel(
            string text,
            int x,
            int y,
            int width,
            int height,
            float fontSize,
            FontStyle fontStyle,
            Color color)
        {
            return new System.Windows.Forms.Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Microsoft YaHei UI", fontSize, fontStyle),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        private static void ApplyRoundRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0) return;

            control.Region?.Dispose();

            using var path = GraphicsExtensions.CreateRoundedRectanglePath(
                new RectangleF(0, 0, control.Width, control.Height),
                radius);

            control.Region = new Region(path);
        }

        public void SetId(string id)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetId(id));
                return;
            }

            if (_idLabel != null)
                _idLabel.Text = string.IsNullOrWhiteSpace(id) ? "------" : id;
        }

        public void SetPassword(string password)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => SetPassword(password));
                return;
            }

            if (_passwordLabel != null)
                _passwordLabel.Text = string.IsNullOrWhiteSpace(password) ? "------" : password;
        }

        public void ShowCopySuccessIcon()
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => ShowCopySuccessIcon());
                return;
            }

            if (_copyIcon == null)
                return;

            _copySuccessTimer ??= new System.Windows.Forms.Timer
            {
                Interval = 1600
            };

            _copySuccessTimer.Stop();
            _copySuccessTimer.Tick -= CopySuccessTimer_Tick;
            _copySuccessTimer.Tick += CopySuccessTimer_Tick;

            _copyIcon.IconType = DotDeskIconType.Check;
            _copyIcon.IconColor = Color.FromArgb(34, 197, 94);
            _copyIcon.Invalidate();

            _copySuccessTimer.Start();
        }

        private void CopySuccessTimer_Tick(object? sender, EventArgs e)
        {
            if (_copySuccessTimer != null)
                _copySuccessTimer.Stop();

            if (_copyIcon == null)
                return;

            _copyIcon.IconType = DotDeskIconType.Copy;
            _copyIcon.IconColor = Color.FromArgb(75, 85, 99);
            _copyIcon.Invalidate();
        }

        public string DeviceIdText => _idLabel?.Text ?? "";

        public string PasswordText => _passwordLabel?.Text ?? "";
    }

    public enum DotDeskIconType
    {
        Remote,
        Device,
        Clock,
        Copy,
        Check,
        Refresh,
        Eye,
        Shield,
        MonitorLarge
    }

    public class GradientCard : UserControl
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 10;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color StartColor { get; set; } = Color.FromArgb(37, 99, 235);

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color EndColor { get; set; } = Color.FromArgb(94, 76, 255);

        public GradientCard()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new RectangleF(0, 0, Width - 1, Height - 1);

            using var path = GraphicsExtensions.CreateRoundedRectanglePath(rect, Radius);

            using var brush = new LinearGradientBrush(
                rect,
                StartColor,
                EndColor,
                LinearGradientMode.ForwardDiagonal);

            e.Graphics.FillPath(brush, path);

            using var circleBrush1 = new SolidBrush(Color.FromArgb(26, Color.White));
            e.Graphics.FillEllipse(circleBrush1, Width - 92, -36, 130, 130);

            using var circleBrush2 = new SolidBrush(Color.FromArgb(16, Color.White));
            e.Graphics.FillEllipse(circleBrush2, Width - 160, 24, 140, 140);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            using var path = GraphicsExtensions.CreateRoundedRectanglePath(
                new RectangleF(0, 0, Width, Height),
                Radius);

            Region = new Region(path);
        }
    }

    public class DotDeskSvgIcon : Control
    {
        private Color _iconColor = Color.FromArgb(75, 85, 99);

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DotDeskIconType IconType { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color IconColor
        {
            get => _iconColor;
            set
            {
                _iconColor = value;
                Invalidate();
            }
        }

        public DotDeskSvgIcon()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(IconColor, 1.9f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            using var brush = new SolidBrush(IconColor);

            float w = Width;
            float h = Height;

            switch (IconType)
            {
                case DotDeskIconType.Remote:
                    DrawRemote(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.Device:
                    DrawDevice(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.Clock:
                    DrawClock(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.Copy:
                    DrawCopy(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.Check:
                    DrawCheck(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.Refresh:
                    DrawRefresh(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.Eye:
                    DrawEye(e.Graphics, pen, brush, w, h);
                    break;

                case DotDeskIconType.Shield:
                    DrawShield(e.Graphics, pen, w, h);
                    break;

                case DotDeskIconType.MonitorLarge:
                    DrawMonitorLarge(e.Graphics, w, h);
                    break;
            }
        }

        private static void DrawRemote(Graphics g, Pen pen, float w, float h)
        {
            var rect = new RectangleF(w * 0.12f, h * 0.20f, w * 0.76f, h * 0.52f);
            g.DrawRoundedRectangle(pen, rect, 2.5f);
            g.DrawLine(pen, w * 0.38f, h * 0.80f, w * 0.62f, h * 0.80f);
            g.DrawLine(pen, w * 0.50f, h * 0.72f, w * 0.50f, h * 0.80f);
        }

        private static void DrawDevice(Graphics g, Pen pen, float w, float h)
        {
            var rect = new RectangleF(w * 0.17f, h * 0.14f, w * 0.66f, h * 0.58f);
            g.DrawRoundedRectangle(pen, rect, 2.2f);
            g.DrawLine(pen, w * 0.35f, h * 0.84f, w * 0.65f, h * 0.84f);
            g.DrawLine(pen, w * 0.50f, h * 0.72f, w * 0.50f, h * 0.84f);
        }

        private static void DrawClock(Graphics g, Pen pen, float w, float h)
        {
            g.DrawEllipse(pen, w * 0.15f, h * 0.15f, w * 0.70f, h * 0.70f);
            g.DrawLine(pen, w * 0.50f, h * 0.30f, w * 0.50f, h * 0.52f);
            g.DrawLine(pen, w * 0.50f, h * 0.52f, w * 0.65f, h * 0.62f);
        }

        private static void DrawCopy(Graphics g, Pen pen, float w, float h)
        {
            var back = new RectangleF(w * 0.32f, h * 0.18f, w * 0.48f, h * 0.58f);
            var front = new RectangleF(w * 0.18f, h * 0.30f, w * 0.48f, h * 0.58f);
            g.DrawRoundedRectangle(pen, back, 2f);
            g.DrawRoundedRectangle(pen, front, 2f);
        }

        private static void DrawCheck(Graphics g, Pen pen, float w, float h)
        {
            using var checkPen = new Pen(pen.Color, 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            g.DrawLine(checkPen, w * 0.22f, h * 0.52f, w * 0.42f, h * 0.72f);
            g.DrawLine(checkPen, w * 0.42f, h * 0.72f, w * 0.78f, h * 0.30f);
        }

        private static void DrawRefresh(Graphics g, Pen pen, float w, float h)
        {
            var rect = new RectangleF(w * 0.18f, h * 0.18f, w * 0.64f, h * 0.64f);
            g.DrawArc(pen, rect, 35, 250);
            g.DrawLine(pen, w * 0.78f, h * 0.24f, w * 0.80f, h * 0.08f);
            g.DrawLine(pen, w * 0.78f, h * 0.24f, w * 0.60f, h * 0.23f);
        }

        private static void DrawEye(Graphics g, Pen pen, Brush brush, float w, float h)
        {
            using var path = new GraphicsPath();

            path.AddBezier(
                w * 0.12f,
                h * 0.50f,
                w * 0.28f,
                h * 0.22f,
                w * 0.72f,
                h * 0.22f,
                w * 0.88f,
                h * 0.50f);

            path.AddBezier(
                w * 0.88f,
                h * 0.50f,
                w * 0.72f,
                h * 0.78f,
                w * 0.28f,
                h * 0.78f,
                w * 0.12f,
                h * 0.50f);

            g.DrawPath(pen, path);
            g.FillEllipse(brush, w * 0.42f, h * 0.42f, w * 0.16f, h * 0.16f);
        }

        private static void DrawShield(Graphics g, Pen pen, float w, float h)
        {
            using var path = new GraphicsPath();

            path.StartFigure();
            path.AddLine(w * 0.50f, h * 0.08f, w * 0.82f, h * 0.22f);
            path.AddLine(w * 0.82f, h * 0.45f, w * 0.72f, h * 0.70f);
            path.AddLine(w * 0.50f, h * 0.90f, w * 0.28f, h * 0.70f);
            path.AddLine(w * 0.18f, h * 0.45f, w * 0.18f, h * 0.22f);
            path.CloseFigure();

            g.DrawPath(pen, path);

            using var checkPen = new Pen(pen.Color, 2.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            g.DrawLine(checkPen, w * 0.36f, h * 0.50f, w * 0.47f, h * 0.62f);
            g.DrawLine(checkPen, w * 0.47f, h * 0.62f, w * 0.66f, h * 0.39f);
        }

        private static void DrawMonitorLarge(Graphics g, float w, float h)
        {
            using var lightPen = new Pen(Color.FromArgb(190, 215, 255), 2.3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            var rect = new RectangleF(w * 0.10f, h * 0.18f, w * 0.78f, h * 0.54f);

            g.DrawRoundedRectangle(lightPen, rect, 5f);

            g.DrawLine(lightPen, w * 0.45f, h * 0.73f, w * 0.45f, h * 0.84f);
            g.DrawLine(lightPen, w * 0.33f, h * 0.86f, w * 0.58f, h * 0.86f);
        }
    }

    public class StatusDot : Control
    {
        public StatusDot()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(Color.FromArgb(34, 197, 94));

            e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
        }
    }

    public static class GraphicsExtensions
    {
        public static void DrawRoundedRectangle(
            this Graphics graphics,
            Pen pen,
            RectangleF bounds,
            float radius)
        {
            using var path = CreateRoundedRectanglePath(bounds, radius);
            graphics.DrawPath(pen, path);
        }

        public static GraphicsPath CreateRoundedRectanglePath(
            RectangleF bounds,
            float radius)
        {
            float diameter = radius * 2f;
            var path = new GraphicsPath();

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
