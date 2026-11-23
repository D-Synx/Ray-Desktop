using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal sealed class SessionWorkspace : Panel
    {
        private sealed class Slot
        {
            internal Panel View;
            internal string Name;
            internal string Status;
            internal RemoteDesktop Remote;
            internal ChildSessionControl PreviewSurface;
            internal readonly string ThumbnailId = Guid.NewGuid().ToString("N");
            internal Bitmap Preview;
            internal int PreviewAttempts;
        }

        private readonly Panel sidebar;
        private readonly Panel surface;
        private readonly Panel toolbarBand;
        private readonly FlowLayoutPanel list;
        private readonly Button splitButton;
        private readonly Slot local;
        private readonly List<Slot> slots = new List<Slot>();
        private readonly SessionThumbnails thumbnails = new SessionThumbnails();
        private readonly GpuPreview previewRenderer;
        private readonly Timer previewTimer;
        private Slot selected, previous, left, right, startupSlot;
        private int userSelection;
        private bool capturingPreview;

        internal event Action AddRequested;
        internal event Action LocalConnectRequested;
        internal event Action LocalViewportChanged;
        internal event Action SelectionChanged;
        internal event Action SidebarChanged;
        internal RemoteDesktop SelectedRemote { get { return selected.Remote; } }
        internal string SelectedName { get { return selected.Name; } }
        internal string SelectedStatus { get { return selected.Status; } }
        internal bool HasRemoteConnections { get { return slots.Count > 1; } }
        internal bool IsExpanded { get { return sidebar.Visible; } }
        internal Panel ToolbarBand { get { return toolbarBand; } }
        internal string ThumbnailFolder { get { return thumbnails.Folder; } }

        internal SessionWorkspace(ChildSessionControl localDesktop, string localAccount, PreviewEngine engine)
        {
            previewRenderer = new GpuPreview(engine);
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(13, 19, 29);
            surface = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            Controls.Add(surface);

            sidebar = new Panel { Dock = DockStyle.Left, Width = 204, BackColor = Color.FromArgb(23, 34, 50), Visible = false };
            Controls.Add(sidebar);
            toolbarBand = new Panel { Dock = DockStyle.Top, BackColor = sidebar.BackColor, Visible = false };
            Controls.Add(toolbarBand);
            list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(8, 4, 8, 4)
            };
            var add = MakeButton(Strings.T("sessions_add"));
            add.Dock = DockStyle.Bottom;
            add.Click += delegate { if (AddRequested != null) AddRequested(); };
            splitButton = MakeButton(Strings.T("sessions_split"));
            splitButton.Dock = DockStyle.Bottom;
            splitButton.Click += delegate { ToggleSplit(); };
            var collapse = MakeButton(Strings.T("sessions_hide"));
            collapse.Dock = DockStyle.Top;
            collapse.Click += delegate { ToggleSidebar(); };
            sidebar.Controls.Add(list);
            sidebar.Controls.Add(splitButton);
            sidebar.Controls.Add(add);
            sidebar.Controls.Add(collapse);

            local = AddSlot(Strings.T("sessions_local", localAccount), localDesktop, localDesktop, null);
            local.View.SizeChanged += delegate { if (LocalViewportChanged != null) LocalViewportChanged(); };
            selected = left = local;
            previewTimer = new Timer { Interval = 900 };
            previewTimer.Tick += delegate
            {
                foreach (Slot slot in slots)
                {
                    if (capturingPreview) break;
                    if (slot.PreviewAttempts <= 0 || !slot.View.Visible) continue;
                    slot.PreviewAttempts--;
                    StartPreview(slot);
                    break;
                }
                UpdatePreviewTimer();
            };
            surface.SizeChanged += delegate { Arrange(); };
            RebuildList();
            Arrange();
        }

        private static Button MakeButton(string text)
        {
            var button = new Button
            {
                Text = text, TextAlign = ContentAlignment.MiddleLeft, Height = 38,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(34, 49, 68),
                UseMnemonic = false
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private Slot AddSlot(string name, Control desktop, ChildSessionControl previewSurface, RemoteDesktop remote)
        {
            var slot = new Slot
            {
                Name = name, Status = Strings.T("status_not_connected"), Remote = remote, PreviewSurface = previewSurface,
                View = new Panel { BackColor = Color.Black, Visible = false }
            };
            slots.Add(slot);
            slot.View.Controls.Add(desktop);
            surface.Controls.Add(slot.View);
            desktop.Enter += delegate
            {
                if (slot == left || slot == right) Select(slot);
            };
            return slot;
        }

        internal bool SelectExisting(RdpEndpoint endpoint, string account)
        {
            foreach (var slot in slots)
            {
                if (slot.Remote != null && endpoint.Port == slot.Remote.Endpoint.Port
                    && string.Equals(endpoint.Host, slot.Remote.Endpoint.Host, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(account, slot.Remote.Account, StringComparison.OrdinalIgnoreCase))
                {
                    if (!sidebar.Visible) ToggleSidebar();
                    Select(slot);
                    return true;
                }
            }
            return false;
        }

        internal void AddRemote(RemoteDesktop remote, bool activate = true)
        {
            var slot = AddSlot(remote.Endpoint.Display + " · " + remote.Account, remote, remote.PreviewSurface, remote);
            remote.StatusChanged += delegate
            {
                slot.Status = remote.Status;
                RefreshList();
            };
            remote.DesktopOpened += delegate { QueuePreview(slot); };
            if (!activate) { Arrange(); return; }
            if (!sidebar.Visible) ToggleSidebar();
            Select(slot);
        }

        internal async Task<bool> ReconnectAtStartupAsync(IEnumerable<RemoteDesktop> remotes)
        {
            int initialSelection = userSelection;
            foreach (RemoteDesktop remote in remotes)
            {
                if (IsDisposed || Disposing) return false;
                Slot slot = slots.Find(item => item.Remote == remote);
                if (slot == null) continue;

                startupSlot = slot;
                Arrange();
                try
                {
                    if (await remote.ConnectAtStartupAsync())
                    {
                        await Task.Delay(250);
                        if (!IsDisposed && !Disposing && !remote.IsDisposed) StartPreview(slot);
                    }
                    else await Task.Yield();
                }
                finally
                {
                    startupSlot = null;
                    if (!IsDisposed && !Disposing) Arrange();
                }
            }

            if (IsDisposed || Disposing) return false;
            Slot ready = slots.Find(slot => slot.Remote != null && slot.Remote.IsReady);
            if (ready != null && userSelection == initialSelection && selected == local) Select(ready);
            return ready != null;
        }

        internal void CloseSelectedView()
        {
            if (selected == local) return;
            Slot remove = selected;
            Slot next = right == null ? local : left == remove ? right : left;
            slots.Remove(remove);
            if (previous == remove) previous = null;
            right = null;
            left = selected = next;
            remove.View.Dispose();
            RebuildList();
            if (remove.Preview != null) remove.Preview.Dispose();
            try { thumbnails.Remove(remove.ThumbnailId); }
            catch (IOException ex) { Program.Log("Could not remove a session preview: " + ex.GetType().Name); }
            catch (UnauthorizedAccessException ex) { Program.Log("Could not remove a session preview: " + ex.GetType().Name); }
            Arrange();
        }

        internal void UpdateLocalStatus(string message)
        {
            if (local.Status == message) return;
            local.Status = message;
            RefreshList();
        }

        internal void QueueLocalPreview() { QueuePreview(local); }
        internal void QueueSelectedPreview() { QueuePreview(selected); }
        internal GpuPreview PreviewRenderer { get { return previewRenderer; } }

        private void QueuePreview(Slot slot)
        {
            slot.PreviewAttempts = 5;
            UpdatePreviewTimer();
        }

        private void UpdatePreviewTimer()
        {
            if (IsDisposed || Disposing) return;
            previewTimer.Enabled = slots.Exists(slot => slot.PreviewAttempts > 0 && slot.View.Visible);
        }

        internal void ToggleSidebar()
        {
            sidebar.Visible = !sidebar.Visible;
            if (SidebarChanged != null) SidebarChanged();
            Arrange();
        }

        private void Select(Slot slot)
        {
            if (slot == selected) return;
            StartPreview(selected);
            if (right == null) left = slot;
            else if (slot != left && slot != right)
            {
                if (selected == left) left = slot;
                else right = slot;
            }
            previous = selected;
            selected = slot;
            Arrange();
        }

        private void ToggleSplit()
        {
            if (right != null)
            {
                StartPreview(left == selected ? right : left);
                left = selected;
                right = null;
            }
            else if (slots.Count > 1)
            {
                left = selected;
                right = previous != null && previous != selected ? previous : selected == local ? slots[1] : local;
            }
            Arrange();
        }

        private void RebuildList()
        {
            while (list.Controls.Count > 0) list.Controls[0].Dispose();
            foreach (Slot slot in slots)
            {
                var row = MakeButton("");
                row.Width = 165;
                row.Height = 54;
                row.TextImageRelation = TextImageRelation.ImageAboveText;
                row.ImageAlign = ContentAlignment.TopCenter;
                row.Tag = slot;
                row.AutoEllipsis = true;
                row.Click += delegate
                {
                    userSelection++;
                    Select(slot);
                    if (slot.Remote != null && slot.Remote.CanConnectFromList) slot.Remote.Connect();
                    else if (slot == local && LocalConnectRequested != null) LocalConnectRequested();
                };
                list.Controls.Add(row);
            }
            RefreshList();
        }

        private void RefreshList()
        {
            foreach (Button row in list.Controls)
            {
                var slot = (Slot)row.Tag;
                // Keep the connection state readable even when the endpoint and account are long.
                string summary = slot.Status.Split('\r', '\n')[0];
                row.Text = summary + Environment.NewLine + slot.Name;
                row.AccessibleName = slot.Name + ". " + slot.Status;
                row.Image = slot.Preview;
                row.Height = slot.Preview == null ? 54 : SessionThumbnails.Height + 54;
                row.BackColor = slot == selected ? Color.FromArgb(26, 92, 92) : Color.FromArgb(34, 49, 68);
            }
            if (SelectionChanged != null) SelectionChanged();
        }

        private async void StartPreview(Slot slot)
        {
            if (slot == null || capturingPreview || IsDisposed || Disposing) return;
            Bitmap next = null;
            capturingPreview = true;
            try
            {
                using (Bitmap frame = SessionThumbnails.Capture(slot.PreviewSurface))
                {
                    if (frame == null) return;
                    next = await previewRenderer.ResizeAsync(frame);
                }
                if (next == null || IsDisposed || Disposing || !slots.Contains(slot)) return;
                try { thumbnails.Save(slot.ThumbnailId, next); }
                catch (IOException ex) { Program.Log("Could not save a session preview: " + ex.GetType().Name); }
                catch (UnauthorizedAccessException ex) { Program.Log("Could not save a session preview: " + ex.GetType().Name); }
                slot.PreviewAttempts = 0;
                var old = slot.Preview;
                slot.Preview = next;
                next = null;
                try { RefreshList(); }
                finally { if (old != null) old.Dispose(); }
            }
            catch (Exception ex)
            {
                Program.Log("Session preview unavailable: " + ex.GetType().Name);
            }
            finally
            {
                if (next != null) next.Dispose();
                capturingPreview = false;
                UpdatePreviewTimer();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                if (previewTimer != null) previewTimer.Dispose();
                previewRenderer.Dispose();
            }
            base.Dispose(disposing);
            if (!disposing) return;
            foreach (Slot slot in slots)
                if (slot.Preview != null) slot.Preview.Dispose();
            slots.Clear();
            try { thumbnails.Dispose(); }
            catch (IOException ex) { Program.Log("Could not clear session previews: " + ex.GetType().Name); }
            catch (UnauthorizedAccessException ex) { Program.Log("Could not clear session previews: " + ex.GetType().Name); }
        }

        private void Arrange()
        {
            if (selected == null || IsDisposed) return;
            const int Divider = 4;
            int leftWidth = right == null ? surface.Width : Math.Max(0, (surface.Width - Divider) / 2);
            foreach (Slot slot in slots)
            {
                if (slot == left) slot.View.SetBounds(0, 0, leftWidth, surface.Height);
                else if (slot == right) slot.View.SetBounds(leftWidth + Divider, 0,
                    Math.Max(0, surface.Width - leftWidth - Divider), surface.Height);
                else if (slot == startupSlot)
                {
                    slot.View.SetBounds(0, 0, surface.Width, surface.Height);
                    slot.View.SendToBack();
                }
                if (slot == startupSlot && (slot == left || slot == right)) slot.View.BringToFront();
                slot.View.Visible = slot == left || slot == right || slot == startupSlot;
            }
            splitButton.Enabled = slots.Count > 1;
            splitButton.Text = Strings.T(right == null ? "sessions_split" : "sessions_single");
            if (list.Controls.Count != slots.Count) RebuildList();
            else RefreshList();
            UpdatePreviewTimer();
        }
    }
}
