using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSTSCLib;

namespace ChildSessionDesktop
{
    internal sealed class RemoteDesktop : UserControl
    {
        private enum Phase { Idle, Connecting, SigningIn, Ready, Stopping, Failed }

        private readonly ChildSessionControl view;
        private readonly Panel viewport;
        private readonly TableLayoutPanel messageOverlay;
        private readonly Label statusLine, messagePane;
        private readonly Button retryCertificateButton;
        private readonly Timer timer, signInTimer, displayTimer;
        private RdpPerformanceProfile activePerformance;
        private int? defaultPerformanceFlags;
        private bool? priorBandwidthDetection;
        private readonly DesktopPreferences preferences;
        private bool reconnect, explicitDisconnect, connectQueued;
        private int connectionRequest;
        private DateTime deadline;
        private string pendingPassword;
        private bool pendingRememberPassword, usedSavedPassword;
        private bool savedPasswordRejected;
        private string passwordToSave;
        private IMsTscAxEvents_Event events;
        private Phase phase;
        private Size lastDisplaySize;
        private int? logonError, fatalError;
        private bool loginCompleted, certificateWarningRequested, rememberCertificateWarning;
        private bool? certificateWarningOverride;
        private TaskCompletionSource<bool> startupAttempt;
        private string lastFailure = "";
        internal string DiagnosticText { get { return lastFailure.Length == 0 ? Status : lastFailure; } }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);

        internal RdpEndpoint Endpoint { get; private set; }
        internal ChildSessionControl PreviewSurface { get { return view; } }
        internal string Account { get; private set; }
        internal bool ShareClipboard { get; set; }
        internal bool AskOnCertificateError { get { return certificateWarningOverride ?? preferences.UsesCertificateWarning(Endpoint); } }
        internal string Status { get; private set; }
        internal bool CanConnectFromList { get { return !connectQueued && (phase == Phase.Idle || phase == Phase.Failed); } }
        internal bool IsReady { get { return phase == Phase.Ready; } }
        internal Size InitialResolution { get; private set; }
        internal Size? ResolutionOverride { get; private set; }
        internal bool CanEditSavedPassword
        {
            get { return startupAttempt == null && !connectQueued && phase != Phase.Connecting && phase != Phase.SigningIn && phase != Phase.Stopping; }
        }
        internal event Action StatusChanged;
        internal event Action DesktopOpened;

        internal RemoteDesktop(RdpEndpoint endpoint, string account, DesktopPreferences preferences)
        {
            Endpoint = endpoint;
            Account = account;
            this.preferences = preferences;
            ShareClipboard = true;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(13, 19, 29);
            viewport = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            view = new ChildSessionControl { Dock = DockStyle.Fill, BackColor = Color.Black };
            messagePane = new Label
            {
                AutoSize = true, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(16, 8, 16, 8), ForeColor = Color.Gainsboro
            };
            retryCertificateButton = new Button
            {
                Text = Strings.T("remote_retry_certificate"), AutoSize = true, Anchor = AnchorStyles.None,
                Padding = new Padding(10, 4, 10, 4), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(23, 34, 50), ForeColor = Color.White,
                Visible = false
            };
            retryCertificateButton.FlatAppearance.BorderColor = Color.FromArgb(65, 86, 107);
            retryCertificateButton.Click += delegate { Connect(null, askOnCertificateErrorForAttempt: true); };
            messageOverlay = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            messageOverlay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            messageOverlay.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            messageOverlay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            messageOverlay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            messageOverlay.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            messageOverlay.Controls.Add(messagePane, 0, 1);
            messageOverlay.Controls.Add(retryCertificateButton, 0, 2);
            statusLine = new Label
            {
                Dock = DockStyle.Top, Height = 30, Padding = new Padding(10, 4, 10, 0),
                AutoEllipsis = true, ForeColor = Color.White, BackColor = Color.FromArgb(23, 34, 50)
            };
            viewport.Controls.Add(view);
            viewport.Controls.Add(messageOverlay);
            messageOverlay.BringToFront();
            Controls.Add(viewport);
            Controls.Add(statusLine);

            timer = new Timer { Interval = 160 };
            timer.Tick += delegate { WaitForDisconnect(); };
            signInTimer = new Timer { Interval = 45000 };
            signInTimer.Tick += delegate { ReportSlowLogin(); };
            displayTimer = new Timer { Interval = 220 };
            displayTimer.Tick += delegate { displayTimer.Stop(); UpdateDisplay(false); };
            viewport.SizeChanged += delegate
            {
                messagePane.MaximumSize = new Size(Math.Max(100, Math.Min(760, viewport.ClientSize.Width - 32)), 0);
                QueueDisplayUpdate();
            };
            VisibleChanged += delegate { QueueDisplayUpdate(); };
            SetStatus(Phase.Idle, Strings.T("status_not_connected"));
        }

        internal void SetCertificateWarningPreference(bool enabled)
        {
            certificateWarningOverride = enabled ? (bool?)true : null;
            if (enabled) return;
            rememberCertificateWarning = false;
            if (!preferences.SetCertificateWarning(Endpoint, false))
            {
                certificateWarningOverride = false;
                Program.Log("Could not forget the RDP certificate warning preference for " + Endpoint.Display);
            }
        }

        private void SetStatus(Phase next, string message, bool offerCertificateRetry = false)
        {
            phase = next;
            Status = message;
            statusLine.Text = Endpoint.Display + "   ·   " + message;
            messagePane.Text = message;
            retryCertificateButton.Visible = offerCertificateRetry;
            messageOverlay.Visible = next == Phase.Idle || next == Phase.Failed;
            if (messageOverlay.Visible) messageOverlay.BringToFront();
            if (StatusChanged != null) StatusChanged();
            if (startupAttempt != null && (next == Phase.Ready || next == Phase.Idle || next == Phase.Failed))
                startupAttempt.TrySetResult(next == Phase.Ready);
        }

        internal async Task<bool> ConnectAtStartupAsync()
        {
            if (IsDisposed || startupAttempt != null || connectQueued || phase != Phase.Idle) return false;
            if (AskOnCertificateError || savedPasswordRejected || !preferences.HasSessionPassword(Endpoint, Account))
            {
                SetStatus(Phase.Idle, Strings.T("remote_startup_manual"));
                return false;
            }

            var attempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            startupAttempt = attempt;
            try
            {
                SetStatus(Phase.Connecting, Strings.T("sessions_connecting"));
                Task<bool> resolve = Endpoint.ResolvesRemotelyAsync();
                Task completed = await Task.WhenAny(resolve, attempt.Task, Task.Delay(10000));
                if (IsDisposed || attempt.Task.IsCompleted) return false;
                if (completed != resolve)
                {
                    Fail(Strings.T("remote_startup_timeout"));
                    return false;
                }
                if (!await resolve)
                {
                    Fail(Strings.T("sessions_remote_only"));
                    return false;
                }

                StartConnection(null, false, false);
                if (await Task.WhenAny(attempt.Task, Task.Delay(15000)) == attempt.Task)
                    return await attempt.Task;

                if (!IsDisposed)
                {
                    Program.Log("Remote RDP " + Endpoint.Display + " startup sign-in timed out.");
                    Disconnect(false);
                }
                return false;
            }
            catch (Exception ex)
            {
                if (!IsDisposed) ShowFailure(ex);
                return false;
            }
            finally { startupAttempt = null; }
        }

        internal void Connect(string password = null, bool askOnCertificateErrorForAttempt = false, bool rememberPassword = false)
        {
            if (IsDisposed || timer.Enabled || connectQueued || startupAttempt != null) return;
            connectQueued = true;
            int request = ++connectionRequest;
            try
            {
                // The split/single view layout must finish before the ActiveX control is activated.
                BeginInvoke((MethodInvoker)async delegate
                {
                    try
                    {
                        if (IsDisposed || !Visible || request != connectionRequest) return;
                        bool remote = await Endpoint.ResolvesRemotelyAsync();
                        if (IsDisposed || request != connectionRequest) return;
                        if (!remote)
                            Fail(Strings.T("sessions_remote_only"));
                        else StartConnection(password, askOnCertificateErrorForAttempt, rememberPassword);
                    }
                    catch (Exception ex) { if (!IsDisposed && request == connectionRequest) ShowFailure(ex); }
                    finally { if (request == connectionRequest) connectQueued = false; }
                });
            }
            catch (InvalidOperationException ex)
            {
                connectQueued = false;
                ShowFailure(ex);
            }
        }

        private void StartConnection(string password, bool askOnCertificateErrorForAttempt, bool rememberPassword)
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
                activePerformance = preferences.RdpPerformance;
                if (InitialResolution.IsEmpty)
                    InitialResolution = Geometry.RemoteDisplaySize(viewport.ClientSize.Width, viewport.ClientSize.Height, false);
                Size size = PreferredDisplaySize();
                client.DesktopWidth = size.Width;
                client.DesktopHeight = size.Height;
                var settings = client.AdvancedSettings9;
                certificateWarningRequested = AskOnCertificateError || askOnCertificateErrorForAttempt;
                bool unattended = startupAttempt != null;
                rememberCertificateWarning = certificateWarningRequested;
                settings.EnableCredSspSupport = true;
                settings.AuthenticationLevel = certificateWarningRequested
                    ? MaintenanceProfile.WarnOnServerAuthenticationFailure
                    : MaintenanceProfile.RequireServerAuthentication;
                settings.RDPPort = Endpoint.Port;
                settings.SmartSizing = true;
                if (!defaultPerformanceFlags.HasValue) defaultPerformanceFlags = settings.PerformanceFlags;
                settings.PerformanceFlags = GraphicsOptions.PerformanceFlags(activePerformance, defaultPerformanceFlags.Value);
                ConfigureBandwidthDetection(settings);
                settings.RedirectClipboard = ShareClipboard;
                settings.RedirectDrives = false;
                var credentials = (IMsTscNonScriptable)ocx;
                credentials.ResetPassword();
                passwordToSave = null;
                usedSavedPassword = false;
                if (!certificateWarningRequested)
                {
                    if (password == null)
                    {
                        password = savedPasswordRejected ? null : preferences.ReadSessionPassword(Endpoint, Account);
                        usedSavedPassword = password != null;
                    }
                    else if (rememberPassword) passwordToSave = password;
                }
                bool promptForCredentials = certificateWarningRequested || password == null;
                if (unattended && promptForCredentials)
                {
                    SetStatus(Phase.Idle, Strings.T("remote_startup_manual"));
                    return;
                }
                ((IMsRdpClientNonScriptable5)ocx).AllowPromptingForCredentials = !unattended;
                if (!promptForCredentials) credentials.ClearTextPassword = password;
                ((IMsRdpClientNonScriptable3)ocx).PromptForCredentials = promptForCredentials;
                explicitDisconnect = false;
                lastDisplaySize = Size.Empty;
                signInTimer.Start();
                Program.Log("Remote RDP " + Endpoint.Display + " starting at " + size.Width + "x" + size.Height +
                    "; RDP profile=" + activePerformance + "; RedirectClipboard=" + settings.RedirectClipboard +
                    "; AuthenticationLevel=" + settings.AuthenticationLevel);
                if (!unattended) view.Select();
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

        internal void Disconnect(bool resume, string password = null, bool rememberPassword = false)
        {
            if (timer.Enabled || IsDisposed) return;
            // Invalidate a pending DNS lookup before opening or restarting a connection.
            if (connectQueued)
            {
                connectionRequest++;
                connectQueued = false;
            }
            try
            {
                signInTimer.Stop();
                if (events == null || ((IMsRdpClient9)view.GetOcxObject()).Connected == 0)
                {
                    SetStatus(Phase.Idle, Strings.T("status_not_connected"));
                    if (resume) Connect(password, rememberPassword: rememberPassword);
                    return;
                }
                explicitDisconnect = true;
                reconnect = resume;
                pendingPassword = resume ? password : null;
                pendingRememberPassword = resume && rememberPassword;
                passwordToSave = null;
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
                    bool rememberPassword = pendingRememberPassword;
                    pendingRememberPassword = false;
                    SetStatus(Phase.Idle, Strings.T("status_not_connected"));
                    if (reconnect) Connect(password, rememberPassword: rememberPassword);
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    timer.Stop();
                    pendingPassword = null;
                    pendingRememberPassword = false;
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
            if (rememberCertificateWarning)
            {
                bool saved = preferences.SetCertificateWarning(Endpoint, true);
                certificateWarningOverride = saved ? (bool?)null : true;
                if (!saved)
                    Program.Log("Could not save the RDP certificate warning preference for " + Endpoint.Display);
            }
            bool saveFailed = passwordToSave != null && !preferences.SetSessionPassword(Endpoint, Account, passwordToSave);
            if (passwordToSave != null && !saveFailed) savedPasswordRejected = false;
            passwordToSave = null;
            usedSavedPassword = false;
            if (saveFailed) Program.Log("Could not protect or save the RDP password in data.ini.");
            SetStatus(Phase.Ready, Strings.T("remote_login_complete") + (saveFailed ? " · " + Strings.T("remote_password_save_failed") : ""));
            QueueDisplayUpdate();
            if (DesktopOpened != null) DesktopOpened();
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

            bool offerCertificateRetry = !logonError.HasValue && !fatalError.HasValue &&
                RdpFailure.CanRetryWithCertificateWarning(reason, extended, loginCompleted, certificateWarningRequested);
            string hint = offerCertificateRetry ? Strings.T("remote_retry_certificate_hint")
                : RdpFailure.DisconnectHint(reason, extended, loginCompleted, Endpoint.Address != null);
            if (hint.Length != 0) message += Environment.NewLine + Environment.NewLine + hint;
            if (fatalError.HasValue) message += Environment.NewLine + Strings.T("status_fatal", fatalError.Value);
            Fail(message, offerCertificateRetry);
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
                if (usedSavedPassword && key == "remote_logon_invalid")
                {
                    usedSavedPassword = false;
                    savedPasswordRejected = true;
                    if (!preferences.SetSessionPassword(Endpoint, Account, null))
                        Program.Log("Could not remove the rejected RDP password from data.ini.");
                }
                if (key == "remote_logon_invalid") passwordToSave = null;
                logonError = code;
                SetStatus(Phase.SigningIn, Strings.T(key));
            }
        }

        private void ConfigureBandwidthDetection(IMsRdpClientAdvancedSettings8 settings)
        {
            try
            {
                if (activePerformance == RdpPerformanceProfile.Fast)
                {
                    if (!priorBandwidthDetection.HasValue) priorBandwidthDetection = settings.BandwidthDetection;
                    settings.BandwidthDetection = true;
                }
                else if (priorBandwidthDetection.HasValue)
                {
                    settings.BandwidthDetection = priorBandwidthDetection.Value;
                    priorBandwidthDetection = null;
                }
            }
            catch (COMException ex)
            {
                Program.Log("RDP bandwidth detection setting unavailable: 0x" + ex.HResult.ToString("X8"));
            }
        }

        private Size PreferredDisplaySize()
        {
            if (ResolutionOverride.HasValue) return ResolutionOverride.Value;
            if (activePerformance == RdpPerformanceProfile.Fast)
                return GraphicsOptions.LimitFastSize(Math.Max(640, viewport.ClientSize.Width), Math.Max(480, viewport.ClientSize.Height));
            return Geometry.RemoteDisplaySize(viewport.ClientSize.Width, viewport.ClientSize.Height, activePerformance == RdpPerformanceProfile.Fluid);
        }

        private void QueueDisplayUpdate()
        {
            if (displayTimer == null || phase != Phase.Ready || !Visible || IsDisposed) return;
            displayTimer.Stop();
            displayTimer.Interval = lastDisplaySize.IsEmpty ? 900 : 220;
            displayTimer.Start();
        }

        internal void RefreshDisplay() { UpdateDisplay(true); }

        internal bool ApplyResolution(Size? manual)
        {
            if (!IsReady) return false;
            Size? previous = ResolutionOverride;
            ResolutionOverride = manual;
            if (UpdateDisplay(true)) return true;
            ResolutionOverride = previous;
            return false;
        }

        private bool UpdateDisplay(bool force)
        {
            if (IsDisposed || !view.IsHandleCreated || !Visible) return false;
            try
            {
                var client = (IMsRdpClient9)view.GetOcxObject();
                if (client.Connected != 1) return false;
                viewport.PerformLayout();
                Size size = PreferredDisplaySize();
                if (phase == Phase.Ready && (force || lastDisplaySize != size))
                {
                    using (Graphics graphics = view.CreateGraphics())
                    {
                        client.UpdateSessionDisplaySettings((uint)size.Width, (uint)size.Height,
                            Geometry.ToMillimeters(size.Width, graphics.DpiX), Geometry.ToMillimeters(size.Height, graphics.DpiY),
                            0, 100, 100);
                    }
                    lastDisplaySize = size;
                }
                else if (force)
                {
                    client.SyncSessionDisplaySettings();
                }
                return true;
            }
            catch (Exception ex)
            {
                Program.Log("Remote RDP " + Endpoint.Display + " display synchronization: 0x" + ex.HResult.ToString("X8"));
                return false;
            }
            finally
            {
                const uint Invalidate = 0x1, AllChildren = 0x80, UpdateNow = 0x100;
                if (!IsDisposed && view.IsHandleCreated)
                    RedrawWindow(view.Handle, IntPtr.Zero, IntPtr.Zero, Invalidate | AllChildren | UpdateNow);
            }
        }

        private void Fail(string message, bool offerCertificateRetry = false)
        {
            if (timer.Enabled || IsDisposed) return;
            passwordToSave = null;
            usedSavedPassword = false;
            signInTimer.Stop();
            displayTimer.Stop();
            lastFailure = message;
            Program.Log("Remote RDP " + Endpoint.Display + " " + message.Replace(Environment.NewLine, " "));
            SetStatus(Phase.Failed, message, offerCertificateRetry);
        }

        private void ShowFailure(Exception ex)
        {
            timer.Stop();
            pendingPassword = null;
            pendingRememberPassword = false;
            Fail(Strings.T("remote_setup_error", ex.HResult));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (startupAttempt != null) startupAttempt.TrySetResult(false);
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
                passwordToSave = null;
            }
            base.Dispose(disposing);
        }
    }
}
