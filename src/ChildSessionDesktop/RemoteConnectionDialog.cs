using System;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal static class RemoteConnectionDialog
    {
        internal static bool Prompt(IWin32Window parent, DesktopPreferences preferences, out RdpEndpoint endpoint, out string account,
            out string password, out RdpImport imported, out bool rememberPassword)
        {
            endpoint = null;
            account = password = null;
            imported = null;
            rememberPassword = false;
            RdpImport selectedProfile = null;
            using (var dialog = new Form
            {
                Text = Strings.T("sessions_add"), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, MaximizeBox = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScaleMode = AutoScaleMode.Font
            })
            {
                var server = new TextBox { Width = 370, MaxLength = 270 };
                var user = new TextBox { Width = 370, MaxLength = 255 };
                var secret = new TextBox
                {
                    Width = 370, MaxLength = 1024, UseSystemPasswordChar = true,
                    AccessibleName = Strings.T("credentials_password")
                };
                var summary = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(370, 0) };
                var remember = new CheckBox { AutoSize = true, Text = Strings.T("remote_remember_password") };
                var connect = new Button { Text = Strings.T("credentials_connect"), AutoSize = true, DialogResult = DialogResult.OK, Enabled = false };
                var cancel = new Button { Text = Strings.T("credentials_cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
                var import = new Button { Text = Strings.T("rdp_import"), AutoSize = true };
                Action validate = delegate
                {
                    RdpEndpoint target;
                    connect.Enabled = RdpEndpoint.TryForAccount(server.Text, user.Text.Trim(), out target);
                    summary.Text = !connect.Enabled ? "" : Strings.T("rdp_target", target.Display);
                    bool certificateWarning = connect.Enabled && preferences.UsesCertificateWarning(target);
                    remember.Enabled = connect.Enabled && !certificateWarning && secret.TextLength != 0;
                    if (!remember.Enabled) remember.Checked = false;
                    if (certificateWarning) summary.Text += Environment.NewLine + Strings.T("remote_password_requires_certificate");
                    if (selectedProfile != null && selectedProfile.HasProtectedPassword && secret.TextLength == 0)
                        summary.Text += Environment.NewLine + Strings.T("rdp_protected_ready");
                };
                EventHandler targetChanged = delegate { selectedProfile = null; secret.Clear(); validate(); };
                server.TextChanged += targetChanged;
                user.TextChanged += targetChanged;
                secret.TextChanged += delegate { validate(); };
                import.Click += delegate
                {
                    using (var picker = new OpenFileDialog
                    {
                        Filter = Strings.T("rdp_file_filter"), DefaultExt = "rdp", CheckFileExists = true,
                        CheckPathExists = true, Multiselect = false, RestoreDirectory = true
                    })
                    {
                        if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                        try
                        {
                            var loaded = RdpImport.Read(picker.FileName);
                            server.Text = loaded.Endpoint.Display;
                            user.Text = loaded.Account;
                            secret.Clear();
                            selectedProfile = loaded;
                            validate();
                        }
                        catch (Exception ex)
                        {
                            ShowImportError(dialog, ex);
                        }
                    }
                };
                var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                actions.Controls.Add(connect);
                actions.Controls.Add(cancel);
                actions.Controls.Add(import);
                var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(14) };
                layout.Controls.Add(new Label { Text = Strings.T("sessions_remote_host"), AutoSize = true });
                layout.Controls.Add(server);
                layout.Controls.Add(new Label { Text = Strings.T("sessions_remote_account"), AutoSize = true });
                layout.Controls.Add(user);
                layout.Controls.Add(new Label { Text = Strings.T("rdp_password_optional"), AutoSize = true });
                layout.Controls.Add(secret);
                layout.Controls.Add(remember);
                layout.Controls.Add(summary);
                layout.Controls.Add(actions);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = connect;
                dialog.CancelButton = cancel;
                dialog.ActiveControl = server;
                if (dialog.ShowDialog(parent) != DialogResult.OK) return false;
                account = user.Text.Trim();
                if (!RdpEndpoint.TryForAccount(server.Text, account, out endpoint)) return false;
                password = secret.TextLength == 0 ? null : secret.Text;
                rememberPassword = remember.Checked;
                secret.Clear();
                imported = selectedProfile;
                return true;
            }
        }

        private static void ShowImportError(IWin32Window owner, Exception error)
        {
            string message = error is System.IO.InvalidDataException ? error.Message : Strings.T("rdp_import_unreadable");
            MessageBox.Show(owner, message, Strings.T("rdp_import"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
