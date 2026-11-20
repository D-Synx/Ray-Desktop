using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSTSCLib;

namespace ChildSessionDesktop
{
    internal sealed partial class DesktopForm
    {
        private ContextMenuStrip _filesMenu;
        private bool copyingClipboardFiles;
        private CancellationTokenSource activeFileCopy;
        private bool localRdpClipboardActive;

        private bool WantsLocalRdpClipboard { get { return preferences.LocalRdpClipboard ?? (connectionAccount != null); } }

        private bool? FileTransferToggleState(bool outbound)
        {
            return sessions.SelectedRemote == null && !copyingClipboardFiles ? (bool?)preferences.AllowsFileTransfer(outbound) : null;
        }

        private void ToggleFileTransfer(bool outbound)
        {
            bool? current = FileTransferToggleState(outbound);
            if (current.HasValue) SavePreferences(preferences.SetFileTransfer(outbound, !current.Value));
        }

        private bool WantsSelectedRdpClipboard()
        {
            var remote = sessions.SelectedRemote;
            return remote == null ? WantsLocalRdpClipboard : remote.ShareClipboard;
        }

        private void SetSelectedRdpClipboard(bool enabled)
        {
            var remote = sessions.SelectedRemote;
            if (remote == null) SavePreferences(preferences.SetLocalRdpClipboard(enabled));
            else
            {
                remote.ShareClipboard = enabled;
                SavePreferences(preferences.SetSession(remote.Endpoint, remote.Account, enabled));
            }
            if (toolBar != null) toolBar.Invalidate();
        }

        private void CancelFileCopy()
        {
            if (activeFileCopy != null) activeFileCopy.Cancel();
        }

        private void ShowFilesMenuAt(Rectangle anchor)
        {
            var menu = MenuInstance(ref _filesMenu);
            var send = menu.Items.Add(Strings.T("files_send"));
            send.Click += delegate { TransferClipboardFiles(true); };
            var receive = menu.Items.Add(Strings.T("files_receive"));
            receive.Click += delegate { TransferClipboardFiles(false); };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.T("files_status")).Click += delegate { ShowFileDiagnostics(); };
            send.Enabled = FileTransferToggleState(true) == true;
            receive.Enabled = FileTransferToggleState(false) == true;
            menu.Show(new Point(anchor.Left, anchor.Bottom));
        }

        private string GetConnectedChildSid()
        {
            uint session;
            if (((IMsRdpClient9)rdp.GetOcxObject()).Connected != 1
                || !Program.WTSGetChildSessionId(out session) || session == uint.MaxValue)
                throw new InvalidOperationException(Strings.T("files_connect_first"));
            string user = ReadSessionAccountField(session, 5);
            string domain = ReadSessionAccountField(session, 7);
            if (string.IsNullOrWhiteSpace(user)) throw new InvalidOperationException(Strings.T("files_connect_first"));
            var account = new System.Security.Principal.NTAccount(string.IsNullOrEmpty(domain) ? user : domain + "\\" + user);
            return account.Translate(typeof(System.Security.Principal.SecurityIdentifier)).Value;
        }

        private async void TransferClipboardFiles(bool outbound)
        {
            if (FileTransferToggleState(outbound) != true || closing || disconnectTimer != null) return;
            copyingClipboardFiles = true;
            if (toolBar != null) toolBar.Invalidate();
            try
            {
                string sid = GetConnectedChildSid();
                if (!ClipboardRestrictions.AllowsFiles(outbound, sid))
                    throw new InvalidOperationException(Strings.T("files_policy_block"));
                IDataObject data = Clipboard.GetDataObject();
                bool virtualFiles = data != null && !outbound && (data.GetDataPresent("FileGroupDescriptorW", false)
                    || data.GetDataPresent("FileGroupDescriptor", false));
                var paths = new List<string>();
                if (!virtualFiles)
                {
                    foreach (string path in Clipboard.GetFileDropList()) paths.Add(path);
                    if (paths.Count == 0) throw new InvalidOperationException(Strings.T(outbound ? "files_copy_host" : "files_copy_child"));
                }
                else if (IsMoveClipboard(data))
                {
                    throw new InvalidOperationException(Strings.T("files_copy_child"));
                }
                uint clipboardVersion = ShellClipboard.GetClipboardSequenceNumber();
                string desktop = outbound ? GetChildSessionDesktop() : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string destination;
                using (var dialog = new FolderBrowserDialog { Description = Strings.T(outbound ? "files_send" : "files_receive"), SelectedPath = desktop })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    destination = dialog.SelectedPath;
                }
                if (clipboardVersion != ShellClipboard.GetClipboardSequenceNumber())
                    throw new InvalidOperationException(Strings.T("files_clipboard_changed"));
                if (!string.Equals(sid, GetConnectedChildSid(), StringComparison.Ordinal)
                    || !ClipboardRestrictions.AllowsFiles(outbound, sid))
                    throw new InvalidOperationException(Strings.T("files_session_changed"));

                if (virtualFiles)
                {
                    try { ShellClipboard.PasteInto(Handle, destination); }
                    catch (Exception ex)
                    {
                        Program.Log("Shell clipboard paste failed: " + ex.GetBaseException().Message);
                        if (MessageBox.Show(this, Strings.T("files_shell_unavailable"), Strings.T("files_title"),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            System.Diagnostics.Process.Start("explorer.exe", "\"" + destination + "\"");
                    }
                    return;
                }

                var duplicateChoice = MessageBox.Show(this, Strings.T("files_confirm", destination, paths.Count),
                    Strings.T("files_title"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (duplicateChoice == DialogResult.Cancel) return;
                if (!string.Equals(sid, GetConnectedChildSid(), StringComparison.Ordinal)
                    || !ClipboardRestrictions.AllowsFiles(outbound, sid))
                    throw new InvalidOperationException(Strings.T("files_session_changed"));
                await RunFileCopy(paths, destination, duplicateChoice == DialogResult.Yes);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !closing)
                    MessageBox.Show(this, ex.GetBaseException().Message, Strings.T("files_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                copyingClipboardFiles = false;
                if (toolBar != null) toolBar.Invalidate();
            }
        }

        private static bool IsMoveClipboard(IDataObject data)
        {
            using (var effect = data.GetData("Preferred DropEffect", false) as MemoryStream)
            {
                if (effect == null) return false;
                byte[] bytes = effect.ToArray();
                if (bytes.Length < sizeof(uint)) return false;
                uint flags = BitConverter.ToUInt32(bytes, 0);
                return (flags & 2) != 0 && (flags & 1) == 0;
            }
        }

        private async Task RunFileCopy(List<string> paths, string destination, bool keepBoth)
        {
            using (var cancellation = new CancellationTokenSource())
            using (var window = new Form { Text = Strings.T("files_title"), StartPosition = FormStartPosition.CenterParent,
                Size = new Size(460, 140), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                ShowInTaskbar = false })
            {
                var filename = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12), Text = Strings.T("files_progress") };
                var progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 10, Style = ProgressBarStyle.Marquee };
                var cancel = new Button { Dock = DockStyle.Bottom, Height = 30, Text = Strings.T("credentials_cancel") };
                cancel.Click += delegate { cancellation.Cancel(); cancel.Enabled = false; };
                FormClosingEventHandler cancelOnClose = delegate(object sender, FormClosingEventArgs e)
                {
                    if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; cancellation.Cancel(); cancel.Enabled = false; }
                };
                window.FormClosing += cancelOnClose;
                window.Controls.Add(filename);
                window.Controls.Add(progressBar);
                window.Controls.Add(cancel);

                // Poll the latest transfer name instead of queuing a UI callback for every file in large directories.
                string latest = null;
                var result = new TransferResult();
                bool cancelled = false;
                window.Show(this);
                activeFileCopy = cancellation;
                using (var refresh = new System.Windows.Forms.Timer { Interval = 150 })
                {
                    refresh.Tick += delegate
                    {
                        string name = Volatile.Read(ref latest);
                        if (name != null && !window.IsDisposed) filename.Text = name;
                    };
                    refresh.Start();
                    try
                    {
                        await Task.Run(() => FileTransfer.Copy(paths, destination, keepBoth, cancellation.Token,
                            name => Volatile.Write(ref latest, name), result));
                    }
                    catch (OperationCanceledException) { cancelled = true; }
                    finally
                    {
                        activeFileCopy = null;
                        window.FormClosing -= cancelOnClose;
                        if (!window.IsDisposed) window.Close();
                    }
                }
                if (IsDisposed || closing) return;
                string summary = Strings.T(cancelled ? "files_cancelled" : "files_done", result.Copied, result.Skipped, result.Failures.Count)
                    + Environment.NewLine + destination;
                if (result.Failures.Count > 0)
                    summary += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Failures.GetRange(0, Math.Min(5, result.Failures.Count)));
                MessageBox.Show(this, summary, Strings.T("files_title"), MessageBoxButtons.OK,
                    result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        private void ShowFileDiagnostics()
        {
            string message;
            try
            {
                if (sessions.SelectedRemote != null) message = Strings.T("files_local_only");
                else
                {
                    string sid = GetConnectedChildSid();
                    var settings = ((IMsRdpClient9)rdp.GetOcxObject()).AdvancedSettings9;
                    message = Strings.T("files_report",
                        FileTransferDiagnostic(true, sid), FileTransferDiagnostic(false, sid),
                        SettingLabel(settings.RedirectClipboard), SettingLabel(WantsLocalRdpClipboard));
                }
            }
            catch (Exception ex) { message = ex.GetBaseException().Message; }
            MessageBox.Show(this, message, Strings.T("files_status"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string SettingLabel(bool enabled) { return Strings.T(enabled ? "policy_enabled" : "policy_disabled"); }

        private string FileTransferDiagnostic(bool outbound, string sid)
        {
            return Strings.T("files_direction_status", SettingLabel(preferences.AllowsFileTransfer(outbound)),
                Strings.T(ClipboardRestrictions.AllowsFiles(outbound, sid) ? "policy_allowed" : "policy_blocked"));
        }
    }
}
