using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MSTSCLib;

namespace ChildSessionDesktop
{
    internal sealed class RemoteDesktop : UserControl
    {
        private enum Phase { Idle, Connecting, SigningIn, Ready, Stopping, Failed }

        private readonly ChildSessionControl view;
        private readonly Panel viewport;
        private readonly Label statusLine, messagePane;
        private readonly Timer timer, signInTimer, displayTimer;
        private readonly bool fluid;
        private bool reconnect, explicitDisconnect, connectQueued;
        private DateTime deadline;
        private string pendingPassword;
        private IMsTscAxEvents_Event events;
        private Phase phase;
        private Size lastDisplaySize;
        private int? logonError, fatalError;
        private bool loginCompleted;
        private string lastFailure = "";
        internal string DiagnosticText { get { return lastFailure.Length == 0 ? Status : lastFailure; } }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);

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
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(13, 19, 29);
            viewport = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            view = new ChildSessionControl { Dock = DockStyle.Fill, BackColor = Color.Black };
            messagePane = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(22), ForeColor = Color.Gainsboro, BackColor = BackColor
            };
            statusLine = new Label
            {
                Dock = DockStyle.Top, Height = 30, Padding = new Padding(10, 4, 10, 0),
                AutoEllipsis = true, ForeColor = Color.White, BackColor = Color.FromArgb(23, 34, 50)
            };
            viewport.Controls.Add(view);
            viewport.Controls.Add(messagePane);
            messagePane.BringToFront();
            Controls.Add(viewport);
            Controls.Add(statusLine);

            timer = new Timer { Interval = 160 };
            timer.Tick += delegate { WaitForDisconnect(); };
            signInTimer = new Timer { Interval = 45000 };
            signInTimer.Tick += delegate { ReportSlowLogin(); };
            displayTimer = new Timer { Interval = 220 };
            displayTimer.Tick += delegate { displayTimer.Stop(); UpdateDisplay(false); };
            viewport.SizeChanged += delegate { QueueDisplayUpdate(); };
            VisibleChanged += delegate { QueueDisplayUpdate(); };
            SetStatus(Phase.Idle, Strings.T("status_not_connected"));
        }

        private void SetStatus(Phase next, string message)
        {
            phase = next;
            Status = message;
            statusLine.Text = Endpoint.Display + "   ·   " + message;
            messagePane.Text = message;
            messagePane.Visible = next == Phase.Idle || next == Phase.Failed;
            if (messagePane.Visible) messagePane.BringToFront();
            if (StatusChanged != null) StatusChanged();
        }

        internal void Connect(string password = null)
        {
            if (IsDisposed || timer.Enabled || connectQueued) return;
            connectQueued = true;
            try
            {
                // The split/single view layout must finish before the ActiveX control is activated.
                BeginInvoke((MethodInvoker)delegate
                {
                    connectQueued = false;
                    if (!IsDisposed) StartConnection(password);
                });
            }
            catch (InvalidOperationException ex)
            {
                connectQueued = false;
                ShowFailure(ex);
            }
        }

        private void StartConnection(string password)
        {
            if (timer.Enabled) return;
            try
            {
                if (!Visible || viewport.ClientSize.Width == 0 || viewport.ClientSize.Height == 0)
                {
                    SetStatus(Phase.Idle, Strings.T("remote_select_to_connect"));
                    return;
                }

                view.CreateControl();
                object ocx = view.GetOcxObject();
                var client = (IMsRdpClient9)ocx;
                if (client.Connected != 0) return;
                AttachEvents(ocx);
                logonError = null;
                fatalError = null;
                loginCompleted = false;
                lastFailure = "";
                SetStatus(Phase.Connecting, Strings.T("sessions_connecting"));
                client.Server = Endpoint.Host;
                int separator = Account.IndexOf('\\');
                client.Domain = separator < 0 ? "" : Account.Substring(0, separator);
                client.UserName = separator < 0 ? Account : Account.Substring(separator + 1);
                Size size = PreferredDisplaySize();
                client.DesktopWidth = size.Width;
                client.DesktopHeight = size.Height;
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
                lastDisplaySize = Size.Empty;
                signInTimer.Start();
                Program.Log("Remote RDP " + Endpoint.Display + " starting at " + size.Width + "x" + size.Height);
                view.Select();
                ((IMsTscAx)ocx).Connect();
            }
            catch (Exception ex) { ShowFailure(ex); }
        }

        private void AttachEvents(object ocx)
        {
            if (events != null) return;
            events = (IMsTscAxEvents_Event)ocx;
            events.OnConnected += OnConnected;
            events.OnLoginComplete += OnLoginComplete;
            events.OnDisconnected += OnDisconnected;
            events.OnFatalError += OnFatalError;
            events.OnLogonError += OnLogonError;
            events.OnRemoteDesktopSizeChange += OnRemoteDesktopSizeChange;
        }

        internal void Disconnect(bool resume, string password = null)
        {
            if (timer.Enabled || connectQueued || IsDisposed) return;
            try
            {
                signInTimer.Stop();
                if (events == null || ((IMsRdpClient9)view.GetOcxObject()).Connected == 0)
                {
                    if (resume) Connect(password);
                    else SetStatus(Phase.Idle, Strings.T("status_not_connected"));
                    return;
                }
                explicitDisconnect = true;
                reconnect = resume;
                pendingPassword = resume ? password : null;
                deadline = DateTime.UtcNow.AddSeconds(10);
                timer.Start();
                SetStatus(Phase.Stopping, Strings.T("sessions_disconnect_wait"));
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
                    SetStatus(Phase.Idle, Strings.T("status_not_connected"));
                    if (reconnect) Connect(password);
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    timer.Stop();
                    pendingPassword = null;
                    SetStatus(Phase.Failed, Strings.T("sessions_disconnect_timeout"));
                }
            }
            catch (Exception ex) { ShowFailure(ex); }
        }

        private void OnConnected()
        {
            Program.Log("Remote RDP " + Endpoint.Display + " OnConnected (sign-in pending)");
            if (phase == Phase.Connecting) SetStatus(Phase.SigningIn, Strings.T("remote_signing_in"));
        }

        private void OnLoginComplete()
        {
            if (phase != Phase.Connecting && phase != Phase.SigningIn) return;
            signInTimer.Stop();
            loginCompleted = true;
            logonError = null;
            Program.Log("Remote RDP " + Endpoint.Display + " OnLoginComplete");
            SetStatus(Phase.Ready, Strings.T("remote_login_complete"));
            QueueDisplayUpdate();
        }

        private void OnRemoteDesktopSizeChange(int width, int height)
        {
            Program.Log("Remote RDP " + Endpoint.Display + " display " + width + "x" + height);
            QueueDisplayUpdate();
        }

        private void ReportSlowLogin()
        {
            signInTimer.Stop();
            Program.Log("Remote RDP " + Endpoint.Display + " sign-in not confirmed after 45 seconds (" + phase + ")");
            if (phase == Phase.Connecting || phase == Phase.SigningIn)
                SetStatus(phase, Strings.T("remote_waiting_long"));
        }

        private void OnDisconnected(int reason)
        {
            if (timer.Enabled || IsDisposed) return;
            signInTimer.Stop();
            displayTimer.Stop();
            if (explicitDisconnect)
            {
                SetStatus(Phase.Idle, Strings.T("status_not_connected"));
                return;
            }
            var message = Strings.T("status_disconnected", reason);
            uint? extended = null;
            try
            {
                var client = (IMsRdpClient9)view.GetOcxObject();
                extended = (uint)client.ExtendedDisconnectReason;
                string description = client.GetErrorDescription(unchecked((uint)reason), extended.Value);
                if (!string.IsNullOrWhiteSpace(description)) message += Environment.NewLine + description;
            }
            catch (Exception ex)
            {
                Program.Log("Remote RDP could not get the Windows disconnection details (0x" + ex.HResult.ToString("X8") + ")");
            }

            string fields = extended.HasValue ? "0x" + extended.Value.ToString("X8") : Strings.T("remote_code_unavailable");
            string phaseText = Strings.T(loginCompleted ? "remote_stage_signed_in" : "remote_stage_before_login");
            message += Environment.NewLine + Strings.T("remote_disconnect_fields", fields, phaseText);
            if (logonError.HasValue)
            {
                string key = RdpFailure.LoginMessageKey(logonError.Value);
                if (key != null) message += Environment.NewLine + Strings.T(key);
                message += Environment.NewLine + Strings.T("remote_logon_code", logonError.Value);
            }

            string hint = RdpFailure.DisconnectHint(reason, extended, loginCompleted, Endpoint.Address != null);
            if (hint.Length != 0) message += Environment.NewLine + Environment.NewLine + hint;
            if (fatalError.HasValue) message += Environment.NewLine + Strings.T("status_fatal", fatalError.Value);
            Fail(message);
        }

        private void OnFatalError(int code)
        {
            fatalError = code;
            Fail(Strings.T("status_fatal", code));
        }

        private void OnLogonError(int code)
        {
            Program.Log("Remote RDP " + Endpoint.Display + " logon event " + code);
            string key = RdpFailure.LoginMessageKey(code);
            if (key != null && (phase == Phase.Connecting || phase == Phase.SigningIn))
            {
                logonError = code;
                SetStatus(Phase.SigningIn, Strings.T(key));
            }
        }

        private Size PreferredDisplaySize()
        {
            return Geometry.RemoteDisplaySize(viewport.ClientSize.Width, viewport.ClientSize.Height, fluid);
        }

        private void QueueDisplayUpdate()
        {
            if (displayTimer == null || phase != Phase.Ready || !Visible || IsDisposed) return;
            displayTimer.Stop();
            displayTimer.Interval = lastDisplaySize.IsEmpty ? 900 : 220;
            displayTimer.Start();
        }

        internal void RefreshDisplay() { UpdateDisplay(true); }

        private void UpdateDisplay(bool force)
        {
            if (IsDisposed || !view.IsHandleCreated || !Visible) return;
            try
            {
                var client = (IMsRdpClient9)view.GetOcxObject();
                if (client.Connected != 1) return;
                viewport.PerformLayout();
                Size size = PreferredDisplaySize();
                if (phase == Phase.Ready && (force || lastDisplaySize != size))
                {
                    lastDisplaySize = size;
                    using (Graphics graphics = view.CreateGraphics())
                    {
                        client.UpdateSessionDisplaySettings((uint)size.Width, (uint)size.Height,
                            Geometry.ToMillimeters(size.Width, graphics.DpiX), Geometry.ToMillimeters(size.Height, graphics.DpiY),
                            0, 100, 100);
                    }
                }
                else if (force)
                {
                    client.SyncSessionDisplaySettings();
                }
            }
            catch (Exception ex)
            {
                Program.Log("Remote RDP " + Endpoint.Display + " display synchronization: 0x" + ex.HResult.ToString("X8"));
            }
            finally
            {
                const uint Invalidate = 0x1, AllChildren = 0x80, UpdateNow = 0x100;
                if (!IsDisposed && view.IsHandleCreated)
                    RedrawWindow(view.Handle, IntPtr.Zero, IntPtr.Zero, Invalidate | AllChildren | UpdateNow);
            }
        }

        private void Fail(string message)
        {
            if (timer.Enabled || IsDisposed) return;
            signInTimer.Stop();
            displayTimer.Stop();
            lastFailure = message;
            Program.Log("Remote RDP " + Endpoint.Display + " " + message.Replace(Environment.NewLine, " "));
            SetStatus(Phase.Failed, message);
        }

        private void ShowFailure(Exception ex)
        {
            timer.Stop();
            pendingPassword = null;
            Fail(Strings.T("remote_setup_error", ex.HResult));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (events != null)
                {
                    events.OnConnected -= OnConnected;
                    events.OnLoginComplete -= OnLoginComplete;
                    events.OnDisconnected -= OnDisconnected;
                    events.OnFatalError -= OnFatalError;
                    events.OnLogonError -= OnLogonError;
                    events.OnRemoteDesktopSizeChange -= OnRemoteDesktopSizeChange;
                    events = null;
                }
                timer.Dispose();
                signInTimer.Dispose();
                displayTimer.Dispose();
                pendingPassword = null;
            }
            base.Dispose(disposing);
        }
    }
}
