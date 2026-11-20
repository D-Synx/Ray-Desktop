using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal sealed class LogPane : Panel
    {
        private const int EdgeWidth = 4;
        private readonly string path;
        private readonly Button edge;
        private readonly Panel body;
        private readonly TextBox viewer;
        private readonly Timer refreshTimer;
        private readonly ToolTip edgeHint;

        internal bool EdgeEnabled { get; private set; }
        internal bool IsOpen { get; private set; }

        internal LogPane(string path, bool edgeEnabled)
        {
            this.path = path;
            EdgeEnabled = edgeEnabled;
            Dock = DockStyle.Right;
            Width = EdgeWidth;
            BackColor = Color.FromArgb(23, 34, 50);

            edge = MakeButton(string.Empty, Strings.T("log_show"));
            edge.Dock = DockStyle.Fill;
            edge.BackColor = Color.FromArgb(69, 143, 155);
            edge.Cursor = Cursors.Hand;
            edge.Click += delegate { SetOpen(true); };
            edgeHint = new ToolTip();
            edgeHint.SetToolTip(edge, Strings.T("log_show"));

            body = new Panel { Dock = DockStyle.Fill, Visible = false };
            var heading = new Panel { Dock = DockStyle.Top, Height = 32 };
            var title = new Label
            {
                Text = Strings.T("log_title"), Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, AutoEllipsis = true
            };
            var collapse = MakeButton("›", Strings.T("log_collapse"));
            collapse.Dock = DockStyle.Right;
            collapse.Width = 32;
            collapse.Click += delegate { SetOpen(false); };
            heading.Controls.Add(title);
            heading.Controls.Add(collapse);

            viewer = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(13, 19, 29), ForeColor = Color.Gainsboro,
                AccessibleName = Strings.T("log_title")
            };
            body.Controls.Add(viewer);
            body.Controls.Add(heading);
            Controls.Add(edge);
            Controls.Add(body);

            refreshTimer = new Timer { Interval = 900 };
            refreshTimer.Tick += delegate { RefreshLog(); };
            Visible = EdgeEnabled;
        }

        private static Button MakeButton(string text, string accessibleName)
        {
            var button = new Button
            {
                Text = text, AccessibleName = accessibleName, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 49, 68), ForeColor = Color.White
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        internal void SetEdgeEnabled(bool enabled)
        {
            EdgeEnabled = enabled;
            if (!enabled) SetOpen(false);
            else Visible = true;
        }

        internal void SetOpen(bool open)
        {
            IsOpen = open;
            edge.Visible = !open;
            body.Visible = open;
            ResizeToFit(Parent == null ? 1024 : Parent.ClientSize.Width);
            Visible = open || EdgeEnabled;
            refreshTimer.Enabled = open;
            if (open)
            {
                RefreshLog();
                viewer.Focus();
            }
            else viewer.SelectionLength = 0;
        }

        internal void ResizeToFit(int availableWidth)
        {
            Width = IsOpen ? Math.Min(440, Math.Max(1, availableWidth * 2 / 5)) : EdgeWidth;
        }

        private void RefreshLog()
        {
            if (!Visible || viewer.SelectionLength != 0) return;

            string content;
            try { content = ReadRecentText(path); }
            catch (FileNotFoundException) { content = string.Empty; }
            catch (DirectoryNotFoundException) { content = string.Empty; }
            catch (IOException) { content = Strings.T("log_unavailable"); }
            catch (UnauthorizedAccessException) { content = Strings.T("log_unavailable"); }
            if (content.Length == 0) content = Strings.T("log_empty");
            if (string.Equals(viewer.Text, content, StringComparison.Ordinal)) return;
            viewer.Text = content;
            viewer.SelectionStart = viewer.TextLength;
            viewer.ScrollToCaret();
        }

        internal static string ReadRecentText(string path, int maxBytes = 32 * 1024)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, stream.Length - maxBytes);
                bool partialLine = false;
                if (start > 0)
                {
                    stream.Position = start - 1;
                    partialLine = stream.ReadByte() != '\n';
                }
                using (var reader = new StreamReader(stream, Encoding.UTF8, start == 0))
                {
                    if (partialLine) reader.ReadLine();
                    return reader.ReadToEnd();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                edgeHint.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
