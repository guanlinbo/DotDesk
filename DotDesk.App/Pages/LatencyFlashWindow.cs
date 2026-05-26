using System;
using System.Drawing;
using System.Windows.Forms;

namespace DotDesk.App
{
    internal sealed class LatencyFlashWindow : Form
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Label _label;
        private bool _white;

        public LatencyFlashWindow()
        {
            Text = "Latency Flash";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(80, 80);
            ClientSize = new Size(260, 150);
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;

            _label = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Consolas", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Text = "FLASH 500ms"
            };
            Controls.Add(_label);

            _timer = new System.Windows.Forms.Timer { Interval = 500 };
            _timer.Tick += (_, _) => ToggleFlash();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        private void ToggleFlash()
        {
            _white = !_white;
            BackColor = _white ? Color.White : Color.Black;
            _label.ForeColor = _white ? Color.Black : Color.White;
            _label.Text = $"{(_white ? "WHITE" : "BLACK")} {DateTime.Now:HH:mm:ss.fff}";
        }
    }
}
