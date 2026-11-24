using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal sealed class ToolbarControl : Control
    {
        private enum Tool { Move, Display, Resolution, Fullscreen, Files, SendFiles, ReceiveFiles, RdpClipboard, Sessions, Reconnect, More, Dock, Collapse, Close }

        private static readonly Tool[] Tools = {
            Tool.Move, Tool.Display, Tool.Resolution, Tool.Fullscreen,
            Tool.Files, Tool.SendFiles, Tool.ReceiveFiles, Tool.RdpClipboard,
            Tool.Sessions, Tool.Reconnect, Tool.More, Tool.Dock, Tool.Collapse, Tool.Close
        };

        private const double BadgeOpacity = 0.82;
        private const int BarHeight = 48, BadgeSize = 42, PaddingSize = 6;
        private readonly Rectangle[] buttons = new Rectangle[Tools.Length];
        private readonly TooltipForm tooltip;
        private readonly Font labelFont;

        private readonly Form host;
        private readonly Panel band;
        private readonly Form floatingWindow;
        private Point? hostOffset;
        private int hover = -1, pressed = -1, focused = -1;
        private Point dragOrigin;
        private Point cursorOrigin;
        private bool moving, moved, collapsed, compact, sessionsExpanded;
        private int hostWidth = int.MaxValue;
        private float scale = 1;

        internal Func<bool> ToggleFullscreen;
        internal Func<Rectangle, bool> ShowResMenu;
        internal Func<Rectangle, bool> ShowDisplayModeMenu;
        internal Func<Rectangle, bool> ShowMoreMenu;
        internal Action<Rectangle> ShowFilesMenu;
        internal Func<bool, bool?> FileTransferState;
        internal Action<bool> ToggleFileTransfer;
        internal Func<bool> RdpClipboardState;
        internal Action ToggleRdpClipboard;
        internal Action ReconnectSession;
        internal Action ToggleSessions;
        internal Action ToggleDock;
        internal bool IsDynamicMode;
        internal bool Fullscreen;
        internal bool IsDocked { get { return Parent == band; } }

        internal ToolbarControl(Form host, Panel band)
        {
            this.host = host;
            this.band = band;
            floatingWindow = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.None, BackColor = ChromeColors.Surface, Owner = host
            };
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            scale = Math.Max(1f, host.DeviceDpi / 96f);
            labelFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            tooltip = new TooltipForm();
            band.ClientSizeChanged += OnBandSizeChanged;
            host.DpiChanged += OnHostDpiChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                band.ClientSizeChanged -= OnBandSizeChanged;
                host.DpiChanged -= OnHostDpiChanged;
                tooltip.Dispose();
                labelFont.Dispose();
                if (!floatingWindow.Disposing && !floatingWindow.IsDisposed)
                {
                    if (Parent == floatingWindow) floatingWindow.Controls.Remove(this);
                    var outline = floatingWindow.Region;
                    floatingWindow.Region = null;
                    if (outline != null) outline.Dispose();
                    floatingWindow.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private int Px(int logical) { return (int)Math.Round(logical * scale); }

        private string TextLabel(Tool tool)
        {
            switch (tool)
            {
                case Tool.Display: return Strings.T("btn_display");
                case Tool.Resolution: return Strings.T("btn_resolution");
                case Tool.Files: return Strings.T("btn_files");
                default: return "";
            }
        }

        private string ToolName(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return Strings.T("btn_drag");
                case Tool.Display:
                    return Strings.T(IsDynamicMode ? "tip_display_dynamic" : "tip_display_fixed");
                case Tool.Resolution: return TextLabel(tool);
                case Tool.Fullscreen:
                    return Strings.T(Fullscreen ? "tip_exit_fullscreen" : "btn_fullscreen");
                case Tool.Files: return Strings.T("tip_files");
                case Tool.SendFiles: return ToggleName(tool, "files_toggle_send");
                case Tool.ReceiveFiles: return ToggleName(tool, "files_toggle_receive");
                case Tool.RdpClipboard: return ToggleName(tool, "files_toggle_clipboard");
                case Tool.Sessions: return Strings.T(sessionsExpanded ? "sessions_hide" : "sessions_show");
                case Tool.Reconnect: return Strings.T("menu_reconnect");
                case Tool.More: return Strings.T("btn_more");
                case Tool.Dock: return Strings.T(IsDocked ? "toolbar_float" : "toolbar_dock");
                case Tool.Collapse: return Strings.T("tip_collapse");
                case Tool.Close: return Strings.T("btn_exit");
                default: return "";
            }
        }

        private static bool IsToggle(Tool tool)
        {
            return tool == Tool.SendFiles || tool == Tool.ReceiveFiles || tool == Tool.RdpClipboard;
        }

        private bool? ToggleState(Tool tool)
        {
            if (tool == Tool.RdpClipboard) return RdpClipboardState == null ? (bool?)null : RdpClipboardState();
            return FileTransferState == null ? null : FileTransferState(tool == Tool.SendFiles);
        }

        private string ToggleName(Tool tool, string label)
        {
            bool? value = ToggleState(tool);
            return Strings.T(label) + " · " + Strings.T(!value.HasValue ? "files_toggle_unavailable"
                : value.Value ? "policy_enabled" : "policy_disabled");
        }

        private string ExpandName { get { return Strings.T("tip_expand"); } }

        private int StandardButtonWidth(Tool tool)
        {
            string label = TextLabel(tool);
            if (label.Length == 0) return 35;
            return Math.Max(tool == Tool.Files ? 80 : 103, TextRenderer.MeasureText(label, labelFont).Width + 32);
        }

        private void RebuildLayout()
        {
            int first = IsDocked ? 1 : 0;
            int count = Tools.Length - first;
            int normalWidth = 2 * PaddingSize + 16;
            for (int i = first; i < Tools.Length; i++) normalWidth += StandardButtonWidth(Tools[i]) + 2;
            compact = Px(normalWidth) > hostWidth;
            int available = (int)(hostWidth / scale) - 2 * PaddingSize;
            int perRow = compact ? Math.Max(1, Math.Min(count, available / 22)) : count;
            int iconWidth = compact ? Math.Max(20, Math.Min(35, available / perRow - 2)) : 35;
            int rows = (count + perRow - 1) / perRow;
            buttons[0] = Rectangle.Empty;
            int x = PaddingSize;
            for (int i = first; i < Tools.Length; i++)
            {
                int row = (i - first) / perRow;
                if (compact && (i - first) % perRow == 0)
                    x = PaddingSize + Math.Max(0, perRow - (Tools.Length - i)) * (iconWidth + 2) / 2;
                Tool tool = Tools[i];
                if (!compact && (tool == Tool.Files || tool == Tool.Close)) x += 8;
                int width = compact ? iconWidth : StandardButtonWidth(tool);
                int y = row * (BarHeight - PaddingSize);
                buttons[i] = Rectangle.FromLTRB(Px(x), Px(y + 6), Px(x + width), Px(y + 42));
                x += width + 2;
            }

            int barWidth = compact ? 2 * PaddingSize + perRow * (iconWidth + 2) : normalWidth;
            Size = collapsed ? new Size(Px(BadgeSize), Px(BadgeSize))
                : new Size(Px(barWidth), Px(BarHeight + (rows - 1) * (BarHeight - PaddingSize)));
            if (!IsDocked)
            {
                floatingWindow.ClientSize = Size;
                using (var path = Shape(new RectangleF(0, 0, Width, Height), Px(collapsed ? 14 : 12)))
                {
                    var previous = floatingWindow.Region;
                    floatingWindow.Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            }
            Invalidate();
        }

        internal void ResetToTopCenter() { hostOffset = null; }

        internal void SetSessionsExpanded(bool expanded)
        {
            if (sessionsExpanded == expanded) return;
            sessionsExpanded = expanded;
            if (IsHandleCreated && !collapsed)
                AccessibilityNotifyClients(AccessibleEvents.StateChange, Array.IndexOf(Tools, Tool.Sessions) - 1);
            Invalidate();
        }

        internal void UpdateState(bool fullscreen)
        {
            Fullscreen = fullscreen;
            Invalidate();
        }

        internal void SetDocked(bool docked)
        {
            if (Parent != null && IsDocked == docked) return;
            bool restoreFocus = Focused;
            hover = pressed = focused = -1;
            moving = moved = false;
            Capture = false;
            Cursor = Cursors.Default;
            tooltip.HideAway();
            if (docked)
            {
                floatingWindow.Hide();
                band.Controls.Add(this);
                BackColor = band.BackColor;
                RefreshLayout();
                band.Visible = true;
            }
            else
            {
                band.Visible = false;
                floatingWindow.Controls.Add(this);
                Location = Point.Empty;
                BackColor = ChromeColors.Surface;
                floatingWindow.Opacity = collapsed ? BadgeOpacity : 1;
                RefreshLayout();
                floatingWindow.Show();
            }
            if (restoreFocus) Focus();
        }

        private void RefreshLayout()
        {
            hostWidth = Math.Max(1, IsDocked ? band.ClientSize.Width : host.ClientSize.Width);
            RebuildLayout();
            FollowHost();
        }

        private void OnBandSizeChanged(object sender, EventArgs e)
        {
            if (IsDocked) FollowHost();
        }

        private void OnHostDpiChanged(object sender, DpiChangedEventArgs e)
        {
            scale = Math.Max(1f, e.DeviceDpiNew / 96f);
            tooltip.HideAway();
            RefreshLayout();
        }

        internal void FollowHost()
        {
            int width = IsDocked ? band.ClientSize.Width : host.ClientSize.Width;
            if (width > 0 && width != hostWidth)
            {
                hostWidth = width;
                RebuildLayout();
            }
            if (IsDocked)
            {
                band.Height = Height + Px(8);
                Location = new Point(Math.Max(0, (band.ClientSize.Width - Width) / 2), Px(4));
            }
            else
            {
                Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
                Point offset = !Fullscreen && host.WindowState != FormWindowState.Maximized && hostOffset.HasValue
                    ? hostOffset.Value : new Point((bounds.Width - Width) / 2, Px(14));
                MoveInsideHost(new Point(bounds.X + offset.X, bounds.Y + offset.Y));
            }
            if (tooltip.Visible)
            {
                if (collapsed) tooltip.Reposition(RectangleToScreen(ClientRectangle));
                else if (hover >= 0) tooltip.Reposition(RectangleToScreen(buttons[hover]));
            }
        }

        private void MoveInsideHost(Point location)
        {
            Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
            location.X = Math.Max(bounds.Left, Math.Min(location.X, bounds.Right - Width));
            location.Y = Math.Max(bounds.Top, Math.Min(location.Y, bounds.Bottom - Height));
            if (floatingWindow.Location != location) floatingWindow.Location = location;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            if (Width < 3 || Height < 3) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = Shape(new RectangleF(1, 1, Width - 2, Height - 2), Px(collapsed ? 14 : 12)))
            using (var brush = new LinearGradientBrush(ClientRectangle, ChromeColors.Elevated, ChromeColors.Canvas, 55f))
            using (var pen = new Pen(collapsed ? ChromeColors.Accent : ChromeColors.Border))
            {
                graphics.FillPath(brush, outline);
                graphics.DrawPath(pen, outline);
            }
            if (collapsed)
            {
                PaintIcon(graphics, Tool.Display, Width / 2f, Height / 2f, ChromeColors.Accent);
                return;
            }
            for (int i = IsDocked ? 1 : 0; i < Tools.Length; i++)
                PaintButton(graphics, i);
        }

        private void PaintButton(Graphics graphics, int index)
        {
            Tool tool = Tools[index];
            Rectangle bounds = buttons[index];
            bool highlighted = index == hover || index == focused;
            Color text = ChromeColors.Text;
            Color fill = Color.Empty;
            bool toggle = IsToggle(tool);
            bool? enabled = toggle ? ToggleState(tool) : null;
            if (tool == Tool.Files || enabled == true || (tool == Tool.Sessions && sessionsExpanded))
            {
                fill = ChromeColors.Selection;
                text = SystemInformation.HighContrast ? ChromeColors.SelectedText : ChromeColors.Accent;
            }
            else if (toggle) text = ChromeColors.Muted;
            else if (tool == Tool.Dock && IsDocked) text = ChromeColors.Accent;
            if ((highlighted || index == pressed) && (!toggle || enabled.HasValue))
            {
                fill = tool == Tool.Close && !SystemInformation.HighContrast ? Color.FromArgb(133, 52, 70) : ChromeColors.Hover;
                text = ChromeColors.SelectedText;
            }

            if (!fill.IsEmpty)
            {
                using (var path = Shape(bounds, Px(7)))
                using (var brush = new SolidBrush(fill))
                    graphics.FillPath(brush, path);
            }
            if (!compact && (tool == Tool.Files || tool == Tool.Close))
            {
                using (var pen = new Pen(ChromeColors.Border))
                    graphics.DrawLine(pen, bounds.Left - Px(5), bounds.Top + Px(5),
                        bounds.Left - Px(5), bounds.Bottom - Px(5));
            }

            string label = compact ? "" : TextLabel(tool);
            float x = label.Length == 0 ? bounds.Left + bounds.Width / 2f : bounds.Left + Px(15);
            PaintIcon(graphics, tool, x, bounds.Top + bounds.Height / 2f, text);
            if (toggle && enabled.HasValue)
            {
                float y = bounds.Bottom - Px(5);
                using (var pen = new Pen(text, Px(1)))
                using (var brush = new SolidBrush(text))
                {
                    var marker = new RectangleF(x - Px(2), y - Px(2), Px(4), Px(4));
                    if (enabled.Value) graphics.FillEllipse(brush, marker);
                    else graphics.DrawEllipse(pen, marker);
                }
            }
            if (label.Length > 0)
            {
                var state = graphics.Save();
                graphics.TranslateTransform(bounds.Left + Px(28), bounds.Top);
                graphics.ScaleTransform(scale, scale);
                using (var brush = new SolidBrush(text))
                using (var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    graphics.DrawString(label, labelFont, brush,
                        new RectangleF(0, 0, bounds.Width / scale - 31, bounds.Height / scale), format);
                graphics.Restore(state);
            }
        }

        private void PaintIcon(Graphics g, Tool tool, float x, float y, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(scale, scale);
            using (var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (tool)
                {
                    case Tool.Move:
                        for (int row = -1; row <= 1; row++)
                            for (int column = -1; column <= 1; column += 2)
                                g.FillEllipse(brush, column * 2 - 1, row * 4 - 1, 2, 2);
                        break;
                    case Tool.Display:
                        g.DrawRectangle(pen, -8, -7, 16, 11);
                        g.DrawLine(pen, 0, 4, 0, 7);
                        g.DrawLine(pen, -4, 7, 4, 7);
                        break;
                    case Tool.Resolution:
                        g.DrawRectangle(pen, -7, -7, 14, 14);
                        g.DrawLine(pen, -7, 0, 7, 0);
                        g.DrawLine(pen, 0, -7, 0, 7);
                        break;
                    case Tool.Fullscreen:
                        if (Fullscreen)
                        {
                            g.DrawLines(pen, new[] { new Point(-7, 2), new Point(-7, -7), new Point(2, -7) });
                            g.DrawRectangle(pen, -3, -3, 10, 10);
                        }
                        else
                        {
                            for (int side = -1; side <= 1; side += 2)
                                for (int vertical = -1; vertical <= 1; vertical += 2)
                                    g.DrawLines(pen, new[] { new Point(side * 3, vertical * 7),
                                        new Point(side * 7, vertical * 7), new Point(side * 7, vertical * 3) });
                        }
                        break;
                    case Tool.Files:
                        g.DrawPolygon(pen, new[] { new Point(-8, -6), new Point(-2, -6), new Point(0, -3),
                            new Point(8, -3), new Point(8, 6), new Point(-8, 6) });
                        break;
                    case Tool.SendFiles:
                    case Tool.ReceiveFiles:
                        int direction = tool == Tool.SendFiles ? 1 : -1;
                        g.DrawLine(pen, -6 * direction, 0, 6 * direction, 0);
                        g.DrawLines(pen, new[] { new Point(direction, -5), new Point(6 * direction, 0), new Point(direction, 5) });
                        break;
                    case Tool.RdpClipboard:
                        g.DrawRectangle(pen, -6, -6, 12, 13);
                        g.DrawRectangle(pen, -3, -8, 6, 4);
                        g.DrawLine(pen, -3, 0, 3, 0);
                        g.DrawLine(pen, -3, 3, 3, 3);
                        break;
                    case Tool.Sessions:
                        g.DrawRectangle(pen, -8, -6, 16, 12);
                        g.DrawLine(pen, -3, -6, -3, 6);
                        g.DrawLine(pen, 0, -2, 4, -2);
                        g.DrawLine(pen, 0, 2, 4, 2);
                        break;
                    case Tool.Reconnect:
                        g.DrawArc(pen, -7, -7, 14, 14, 5, 300);
                        g.DrawLines(pen, new[] { new Point(7, -7), new Point(7, -2), new Point(2, -2) });
                        break;
                    case Tool.More:
                        for (int i = -1; i <= 1; i++) g.FillEllipse(brush, i * 5 - 1.4f, -1.4f, 2.8f, 2.8f);
                        break;
                    case Tool.Dock:
                        g.DrawRectangle(pen, -8, -7, 16, 14);
                        g.DrawLine(pen, -8, -3, 8, -3);
                        int arrow = IsDocked ? 1 : -1;
                        g.DrawLine(pen, 0, 2 - arrow * 3, 0, 2 + arrow * 3);
                        g.DrawLines(pen, new[] { new Point(-2, 2 + arrow), new Point(0, 2 + arrow * 3), new Point(2, 2 + arrow) });
                        break;
                    case Tool.Collapse:
                        g.DrawLines(pen, new[] { new Point(-6, 2), new Point(0, -4), new Point(6, 2) });
                        break;
                    case Tool.Close:
                        g.DrawLine(pen, -5, -5, 5, 5);
                        g.DrawLine(pen, -5, 5, 5, -5);
                        break;
                }
            }
            g.Restore(state);
        }

        private static GraphicsPath Shape(RectangleF bounds, int radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void SetCollapsed(bool value)
        {
            int previousWidth = Width;
            collapsed = value;
            if (!IsDocked) floatingWindow.Opacity = value ? BadgeOpacity : 1;
            hover = pressed = focused = -1;
            tooltip.HideAway();
            RebuildLayout();
            if (!IsDocked && hostOffset.HasValue)
            {
                Point offset = hostOffset.Value;
                hostOffset = new Point(offset.X + (previousWidth - Width) / 2, offset.Y);
            }
            FollowHost();
            AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            pressed = HitTest(e.Location);
            moving = !IsDocked && (collapsed || (pressed >= 0 && Tools[pressed] == Tool.Move));
            moved = false;
            dragOrigin = floatingWindow.Location;
            cursorOrigin = Control.MousePosition;
            tooltip.HideAway();
            Capture = collapsed || pressed >= 0;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (moving)
            {
                Point cursor = Control.MousePosition;
                int dx = cursor.X - cursorOrigin.X, dy = cursor.Y - cursorOrigin.Y;
                moved = moved || Math.Abs(dx) > Px(3) || Math.Abs(dy) > Px(3);
                if (moved) MoveInsideHost(new Point(dragOrigin.X + dx, dragOrigin.Y + dy));
                return;
            }
            if (collapsed) return;
            int index = HitTest(e.Location);
            if (index == hover) return;
            hover = index;
            Cursor = index < 0 ? Cursors.Default : (Tools[index] == Tool.Move ? Cursors.SizeAll : Cursors.Hand);
            if (index >= 0) tooltip.ShowFor(RectangleToScreen(buttons[index]), ToolName(Tools[index]));
            else tooltip.HideAway();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!collapsed) return;
            if (!IsDocked) floatingWindow.Opacity = 1;
            Cursor = Cursors.Hand;
            if (!moving) tooltip.ShowFor(RectangleToScreen(ClientRectangle), ExpandName);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            tooltip.HideAway();
            if (collapsed && !IsDocked) floatingWindow.Opacity = BadgeOpacity;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool expand = collapsed && Capture && !moved && ClientRectangle.Contains(e.Location);
            int activated = !moved && pressed == HitTest(e.Location) ? pressed : -1;
            if (moved && !IsDocked)
            {
                Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
                hostOffset = new Point(floatingWindow.Left - bounds.Left, floatingWindow.Top - bounds.Top);
            }
            moving = false;
            pressed = -1;
            Capture = false;
            Invalidate();
            if (expand) SetCollapsed(false);
            else if (activated >= 0) Activate(activated);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            moving = false;
            pressed = -1;
            Invalidate();
        }

        private void Activate(int index)
        {
            tooltip.HideAway();
            Tool tool = Tools[index];
            if (IsToggle(tool))
            {
                if (!ToggleState(tool).HasValue) return;
                if (tool == Tool.RdpClipboard)
                {
                    if (ToggleRdpClipboard != null) ToggleRdpClipboard();
                }
                else if (ToggleFileTransfer != null) ToggleFileTransfer(tool == Tool.SendFiles);
                AccessibilityNotifyClients(AccessibleEvents.StateChange, index - 1);
                Invalidate();
                return;
            }
            Rectangle anchor = RectangleToScreen(buttons[index]);
            switch (tool)
            {
                case Tool.Display: if (ShowDisplayModeMenu != null) ShowDisplayModeMenu(anchor); break;
                case Tool.Resolution: if (ShowResMenu != null) ShowResMenu(anchor); break;
                case Tool.Fullscreen: if (ToggleFullscreen != null) ToggleFullscreen(); break;
                case Tool.Files: if (ShowFilesMenu != null) ShowFilesMenu(anchor); break;
                case Tool.Sessions: if (ToggleSessions != null) ToggleSessions(); break;
                case Tool.Reconnect: if (ReconnectSession != null) ReconnectSession(); break;
                case Tool.More: if (ShowMoreMenu != null) ShowMoreMenu(anchor); break;
                case Tool.Dock: if (ToggleDock != null) ToggleDock(); break;
                case Tool.Collapse: SetCollapsed(true); break;
                case Tool.Close: host.Close(); break;
            }
        }

        private int HitTest(Point point)
        {
            if (collapsed) return -1;
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i].Contains(point)) return i;
            return -1;
        }

        protected override bool IsInputKey(Keys key)
        {
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down
                || key == Keys.Enter || key == Keys.Space || base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                if (collapsed) SetCollapsed(false);
                else if (focused >= 0) Activate(focused);
            }
            else if (!collapsed && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up))
                focused = focused <= 1 ? Tools.Length - 1 : focused - 1;
            else if (!collapsed && (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down))
                focused = focused < 1 || focused == Tools.Length - 1 ? 1 : focused + 1;
            else return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            focused = -1;
            Invalidate();
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ToolbarAccessibility(this); }

        private sealed class ToolbarAccessibility : ControlAccessibleObject
        {
            private readonly ToolbarControl toolbar;
            public ToolbarAccessibility(ToolbarControl owner) : base(owner) { toolbar = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.ToolBar; } }
            public override string Name { get { return "RayXDesktop"; } }
            public override int GetChildCount() { return toolbar.collapsed ? 1 : Tools.Length - 1; }
            public override AccessibleObject GetChild(int index)
            {
                return index < 0 || index >= GetChildCount() ? null : new ToolbarButtonAccessibility(toolbar, toolbar.collapsed ? -1 : index + 1);
            }
        }

        private sealed class ToolbarButtonAccessibility : AccessibleObject
        {
            private readonly ToolbarControl toolbar;
            private readonly int index;
            public ToolbarButtonAccessibility(ToolbarControl owner, int index) { toolbar = owner; this.index = index; }
            public override string Name { get { return index < 0 ? toolbar.ExpandName : toolbar.ToolName(Tools[index]); } }
            public override AccessibleRole Role { get { return index >= 0 && (IsToggle(Tools[index]) || Tools[index] == Tool.Sessions)
                ? AccessibleRole.CheckButton : AccessibleRole.PushButton; } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = base.State;
                    if (index >= 0 && Tools[index] == Tool.Sessions)
                        return toolbar.sessionsExpanded ? state | AccessibleStates.Checked : state;
                    if (index < 0 || !IsToggle(Tools[index])) return state;
                    bool? value = toolbar.ToggleState(Tools[index]);
                    if (!value.HasValue) return state | AccessibleStates.Unavailable;
                    return value.Value ? state | AccessibleStates.Checked : state;
                }
            }
            public override Rectangle Bounds { get { return toolbar.RectangleToScreen(index < 0 ? toolbar.ClientRectangle : toolbar.buttons[index]); } }
            public override void DoDefaultAction()
            {
                if (index < 0) toolbar.SetCollapsed(false);
                else toolbar.Activate(index);
            }
        }
    }
}
