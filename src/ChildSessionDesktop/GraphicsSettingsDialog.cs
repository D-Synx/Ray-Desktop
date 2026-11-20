using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal static class GraphicsSettingsDialog
    {
        internal static void Show(IWin32Window owner, bool fluid, int width, int height)
        {
            using (var form = new Form
            {
                Text = Strings.T("graphics_title"), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, MaximizeBox = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScaleMode = AutoScaleMode.Font
            })
            {
                var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(16) };
                var policies = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
                var render = AddPolicy(policies, "graphics_gpu_label");
                var encoder = AddPolicy(policies, "graphics_encoder_label");
                var codec = AddPolicy(policies, "graphics_codec_label");
                Action refresh = delegate
                {
                    render.Text = GraphicsOptions.DescribePolicy("bEnumerateHWBeforeSW");
                    encoder.Text = GraphicsOptions.DescribePolicy("AVCHardwareEncodePreferred");
                    codec.Text = GraphicsOptions.DescribePolicy("AVC444ModePreferred");
                };
                refresh();

                layout.Controls.Add(MakeLabel(Strings.T("graphics_host_note")));
                layout.Controls.Add(MakeLabel(Strings.T("graphics_current",
                    Strings.T(fluid ? "graphics_fluid" : "graphics_default"), width, height)));
                layout.Controls.Add(policies);
                layout.Controls.Add(MakeLabel(Strings.T("graphics_policy_path")));
                layout.Controls.Add(MakeLabel(Strings.T("graphics_policy_diagnostic")));
                var actions = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(590, 0) };
                var policyEditor = new Button { AutoSize = true, Text = Strings.T("graphics_edit_windows") };
                policyEditor.Click += delegate { OpenConsole(form, "gpedit.msc", true); };
                var events = new Button { AutoSize = true, Text = Strings.T("graphics_events") };
                events.Click += delegate { OpenConsole(form, "eventvwr.msc", false); };
                var refreshButton = new Button { AutoSize = true, Text = Strings.T("graphics_refresh_values") };
                refreshButton.Click += delegate { refresh(); };
                var close = new Button { AutoSize = true, Text = Strings.T("graphics_close"), DialogResult = DialogResult.Cancel };
                actions.Controls.Add(policyEditor);
                actions.Controls.Add(events);
                actions.Controls.Add(refreshButton);
                actions.Controls.Add(close);
                layout.Controls.Add(actions);
                form.Controls.Add(layout);
                form.CancelButton = close;
                form.ShowDialog(owner);
            }
        }

        private static Label MakeLabel(string text)
        {
            return new Label { AutoSize = true, Text = text, MaximumSize = new Size(575, 0), Margin = new Padding(4, 5, 4, 5) };
        }

        private static Label AddPolicy(TableLayoutPanel table, string key)
        {
            table.Controls.Add(MakeLabel(Strings.T(key)));
            var value = MakeLabel("");
            table.Controls.Add(value);
            return value;
        }

        private static void OpenConsole(IWin32Window owner, string file, bool elevated)
        {
            string target = Path.Combine(Environment.SystemDirectory, file);
            try
            {
                if (!File.Exists(target)) throw new InvalidOperationException(Strings.T("graphics_console_missing"));
                var info = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "mmc.exe"),
                    Arguments = "\"" + target + "\"",
                    UseShellExecute = true
                };
                if (elevated) info.Verb = "runas";
                using (Process started = Process.Start(info)) { }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) ShowOpenError(owner, ex);
            }
            catch (Exception ex) { ShowOpenError(owner, ex); }
        }

        private static void ShowOpenError(IWin32Window owner, Exception ex)
        {
            MessageBox.Show(owner, ex.Message, Strings.T("graphics_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
