namespace DotDesk.App
{
    public partial class manUi : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public manUi()
        {
            InitializeComponent();
            InitChrome();

            var homePage = new HomePage
            {
                Dock = DockStyle.Fill
            };
            contentPanel.Controls.Add(homePage);
        }

        private void InitChrome()
        {
            BackColor = Color.FromArgb(246, 249, 255);
            titleBar.BackColor = Color.White;

            appLogoLabel.Text = "D";
            appLogoLabel.ForeColor = Color.FromArgb(37, 99, 235);
            appLogoLabel.Font = new Font("Segoe UI", 22F, FontStyle.Bold);

            homeTabButton.Text = "主页";
            homeTabButton.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            homeTabButton.ForeColor = Color.FromArgb(15, 23, 42);
            homeTabButton.OriginalBackColor = Color.White;

            settingsTabButton.Text = "设置";
            settingsTabButton.Font = new Font("Microsoft YaHei UI", 12F);
            settingsTabButton.ForeColor = Color.FromArgb(55, 65, 81);
            settingsTabButton.OriginalBackColor = Color.White;

            menuButton.Text = "=";
            minimizeWindowButton.Text = "-";
            maximizeWindowButton.Text = "□";
            closeWindowButton.Text = "x";
            menuButton.OriginalBackColor = Color.White;
            minimizeWindowButton.OriginalBackColor = Color.White;
            maximizeWindowButton.OriginalBackColor = Color.White;
            closeWindowButton.OriginalBackColor = Color.White;
            menuButton.ForeColor = Color.FromArgb(15, 23, 42);
            minimizeWindowButton.ForeColor = Color.FromArgb(15, 23, 42);
            maximizeWindowButton.ForeColor = Color.FromArgb(15, 23, 42);
            closeWindowButton.ForeColor = Color.FromArgb(15, 23, 42);

            titleBar.MouseDown += DragWindow;
            appLogoLabel.MouseDown += DragWindow;
        }

        private void DragWindow(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }

        private void minimizeWindowButton_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void maximizeWindowButton_Click(object sender, EventArgs e)
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void closeWindowButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void WndProc(ref Message m)
        {
            const int wmNcHitTest = 0x0084;
            const int htClient = 1;
            const int htCaption = 2;

            base.WndProc(ref m);

            if (m.Msg != wmNcHitTest || (int)m.Result != htClient)
                return;

            var screenPoint = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
            var point = PointToClient(screenPoint);
            if (point.Y <= titleBar.Height && point.X > 320 && point.X < ClientSize.Width - 190)
                m.Result = htCaption;
        }
    }
}
