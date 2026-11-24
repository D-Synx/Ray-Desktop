using System.Drawing;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal sealed class PasswordDialog : ChromeForm
    {
        private readonly TextBox passwordBox;

        private PasswordDialog(string account)
        {
            Text = Strings.T("credentials_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoSize = true;

            passwordBox = new TextBox
            {
                UseSystemPasswordChar = true,
                Dock = DockStyle.Fill,
                AccessibleName = Strings.T("credentials_password")
            };
            var connect = new Button
            {
                Text = Strings.T("credentials_connect"),
                AutoSize = true,
                DialogResult = DialogResult.OK,
                Enabled = false
            };
            var cancel = new Button
            {
                Text = Strings.T("credentials_cancel"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            ChromeColors.StyleField(passwordBox);
            ChromeColors.StyleButton(connect, true);
            ChromeColors.StyleButton(cancel);
            passwordBox.TextChanged += delegate { connect.Enabled = passwordBox.TextLength > 0; };
            AcceptButton = connect;
            CancelButton = cancel;

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0, 12, 0, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(connect);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(360, 0),
                Padding = new Padding(16)
            };
            layout.Controls.Add(new Label { Text = Strings.T("credentials_account", account), AutoSize = true, UseMnemonic = false });
            layout.Controls.Add(new Label { Text = Strings.T("credentials_password"), AutoSize = true, Margin = new Padding(3, 10, 3, 0) });
            layout.Controls.Add(passwordBox);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            ActiveControl = passwordBox;
        }

        internal static string Prompt(IWin32Window owner, string account)
        {
            using (var dialog = new PasswordDialog(account))
            {
                try
                {
                    return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.passwordBox.Text : null;
                }
                finally
                {
                    dialog.passwordBox.Clear();
                }
            }
        }
    }
}
