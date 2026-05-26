using System;
using System.Drawing;
using System.Windows.Forms;
using DotDesk.Core.Config;

namespace DotDesk.App
{
    public sealed class FixedPasswordDialog : Form
    {
        private readonly TextBox _passwordInput;

        public string? FixedPassword { get; private set; }

        public FixedPasswordDialog(string? currentPassword)
        {
            Text = "设置固定访问密码";
            ClientSize = new Size(380, 226);
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
                Text = "固定访问密码",
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(28, 24),
                Size = new Size(324, 28)
            };

            var hint = new Label
            {
                AutoSize = false,
                Text = "设置后临时密码会固定为该值。清空并保存可恢复随机密码。",
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(28, 58),
                Size = new Size(324, 42)
            };

            _passwordInput = new TextBox
            {
                Location = new Point(28, 108),
                Size = new Size(324, 27),
                MaxLength = 6,
                Text = currentPassword ?? "",
                TextAlign = HorizontalAlignment.Center
            };

            var clearButton = new Button
            {
                Text = "恢复随机",
                Location = new Point(108, 164),
                Size = new Size(82, 30)
            };

            var cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(196, 164),
                Size = new Size(72, 30)
            };

            var saveButton = new Button
            {
                Text = "保存",
                DialogResult = DialogResult.OK,
                Location = new Point(280, 164),
                Size = new Size(72, 30)
            };

            _passwordInput.KeyPress += (_, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsLetterOrDigit(e.KeyChar))
                    e.Handled = true;
            };

            clearButton.Click += (_, _) =>
            {
                FixedPassword = null;
                DialogResult = DialogResult.OK;
                Close();
            };

            saveButton.Click += (_, _) =>
            {
                var normalized = DotDeskSettingsStore.NormalizePassword(_passwordInput.Text);

                if (normalized == null)
                {
                    MessageBox.Show(
                        this,
                        "请输入6位字母或数字，或点击“恢复随机”。",
                        "提示",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    _passwordInput.Focus();
                    _passwordInput.SelectAll();
                    DialogResult = DialogResult.None;
                    return;
                }

                FixedPassword = normalized;
            };

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_passwordInput);
            Controls.Add(clearButton);
            Controls.Add(cancelButton);
            Controls.Add(saveButton);

            Shown += (_, _) =>
            {
                _passwordInput.Focus();
                _passwordInput.SelectAll();
            };
        }
    }
}