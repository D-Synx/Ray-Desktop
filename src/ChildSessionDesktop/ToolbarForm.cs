using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal sealed class ToolbarForm : Form
    {
        private enum Tool { Move, Display, Resolution, Fullscreen, Files, More, Collapse, Close }

        private static readonly Tool[] Tools = {
            Tool.Move, Tool.Display, Tool.Resolution, Tool.Fullscreen,
            Tool.Files, Tool.More, Tool.Collapse, Tool.Close
        };

        private static readonly Color Surface = Color.FromArgb(20, 30, 43);
        private static readonly Color Foreground = Color.FromArgb(236, 244, 250);
        private static readonly Color Accent = Color.FromArgb(130, 229, 215);
        private static readonly Color Border = Color.FromArgb(59, 79, 100);
        private const double BadgeOpacity = 0.82;
        private const int BarHeight = 48, BadgeSize = 42, PaddingSize = 6;
        private readonly Rectangle[] buttons = new Rectangle[Tools.Length];
        private readonly TooltipForm tooltip;
        private readonly Font labelFont;
        private readonly bool french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

        private Form host;
        private Point? hostOffset;
        private int hover = -1, pressed = -1, focused = -1;
        private Point dragOrigin;
        private Point cursorOrigin;
        private bool moving, moved, collapsed;
        private float scale = 1;

        internal Func<bool> ToggleFullscreen;
        internal Func<Rectangle, bool> ShowResMenu;
        internal Func<Rectangle, bool> ShowDisplayModeMenu;
        internal Func<Rectangle, bool> ShowMoreMenu;
        internal Action TransferFiles;
        internal bool IsDynamicMode;
        internal bool Fullscreen;

        internal ToolbarForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Surface;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                scale = Math.Max(1f, graphics.DpiX / 96f);
            labelFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            tooltip = new TooltipForm();
            RebuildLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                tooltip.Dispose();
                labelFont.Dispose();
                var oldRegion = Region;
                Region = null;
                if (oldRegion != null) oldRegion.Dispose();
            }
            base.Dispose(disposing);
        }

        private int Px(int logical) { return (int)Math.Round(logical * scale); }

        private string TextLabel(Tool tool)
        {
            switch (tool)
            {
                case Tool.Display: return french ? "Affichage" : Strings.T("btn_display");
                case Tool.Resolution: return french ? "Résolution" : Strings.T("btn_resolution");
                case Tool.Files: return french ? "Fichiers" : "Files";
                default: return "";
            }
        }

        private string ToolName(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return french ? "Déplacer la barre" : Strings.T("btn_drag");
                case Tool.Display:
                    if (french) return IsDynamicMode ? "Affichage dynamique" : "Affichage fixe";
                    return Strings.T(IsDynamicMode ? "tip_display_dynamic" : "tip_display_fixed");
                case Tool.Resolution: return TextLabel(tool);
                case Tool.Fullscreen:
                    if (french) return Fullscreen ? "Quitter le plein écran" : "Plein écran";
                    return Fullscreen ? "Exit full screen" : Strings.T("btn_fullscreen");
                case Tool.Files: return french ? "Transférer les fichiers copiés" : "Transfer copied files";
                case Tool.More: return french ? "Plus d’options" : Strings.T("btn_more");
                case Tool.Collapse: return french ? "Réduire la barre" : Strings.T("tip_collapse");
                case Tool.Close: return french ? "Fermer la session" : Strings.T("btn_exit");
                default: return "";
            }
        }

        private string ExpandName { get { return french ? "Afficher la barre" : Strings.T("tip_expand"); } }

        private void RebuildLayout()
        {
            int x = PaddingSize;
            for (int i = 0; i < Tools.Length; i++)
            {
                Tool tool = Tools[i];
                if (tool == Tool.Files || tool == Tool.Close) x += 8;
                string label = TextLabel(tool);
                int width = label.Length == 0 ? 35 : 103;
                if (tool == Tool.Files) width = 80;
                buttons[i] = Rectangle.FromLTRB(Px(x), Px(6), Px(x + width), Px(42));
                x += width + 2;
            }

            Size = collapsed ? new Size(Px(BadgeSize), Px(BadgeSize)) : new Size(Px(x + PaddingSize), Px(BarHeight));
            using (var path = Shape(new RectangleF(0, 0, Width, Height), Px(collapsed ? 14 : 12)))
            {
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
            Invalidate();
        }

        internal void ResetToTopCenter() { hostOffset = null; }

        internal void UpdateState(bool fullscreen)
        {
            Fullscreen = fullscreen;
            Invalidate();
        }

        internal void FollowHost(Form newHost)
        {
            host = newHost;
            Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
            Point offset = !Fullscreen && host.WindowState != FormWindowState.Maximized && hostOffset.HasValue
                ? hostOffset.Value : new Point((bounds.Width - Width) / 2, Px(14));
            MoveInsideHost(new Point(bounds.X + offset.X, bounds.Y + offset.Y));
            if (tooltip.Visible && hover >= 0 && !collapsed)
                tooltip.Reposition(RectangleToScreen(buttons[hover]));
        }

        private void MoveInsideHost(Point location)
        {
            if (host != null)
            {
                Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
                location.X = Math.Max(bounds.Left, Math.Min(location.X, bounds.Right - Width));
                location.Y = Math.Max(bounds.Top, Math.Min(location.Y, bounds.Bottom - Height));
            }
            if (Location != location) Location = location;
        }

        protected override void WndProc(ref Message message)
        {
            const int dpiChanged = 0x02E0;
            if (message.Msg == dpiChanged)
            {
                int dpi = (ushort)(message.WParam.ToInt64() & 0xFFFF);
                if (dpi > 0)
                {
                    scale = Math.Max(1f, dpi / 96f);
                    tooltip.HideAway();
                    RebuildLayout();
                    if (host != null) FollowHost(host);
                }
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = Shape(new RectangleF(1, 1, Width - 2, Height - 2), Px(collapsed ? 14 : 12)))
            using (var brush = new SolidBrush(Surface))
            using (var pen = new Pen(collapsed ? Accent : Border))
            {
                graphics.FillPath(brush, outline);
                graphics.DrawPath(pen, outline);
            }
            if (collapsed)
            {
                PaintIcon(graphics, Tool.Display, Width / 2f, Height / 2f, Accent);
                return;
            }
            for (int i = 0; i < Tools.Length; i++)
                PaintButton(graphics, i);
        }

        private void PaintButton(Graphics graphics, int index)
        {
            Tool tool = Tools[index];
            Rectangle bounds = buttons[index];
            bool highlighted = index == hover || index == focused;
            Color text = tool == Tool.Files ? Accent : Foreground;
            Color fill = Color.Empty;
            if (tool == Tool.Files) fill = Color.FromArgb(25, 65, 65);
            if (highlighted || index == pressed)
                fill = tool == Tool.Close ? Color.FromArgb(123, 53, 64) : Color.FromArgb(53, 73, 94);

            if (!fill.IsEmpty)
            {
                using (var path = Shape(bounds, Px(7)))
                using (var brush = new SolidBrush(fill))
                    graphics.FillPath(brush, path);
            }
            if (tool == Tool.Files || tool == Tool.Close)
            {
                using (var pen = new Pen(Border))
                    graphics.DrawLine(pen, bounds.Left - Px(5), bounds.Top + Px(5),
                        bounds.Left - Px(5), bounds.Bottom - Px(5));
            }

            string label = TextLabel(tool);
            float x = label.Length == 0 ? bounds.Left + bounds.Width / 2f : bounds.Left + Px(15);
            PaintIcon(graphics, tool, x, bounds.Top + bounds.Height / 2f, text);
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
                    case Tool.More:
                        for (int i = -1; i <= 1; i++) g.FillEllipse(brush, i * 5 - 1.4f, -1.4f, 2.8f, 2.8f);
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
            int center = Left + Width / 2;
            collapsed = value;
            Opacity = value ? BadgeOpacity : 1;
            hover = pressed = focused = -1;
            tooltip.HideAway();
            RebuildLayout();
            if (host == null) return;
            if (hostOffset.HasValue)
            {
                Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
                hostOffset = new Point(center - Width / 2 - bounds.Left, hostOffset.Value.Y);
            }
            FollowHost(host);
            AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = HitTest(e.Location);
            moving = collapsed || (pressed >= 0 && Tools[pressed] == Tool.Move);
            moved = false;
            dragOrigin = Location;
            cursorOrigin = Control.MousePosition;
            tooltip.HideAway();
            Capture = moving || pressed >= 0;
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
            Opacity = 1;
            Cursor = Cursors.Hand;
            if (!moving) tooltip.ShowFor(Bounds, ExpandName);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            tooltip.HideAway();
            if (collapsed) Opacity = BadgeOpacity;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool expand = collapsed && moving && !moved && ClientRectangle.Contains(e.Location);
            int activated = !moved && pressed == HitTest(e.Location) ? pressed : -1;
            if (moved && host != null)
            {
                Rectangle bounds = host.RectangleToScreen(host.ClientRectangle);
                hostOffset = new Point(Left - bounds.Left, Top - bounds.Top);
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
            Rectangle anchor = RectangleToScreen(buttons[index]);
            switch (Tools[index])
            {
                case Tool.Display: if (ShowDisplayModeMenu != null) ShowDisplayModeMenu(anchor); break;
                case Tool.Resolution: if (ShowResMenu != null) ShowResMenu(anchor); break;
                case Tool.Fullscreen: if (ToggleFullscreen != null) ToggleFullscreen(); break;
                case Tool.Files: if (TransferFiles != null) TransferFiles(); break;
                case Tool.More: if (ShowMoreMenu != null) ShowMoreMenu(anchor); break;
                case Tool.Collapse: SetCollapsed(true); break;
                case Tool.Close: if (host != null) host.Close(); break;
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
            private readonly ToolbarForm toolbar;
            public ToolbarAccessibility(ToolbarForm owner) : base(owner) { toolbar = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.ToolBar; } }
            public override string Name { get { return "Ray Desktop"; } }
            public override int GetChildCount() { return toolbar.collapsed ? 1 : Tools.Length - 1; }
            public override AccessibleObject GetChild(int index)
            {
                return index < 0 || index >= GetChildCount() ? null : new ToolbarButtonAccessibility(toolbar, toolbar.collapsed ? -1 : index + 1);
            }
        }

        private sealed class ToolbarButtonAccessibility : AccessibleObject
        {
            private readonly ToolbarForm toolbar;
            private readonly int index;
            public ToolbarButtonAccessibility(ToolbarForm owner, int index) { toolbar = owner; this.index = index; }
            public override string Name { get { return index < 0 ? toolbar.ExpandName : toolbar.ToolName(Tools[index]); } }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override Rectangle Bounds { get { return index < 0 ? toolbar.Bounds : toolbar.RectangleToScreen(toolbar.buttons[index]); } }
            public override void DoDefaultAction()
            {
                if (index < 0) toolbar.SetCollapsed(false);
                else toolbar.Activate(index);
            }
        }
    }
}
