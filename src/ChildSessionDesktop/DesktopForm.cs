using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;
using MSTSCLib;

namespace ChildSessionDesktop
{
    internal sealed partial class DesktopForm : Form
    {
        private readonly ChildSessionControl rdp;
        private readonly DesktopPreferences preferences = new DesktopPreferences();
        private readonly LogPane logPane;
        private bool preferencesWarningShown;
        private readonly Timer statusTimer;
        private readonly Timer displayResizeTimer;
        private readonly Timer clipboardRelayTimer;
        private System.Windows.Forms.Timer lateResizeTimer;
        private readonly ClipboardFileRelay clipboardRelay;
        private IMsTscAxEvents_Event rdpEvents;
        private bool connecting;
        private bool closing;
        private bool eventsAttached;
        private int lastLoggedState = -1;
        private Size lastRequestedDisplaySize = Size.Empty;
        private Size lastFailedDisplaySize = Size.Empty;
        private DateTime connectionStarted;
        private DateTime connectionReadyAt = DateTime.MinValue;
        private DateTime nextResizeAttempt = DateTime.MinValue;
        private int resizeRetryCount;

        internal bool dynamicMode;
        private int _fixedSessW, _fixedSessH;
        private double aspect;
        private int currentSessW, currentSessH;
        private bool _resChangePending;
        private bool _lastMaximized;
        private int _lbLastW = -1, _lbLastH = -1;
        private double _lbLastA = -1;
        private bool fullscreen;
        private Rectangle prevBounds;
        private Rectangle _trackedScreen = Rectangle.Empty;
        private bool _followScreenRes = true;
        private int _maxSessW, _maxSessH;
        private Size _fixedMatchClient = Size.Empty;
        private FormBorderStyle prevBorder;
        private ToolbarForm toolBar;
        private ContextMenuStrip _displayMenu, _moreMenu, _resMenu;
        private Timer reconnectTimer;
        private readonly ReconnectPolicy reconnectPolicy = new ReconnectPolicy();
        private const int WM_SIZING = 0x0214;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int SW_SHOWNORMAL = 1;
        private const uint VK_F11 = 0x7A;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly string connectionAccount;
        private readonly string connectionPassword;
        private bool promptingForPassword;

        internal DesktopForm(bool dynamicMode, string account = null, string password = null)
        {
            dynamicMode = dynamicMode || preferences.DynamicDisplay;
            this.dynamicMode = dynamicMode;
            fluidGraphics = preferences.FluidGraphics;
            connectionAccount = account;
            connectionPassword = password;
            Text = "RayXDesktop";
            BackColor = Color.Black;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            int screenW = Screen.PrimaryScreen.Bounds.Width;
            int screenH = Screen.PrimaryScreen.Bounds.Height;
            if (screenW <= 0 || screenH <= 0) { screenW = 1280; screenH = 720; }
            SetSessionResolution(screenW, screenH);
            if (!dynamicMode) { _fixedSessW = screenW; _fixedSessH = screenH; }
            int initialW = (int)(screenW * 0.9);
            int initialH = (int)(initialW / aspect + 0.5);
            ClientSize = new Size(initialW, initialH);
            if (!dynamicMode) _fixedMatchClient = new Size(initialW, initialH);
            int minW = 640;
            MinimumSize = new Size(minW, (int)(minW / aspect + 0.5));

            rdp = new ChildSessionControl { Dock = DockStyle.None, BackColor = Color.Black };
            if (!dynamicMode) Program.Log("Letterbox (fixed-resolution) mode enabled; session resolution is pinned to the host screen");
            if (connectionAccount == null)
            {
                clipboardRelay = new ClipboardFileRelay(false);
                clipboardRelayTimer = new Timer { Interval = 250 };
                clipboardRelayTimer.Tick += delegate
                {
                    clipboardRelay.PollIncoming();
                    clipboardRelay.RetryPendingClipboardRead();
                };
                clipboardRelayTimer.Start();
            }
            sessions = new SessionWorkspace(rdp, connectionAccount ?? Environment.UserName);
            Controls.Add(sessions);
            sessions.LocalViewportChanged += RefreshLocalViewport;
            sessions.SelectionChanged += RefreshSessionTitle;
            sessions.AddRequested += AddRemote;
            sessions.LocalConnectRequested += ConnectChildSession;

            statusTimer = new Timer { Interval = 750 };
            statusTimer.Tick += delegate { UpdateConnectionStatus(); };
            displayResizeTimer = new Timer { Interval = 300 };
            displayResizeTimer.Tick += delegate
            {
                displayResizeTimer.Stop();
                ApplyDisplaySize();
            };
            reconnectTimer = new Timer { Interval = 2000 };
            reconnectTimer.Tick += delegate
            {
                if (closing || IsDisposed) return;
                TryAutoReconnect();
            };
            logPane = new LogPane(Program.LogPath, preferences.LogEdgeEnabled);
            Controls.Add(logPane);
            Shown += delegate
            {
                Program.Log("Host form shown");
                if (dynamicMode) SetDynamicLayout();
                else ApplyLetterbox();
                if (clipboardRelay != null)
                    ClipboardFileRelay.RegisterChildAgent();
                else
                {
                    ClipboardFileRelay.UnregisterChildAgent();
                    ClipboardFileRelay.UnregisterChildAgent(connectionAccount);
                }
                CreateToolbar();
                RemoteDesktop startup = RestoreSavedSessions();
                if (startup != null && sessions.SelectExisting(startup.Endpoint, startup.Account)) startup.Connect();
                else BeginInvoke((MethodInvoker)ConnectChildSession);
            };
            FormClosing += OnClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (clipboardRelay != null && !ClipboardFileRelay.AddClipboardFormatListener(Handle))
                Program.Log("Could not register the host-session clipboard listener; Windows error " + Marshal.GetLastWin32Error());
            if (!dynamicMode && !RegisterHotKey(Handle, 1, 0, VK_F11))
                Program.Log("Could not register the F11 fullscreen hotkey; Windows error " + Marshal.GetLastWin32Error());
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (IsHandleCreated && clipboardRelay != null) ClipboardFileRelay.RemoveClipboardFormatListener(Handle);
            if (!dynamicMode && !UnregisterHotKey(Handle, 1))
                Program.Log("Could not unregister the F11 fullscreen hotkey; Windows error " + Marshal.GetLastWin32Error());
            base.OnHandleDestroyed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (logPane != null) logPane.ResizeToFit(ClientSize.Width);
            bool max = (WindowState == FormWindowState.Maximized);
            if (max != _lastMaximized)
            {
                bool wasMax = _lastMaximized;
                _lastMaximized = max;
                if (max)
                {
                    _maxSessW = currentSessW; _maxSessH = currentSessH;
                    if (dynamicMode) QueueDisplayResize(true);
                }
                else if (wasMax && _maxSessW > 0)
                {
                    ApplySessionResolution(_maxSessW, _maxSessH, false);
                }
                if (toolBar != null) toolBar.ResetToTopCenter();
            }
            if (dynamicMode) SetDynamicLayout();
            else ApplyLetterbox();
            if (toolBar != null) toolBar.FollowHost(this);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (toolBar != null) toolBar.FollowHost(this);
            AutoFollowScreen();
        }

        private void AutoFollowScreen()
        {
            if (Handle == IntPtr.Zero || IsDisposed) return;
            if (fullscreen) return;
            var scr = Screen.FromControl(this).Bounds;
            if (scr.Width <= 0 || scr.Height <= 0) return;
            if (_trackedScreen == Rectangle.Empty) { _trackedScreen = scr; return; }
            if (scr != _trackedScreen)
            {
                _trackedScreen = scr;
                Program.Log("AutoFollow: screen=" + scr.Width + "x" + scr.Height +
                            " follow=" + _followScreenRes + " sess=" + currentSessW + "x" + currentSessH);
                if (_followScreenRes && rdp != null)
                {
                    try
                    {
                        var client = (IMsRdpClient9)rdp.GetOcxObject();
                        if (client.Connected == 1)
                        {
                            int w = scr.Width & ~1;
                            ApplySessionResolution(w, scr.Height, false);
                        }
                    }
                    catch (Exception ex) { Program.Log("AutoFollowScreen apply failed: " + ex.GetBaseException().Message); }
                }
                else if (!_followScreenRes && rdp != null)
                {
                    MatchWindowToSession(currentSessW, currentSessH);
                    HoldCurrentDisplaySize();
                }
            }
        }

        protected override void WndProc(ref Message m)
        {

            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == 1)
            {
                ToggleFullscreen();
                return;
            }

            if (m.Msg == WM_DISPLAYCHANGE)
            {
                AutoFollowScreen();
                return;
            }

            if (m.Msg == WM_EXITSIZEMOVE)
            {
                SyncRestoreRect();
                if (dynamicMode) QueueDisplayResize(true);
            }

            if (!dynamicMode && m.Msg == WM_SIZING && aspect > 0 && (sessions == null || !sessions.IsExpanded))
            {
                RECT r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                int bw = Width - ClientSize.Width;
                int bh = Height - ClientSize.Height;
                Rect result;
                if (Geometry.LockAspectRect(
                    new Rect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom },
                    new Rect { Left = Left, Top = Top, Right = Right, Bottom = Bottom },
                    bw, bh, aspect, m.WParam.ToInt32(), out result))
                {
                    r.Left = result.Left; r.Top = result.Top; r.Right = result.Right; r.Bottom = result.Bottom;
                    Marshal.StructureToPtr(r, m.LParam, false);
                    m.Result = (IntPtr)1;
                    return;
                }
            }


            if (m.Msg == ClipboardFileRelay.WmClipboardUpdate)
            {
                if (clipboardRelay != null) clipboardRelay.OnClipboardChanged();
            }
            base.WndProc(ref m);
        }

        private void SyncRestoreRect()
        {
            try
            {
                if (Handle == IntPtr.Zero || IsDisposed) return;
                var wp = new WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                if (!GetWindowPlacement(Handle, ref wp)) return;
                if (wp.showCmd != SW_SHOWNORMAL) return;
                wp.flags = 0;
                wp.rcNormalPosition = new RECT { Left = Left, Top = Top, Right = Right, Bottom = Bottom };
                SetWindowPlacement(Handle, ref wp);
            }
            catch (Exception ex) { Program.Log("SyncRestoreRect failed: " + ex.GetBaseException().Message); }
        }

        private void SetSessionResolution(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            currentSessW = w; currentSessH = h;
            aspect = (double)w / (double)h;
        }

        private void ApplyLetterbox()
        {
            if (rdp == null || rdp.IsDisposed || rdp.Parent == null) return;
            int cw = rdp.Parent.ClientSize.Width;
            int ch = rdp.Parent.ClientSize.Height;
            if (cw <= 0 || ch <= 0) return;
            int dw, dh, dx, dy;
            Geometry.ComputeLetterbox(cw, ch,
                currentSessW > 0 ? currentSessW : 0,
                currentSessH > 0 ? currentSessH : 0,
                out dw, out dh, out dx, out dy);
            rdp.Location = new Point(dx, dy);
            rdp.Size = new Size(dw, dh);
            if (cw != _lbLastW || ch != _lbLastH || Math.Abs(aspect - _lbLastA) > 0.0001)
            {
                _lbLastW = cw; _lbLastH = ch; _lbLastA = aspect;
                Program.Log("Letterbox: aspect=" + aspect.ToString("0.0000")
                    + " client=" + cw + "x" + ch
                    + " rdp=" + dw + "x" + dh + " at (" + rdp.Location.X + "," + rdp.Location.Y + ")");
            }
        }

        private void SetDynamicLayout()
        {
            if (rdp == null || rdp.IsDisposed || rdp.Parent == null) return;
            rdp.Bounds = rdp.Parent.ClientRectangle;
        }

        private bool ShowDisplayModeMenuAt(Rectangle anchor)
        {
            var menu = MenuInstance(ref _displayMenu);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            var itemFixed = new ToolStripMenuItem(Strings.T("menu_fixed"));
            itemFixed.Click += delegate { SetDisplayMode(false); };
            itemFixed.Checked = !dynamicMode;
            var itemDynamic = new ToolStripMenuItem(Strings.T("menu_dynamic"));
            itemDynamic.Click += delegate { SetDisplayMode(true); };
            itemDynamic.Checked = dynamicMode;
            menu.Items.Add(itemFixed);
            menu.Items.Add(itemDynamic);
            Point pos = new Point(anchor.Left, anchor.Bottom);
            var wa = Screen.FromControl(this).WorkingArea;
            pos.X = Math.Max(wa.Left, Math.Min(pos.X, wa.Right - 120));
            pos.Y = Math.Max(wa.Top, Math.Min(pos.Y, wa.Bottom - 60));
            menu.Show(pos);
            return true;
        }

        private bool ShowMoreMenuAt(Rectangle anchor)
        {
            var menu = MenuInstance(ref _moreMenu);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            var itemAbout = new ToolStripMenuItem(Strings.T("menu_about"));
            itemAbout.Click += delegate { AboutBox(); };
            var itemLog = new ToolStripMenuItem(Strings.T(logPane.IsOpen ? "log_collapse" : "log_show"));
            itemLog.Click += delegate { logPane.SetOpen(!logPane.IsOpen); };
            var itemLogEdge = new ToolStripMenuItem(Strings.T("log_show_edge"))
            {
                Checked = logPane.EdgeEnabled, CheckOnClick = true
            };
            itemLogEdge.Click += delegate
            {
                logPane.SetEdgeEnabled(itemLogEdge.Checked);
                SavePreferences(preferences.SetLogEdgeEnabled(itemLogEdge.Checked));
            };
            var itemLogFolder = new ToolStripMenuItem(Strings.T("menu_open_log"));
            itemLogFolder.Click += delegate { OpenLogFolder(); };
            var itemUpdate = new ToolStripMenuItem(Strings.T("menu_check_update"));
            itemUpdate.Click += delegate
            {
                try { Process.Start("https://github.com/D-Synx/Ray-Desktop/actions"); }
                catch (Exception ex) { Program.Log("Could not open update page: " + ex.GetBaseException().Message); }
            };
            AddSessionMenuItems(menu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemAbout);
            menu.Items.Add(itemLog);
            menu.Items.Add(itemLogEdge);
            menu.Items.Add(itemLogFolder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemUpdate);
            Point pos = new Point(anchor.Left, anchor.Bottom);
            var wa = Screen.FromControl(this).WorkingArea;
            pos.X = Math.Max(wa.Left, Math.Min(pos.X, wa.Right - 120));
            pos.Y = Math.Max(wa.Top, Math.Min(pos.Y, wa.Bottom - 60));
            menu.Show(pos);
            return true;
        }

        private void SetDisplayMode(bool mode)
        {
            if (dynamicMode == mode) return;
            SavePreferences(preferences.SetDynamicDisplay(mode));
            if (mode)
            {
                _fixedSessW = currentSessW; _fixedSessH = currentSessH;
            }
            dynamicMode = mode;
            Program.Log("Display mode switched to " + (mode ? "dynamic" : "fixed"));
            if (Handle != IntPtr.Zero)
            {
                if (mode)
                {
                    if (!UnregisterHotKey(Handle, 1))
                        Program.Log("Could not unregister F11 hotkey on switching to dynamic; error " + Marshal.GetLastWin32Error());
                }
                else if (!RegisterHotKey(Handle, 1, 0, VK_F11))
                {
                    Program.Log("Could not register F11 hotkey on switching to fixed; Windows error " + Marshal.GetLastWin32Error());
                }
            }
            if (mode)
            {
                SetDynamicLayout();
                bool stretched = _fixedMatchClient != Size.Empty &&
                    (Math.Abs(ClientSize.Width - _fixedMatchClient.Width) > 3 ||
                     Math.Abs(ClientSize.Height - _fixedMatchClient.Height) > 3);
                if (stretched) QueueDisplayResize();
            }
            else
            {
                if (displayResizeTimer != null) displayResizeTimer.Stop();
                int sw = Screen.PrimaryScreen.Bounds.Width;
                int sh = Screen.PrimaryScreen.Bounds.Height;
                if (sw <= 0 || sh <= 0) { sw = 1280; sh = 720; }
                int fw = (_fixedSessW > 0 && _fixedSessH > 0) ? _fixedSessW : sw;
                int fh = (_fixedSessW > 0 && _fixedSessH > 0) ? _fixedSessH : sh;
                fw &= ~1;
                try
                {
                    var client = (IMsRdpClient9)rdp.GetOcxObject();
                    if (client.Connected == 1)
                    {
                        float dpi = 96f;
                        try { using (var gr = rdp.CreateGraphics()) dpi = gr.DpiX; } catch (Exception ex) { Program.Log("Could not read RDP DPI from graphics: " + ex.GetBaseException().Message); }
                        uint pw = Geometry.ToMillimeters(fw, dpi);
                        uint ph = Geometry.ToMillimeters(fh, dpi);
                        client.UpdateSessionDisplaySettings((uint)fw, (uint)fh, pw, ph, 0, 100, 100);
                        _resChangePending = true;
                        Program.Log("Display mode fixed: session set to " + fw + "x" + fh);
                    }
                }
                catch (Exception ex) { Program.Log("Could not set fixed session resolution: " + ex.GetBaseException().Message); }
                SetSessionResolution(fw, fh);
                MatchWindowToSession(fw, fh);
                ApplyLetterbox();
                DisposeTimer(ref lateResizeTimer);
                lateResizeTimer = new Timer { Interval = 900 };
                lateResizeTimer.Tick += delegate
                {
                    DisposeTimer(ref lateResizeTimer);
                    try
                    {
                        var c2 = (IMsRdpClient9)rdp.GetOcxObject();
                        int dw = c2.DesktopWidth, dh = c2.DesktopHeight;
                        if (dw > 0 && dh > 0) SetSessionResolution(dw, dh);
                    }
                    catch (Exception ex) { Program.Log("Resolution readback failed: " + ex.GetBaseException().Message); }
                    _resChangePending = false;
                    MatchWindowToSession(currentSessW, currentSessH);
                    if (toolBar != null) toolBar.FollowHost(this);
                };
                lateResizeTimer.Start();
            }
            if (toolBar != null)
            {
                toolBar.IsDynamicMode = mode;
                toolBar.Invalidate();
                toolBar.FollowHost(this);
            }
        }

        private void HoldCurrentDisplaySize()
        {
            try
            {
                int w = Math.Max(200, Math.Min(8192, ClientSize.Width));
                int h = Math.Max(200, Math.Min(8192, ClientSize.Height));
                lastRequestedDisplaySize = new Size(w & ~1, h);
            }
            catch (Exception ex) { Program.Log("HoldCurrentDisplaySize failed: " + ex.GetBaseException().Message); }
        }

        private string GetChildSessionDesktop()
        {
            uint sessionId;
            if (((IMsRdpClient9)rdp.GetOcxObject()).Connected != 1
                || !Program.WTSGetChildSessionId(out sessionId) || sessionId == uint.MaxValue)
                throw new InvalidOperationException("Connecte d’abord le bureau dans RayXDesktop.");

            string user = ReadSessionAccountField(sessionId, 5);
            string domain = ReadSessionAccountField(sessionId, 7);
            if (string.IsNullOrWhiteSpace(user))
                throw new InvalidOperationException("Windows n’a pas indiqué le compte de la session ouverte.");

            var account = string.IsNullOrEmpty(domain)
                ? new System.Security.Principal.NTAccount(user)
                : new System.Security.Principal.NTAccount(domain, user);
            var sid = (System.Security.Principal.SecurityIdentifier)account.Translate(
                typeof(System.Security.Principal.SecurityIdentifier));

            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                if (sid.Equals(identity.User))
                    return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            const string profileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\";
            const string shellFolders = @"\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
            using (var profile = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(profileList + sid.Value))
            using (var folders = Microsoft.Win32.Registry.Users.OpenSubKey(sid.Value + shellFolders))
            {
                string root = profile == null ? null : profile.GetValue("ProfileImagePath") as string;
                string desktop = folders == null ? null : folders.GetValue("Desktop", null,
                    Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(desktop))
                    throw new InvalidOperationException("Impossible de lire le dossier Bureau du compte " + account.Value + ".");

                const string profileVariable = @"%USERPROFILE%\";
                if (desktop.StartsWith(profileVariable, StringComparison.OrdinalIgnoreCase))
                    desktop = Path.Combine(root, desktop.Substring(profileVariable.Length));
                if (desktop.IndexOf('%') >= 0 || !Path.IsPathRooted(desktop) || !Directory.Exists(desktop))
                    throw new InvalidOperationException("Le dossier Bureau de " + account.Value + " est indisponible ou inaccessible.");
                return desktop;
            }
        }

        private static string ReadSessionAccountField(uint sessionId, int field)
        {
            IntPtr buffer;
            uint size;
            if (!Program.WTSQuerySessionInformation(IntPtr.Zero, sessionId, field, out buffer, out size))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Impossible de lire le compte de la session RayXDesktop.");
            try { return size == 0 ? "" : Marshal.PtrToStringAnsi(buffer) ?? ""; }
            finally { Program.WTSFreeMemory(buffer); }
        }

        private void OpenLogFolder()
        {
            try
            {
                string log = Program.LogPath;
                if (File.Exists(log)) Process.Start("explorer.exe", "/select,\"" + log + "\"");
                else Process.Start("explorer.exe", Path.GetDirectoryName(log));
            }
            catch (Exception ex) { Program.Log("Could not open log folder: " + ex.GetBaseException().Message); }
        }

        private void SavePreferences(bool saved)
        {
            if (saved || preferencesWarningShown) return;
            preferencesWarningShown = true;
            Program.Log("Could not save RayXDesktop preferences to data.ini.");
            MessageBox.Show(this, Strings.T("preferences_save_failed"), "RayXDesktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void AboutBox()
        {
            MessageBox.Show(this, Strings.T("about_body", Application.ProductVersion), Strings.T("menu_about"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool ToggleFullscreen()
        {
            fullscreen = !fullscreen;
            if (toolBar != null) toolBar.Fullscreen = fullscreen;
            if (fullscreen)
            {
                prevBounds = Bounds;
                prevBorder = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = prevBorder;
                if (toolBar != null) toolBar.ResetToTopCenter();
                Bounds = prevBounds;
            }
            if (dynamicMode) { SetDynamicLayout(); QueueDisplayResize(); }
            else ApplyLetterbox();
            if (toolBar != null) toolBar.UpdateState(fullscreen);
            Program.Log("Fullscreen " + (fullscreen ? "enter" : "exit") + " Bounds=" + Bounds + " client=" + ClientSize.Width + "x" + ClientSize.Height
                + " aspect=" + aspect.ToString("0.0000") + " sess=" + currentSessW + "x" + currentSessH);
            return fullscreen;
        }

        private void SetStatus(string message)
        {
            sessions.UpdateLocalStatus(message);
        }

        private void CreateToolbar()
        {
            if (toolBar != null) return;
            toolBar = new ToolbarForm();
            toolBar.ToggleFullscreen = ToggleFullscreen;
            toolBar.ShowResMenu = ShowResMenuAt;
            toolBar.ShowDisplayModeMenu = ShowDisplayModeMenuAt;
            toolBar.ShowMoreMenu = ShowMoreMenuAt;
            toolBar.ReconnectSession = ReconnectSelected;
            toolBar.ShowFilesMenu = ShowFilesMenuAt;
            toolBar.IsDynamicMode = dynamicMode;
            toolBar.FollowHost(this);
            toolBar.Show(this);
        }

        private static ContextMenuStrip MakeToolbarMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolbarMenuRenderer();
            menu.ShowImageMargin = false;
            return menu;
        }

        private ContextMenuStrip MenuInstance(ref ContextMenuStrip field)
        {
            if (field == null || field.IsDisposed) field = MakeToolbarMenu();
            while (field.Items.Count > 0)
            {
                ToolStripItem item = field.Items[0];
                field.Items.RemoveAt(0);
                item.Dispose();
            }
            return field;
        }

        private sealed class ToolbarMenuRenderer : ToolStripProfessionalRenderer
        {
            internal ToolbarMenuRenderer() : base(new ToolbarMenuColorTable()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item is ToolStripSeparator) { base.OnRenderItemText(e); return; }
                e.TextColor = e.Item.Selected ? Color.White : Color.FromArgb(216, 216, 222);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Selected)
                {
                    var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                    using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                        e.Graphics.FillRectangle(b, r);
                }
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var r = e.ImageRectangle;
                using (var b = new SolidBrush(Color.FromArgb(36, 36, 42)))
                    e.Graphics.FillRectangle(b, r);
                using (var p = new Pen(Color.White, 1.7f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    int x = r.X + 2, cy = r.Y + r.Height / 2 - 1;
                    e.Graphics.DrawLines(p, new[] {
                        new Point(x, cy),
                        new Point(x + 4, cy + 4),
                        new Point(x + 8, cy - 2)
                    });
                }
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (var p = new Pen(Color.FromArgb(58, 58, 66)))
                    e.Graphics.DrawLine(p, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var p = new Pen(Color.FromArgb(60, 60, 70)))
                    e.Graphics.DrawRectangle(p, new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1));
            }
        }

        private sealed class ToolbarMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(32, 32, 38); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(32, 32, 38); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(32, 32, 38); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(32, 32, 38); } }
            public override Color MenuBorder { get { return Color.FromArgb(60, 60, 70); } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(48, 48, 56); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(48, 48, 56); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(48, 48, 56); } }
            public override Color CheckBackground { get { return Color.FromArgb(36, 36, 42); } }
            public override Color CheckSelectedBackground { get { return Color.FromArgb(36, 36, 42); } }
            public override Color CheckPressedBackground { get { return Color.FromArgb(36, 36, 42); } }
            public override Color SeparatorDark { get { return Color.FromArgb(58, 58, 66); } }
            public override Color SeparatorLight { get { return Color.FromArgb(58, 58, 66); } }
        }

        private bool ShowResMenuAt(Rectangle anchor)
        {
            var menu = MenuInstance(ref _resMenu);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            int sw = Screen.FromControl(this).Bounds.Width;
            int sh = Screen.FromControl(this).Bounds.Height;
            Program.Log("ResMenu open: sess=" + currentSessW + "x" + currentSessH +
                        " screen=" + sw + "x" + sh + " follow=" + _followScreenRes);
            var itemScreen = new ToolStripMenuItem(Strings.T("res_your_screen", sw, sh));
            itemScreen.Click += delegate { ApplyResolutionPick(0, 0); };
            itemScreen.Checked = (currentSessW == (sw & ~1) && currentSessH == sh);
            menu.Items.Add(itemScreen);
            int[,] presets = {
                {3840, 2160}, {2560, 1440}, {2560, 1080}, {1920, 1080}, {1366, 768}, {1280, 720}
            };
            for (int i = 0; i < presets.GetLength(0); i++)
            {
                int w = presets[i, 0], h = presets[i, 1];
                if (w == sw && h == sh) continue;
                var item = new ToolStripMenuItem(w + " × " + h);
                item.Click += delegate { ApplyResolutionPick(w, h); };
                item.Checked = (currentSessW == w && currentSessH == h);
                menu.Items.Add(item);
            }
            Point pos = new Point(anchor.Left, anchor.Bottom);
            var wa = Screen.FromControl(this).WorkingArea;
            pos.X = Math.Max(wa.Left, Math.Min(pos.X, wa.Right - 140));
            pos.Y = Math.Max(wa.Top, Math.Min(pos.Y, wa.Bottom - 60));
            menu.Show(pos);
            return true;
        }

        private void ApplyResolutionPick(int pickW, int pickH)
        {
            int sw = Screen.FromControl(this).Bounds.Width;
            int sh = Screen.FromControl(this).Bounds.Height;
            int w = pickW, h = pickH;
            if (w == 0) { w = sw; h = sh; }
            if (w <= 0 || h <= 0) return;
            w &= ~1;

            var result = MessageBox.Show(this,
                Strings.T("res_confirm_body", w, h),
                Strings.T("res_confirm_title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (result != DialogResult.OK) return;

            _followScreenRes = (pickW == 0);
            ApplySessionResolution(w, h, true);
        }

        private void ApplySessionResolution(int w, int h, bool confirmUi)
        {
            try
            {
                var client = (IMsRdpClient9)rdp.GetOcxObject();
                if (client.Connected != 1)
                {
                    if (confirmUi)
                        MessageBox.Show(this, Strings.T("res_not_connected"), Strings.T("res_confirm_title"),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                float dpi = 96f;
                try { using (var gr = rdp.CreateGraphics()) dpi = gr.DpiX; } catch (Exception ex) { Program.Log("Could not read RDP DPI from graphics: " + ex.GetBaseException().Message); }
                uint pw = Geometry.ToMillimeters(w, dpi);
                uint ph = Geometry.ToMillimeters(h, dpi);
                client.UpdateSessionDisplaySettings((uint)w, (uint)h, pw, ph, 0, 100, 100);
                Program.Log("Resolution menu: session set to " + w + "x" + h);
                _resChangePending = true;
                SetSessionResolution(w, h);
                if (toolBar != null) toolBar.ResetToTopCenter();
                MatchWindowToSession(w, h);
                if (dynamicMode) HoldCurrentDisplaySize();
                DisposeTimer(ref lateResizeTimer);
                lateResizeTimer = new Timer { Interval = 900 };
                lateResizeTimer.Tick += delegate
                {
                    DisposeTimer(ref lateResizeTimer);
                    try
                    {
                        var c2 = (IMsRdpClient9)rdp.GetOcxObject();
                        int dw = c2.DesktopWidth, dh = c2.DesktopHeight;
                        if (dw > 0 && dh > 0)
                        {
                            SetSessionResolution(dw, dh);
                            Program.Log("Resolution menu readback " + dw + "x" + dh);
                        }
                    }
                    catch (Exception ex) { Program.Log("Resolution menu readback failed: " + ex.GetBaseException().Message); }
                    _resChangePending = false;
                    MatchWindowToSession(currentSessW, currentSessH);
                    if (dynamicMode) HoldCurrentDisplaySize();
                    if (toolBar != null) toolBar.FollowHost(this);
                };
                lateResizeTimer.Start();
            }
            catch (Exception ex)
            {
                _resChangePending = false;
                Program.Log("Resolution menu apply failed: " + ex);
                if (confirmUi)
                    MessageBox.Show(this, Strings.T("res_fail") + ex.GetBaseException().Message,
                        Strings.T("res_confirm_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void MatchWindowToSession(int sessW, int sessH)
        {
            if (sessions != null && (sessions.IsExpanded || (logPane != null && logPane.IsOpen)))
            {
                RefreshLocalViewport();
                return;
            }
            if (Handle == IntPtr.Zero || IsDisposed) return;
            if (sessW <= 0 || sessH <= 0) return;
            if (fullscreen) { ApplyLetterbox(); return; }
            MinimumSize = new Size(320, (int)(320 / aspect + 0.5));
            var area = Screen.FromControl(this).WorkingArea;
            if (area.Width <= 0 || area.Height <= 0) area = Screen.FromControl(this).Bounds;
            int bw = Width - ClientSize.Width;
            int bh = Height - ClientSize.Height;
            int availW = area.Width - bw - 8;
            int availH = area.Height - bh - 8;
            if (availW < 320) availW = 320;
            if (availH < 240) availH = 240;
            double s = Math.Min((double)availW / sessW, (double)availH / sessH);
            if (s > 1.0) s = 1.0;
            int cw = (int)(sessW * s + 0.5);
            int ch = (int)(sessH * s + 0.5);
            if (!dynamicMode) _fixedMatchClient = new Size(cw, ch);
            int ww = cw + bw, wh = ch + bh;
            int L = area.Left + (area.Width - ww) / 2;
            int T = area.Top + (area.Height - wh) / 2;
            if (L < area.Left) L = area.Left;
            if (T < area.Top) T = area.Top;
            SetWindowPos(Handle, IntPtr.Zero, L, T, ww, wh,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            SyncRestoreRect();
            Program.Log("Window matched to session " + sessW + "x" + sessH + "; window " + ww + "x" + wh
                + "; client area " + ClientSize.Width + "x" + ClientSize.Height
                + "; scale " + s.ToString("0.000") + " aspect=" + aspect.ToString("0.0000"));
            if (dynamicMode) SetDynamicLayout();
            else ApplyLetterbox();
            if (toolBar != null) toolBar.FollowHost(this);
        }

        private void ConnectChildSession()
        {
            if (connecting || promptingForPassword || closing || rdp.IsDisposed) return;
            try
            {
                bool childSessionsEnabled;
                if (!Program.WTSIsChildSessionsEnabled(out childSessionsEnabled))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(Strings.T("msg_check_fail", error));
                }
                if (!childSessionsEnabled)
                {
                    connecting = false;
                    Program.Log("Child sessions are disabled on this computer");
                    SetStatus("Child Sessions disabled. An authorized administrator can run EnableChildSessions.exe --enable-child-sessions.");
                    return;
                }
                Program.Log("Child sessions enabled on this computer");

                object ocx = rdp.GetOcxObject();
                AttachConnectionEvents(ocx);
                var client = (IMsRdpClient9)ocx;
                int state = client.Connected;
                if (!hasDefaultPerformanceFlags)
                {
                    defaultPerformanceFlags = client.AdvancedSettings9.PerformanceFlags;
                    hasDefaultPerformanceFlags = true;
                }
                if (state == 1)
                {
                    connecting = false;
                    SetStatus(Strings.T("status_connected"));
                    statusTimer.Start();
                    if (dynamicMode) QueueDisplayResize();
                    else ApplyLetterbox();
                    return;
                }
                if (state == 2)
                {
                    connecting = true;
                    connectionStarted = DateTime.Now;
                    SetStatus(Strings.T("status_connecting"));
                    statusTimer.Interval = 150;
                    statusTimer.Start();
                    return;
                }

                string password = connectionPassword;
                if (connectionAccount != null && password == null)
                {
                    promptingForPassword = true;
                    try
                    {
                        password = PasswordDialog.Prompt(this, connectionAccount);
                    }
                    finally
                    {
                        promptingForPassword = false;
                    }
                    if (closing || rdp.IsDisposed) return;
                    if (password == null)
                    {
                        statusTimer.Stop();
                        SetStatus(Strings.T("status_credentials_cancelled"));
                        return;
                    }
                }

                explicitlyDisconnected = false;
                lastRequestedDisplaySize = Size.Empty;
                connecting = true;
                connectionStarted = DateTime.Now;
                SetStatus(Strings.T("status_connecting_win"));
                Program.Log("Begin child-session setup");
                client.Server = "localhost";
                if (connectionAccount != null)
                {
                    int separator = connectionAccount.IndexOf('\\');
                    string domain = connectionAccount.Substring(0, separator);
                    client.Domain = domain == "." ? Environment.MachineName : domain;
                    client.UserName = connectionAccount.Substring(separator + 1);
                    ((IMsTscNonScriptable)ocx).ClearTextPassword = password;
                    ((IMsRdpClientNonScriptable3)ocx).PromptForCredentials = false;
                    Program.Log("Explicit credentials supplied to the RDP control");
                }
                Program.Log("Server set");
                if (!dynamicMode)
                {
                    int sw = (currentSessW > 0 && currentSessH > 0) ? currentSessW : Screen.PrimaryScreen.Bounds.Width;
                    int sh = (currentSessW > 0 && currentSessH > 0) ? currentSessH : Screen.PrimaryScreen.Bounds.Height;
                    if (sw <= 0 || sh <= 0) { sw = 1280; sh = 720; }
                    if (fluidGraphics) { sw = Math.Min(sw, 1600); sh = Math.Min(sh, 900); }
                    sw &= ~1;
                    sh &= ~1;
                    client.DesktopWidth = sw;
                    client.DesktopHeight = sh;
                    SetSessionResolution(sw, sh);
                    Program.Log("Requesting child-session desktop at " + sw + "x" + sh + " from the start (fixed)");
                }
                var extended = (IMsRdpExtendedSettings)ocx;
                Program.Log("QI IMsRdpExtendedSettings succeeded");
                object connectToChild = true;
                extended.set_Property("ConnectToChildSession", ref connectToChild);
                Program.Log("ConnectToChildSession set true");
                client.AdvancedSettings9.EnableCredSspSupport = true;
                client.AdvancedSettings9.SmartSizing = true;
                localFluidGraphics = fluidGraphics;
                client.AdvancedSettings9.PerformanceFlags = localFluidGraphics ? GraphicsOptions.FluidPerformanceFlags : defaultPerformanceFlags;
                Program.Log("RDP performance profile: " + (fluidGraphics ? "fluid" : "default"));
                client.AdvancedSettings9.RedirectClipboard = connectionAccount != null;
                client.AdvancedSettings9.RedirectDrives = true;
                EnableDriveRedirection(ocx);
                client.AdvancedSettings9.EnableWindowsKey = 1;
                client.AdvancedSettings9.AcceleratorPassthrough = 1;
                client.SecuredSettings3.KeyboardHookMode = 1;
                Program.Log(connectionAccount != null
                    ? "Using native RDP clipboard redirection"
                    : "Using RayXDesktop clipboard bridge");
                ((IMsTscAx)ocx).Connect();
                Program.Log("Connect() returned");
                statusTimer.Interval = 150;
                statusTimer.Start();
            }
            catch (Exception ex)
            {
                Program.Log("Connect setup failed: " + ex);
                connecting = false;
                var message = Strings.T("msg_connect_fail") + ex.GetBaseException().Message;
                SetStatus(message);
            }
        }

        private static void EnableDriveRedirection(object ocx)
        {
            try
            {
                var drives = ((IMsRdpClientNonScriptable3)ocx).DriveCollection;
                drives.RescanDrives(true);
                uint count = drives.DriveCount;
                Program.Log("RDP drive redirection collection contains " + count + " drive(s)");
                for (uint i = 0; i < count; i++)
                {
                    var drive = drives.get_DriveByIndex(i);
                    drive.RedirectionState = true;
                    Program.Log("RDP drive redirection enabled for " + drive.Name.TrimEnd('\0') +
                        " (state=" + drive.RedirectionState + ")");
                }
                if (count == 0)
                    Program.Log("No local drives were exposed by the RDP ActiveX drive collection");
            }
            catch (Exception ex)
            {
                Program.Log("Could not enumerate or enable individual RDP drives: " + ex.GetBaseException().Message);
            }
        }

        private void UpdateConnectionStatus()
        {
            try
            {
                int state = ((IMsRdpClient9)rdp.GetOcxObject()).Connected;
                if (state != lastLoggedState)
                {
                    Program.Log("Connected state changed to " + state);
                    lastLoggedState = state;
                    if (state == 1)
                    {
                        statusTimer.Interval = 750;
                        if (connecting)
                            Program.Log("RDP connected in " + (DateTime.Now - connectionStarted).TotalMilliseconds.ToString("F0") + " ms");
                        if (connectionAccount != null)
                            ClipboardFileRelay.UnregisterChildAgent(connectionAccount);
                        LogRedirectionDiagnostics();
                        connectionReadyAt = DateTime.Now.AddSeconds(2);
                        sessions.QueueLocalPreview();
                        resizeRetryCount = 0;
                        lastFailedDisplaySize = Size.Empty;
                        nextResizeAttempt = DateTime.MinValue;
                    }
                    else if (state == 0)
                    {
                        connectionReadyAt = DateTime.MinValue;
                    }
                }
                if (connecting && (state == 0 || state == 2) &&
                    DateTime.Now - connectionStarted > TimeSpan.FromSeconds(45))
                {
                    connecting = false;
                    statusTimer.Stop();
                    EnterReconnectState();
                    try { ((IMsTscAx)rdp.GetOcxObject()).Disconnect(); } catch (Exception ex) { Program.Log("Disconnect after connection timeout failed: " + ex.GetBaseException().Message); }
                    SetStatus(Strings.T("status_timeout"));
                    Program.Log("Child-session connection timed out after 45 seconds (Connected=" + state + ")");
                    return;
                }

                if (state == 0)
                {
                    if (connecting) SetStatus(Strings.T("status_waiting"));
                    else SetStatus(Strings.T("status_not_connected"));
                }
                else if (state == 1)
                {
                    connecting = false;
                    SetStatus(Strings.T("status_connected"));
                    if (!dynamicMode)
                    {
                        try
                        {
                            var c = (IMsRdpClient9)rdp.GetOcxObject();
                            int dw = c.DesktopWidth, dh = c.DesktopHeight;
                            if (dw > 0 && dh > 0 && !_resChangePending && (dw != currentSessW || dh != currentSessH))
                            {
                                SetSessionResolution(dw, dh);
                                Program.Log("Session resolution readback " + dw + "x" + dh + "; aspect=" + aspect.ToString("0.0000"));
                                ApplyLetterbox();
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.Log("Could not read back session resolution: " + ex.GetBaseException().Message);
                        }
                    }
                }
                else if (state == 2)
                {
                    SetStatus(Strings.T("status_connecting"));
                }
                else SetStatus(Strings.T("status_state") + state);
            }
            catch (Exception ex)
            {
                Program.Log("Could not read connection state: " + ex.GetBaseException().Message);
                SetStatus(Strings.T("status_read_fail") + ex.GetBaseException().Message);
                statusTimer.Stop();
                if (!closing) EnterReconnectState();
            }
        }

        private void AttachConnectionEvents(object ocx)
        {
            if (eventsAttached) return;
            rdpEvents = (IMsTscAxEvents_Event)ocx;
            rdpEvents.OnRemoteDesktopSizeChange += HandleRemoteResize;
            rdpEvents.OnDisconnected += HandleDisconnected;
            rdpEvents.OnFatalError += HandleFatalError;
            rdpEvents.OnLogonError += HandleLogonError;
            eventsAttached = true;
            Program.Log("RDP connection events attached");
        }

        private void LogRedirectionDiagnostics()
        {
            try
            {
                object ocx = rdp.GetOcxObject();
                var client = (IMsRdpClient9)ocx;
                var settings = client.AdvancedSettings9;
                Program.Log("RDP redirection settings after connect: RedirectClipboard=" + settings.RedirectClipboard +
                    ", RedirectDrives=" + settings.RedirectDrives + ", DisableRdpdr=" + settings.DisableRdpdr);
                if (clipboardRelay != null)
                    Program.Log("RayXDesktop clipboard bridge is active");
            }
            catch (Exception ex)
            {
                Program.Log("Could not query RDP clipboard and drive redirection state after connect: " +
                    ex.GetBaseException().Message);
            }
        }

        private void HandleRemoteResize(int width, int height)
        {
            try
            {
                if (rdp == null || rdp.IsDisposed || rdp.Parent == null) return;
                if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { HandleRemoteResize(width, height); }); return; }
                Program.Log("OnRemoteDesktopSizeChange: session=" + currentSessW + "x" + currentSessH
                    + " client=" + ClientSize.Width + "x" + ClientSize.Height
                    + " reported=" + width + "x" + height);
                if (rdp.Size.Width > 2 && rdp.Size.Height > 2)
                    rdp.Size = new Size(rdp.Size.Width - 1, rdp.Size.Height - 1);
                ApplyLetterbox();
                if (toolBar != null) toolBar.FollowHost(this);
            }
            catch (Exception ex) { Program.Log("OnRemoteDesktopSizeChange handler: " + ex); }
        }

        private void HandleDisconnected(int reason)
        {
            if (closing || IsDisposed || disconnectTimer != null || reconnectTimer.Enabled) return;
            CancelFileCopy();
            connecting = false;
            statusTimer.Stop();
            var message = reason == 1
                ? Strings.T("status_disconnected_local")
                : Strings.T("status_disconnected", reason);
            if (reason == 0xC07)
                message += Strings.T("status_disconnected_acct");
            Program.Log(message);
            SetStatus(message);
            if (ReconnectPolicy.ShouldAutoReconnect(reason)) EnterReconnectState();
        }

        private void HandleFatalError(int errorCode)
        {
            if (closing || IsDisposed) return;
            connecting = false;
            statusTimer.Stop();
            var message = Strings.T("status_fatal", errorCode);
            Program.Log(message);
            SetStatus(message);
            EnterReconnectState();
        }

        private void HandleLogonError(int errorCode)
        {
            if (closing || IsDisposed) return;
            if (errorCode == -2)
            {
                Program.Log("Windows is continuing the child-session sign-in process");
                try
                {
                    if (((IMsRdpClient9)rdp.GetOcxObject()).Connected == 1)
                    {
                        connecting = false;
                        SetStatus(Strings.T("status_connected"));
                    }
                    else
                    {
                        SetStatus(Strings.T("status_continuing"));
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("Could not read RDP state after continue-logon event: " + ex.GetBaseException().Message);
                }
                return;
            }

            var message = Strings.T("status_logon_error", errorCode);
            connecting = false;
            statusTimer.Stop();
            Program.Log(message);
            SetStatus(message);
            EnterReconnectState();
        }

        private void EnterReconnectState()
        {
            if (closing || IsDisposed || disconnectTimer != null || explicitlyDisconnected || reconnectTimer == null || connectionAccount != null) return;
            connecting = false;
            if (!reconnectTimer.Enabled)
            {
                reconnectPolicy.Enter();
                reconnectTimer.Interval = 2000;
                reconnectTimer.Start();
                Program.Log("Entering auto-reconnect state");
            }
        }

        private void StopReconnect()
        {
            if (reconnectTimer != null && reconnectTimer.Enabled) reconnectTimer.Stop();
        }

        private void TryAutoReconnect()
        {
            try
            {
                var client = (IMsRdpClient9)rdp.GetOcxObject();
                if (client.Connected == 1)
                {
                    StopReconnect();
                    connecting = false;
                    reconnectPolicy.OnReconnected();
                    statusTimer.Start();
                    SetStatus(Strings.T("status_connected"));
                    return;
                }
                reconnectPolicy.OnAttempt();
                if (reconnectPolicy.GaveUp)
                {
                    StopReconnect();
                    Program.Log("Auto-reconnect gave up after 6 attempts; restart manually");
                    SetStatus(Strings.T("status_reconnect_failed"));
                    return;
                }
                Program.Log("Auto-reconnect attempt " + reconnectPolicy.Attempts);
                try { ((IMsTscAx)rdp.GetOcxObject()).Disconnect(); }
                catch (Exception ex) { Program.Log("Auto-reconnect pre-disconnect failed: " + ex.GetBaseException().Message); }
                ConnectChildSession();
            }
            catch (Exception ex)
            {
                Program.Log("Auto-reconnect failed: " + ex.GetBaseException().Message);
            }
        }

        private void QueueDisplayResize(bool windowSizeChanged = false)
        {
            if (closing || displayResizeTimer == null || rdp.IsDisposed) return;
            if (windowSizeChanged)
            {
                resizeRetryCount = 0;
                lastFailedDisplaySize = Size.Empty;
                nextResizeAttempt = DateTime.MinValue;
                displayResizeTimer.Stop();
                displayResizeTimer.Interval = 300;
            }
            else if (displayResizeTimer.Enabled)
            {
                return;
            }
            else if (nextResizeAttempt > DateTime.Now)
            {
                displayResizeTimer.Interval = Math.Max(100,
                    (int)Math.Ceiling((nextResizeAttempt - DateTime.Now).TotalMilliseconds));
            }
            else
            {
                displayResizeTimer.Interval = 300;
            }
            displayResizeTimer.Start();
        }

        private void ApplyDisplaySize()
        {
            if (closing || rdp.IsDisposed || !rdp.IsHandleCreated) return;
            if (!dynamicMode) return;
            try
            {
                var client = (IMsRdpClient9)rdp.GetOcxObject();
                if (client.Connected != 1) return;

                if (DateTime.Now < connectionReadyAt)
                {
                    displayResizeTimer.Interval = Math.Max(100,
                        (int)Math.Ceiling((connectionReadyAt - DateTime.Now).TotalMilliseconds));
                    displayResizeTimer.Start();
                    return;
                }

                int width = Math.Max(200, Math.Min(localFluidGraphics ? 1600 : 8192, rdp.Parent.ClientSize.Width));
                int height = Math.Max(200, Math.Min(localFluidGraphics ? 900 : 8192, rdp.Parent.ClientSize.Height));
                width &= ~1; // RDP display-control requires an even pixel width.
                var requested = new Size(width, height);
                if (requested == lastRequestedDisplaySize) return;

                float dpiX;
                float dpiY;
                using (Graphics graphics = rdp.CreateGraphics())
                {
                    dpiX = graphics.DpiX;
                    dpiY = graphics.DpiY;
                }
                uint physicalWidth = Geometry.ToMillimeters(width, dpiX);
                uint physicalHeight = Geometry.ToMillimeters(height, dpiY);

                client.UpdateSessionDisplaySettings((uint)width, (uint)height,
                    physicalWidth, physicalHeight, 0, 100, 100);
                lastRequestedDisplaySize = requested;
                lastFailedDisplaySize = Size.Empty;
                resizeRetryCount = 0;
                nextResizeAttempt = DateTime.MinValue;
                SetSessionResolution(width, height);
                Program.Log("Remote desktop resized to " + width + "x" + height);
            }
            catch (Exception ex)
            {
                var requested = new Size(Math.Max(200, Math.Min(localFluidGraphics ? 1600 : 8192, rdp.Parent.ClientSize.Width)) & ~1,
                    Math.Max(200, Math.Min(localFluidGraphics ? 900 : 8192, rdp.Parent.ClientSize.Height)));
                if (requested != lastFailedDisplaySize)
                {
                    lastFailedDisplaySize = requested;
                    resizeRetryCount = 0;
                }
                resizeRetryCount++;
                int retryDelay = Math.Min(4000, 500 * (1 << Math.Min(resizeRetryCount - 1, 3)));
                nextResizeAttempt = DateTime.Now.AddMilliseconds(retryDelay);
                displayResizeTimer.Interval = retryDelay;
                displayResizeTimer.Start();
                Program.Log("Dynamic display resize failed for " + requested.Width + "x" + requested.Height +
                    "; retrying in " + retryDelay + " ms: " + ex.GetBaseException().Message);
            }
        }

        private static void DisposeTimer(ref Timer timer)
        {
            if (timer == null) return;
            timer.Dispose();
            timer = null;
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                bool connected = sessions.HasRemoteConnections;
                try { connected |= ((IMsRdpClient9)rdp.GetOcxObject()).Connected == 1; }
                catch (Exception ex) { Program.Log("Could not read RDP state for close confirm: " + ex.GetBaseException().Message); }
                if (connected)
                {
                    var r = MessageBox.Show(this, Strings.T("close_confirm_body"),
                        Strings.T("close_confirm_title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (r != DialogResult.OK) { e.Cancel = true; return; }
                }
            }
            closing = true;
            CancelFileCopy();
            _resChangePending = false;
            if (toolBar != null) { toolBar.Dispose(); toolBar = null; }
            statusTimer.Dispose();
            displayResizeTimer.Dispose();
            reconnectTimer.Dispose();
            DisposeTimer(ref disconnectTimer);
            if (_filesMenu != null) _filesMenu.Dispose();
            if (_displayMenu != null) { _displayMenu.Dispose(); _displayMenu = null; }
            if (_moreMenu != null) { _moreMenu.Dispose(); _moreMenu = null; }
            if (_resMenu != null) { _resMenu.Dispose(); _resMenu = null; }
            if (clipboardRelayTimer != null)
            {
                clipboardRelayTimer.Stop();
                clipboardRelayTimer.Dispose();
            }
            DisposeTimer(ref lateResizeTimer);
            if (clipboardRelay != null)
            {
                clipboardRelay.Dispose();
                ClipboardFileRelay.UnregisterChildAgent();
                ClipboardFileRelay.ClearMailbox();
            }
            if (eventsAttached && rdpEvents != null)
            {
                try
                {
                    rdpEvents.OnRemoteDesktopSizeChange -= HandleRemoteResize;
                    rdpEvents.OnDisconnected -= HandleDisconnected;
                    rdpEvents.OnFatalError -= HandleFatalError;
                    rdpEvents.OnLogonError -= HandleLogonError;
                }
                catch (Exception ex) { Program.Log("Could not unsubscribe RDP events during shutdown: " + ex.GetBaseException().Message); }
                eventsAttached = false;
            }
            try { ((IMsTscAx)rdp.GetOcxObject()).Disconnect(); }
            catch (Exception ex) { Program.Log("Disconnect during shutdown failed: " + ex.GetBaseException().Message); }


        }
    }
}
