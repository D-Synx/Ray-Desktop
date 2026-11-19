using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
                else sessions.CloseSelectedView();
            };
            var fluid = new ToolStripMenuItem(Strings.T("graphics_fluid")) { Checked = fluidGraphics };
            fluid.Click += delegate
            {
                fluidGraphics = !fluidGraphics;
                MessageBox.Show(this, Strings.T("graphics_applied_on_connect"), Strings.T("graphics_title"));
            };
            menu.Items.Add(fluid);
            menu.Items.Add(Strings.T("graphics_title")).Click += delegate { ShowGraphicsDiagnostics(); };
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

        private async void AddRemote()
        {
            if (closing || addingRemote) return;
            addingRemote = true;
            try
            {
                string host, account;
                if (!RemoteConnectionDialog.Prompt(this, out host, out account)) return;
                if (sessions.SelectExisting(host, account)) return;
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);
                if (closing || IsDisposed) return;
                if (addresses.Length == 0 || ResolvesLocally(addresses))
                    throw new InvalidOperationException(Strings.T("sessions_remote_only"));
                var remote = new RemoteDesktop(host, account, fluidGraphics);
                sessions.AddRemote(remote);
                remote.Connect();
            }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                    MessageBox.Show(this, ex.GetBaseException().Message, Strings.T("sessions_add"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { addingRemote = false; }
        }

        private static bool ResolvesLocally(IPAddress[] addresses)
        {
            foreach (IPAddress address in addresses)
            {
                IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
                if (IPAddress.IsLoopback(normalized) || normalized.Equals(IPAddress.Any) || normalized.Equals(IPAddress.IPv6Any)) return true;
                foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (UnicastIPAddressInformation local in network.GetIPProperties().UnicastAddresses)
                    {
                        if (local.Address.Equals(normalized) || (local.Address.IsIPv4MappedToIPv6 && local.Address.MapToIPv4().Equals(normalized)))
                            return true;
                    }
                }
            }
            return false;
        }

        private void RefreshSessionTitle()
        {
            string title = sessions.SelectedRemote == null
                ? sessions.SelectedStatus
                : sessions.SelectedName + " · " + sessions.SelectedStatus;
            title += " — RayXDesktop";
            if (Text != title) Text = title;
        }

        private void ShowGraphicsDiagnostics()
        {
            string message = Strings.T("graphics_report",
                Strings.T(fluidGraphics ? "graphics_fluid" : "graphics_default"),
                GraphicsOptions.DescribePolicy("bEnumerateHWBeforeSW"),
                GraphicsOptions.DescribePolicy("AVCHardwareEncodePreferred"),
                currentSessW, currentSessH);
            if (MessageBox.Show(this, message, Strings.T("graphics_title"), MessageBoxButtons.YesNo,
                MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { Process.Start("eventvwr.msc"); }
            catch (Exception ex) { Program.Log("Could not open Event Viewer: " + ex.GetBaseException().Message); }
        }
    }
}
