using System;
using System.Drawing;
using System.Windows.Forms;

namespace DotDesk.App
{
    public sealed class PasswordDialog : Form
    {
        private readonly TextBox _passwordInput;
        private bool _handlingInviteText;

        public string Password => _passwordInput.Text
            .Replace("-", "")
            .Replace(" ", "")
            .Trim()
            .ToLowerInvariant();
        public string? InviteDeviceCode { get; private set; }

        public PasswordDialog()
        {
            Text = "输入密码";
            ClientSize = new Size(360, 202);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;

            var title = new Label
            {
                AutoSize = false,
                Text = "请输入对方的一次性密码",
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(28, 24),
                Size = new Size(304, 28)
            };

            var hint = new Label
            {
                AutoSize = false,
                Text = "密码为 6 位字母或数字，验证通过后会继续建立连接。",
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(28, 58),
                Size = new Size(304, 24)
            };

            _passwordInput = new TextBox
            {
                Location = new Point(28, 94),
                Size = new Size(304, 27),
                MaxLength = 512,
                PasswordChar = '*',
                TextAlign = HorizontalAlignment.Center
            };

            var cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(170, 148),
                Size = new Size(76, 30)
            };

            var okButton = new Button
            {
                Text = "连接",
                DialogResult = DialogResult.OK,
                Location = new Point(256, 148),
                Size = new Size(76, 30)
            };

            _passwordInput.KeyPress += (_, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsLetterOrDigit(e.KeyChar))
                    e.Handled = true;
            };

            _passwordInput.TextChanged += (_, _) =>
            {
                if (_handlingInviteText)
                    return;

                if (!DotDeskInviteParser.TryParse(_passwordInput.Text, out var invite))
                    return;

                _handlingInviteText = true;
                InviteDeviceCode = invite.DeviceCode;
                _passwordInput.Text = invite.Password;
                _passwordInput.SelectionStart = _passwordInput.Text.Length;
                _handlingInviteText = false;
            };

            okButton.Click += (_, _) =>
            {
                if (Password.Length == 6)
                    return;

                MessageBox.Show(
                    this,
                    "请输入6位字母或数字密码",
                    "提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                _passwordInput.Focus();
                _passwordInput.SelectAll();
                DialogResult = DialogResult.None;
            };

            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_passwordInput);
            Controls.Add(cancelButton);
            Controls.Add(okButton);

            Shown += (_, _) => _passwordInput.Focus();
        }
    }
}
