using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSTSCLib;

namespace ChildSessionDesktop
{
    internal sealed partial class DesktopForm
    {
        private readonly SessionWorkspace sessions;
        private Timer disconnectTimer;
        private bool explicitlyDisconnected, fluidGraphics, localFluidGraphics, hasDefaultPerformanceFlags;
        private int defaultPerformanceFlags;
        private bool addingRemote;

        private void RefreshLocalViewport()
        {
            if (closing) return;
            if (dynamicMode)
            {
                SetDynamicLayout();
                if (rdp.Visible) QueueDisplayResize(true);
            }
            else ApplyLetterbox();
        }

        private void AddSessionMenuItems(ContextMenuStrip menu)
        {
            var toggle = menu.Items.Add(Strings.T(sessions.IsExpanded ? "sessions_hide" : "sessions_show"));
            toggle.Click += delegate { sessions.ToggleSidebar(); };
            var add = menu.Items.Add(Strings.T("sessions_add"));
            add.Click += delegate { AddRemote(); };
            var startupAll = new ToolStripMenuItem(Strings.T("remote_reconnect_all_at_startup"))
            {
                Checked = preferences.ReconnectAllAtStartup, CheckOnClick = true
            };
            startupAll.Click += delegate { SavePreferences(preferences.SetReconnectAllAtStartup(startupAll.Checked)); };
            menu.Items.Add(startupAll);
            menu.Items.Add(Strings.T("sessions_thumbnail_folder")).Click += delegate { OpenThumbnailFolder(); };
            var disconnect = menu.Items.Add(Strings.T("sessions_disconnect"));
            disconnect.Click += delegate
            {
                if (sessions.SelectedRemote == null) DisconnectLocal(false);
                else sessions.SelectedRemote.Disconnect(false);
            };
            var closeSession = menu.Items.Add(Strings.T(sessions.SelectedRemote == null ? "sessions_logoff" : "sessions_close_view"));
            closeSession.Click += delegate
            {
                if (sessions.SelectedRemote == null) LogOffLocal();
                else
                {
                    RemoteDesktop close = sessions.SelectedRemote;
                    SavePreferences(preferences.RemoveSession(close.Endpoint, close.Account));
                    sessions.CloseSelectedView();
                }
            };
            var fluid = new ToolStripMenuItem(Strings.T("graphics_fluid")) { Checked = fluidGraphics };
            fluid.Click += delegate
            {
                fluidGraphics = !fluidGraphics;
                SavePreferences(preferences.SetFluidGraphics(fluidGraphics));
                MessageBox.Show(this, Strings.T("graphics_applied_on_connect"), Strings.T("graphics_title"));
            };
            menu.Items.Add(fluid);
            menu.Items.Add(Strings.T("graphics_title")).Click += delegate
            {
                GraphicsSettingsDialog.Show(this, fluidGraphics, currentSessW, currentSessH);
            };
            RemoteDesktop remote = sessions.SelectedRemote;
            if (remote != null)
            {
                var clipboard = new ToolStripMenuItem(Strings.T("remote_share_clipboard"))
                {
                    Checked = remote.ShareClipboard,
                    CheckOnClick = true
                };
                clipboard.Click += delegate
                {
                    remote.ShareClipboard = clipboard.Checked;
                    SavePreferences(preferences.SetSession(remote.Endpoint, remote.Account, remote.ShareClipboard));
                };
                menu.Items.Add(clipboard);
                var certificateWarning = new ToolStripMenuItem(Strings.T("remote_ask_on_certificate_error"))
                {
                    Checked = remote.AskOnCertificateError,
                    CheckOnClick = true,
                    ToolTipText = Strings.T("remote_ask_on_certificate_error_hint")
                };
                certificateWarning.Click += delegate { remote.SetCertificateWarningPreference(certificateWarning.Checked); };
                menu.Items.Add(certificateWarning);
                var startup = new ToolStripMenuItem(Strings.T("remote_reconnect_at_startup"))
                {
                    Checked = preferences.ReconnectAllAtStartup || preferences.ReconnectsAtStartup(remote.Endpoint, remote.Account),
                    Enabled = !preferences.ReconnectAllAtStartup, CheckOnClick = true
                };
                startup.Click += delegate { SavePreferences(preferences.SetStartupSession(remote.Endpoint, remote.Account, startup.Checked)); };
                menu.Items.Add(startup);
                var editPassword = menu.Items.Add(Strings.T("remote_edit_password"));
                editPassword.Enabled = remote.CanEditSavedPassword && !remote.AskOnCertificateError;
                editPassword.Click += delegate
                {
                    string secret = PasswordDialog.Prompt(this, remote.Endpoint.Display + " · " + remote.Account);
                    if (!string.IsNullOrEmpty(secret) && !closing && !remote.IsDisposed)
                        remote.Disconnect(true, secret, rememberPassword: true);
                };
                var forgetPassword = menu.Items.Add(Strings.T("remote_forget_password"));
                forgetPassword.Enabled = remote.CanEditSavedPassword && preferences.HasSessionPassword(remote.Endpoint, remote.Account);
                forgetPassword.Click += delegate { SavePreferences(preferences.SetSessionPassword(remote.Endpoint, remote.Account, null)); };
                menu.Items.Add(Strings.T("remote_refresh_display")).Click += delegate { remote.RefreshDisplay(); };
                menu.Items.Add(Strings.T("remote_open_windows")).Click += delegate { OpenSelectedInWindowsRdp(); };
                menu.Items.Add(Strings.T("remote_copy_diagnostic")).Click += delegate
                {
                    try
                    {
                        Clipboard.SetText("RayXDesktop " + Application.ProductVersion + Environment.NewLine + remote.DiagnosticText);
                    }
                    catch (ExternalException ex)
                    {
                        MessageBox.Show(this, ex.Message, Strings.T("remote_copy_diagnostic"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
            }
        }

        private void OpenThumbnailFolder()
        {
            try
            {
                Directory.CreateDirectory(sessions.ThumbnailFolder);
                Process.Start("explorer.exe", "\"" + sessions.ThumbnailFolder + "\"");
            }
            catch (Exception ex)
            {
                Program.Log("Could not open the session previews folder: " + ex.GetType().Name);
                MessageBox.Show(this, ex.GetBaseException().Message, Strings.T("sessions_thumbnail_folder"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ReconnectSelected()
        {
            if (sessions.SelectedRemote == null) DisconnectLocal(true);
            else sessions.SelectedRemote.Disconnect(true);
        }

        private void DisconnectLocal(bool resume)
        {
            if (closing || promptingForPassword || disconnectTimer != null || rdp.IsDisposed) return;
            CancelFileCopy();
            StopReconnect();
            reconnectPolicy.OnManualReconnect();
            statusTimer.Stop();
            explicitlyDisconnected = true;
            connecting = false;
            try
            {
                object ocx = rdp.GetOcxObject();
                var client = (IMsRdpClient9)ocx;
                if (client.Connected == 0)
                {
                    if (resume) ConnectChildSession();
                    else SetStatus(Strings.T("status_not_connected"));
                    return;
                }

                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                disconnectTimer = new Timer { Interval = 150 };
                disconnectTimer.Tick += delegate
                {
                    try
                    {
                        if (client.Connected == 0)
                        {
                            DisposeTimer(ref disconnectTimer);
                            SetStatus(Strings.T("status_not_connected"));
                            if (resume) ConnectChildSession();
                        }
                        else if (DateTime.UtcNow >= deadline)
                        {
                            DisposeTimer(ref disconnectTimer);
                            SetStatus(Strings.T("sessions_disconnect_timeout"));
                        }
                    }
                    catch (Exception ex) { ReportDisconnectFailure(ex); }
                };
                SetStatus(Strings.T("sessions_disconnect_wait"));
                Program.Log(resume ? "Manual reconnect: disconnecting the RDP connection" : "Manual RDP disconnect");
                disconnectTimer.Start();
                ((IMsTscAx)ocx).Disconnect();
            }
            catch (Exception ex) { ReportDisconnectFailure(ex); }
        }

        private void ReportDisconnectFailure(Exception ex)
        {
            DisposeTimer(ref disconnectTimer);
            Program.Log("Local disconnect request failed: " + ex.GetBaseException().Message);
            SetStatus(Strings.T("sessions_disconnect_failed"));
        }

        private void LogOffLocal()
        {
            if (disconnectTimer != null || promptingForPassword || closing) return;
            try
            {
                uint id;
                if (!Program.WTSGetChildSessionId(out id) || id == uint.MaxValue
                    || id == (uint)Process.GetCurrentProcess().SessionId)
                {
                    MessageBox.Show(this, Strings.T("sessions_no_local"), Strings.T("sessions_logoff"));
                    return;
                }
                string account = ReadSessionAccountField(id, 5);
                string domain = ReadSessionAccountField(id, 7);
                if (string.IsNullOrWhiteSpace(account)) throw new InvalidOperationException(Strings.T("sessions_no_local"));
                string display = string.IsNullOrEmpty(domain) ? account : domain + "\\" + account;
                if (MessageBox.Show(this, Strings.T("sessions_confirm_logoff", display), Strings.T("sessions_logoff"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                uint confirmedId;
                if (!Program.WTSGetChildSessionId(out confirmedId) || confirmedId != id
                    || !string.Equals(account, ReadSessionAccountField(id, 5), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(domain, ReadSessionAccountField(id, 7), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Strings.T("sessions_no_local"));
                CancelFileCopy();
                explicitlyDisconnected = true;
                StopReconnect();
                if (!Program.WTSLogoffSession(IntPtr.Zero, id, false))
                {
                    explicitlyDisconnected = false;
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                Program.Log("Requested local child-session logoff for session " + id);
                SetStatus(Strings.T("sessions_logoff_requested"));
            }
            catch (Exception ex)
            {
                Program.Log("Child session logoff failed: " + ex.GetBaseException().Message);
                MessageBox.Show(this, ex.GetBaseException().Message, Strings.T("sessions_logoff"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private List<RemoteDesktop> RestoreSavedSessions()
        {
            var startup = new List<RemoteDesktop>();
            foreach (SavedSession saved in preferences.SavedSessions)
            {
                var remote = new RemoteDesktop(saved.Endpoint, saved.Account, fluidGraphics, preferences);
                remote.ShareClipboard = saved.ShareClipboard;
                sessions.AddRemote(remote, false);
                if (preferences.ReconnectAllAtStartup || saved.ReconnectAtStartup) startup.Add(remote);
            }
            if (sessions.HasRemoteConnections && !sessions.IsExpanded) sessions.ToggleSidebar();
            return startup;
        }

        private void AddRemote()
        {
            if (closing || addingRemote) return;
            addingRemote = true;
            try
            {
                RdpEndpoint endpoint;
                RdpImport imported;
                string account, password;
                bool rememberPassword;
                if (!RemoteConnectionDialog.Prompt(this, preferences, out endpoint, out account, out password, out imported, out rememberPassword)) return;
                if (password == null && (imported == null || !imported.HasProtectedPassword) && sessions.SelectExisting(endpoint, account))
                {
                    sessions.SelectedRemote.Connect();
                    return;
                }
                if (preferences.UsesCertificateWarning(endpoint)) password = null;
                else if (password == null && imported != null && imported.HasProtectedPassword)
                {
                    password = imported.TakePassword();
                    if (password == null)
                        MessageBox.Show(this, Strings.T("rdp_protected_unavailable"), Strings.T("rdp_import"),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                if (sessions.SelectExisting(endpoint, account))
                {
                    if (password != null) sessions.SelectedRemote.Disconnect(true, password, rememberPassword);
                    else sessions.SelectedRemote.Connect();
                    password = null;
                    return;
                }
                var remote = new RemoteDesktop(endpoint, account, fluidGraphics, preferences);
                SavePreferences(preferences.SetSession(endpoint, account, remote.ShareClipboard));
                sessions.AddRemote(remote);
                try { remote.Connect(password, rememberPassword: rememberPassword); }
                finally { password = null; }
            }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                    MessageBox.Show(this, ex.GetBaseException().Message, Strings.T("sessions_add"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { addingRemote = false; }
        }

        private void RefreshSessionTitle()
        {
            string title = sessions.SelectedRemote == null
                ? sessions.SelectedStatus
                : sessions.SelectedName + " · " + sessions.SelectedStatus;
            title += " — RayXDesktop";
            if (Text != title) Text = title;
        }

        private async void OpenSelectedInWindowsRdp()
        {
            RemoteDesktop remote = sessions.SelectedRemote;
            if (remote == null) return;
            RdpEndpoint endpoint = remote.Endpoint;
            string address = endpoint.Port == RdpEndpoint.DefaultPort && endpoint.Host.IndexOf(':') >= 0
                ? "[" + endpoint.Host + "]" : endpoint.Display;
            try
            {
                if (!await endpoint.ResolvesRemotelyAsync())
                    throw new InvalidOperationException(Strings.T("sessions_remote_only"));
                if (closing || IsDisposed || remote.IsDisposed) return;
                string profile = MaintenanceProfile.CreateDirectConnectionFile(address, remote.Account, remote.AskOnCertificateError);
                await Task.Run(() => MaintenanceClient.Run(profile));
            }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                    MessageBox.Show(this, ex.Message, Strings.T("remote_open_windows"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
