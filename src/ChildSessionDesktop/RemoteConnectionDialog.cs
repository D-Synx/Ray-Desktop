using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal static class RemoteConnectionDialog
    {
        internal static bool Prompt(IWin32Window parent, out string host, out string account)
        {
            host = account = null;
            using (var dialog = new Form
            {
                Text = Strings.T("sessions_add"), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, MaximizeBox = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScaleMode = AutoScaleMode.Font
            })
            {
                var serverBox = new TextBox { Width = 320, MaxLength = 253 };
                var accountBox = new TextBox { Width = 320, MaxLength = 255 };
                var connect = new Button { Text = Strings.T("credentials_connect"), AutoSize = true, DialogResult = DialogResult.OK, Enabled = false };
                var cancel = new Button { Text = Strings.T("credentials_cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
                System.EventHandler validate = delegate
                {
                    connect.Enabled = DnsName.IsRemoteName(serverBox.Text.Trim()) && accountBox.Text.Trim().Length != 0;
                };
                serverBox.TextChanged += validate;
                accountBox.TextChanged += validate;
                var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
                actions.Controls.Add(connect);
                actions.Controls.Add(cancel);
                var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(14) };
                layout.Controls.Add(new Label { Text = Strings.T("sessions_remote_host"), AutoSize = true });
                layout.Controls.Add(serverBox);
                layout.Controls.Add(new Label { Text = Strings.T("sessions_remote_account"), AutoSize = true });
                layout.Controls.Add(accountBox);
                layout.Controls.Add(actions);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = connect;
                dialog.CancelButton = cancel;
                dialog.ActiveControl = serverBox;
                if (dialog.ShowDialog(parent) != DialogResult.OK) return false;
                host = serverBox.Text.Trim();
                account = accountBox.Text.Trim();
                return true;
            }
        }
    }

    internal static class DnsName
    {
        internal static bool IsRemoteName(string host)
        {
            return System.Uri.CheckHostName(host) == System.UriHostNameType.Dns
                && !string.Equals(host.TrimEnd('.'), "localhost", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(host.TrimEnd('.'), System.Environment.MachineName, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
