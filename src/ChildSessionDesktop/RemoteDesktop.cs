using System;
using System.Drawing;
using System.Windows.Forms;
using MSTSCLib;

namespace ChildSessionDesktop
{
    internal sealed class RemoteDesktop : UserControl
    {
        private readonly ChildSessionControl view;
        private readonly Timer timer;
        private readonly bool fluid;
        private bool reconnect, explicitDisconnect, attempted;
        private DateTime deadline;
        private string pendingPassword;
        private IMsTscAxEvents_Event events;

        internal RdpEndpoint Endpoint { get; private set; }
        internal ChildSessionControl PreviewSurface { get { return view; } }
        internal string Account { get; private set; }
        internal string Status { get; private set; }
        internal event Action StatusChanged;

        internal RemoteDesktop(RdpEndpoint endpoint, string account, bool fluid)
        {
            Endpoint = endpoint;
            Account = account;
            this.fluid = fluid;
            Status = Strings.T("status_not_connected");
            Dock = DockStyle.Fill;
            view = new ChildSessionControl { Dock = DockStyle.Fill, BackColor = Color.Black };
            Controls.Add(view);
            timer = new Timer { Interval = 160 };
            timer.Tick += delegate { WaitForDisconnect(); };
        }

        private void SetStatus(string message)
        {
            Status = message;
            if (StatusChanged != null) StatusChanged();
        }

        internal void Connect(string password = null)
        {
            if (IsDisposed || timer.Enabled) return;
            view.CreateControl();
            try
            {
                object ocx = view.GetOcxObject();
                var client = (IMsRdpClient9)ocx;
                if (client.Connected != 0) return;
                if (events == null)
                {
                    events = (IMsTscAxEvents_Event)ocx;
                    events.OnConnected += OnConnected;
                    events.OnDisconnected += OnDisconnected;
                    events.OnFatalError += OnFatalError;
                }
                client.Server = Endpoint.Host;
                int separator = Account.IndexOf('\\');
                client.Domain = separator < 0 ? "" : Account.Substring(0, separator);
                client.UserName = separator < 0 ? Account : Account.Substring(separator + 1);
                client.DesktopWidth = Math.Max(640, Math.Min(fluid ? 1600 : 2560, Width)) & ~1;
                client.DesktopHeight = Math.Max(480, Math.Min(fluid ? 900 : 1440, Height));
                var settings = client.AdvancedSettings9;
                settings.EnableCredSspSupport = true;
                settings.AuthenticationLevel = 1;
                settings.RDPPort = Endpoint.Port;
                settings.SmartSizing = true;
                if (fluid) settings.PerformanceFlags = GraphicsOptions.FluidPerformanceFlags;
                settings.RedirectClipboard = false;
                settings.RedirectDrives = false;
                var credentials = (IMsTscNonScriptable)ocx;
                credentials.ResetPassword();
                if (password != null) credentials.ClearTextPassword = password;
                ((IMsRdpClientNonScriptable3)ocx).PromptForCredentials = password == null;
                explicitDisconnect = false;
                attempted = true;
                SetStatus(Strings.T("sessions_connecting"));
                ((IMsTscAx)ocx).Connect();
            }
            catch (Exception ex) { ShowFailure(ex); }
        }

        internal void Disconnect(bool resume, string password = null)
        {
            if (timer.Enabled || IsDisposed) return;
            try
            {
                if (!attempted || ((IMsRdpClient9)view.GetOcxObject()).Connected == 0)
                {
                    if (resume) Connect(password);
                    else SetStatus(Strings.T("status_not_connected"));
                    return;
                }
                explicitDisconnect = true;
                reconnect = resume;
                pendingPassword = resume ? password : null;
                deadline = DateTime.UtcNow.AddSeconds(10);
                timer.Start();
                SetStatus(Strings.T("sessions_disconnect_wait"));
                ((IMsTscAx)view.GetOcxObject()).Disconnect();
            }
            catch (Exception ex) { ShowFailure(ex); }
        }

        private void WaitForDisconnect()
        {
            try
            {
                if (((IMsRdpClient9)view.GetOcxObject()).Connected == 0)
                {
                    timer.Stop();
                    string password = pendingPassword;
                    pendingPassword = null;
                    SetStatus(Strings.T("status_not_connected"));
                    if (reconnect) Connect(password);
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    timer.Stop();
                    pendingPassword = null;
                    SetStatus(Strings.T("sessions_disconnect_timeout"));
                }
            }
            catch (Exception ex) { ShowFailure(ex); }
        }

        private void OnConnected() { SetStatus(Strings.T("status_connected")); }

        private void OnDisconnected(int reason)
        {
            if (timer.Enabled || IsDisposed) return;
            SetStatus(explicitDisconnect ? Strings.T("status_not_connected") : Strings.T("status_disconnected", reason));
        }

        private void OnFatalError(int code) { SetStatus(Strings.T("status_fatal", code)); }

        private void ShowFailure(Exception ex)
        {
            timer.Stop();
            pendingPassword = null;
            SetStatus(Strings.T("msg_connect_fail") + ex.GetBaseException().Message);
            Program.Log("Remote RDP operation failed: " + ex.GetBaseException().Message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                pendingPassword = null;
                if (events != null)
                {
                    events.OnConnected -= OnConnected;
                    events.OnDisconnected -= OnDisconnected;
                    events.OnFatalError -= OnFatalError;
                    events = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
