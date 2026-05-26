using AntdUI;
using DotDesk.Core.Config;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

using AntPanel = AntdUI.Panel;
using AntButton = AntdUI.Button;
using AntInput = AntdUI.Input;
using Label = System.Windows.Forms.Label;
//using Label = AntdUI.Label;

namespace DotDesk.App
{
    /// <summary>
    /// 首页右侧内容区。
    /// 这里只负责右侧 UI，不处理 WebRTC 业务。
    /// </summary>
    public class DotDeskHomeContent : UserControl
    {
        private readonly Color _blue = Color.FromArgb(0, 96, 220);
        private readonly Color _textDark = Color.FromArgb(15, 23, 42);
        private readonly Color _textGray = Color.FromArgb(100, 116, 139);
        private readonly Color _pageBack = Color.FromArgb(248, 250, 252);

        private AntPanel _connectCard = null!;
        private AntPanel _fileTransferCard = null!;
        private AntPanel _terminalCard = null!;
        private AntPanel _recentCard = null!;
        private AntPanel _settingsTipCard = null!;

        private AntInput _remoteIdInput = null!;
        private AntButton _connectButton = null!;
        private AntButton _connectMenuButton = null!;

        private Label _settingsTipLinkLabel = null!;

        private bool _formattingRemoteId;
        private string? _pendingInvitePassword;
        private bool _handlingInvitePaste;
        private bool _connectAllowed = true;

        public event Action? ConnectClicked;
        public event Action? InviteParsed;
        public event Action? ConnectMenuClicked;
        public event Action? FileTransferClicked;
        public event Action? TerminalClicked;
        public event Action? SettingsClicked;

        public string RemoteIdText => _remoteIdInput.Text ?? "";
        public string? PendingInvitePassword => _pendingInvitePassword;

        public string? ConsumePendingInvitePassword()
        {
            var password = _pendingInvitePassword;
            _pendingInvitePassword = null;
            return password;
        }

        public DotDeskHomeContent()
        {
            BackColor = _pageBack;
            DoubleBuffered = true;

            BuildUi();

            Resize += (_, _) => LayoutUi();
        }

        private void BuildUi()
        {
            SuspendLayout();
            try
            {
                Controls.Clear();

                _connectCard = CreateCard(Color.White, 14);
                Controls.Add(_connectCard);
                BuildConnectCard();

                _recentCard = CreateCard(Color.White, 14);
                Controls.Add(_recentCard);
                BuildRecentCard();

                _settingsTipCard = CreateCard(Color.FromArgb(235, 243, 255), 10);
                _settingsTipCard.Cursor = Cursors.Hand;
                _settingsTipCard.Click += (_, _) => SettingsClicked?.Invoke();
                Controls.Add(_settingsTipCard);
                BuildSettingsTipCard();

                LayoutUi();
                RefreshRecentList();
            }
            finally
            {
                ResumeLayout(false);
                Invalidate(true);
            }
        }

        private void BuildConnectCard()
        {
            _connectCard.Controls.Add(CreateLabel(
                "连接远程桌面",
                24,
                22,
                170,
                32,
                17f,
                FontStyle.Bold,
                _textDark));

            var help = CreateLabel(
                "?",
                194,
                26,
                24,
                24,
                10f,
                FontStyle.Bold,
                Color.FromArgb(100, 116, 139));

            help.TextAlign = ContentAlignment.MiddleCenter;
            _connectCard.Controls.Add(help);

            _remoteIdInput = new AntInput
            {
                Location = new Point(24, 64),
                Size = new Size(350, 48),
                Radius = 10,
                Font = new Font("Microsoft YaHei UI", 14f),
                PlaceholderText = "输入对方 ID",
                Text = ""
            };

            _remoteIdInput.TextChanged += (_, _) => FormatRemoteIdInput();
            _connectCard.Controls.Add(_remoteIdInput);

            _connectButton = new AntButton
            {
                Location = new Point(390, 64),
                Size = new Size(88, 48),
                Radius = 10,
                Text = "连接",
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = _blue,
                Cursor = Cursors.Hand
            };

            _connectButton.Click += (_, _) => ConnectClicked?.Invoke();
            _connectCard.Controls.Add(_connectButton);
            UpdateConnectButtonState();

            _connectMenuButton = new AntButton
            {
                Location = new Point(490, 64),
                Size = new Size(42, 48),
                Radius = 10,
                Text = "⌄",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };

            _connectMenuButton.Click += (_, _) => ConnectMenuClicked?.Invoke();
            _connectCard.Controls.Add(_connectMenuButton);

            _fileTransferCard = CreateFeatureCard(
                DotDeskHomeIconType.File,
                Color.FromArgb(139, 92, 246),
                "文件传输",
                "安全快速传输文件");

            _fileTransferCard.Click += (_, _) => FileTransferClicked?.Invoke();
            _connectCard.Controls.Add(_fileTransferCard);

            _terminalCard = CreateFeatureCard(
                DotDeskHomeIconType.Terminal,
                Color.FromArgb(16, 185, 129),
                "远程终端",
                "访问远程命令行");

            _terminalCard.Click += (_, _) => TerminalClicked?.Invoke();
            _connectCard.Controls.Add(_terminalCard);
        }

        private void BuildRecentCard()
        {
            _recentCard.Controls.Add(CreateLabel(
                "最近连接",
                18,
                16,
                120,
                28,
                13.5f,
                FontStyle.Bold,
                _textDark));

            var more = CreateLabel(
                "查看更多  ›",
                0,
                18,
                90,
                24,
                9.2f,
                FontStyle.Regular,
                _blue);

            more.Name = "recent_more";
            more.Cursor = Cursors.Hand;
            more.TextAlign = ContentAlignment.MiddleRight;
            _recentCard.Controls.Add(more);
        }

        private void BuildSettingsTipCard()
        {
            var bulb = new DotDeskHomeIcon
            {
                IconType = DotDeskHomeIconType.Bulb,
                IconColor = _blue,
                Location = new Point(18, 12),
                Size = new Size(20, 20),
                BackColor = Color.Transparent
            };

            _settingsTipCard.Controls.Add(bulb);

            var tip = CreateLabel(
                "想要无人值守访问？试试设置访问密码",
                46,
                10,
                360,
                24,
                9.2f,
                FontStyle.Regular,
                Color.FromArgb(71, 85, 105));

            tip.Cursor = Cursors.Hand;
            tip.Click += (_, _) => SettingsClicked?.Invoke();
            _settingsTipCard.Controls.Add(tip);

            _settingsTipLinkLabel = CreateLabel(
                "前往设置  ›",
                0,
                10,
                100,
                24,
                9.2f,
                FontStyle.Regular,
                _blue);

            _settingsTipLinkLabel.Cursor = Cursors.Hand;
            _settingsTipLinkLabel.TextAlign = ContentAlignment.MiddleRight;
            _settingsTipLinkLabel.Click += (_, _) => SettingsClicked?.Invoke();
            _settingsTipCard.Controls.Add(_settingsTipLinkLabel);
        }

        private void LayoutUi()
        {
            if (Width <= 0 || Height <= 0)
                return;

            const int contentHeight = 492;
            int contentX = 24;
            int contentY = Math.Max(16, Math.Min(42, (Height - contentHeight) / 2));
            int cardWidth = Math.Max(540, Width - 48);

            _connectCard.Location = new Point(contentX, contentY);
            _connectCard.Size = new Size(cardWidth, 218);
            ApplyRoundRegion(_connectCard, 14);

            _remoteIdInput.Location = new Point(24, 64);
            _remoteIdInput.Size = new Size(cardWidth - 196, 48);

            _connectButton.Location = new Point(cardWidth - 156, 64);
            _connectButton.Size = new Size(88, 48);

            _connectMenuButton.Location = new Point(cardWidth - 58, 64);
            _connectMenuButton.Size = new Size(40, 48);

            int featureY = 132;
            int featureGap = 20;
            int featureWidth = (cardWidth - 48 - featureGap) / 2;

            _fileTransferCard.Location = new Point(24, featureY);
            _fileTransferCard.Size = new Size(featureWidth, 64);
            ApplyRoundRegion(_fileTransferCard, 10);

            _terminalCard.Location = new Point(24 + featureWidth + featureGap, featureY);
            _terminalCard.Size = new Size(featureWidth, 64);
            ApplyRoundRegion(_terminalCard, 10);

            _recentCard.Location = new Point(contentX, contentY + 236);
            _recentCard.Size = new Size(cardWidth, 194);
            ApplyRoundRegion(_recentCard, 14);

            var recentMore = _recentCard.Controls.Find("recent_more", false).FirstOrDefault();
            if (recentMore != null)
            {
                recentMore.Location = new Point(cardWidth - 110, 18);
                recentMore.Size = new Size(90, 24);
            }

            _settingsTipCard.Location = new Point(contentX, contentY + 448);
            _settingsTipCard.Size = new Size(cardWidth, 44);
            ApplyRoundRegion(_settingsTipCard, 10);

            _settingsTipLinkLabel.Location = new Point(cardWidth - 120, 10);
            _settingsTipLinkLabel.Size = new Size(100, 24);

            RefreshRecentList();
        }

        private AntPanel CreateFeatureCard(
            DotDeskHomeIconType iconType,
            Color iconBackColor,
            string title,
            string subtitle)
        {
            var card = CreateCard(Color.White, 10);
            card.Cursor = Cursors.Hand;
            card.BorderWidth = 1;
            card.BorderColor = Color.FromArgb(238, 242, 247);
            card.Shadow = 0;
            card.ShadowOpacity = 0f;

            var iconBox = new DotDeskRoundBox
            {
                Location = new Point(16, 17),
                Size = new Size(30, 30),
                Radius = 8,
                BackColor = iconBackColor
            };

            iconBox.Controls.Add(new DotDeskHomeIcon
            {
                IconType = iconType,
                IconColor = Color.White,
                Location = new Point(6, 6),
                Size = new Size(18, 18),
                BackColor = Color.Transparent
            });

            card.Controls.Add(iconBox);

            card.Controls.Add(CreateLabel(
                title,
                62,
                12,
                140,
                24,
                10f,
                FontStyle.Bold,
                _textDark));

            card.Controls.Add(CreateLabel(
                subtitle,
                62,
                34,
                180,
                20,
                8.5f,
                FontStyle.Regular,
                _textGray));

            return card;
        }

        public void RefreshRecentList()
        {
            if (_recentCard == null)
                return;

            for (int i = _recentCard.Controls.Count - 1; i >= 0; i--)
            {
                if (Equals(_recentCard.Controls[i].Tag, "recent-row"))
                {
                    var control = _recentCard.Controls[i];
                    _recentCard.Controls.RemoveAt(i);
                    control.Dispose();
                }
            }

            var records = DotDeskSettingsStore.Load()
                .RecentConnections
                .OrderByDescending(x => x.LastConnectedAt)
                .Take(3)
                .ToList();

            if (records.Count == 0)
            {
                AddEmptyRecent();
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                var item = records[i];

                var name = string.IsNullOrWhiteSpace(item.Name)
                    ? $"远程设备 {item.DisplayCode}"
                    : item.Name;

                var address = string.IsNullOrWhiteSpace(item.Address)
                    ? item.DisplayCode
                    : item.Address;

                AddRecentRow(
                    name,
                    address,
                    FormatRecentTime(item.LastConnectedAt),
                    GetRecentIcon(name),
                    GetRecentColor(i),
                    58 + i * 50);
            }
        }

        private void AddEmptyRecent()
        {
            var empty = CreateLabel(
                "暂无最近连接",
                22,
                70,
                200,
                24,
                9.5f,
                FontStyle.Regular,
                _textGray);

            empty.Tag = "recent-row";
            _recentCard.Controls.Add(empty);
            empty.BringToFront();
        }

        private void AddRecentRow(
            string name,
            string address,
            string time,
            string icon,
            Color iconColor,
            int y)
        {
            int cardWidth = _recentCard.Width;
            int rowWidth = Math.Max(300, cardWidth - 36);

            var row = new AntPanel
            {
                Tag = "recent-row",
                Location = new Point(18, y),
                Size = new Size(rowWidth, 42),
                Radius = 8,
                Back = Color.White,
                BackColor = Color.White,
                BorderWidth = 1,
                BorderColor = Color.FromArgb(238, 242, 247),
                Shadow = 0,
                ShadowOpacity = 0f,
                Cursor = Cursors.Hand
            };

            ApplyRoundRegion(row, 8);

            var iconBox = new DotDeskRoundBox
            {
                BackColor = iconColor,
                Radius = 8,
                Location = new Point(12, 7),
                Size = new Size(28, 28)
            };

            iconBox.Controls.Add(new Label
            {
                Text = icon,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 0),
                Size = new Size(28, 28)
            });

            row.Controls.Add(iconBox);

            row.Controls.Add(CreateLabel(
                name,
                56,
                3,
                220,
                22,
                10f,
                FontStyle.Bold,
                _textDark));

            row.Controls.Add(CreateLabel(
                address,
                56,
                23,
                180,
                18,
                8.5f,
                FontStyle.Regular,
                _textGray));

            var timeLabel = CreateLabel(
                time,
                rowWidth - 150,
                11,
                110,
                20,
                8.5f,
                FontStyle.Regular,
                _textGray);

            timeLabel.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.Add(timeLabel);

            var moreLabel = CreateLabel(
                "⋮",
                rowWidth - 34,
                9,
                24,
                24,
                13f,
                FontStyle.Bold,
                Color.FromArgb(100, 116, 139));

            moreLabel.TextAlign = ContentAlignment.MiddleCenter;
            row.Controls.Add(moreLabel);

            _recentCard.Controls.Add(row);
            row.BringToFront();
        }

        public void SetConnectEnabled(bool enabled)
        {
            _connectAllowed = enabled;
            UpdateConnectButtonState();
        }

        public void ClearRemoteId()
        {
            _remoteIdInput.Text = "";
            _pendingInvitePassword = null;
            UpdateConnectButtonState();
        }

        private void FormatRemoteIdInput()
        {
            if (_formattingRemoteId)
            {
                UpdateConnectButtonState();
                return;
            }

            var text = _remoteIdInput.Text ?? "";
            if (!_handlingInvitePaste && DotDeskInviteParser.TryParse(text, out var invite))
            {
                _handlingInvitePaste = true;
                _pendingInvitePassword = invite.Password;
                SetRemoteIdText(invite.DeviceCode);
                _handlingInvitePaste = false;
                UpdateConnectButtonState();
                InviteParsed?.Invoke();
                return;
            }

            if (!_handlingInvitePaste && !text.Contains("密码", StringComparison.OrdinalIgnoreCase))
                _pendingInvitePassword = null;

            var digits = new string(text
                .Where(char.IsDigit)
                .Take(9)
                .ToArray());

            var formatted = digits.Length <= 3
                ? digits
                : digits.Length <= 6
                    ? $"{digits[..3]} {digits[3..]}"
                    : $"{digits[..3]} {digits[3..6]} {digits[6..]}";

            if (formatted == _remoteIdInput.Text)
            {
                UpdateConnectButtonState();
                return;
            }

            _formattingRemoteId = true;
            _remoteIdInput.Text = formatted;
            _remoteIdInput.SelectionStart = _remoteIdInput.Text.Length;
            _formattingRemoteId = false;
            UpdateConnectButtonState();
        }

        private void SetRemoteIdText(string deviceCode)
        {
            var digits = new string((deviceCode ?? "")
                .Where(char.IsDigit)
                .Take(9)
                .ToArray());

            var formatted = digits.Length <= 3
                ? digits
                : digits.Length <= 6
                    ? $"{digits[..3]} {digits[3..]}"
                    : $"{digits[..3]} {digits[3..6]} {digits[6..]}";

            _formattingRemoteId = true;
            _remoteIdInput.Text = formatted;
            _remoteIdInput.SelectionStart = _remoteIdInput.Text.Length;
            _formattingRemoteId = false;
            UpdateConnectButtonState();
        }

        private void UpdateConnectButtonState()
        {
            if (_connectButton == null || _remoteIdInput == null)
                return;

            var digitCount = (_remoteIdInput.Text ?? "").Count(char.IsDigit);
            var enabled = _connectAllowed && digitCount == 9;

            _connectButton.Enabled = enabled;
            _connectButton.BackColor = enabled ? _blue : Color.FromArgb(148, 163, 184);
            _connectButton.Cursor = enabled ? Cursors.Hand : Cursors.Default;
        }

        private AntPanel CreateCard(Color backColor, int radius)
        {
            return new AntPanel
            {
                Radius = radius,
                Back = backColor,
                BackColor = backColor,
                BorderWidth = 1,
                BorderColor = Color.FromArgb(240, 244, 249),
                Shadow = 8,
                ShadowOpacity = 0.05f
            };
        }

        private static Label CreateLabel(
            string text,
            int x,
            int y,
            int width,
            int height,
            float fontSize,
            FontStyle style,
            Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Microsoft YaHei UI", fontSize, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        private static void ApplyRoundRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0)
                return;

            control.Region?.Dispose();

            using var path = DotDeskHomeGraphics.CreateRoundedRectanglePath(
                new RectangleF(0, 0, control.Width, control.Height),
                radius);

            control.Region = new Region(path);
        }

        private static string FormatRecentTime(DateTime time)
        {
            var now = DateTime.Now;

            if (time.Date == now.Date)
                return $"今天 {time:HH:mm}";

            if (time.Date == now.Date.AddDays(-1))
                return $"昨天 {time:HH:mm}";

            var days = (now.Date - time.Date).Days;

            return days is > 1 and < 7
                ? $"{days} 天前"
                : time.ToString("MM-dd HH:mm");
        }

        private static string GetRecentIcon(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "D";

            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch))
                    return ch.ToString().ToUpperInvariant();
            }

            return "D";
        }

        private static Color GetRecentColor(int index)
        {
            return (index % 3) switch
            {
                0 => Color.FromArgb(37, 99, 235),
                1 => Color.FromArgb(16, 185, 129),
                _ => Color.FromArgb(124, 58, 237)
            };
        }
    }

    public enum DotDeskHomeIconType
    {
        File,
        Terminal,
        Bulb
    }

    public class DotDeskHomeIcon : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DotDeskHomeIconType IconType { get; set; }

        private Color _iconColor = Color.White;

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

        public DotDeskHomeIcon()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(IconColor, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            float w = Width;
            float h = Height;

            switch (IconType)
            {
                case DotDeskHomeIconType.File:
                    DrawFile(e.Graphics, pen, w, h);
                    break;

                case DotDeskHomeIconType.Terminal:
                    DrawTerminal(e.Graphics, pen, w, h);
                    break;

                case DotDeskHomeIconType.Bulb:
                    DrawBulb(e.Graphics, pen, w, h);
                    break;
            }
        }

        private static void DrawFile(Graphics g, Pen pen, float w, float h)
        {
            var rect = new RectangleF(w * 0.18f, h * 0.12f, w * 0.62f, h * 0.74f);
            DotDeskHomeGraphics.DrawRoundRect(g, pen, rect, 2.5f);
            g.DrawLine(pen, w * 0.30f, h * 0.42f, w * 0.70f, h * 0.42f);
            g.DrawLine(pen, w * 0.30f, h * 0.58f, w * 0.62f, h * 0.58f);
        }

        private static void DrawTerminal(Graphics g, Pen pen, float w, float h)
        {
            g.DrawLine(pen, w * 0.20f, h * 0.35f, w * 0.38f, h * 0.50f);
            g.DrawLine(pen, w * 0.38f, h * 0.50f, w * 0.20f, h * 0.65f);
            g.DrawLine(pen, w * 0.48f, h * 0.66f, w * 0.78f, h * 0.66f);
        }

        private static void DrawBulb(Graphics g, Pen pen, float w, float h)
        {
            g.DrawEllipse(pen, w * 0.28f, h * 0.12f, w * 0.44f, h * 0.44f);
            g.DrawLine(pen, w * 0.40f, h * 0.60f, w * 0.60f, h * 0.60f);
            g.DrawLine(pen, w * 0.42f, h * 0.74f, w * 0.58f, h * 0.74f);
            g.DrawLine(pen, w * 0.50f, h * 0.56f, w * 0.50f, h * 0.66f);
        }
    }

    public class DotDeskRoundBox : System.Windows.Forms.Panel
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 8;

        public DotDeskRoundBox()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = DotDeskHomeGraphics.CreateRoundedRectanglePath(
                new RectangleF(0, 0, Width - 1, Height - 1),
                Radius);

            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (Width <= 0 || Height <= 0)
                return;

            using var path = DotDeskHomeGraphics.CreateRoundedRectanglePath(
                new RectangleF(0, 0, Width, Height),
                Radius);

            Region = new Region(path);
        }
    }

    public static class DotDeskHomeGraphics
    {
        public static void DrawRoundRect(
            Graphics graphics,
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

            path.AddArc(
                bounds.Left,
                bounds.Top,
                diameter,
                diameter,
                180,
                90);

            path.AddArc(
                bounds.Right - diameter,
                bounds.Top,
                diameter,
                diameter,
                270,
                90);

            path.AddArc(
                bounds.Right - diameter,
                bounds.Bottom - diameter,
                diameter,
                diameter,
                0,
                90);

            path.AddArc(
                bounds.Left,
                bounds.Bottom - diameter,
                diameter,
                diameter,
                90,
                90);

            path.CloseFigure();
            return path;
        }
    }
}
