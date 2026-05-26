using System.Drawing.Drawing2D;
using AntdUI;

namespace DotDesk.App;

/// <summary>
/// DotDesk 界面公共样式与尺寸。
/// 这里集中管理主窗口尺寸、圆角阴影和常用按钮样式，避免页面里到处写重复代码。
/// </summary>
internal static class DotDeskUi
{
    public static readonly Size MainWindowClientSize = new(950, 570);
    public static readonly Size HomePageSize = new(950, 513);

    public static readonly Color AppBackground = Color.FromArgb(246, 249, 255);
    public static readonly Color SidebarBackground = Color.FromArgb(239, 244, 252);
    public static readonly Color PrimaryBlue = Color.FromArgb(37, 99, 235);
    public static readonly Color TextDark = Color.FromArgb(15, 23, 42);
    public static readonly Color TextGray = Color.FromArgb(100, 116, 139);
    public static readonly Color BorderSoft = Color.FromArgb(230, 235, 245);

    public const int MainSidebarWidth = 280;
    public const int MainWindowRadius = 18;
    public const int MainWindowShadow = 18;

    public static void ApplyFixedMainWindow(BorderlessForm form)
    {
        form.ClientSize = MainWindowClientSize;
        form.MinimumSize = form.Size;
        form.MaximumSize = form.Size;
        form.BackColor = AppBackground;
        form.StartPosition = FormStartPosition.CenterScreen;

        form.Radius = MainWindowRadius;
        form.Shadow = MainWindowShadow;
        form.ShadowColor = Color.FromArgb(90, 15, 23, 42);
        form.ShadowPierce = false;
        form.UseDwm = true;

        // 主界面固定 950x570，不允许拉伸或最大化造成布局错位。
        form.Resizable = false;
        form.MaximizeBox = false;
    }

    public static void StyleCard(AntdUI.Panel panel, Color backColor, int radius = 16, int shadow = 8)
    {
        panel.Back = backColor;
        panel.BackColor = backColor;
        panel.Radius = radius;
        panel.Shadow = shadow;
        panel.ShadowColor = Color.FromArgb(148, 163, 184);
        panel.ShadowOpacity = 0.16F;
        panel.ShadowOffsetX = 0;
        panel.ShadowOffsetY = 4;
    }

    public static void StyleTopButton(AntdUI.Button button, string text, string iconName, bool active)
    {
        var currentColor = active ? PrimaryBlue : TextGray;

        button.Text = text;
        button.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
        button.ForeColor = currentColor;
        button.ForeHover = PrimaryBlue;
        //button.OriginalBackColor = DotDeskUi.AppBackground;
        //button.BackColor = DotDeskUi.AppBackground;
        //button.BackHover = DotDeskUi.AppBackground;
        //button.BackActive = DotDeskUi.AppBackground;
        // 关键：主页 / 设置按钮背景统一使用 AppBackground
        button.OriginalBackColor = DotDeskUi.AppBackground;
        button.BackColor = DotDeskUi.AppBackground;
        button.BackHover = DotDeskUi.AppBackground;
        button.BackActive = DotDeskUi.AppBackground;
        button.Radius = 0;
        button.BorderWidth = 0;
        button.DefaultBorderColor = Color.Transparent;
        button.Ghost = false;
        button.WaveSize = 0;
        button.Padding = new Padding(8, 0, 8, 0);
        button.IconSize = new Size(16, 16);
        button.IconSvg = GetTabIconSvg(iconName, currentColor);
        button.IconHoverSvg = GetTabIconSvg(iconName, PrimaryBlue);
    }

    public static void StyleWindowButton(AntdUI.Button button, string iconName, bool isClose = false)
    {
        var normalColor = Color.FromArgb(30, 41, 59);
        var hoverColor = isClose ? Color.White : TextDark;

        button.Text = "";
        button.Font = new Font("Segoe UI", 12F, FontStyle.Regular);
        button.ForeColor = normalColor;
        button.ForeHover = hoverColor;
        button.IconSize = new Size(17, 17);
        button.IconSvg = GetWindowIconSvg(iconName, normalColor);
        button.IconHoverSvg = GetWindowIconSvg(iconName, hoverColor);
        button.OriginalBackColor = Color.White;
        button.BackColor = Color.White;
        button.BackHover = isClose ? Color.FromArgb(239, 68, 68) : Color.FromArgb(241, 245, 249);
        button.BackActive = isClose ? Color.FromArgb(220, 38, 38) : Color.FromArgb(226, 232, 240);
        button.Radius = 8;
        button.BorderWidth = 0;
        button.WaveSize = 0;
        button.Ghost = false;
        button.DefaultBorderColor = Color.Transparent;
        button.Padding = new Padding(0);
    }

    private static string GetTabIconSvg(string iconName, Color color)
    {
        var fill = ColorTranslator.ToHtml(color);

        return iconName switch
        {
            "home" =>
                $"<svg viewBox=\"0 0 1024 1024\">" +
                $"<path fill=\"{fill}\" d=\"M946.5 505L534.6 93.4a31.93 31.93 0 0 0-45.2 0L77.5 505c-12 12-18.8 28.3-18.8 45.3 0 35.3 28.7 64 64 64h43.4V908c0 17.7 14.3 32 32 32H448V716h128v224h265.9c17.7 0 32-14.3 32-32V614.3h43.4c17 0 33.3-6.7 45.3-18.8 24.9-25 24.9-65.5-.1-90.5z\"/>" +
                $"</svg>",

            "settings" =>
                $"<svg viewBox=\"0 0 1024 1024\">" +
                $"<path fill=\"{fill}\" d=\"M924.8 625.7l-65.5-56c3.1-19 4.7-38.4 4.7-57.8s-1.6-38.8-4.7-57.8l65.5-56a32.03 32.03 0 0 0 9.3-35.2l-.9-2.6a442.81 442.81 0 0 0-79.7-137.9l-1.8-2.1a32.12 32.12 0 0 0-35.1-9.5l-81.3 28.9c-30-24.6-63.5-44-99.7-57.6l-15.7-85a32.05 32.05 0 0 0-25.8-25.7l-2.7-.5c-52.1-9.4-106.9-9.4-159 0l-2.7.5a32.05 32.05 0 0 0-25.8 25.7l-15.8 85.4a351.86 351.86 0 0 0-99 57.4l-81.9-29.1a32 32 0 0 0-35.1 9.5l-1.8 2.1a446.02 446.02 0 0 0-79.7 137.9l-.9 2.6c-4.5 12.5-.8 26.5 9.3 35.2l66.3 56.6c-3.1 18.8-4.6 38-4.6 57.1 0 19.2 1.5 38.4 4.6 57.1L99 625.5a32.03 32.03 0 0 0-9.3 35.2l.9 2.6c18.1 50.4 44.9 96.9 79.7 137.9l1.8 2.1a32.12 32.12 0 0 0 35.1 9.5l81.9-29.1c29.8 24.5 63.1 43.9 99 57.4l15.8 85.4a32.05 32.05 0 0 0 25.8 25.7l2.7.5a449.4 449.4 0 0 0 159 0l2.7-.5a32.05 32.05 0 0 0 25.8-25.7l15.7-85a351.86 351.86 0 0 0 99.7-57.6l81.3 28.9a32 32 0 0 0 35.1-9.5l1.8-2.1c34.8-41.1 61.6-87.5 79.7-137.9l.9-2.6c4.5-12.3.8-26.3-9.3-35zM512 668c-85.7 0-156-70.3-156-156s70.3-156 156-156 156 70.3 156 156-70.3 156-156 156z\"/>" +
                $"</svg>",

            _ => ""
        };
    }

    private static string GetWindowIconSvg(string iconName, Color color)
    {
        var stroke = ColorTranslator.ToHtml(color);

        return iconName switch
        {
            "menu" =>
                $"<svg viewBox=\"0 0 1024 1024\">" +
                $"<path d=\"M224 320H800M224 512H800M224 704H800\" stroke=\"{stroke}\" stroke-width=\"78\" stroke-linecap=\"round\" fill=\"none\"/>" +
                $"</svg>",

            "minimize" =>
                $"<svg viewBox=\"0 0 1024 1024\">" +
                $"<path d=\"M256 512H768\" stroke=\"{stroke}\" stroke-width=\"78\" stroke-linecap=\"round\" fill=\"none\"/>" +
                $"</svg>",

            "maximize" =>
                $"<svg viewBox=\"0 0 1024 1024\">" +
                $"<rect x=\"256\" y=\"256\" width=\"512\" height=\"512\" rx=\"42\" ry=\"42\" stroke=\"{stroke}\" stroke-width=\"72\" fill=\"none\"/>" +
                $"</svg>",

            "close" =>
                $"<svg viewBox=\"0 0 1024 1024\">" +
                $"<path d=\"M304 304L720 720M720 304L304 720\" stroke=\"{stroke}\" stroke-width=\"78\" stroke-linecap=\"round\" fill=\"none\"/>" +
                $"</svg>",

            _ => ""
        };
    }
}

/// <summary>
/// 顶部 Tab 的圆角下划线控件。
/// </summary>
internal sealed class RoundTabLine : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color LineColor { get; set; } = DotDeskUi.PrimaryBlue;

    public RoundTabLine()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        const int lineHeight = 3;
        var y = (Height - lineHeight) / 2;

        using var brush = new SolidBrush(LineColor);
        using var path = CreateRoundRectPath(new RectangleF(0, y, Width, lineHeight), lineHeight / 2f);

        e.Graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        if (diameter > rect.Width) diameter = rect.Width;
        if (diameter > rect.Height) diameter = rect.Height;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
