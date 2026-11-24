using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RayXDesktop.Configuration;

namespace ChildSessionDesktop
{
    internal static class GraphicsSettingsDialog
    {
        private static readonly string[] Labels = { "graphics_gpu_label", "graphics_encoder_label", "graphics_codec_label" };

        internal static void Show(IWin32Window owner, RdpPerformanceProfile profile, int width, int height)
        {
            using (var form = new ChromeForm
            {
                Text = Strings.T("graphics_title"), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, MaximizeBox = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScaleMode = AutoScaleMode.Font
            })
            {
                var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(16) };
                var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
                table.Controls.Add(MakeLabel(Strings.T("graphics_policy")));
                table.Controls.Add(MakeLabel(Strings.T("graphics_effective")));
                table.Controls.Add(MakeLabel(Strings.T("graphics_local_choice")));

                var effective = new Label[RdpGraphicsPolicies.Count];
                var switches = new CheckBox[RdpGraphicsPolicies.Count];
                var initial = new PolicyChoice[RdpGraphicsPolicies.Count];
                var desired = new PolicyChoice[RdpGraphicsPolicies.Count];
                var apply = new Button { AutoSize = true, Text = Strings.T("graphics_apply"), Enabled = false };
                var recommended = new Button { AutoSize = true, Text = Strings.T("graphics_recommended") };
                var note = MakeLabel("");
                bool applying = false;

                Action refreshSwitches = delegate
                {
                    for (int i = 0; i < RdpGraphicsPolicies.Count; i++)
                    {
                        switches[i].Text = ChoiceText(desired[i]);
                        switches[i].CheckState = desired[i] == PolicyChoice.On ? CheckState.Checked
                            : desired[i] == PolicyChoice.Off ? CheckState.Unchecked : CheckState.Indeterminate;
                        switches[i].AccessibleName = Strings.T(Labels[i]) + " · " + switches[i].Text;
                    }
                    apply.Enabled = !applying && HasChanges(initial, desired);
                };

                for (int i = 0; i < RdpGraphicsPolicies.Count; i++)
                {
                    int index = i;
                    var button = new CheckBox
                    {
                        Appearance = Appearance.Button, AutoCheck = false, ThreeState = true,
                        TextAlign = ContentAlignment.MiddleCenter, MinimumSize = new Size(125, 32), AutoSize = true
                    };
                    button.Click += delegate
                    {
                        desired[index] = desired[index] == PolicyChoice.On ? PolicyChoice.Off
                            : desired[index] == PolicyChoice.Off ? PolicyChoice.Default : PolicyChoice.On;
                        refreshSwitches();
                    };
                    table.Controls.Add(MakeLabel(Strings.T(Labels[i])));
                    effective[i] = MakeLabel("");
                    table.Controls.Add(effective[i]);
                    switches[i] = button;
                    table.Controls.Add(button);
                }

                Action reload = delegate
                {
                    PolicyChoice[] active = TryRead(false);
                    PolicyChoice[] local = TryRead(true);
                    for (int i = 0; i < RdpGraphicsPolicies.Count; i++)
                    {
                        effective[i].Text = active == null ? Strings.T("policy_unreadable") : ChoiceText(active[i]);
                        initial[i] = local == null ? PolicyChoice.Keep : local[i];
                        desired[i] = initial[i];
                    }
                    note.Text = local == null ? Strings.T("graphics_local_unreadable") : "";
                    note.Visible = local == null;
                    refreshSwitches();
                };
                recommended.Click += delegate
                {
                    desired[0] = PolicyChoice.On;
                    desired[1] = PolicyChoice.On;
                    desired[2] = PolicyChoice.Default;
                    refreshSwitches();
                };

                layout.Controls.Add(MakeLabel(Strings.T("graphics_host_named", Environment.MachineName)));
                layout.Controls.Add(MakeLabel(Strings.T("graphics_current",
                    GraphicsOptions.ProfileLabel(profile), width, height)));
                layout.Controls.Add(table);
                layout.Controls.Add(note);
                layout.Controls.Add(MakeLabel(Strings.T("graphics_switch_hint")));
                layout.Controls.Add(MakeLabel(Strings.T("graphics_policy_diagnostic")));
                var actions = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(630, 0) };
                var policyEditor = new Button { AutoSize = true, Text = Strings.T("graphics_edit_windows") };
                policyEditor.Click += delegate { OpenConsole(form, "gpedit.msc", true); };
                var events = new Button { AutoSize = true, Text = Strings.T("graphics_events") };
                events.Click += delegate { OpenConsole(form, "eventvwr.msc", false); };
                var refresh = new Button { AutoSize = true, Text = Strings.T("graphics_refresh_values") };
                refresh.Click += delegate { reload(); };
                var close = new Button { AutoSize = true, Text = Strings.T("graphics_close"), DialogResult = DialogResult.Cancel };
                actions.Controls.Add(apply);
                actions.Controls.Add(recommended);
                actions.Controls.Add(refresh);
                actions.Controls.Add(policyEditor);
                actions.Controls.Add(events);
                actions.Controls.Add(close);
                foreach (Button button in actions.Controls) ChromeColors.StyleButton(button, button == apply);
                layout.Controls.Add(actions);
                form.Controls.Add(layout);
                form.CancelButton = close;
                form.FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (applying && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
                };

                apply.Click += async delegate
                {
                    string[] values = new string[RdpGraphicsPolicies.Count];
                    string details = "";
                    for (int i = 0; i < values.Length; i++)
                    {
                        bool changed = initial[i] != desired[i];
                        values[i] = RdpGraphicsPolicies.Argument(changed ? desired[i] : PolicyChoice.Keep);
                        if (changed) details += Environment.NewLine + Strings.T(Labels[i]) + " → " + ChoiceText(desired[i]);
                    }
                    if (MessageBox.Show(form, Strings.T("graphics_confirm_apply", Environment.MachineName, details),
                        Strings.T("graphics_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

                    applying = true;
                    foreach (Control button in actions.Controls) button.Enabled = false;
                    foreach (CheckBox row in switches) row.Enabled = false;
                    try
                    {
                        int result = await ApplyLocalAsync(values);
                        if (form.IsDisposed) return;
                        if (result == 0 || result == 4)
                        {
                            reload();
                            MessageBox.Show(form, Strings.T(result == 0 ? "graphics_saved" : "graphics_saved_refresh_failed"),
                                Strings.T("graphics_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else if (result != 1)
                        {
                            ShowOpenError(form, new InvalidOperationException(Strings.T("graphics_helper_failed", result)));
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        if (ex.NativeErrorCode != 1223 && !form.IsDisposed) ShowOpenError(form, ex);
                    }
                    catch (Exception ex)
                    {
                        if (!form.IsDisposed) ShowOpenError(form, ex);
                    }
                    finally
                    {
                        applying = false;
                        if (!form.IsDisposed)
                        {
                            foreach (Control button in actions.Controls) button.Enabled = true;
                            foreach (CheckBox row in switches) row.Enabled = true;
                            refreshSwitches();
                        }
                    }
                };

                reload();
                form.ShowDialog(owner);
            }
        }

        private static bool HasChanges(PolicyChoice[] initial, PolicyChoice[] desired)
        {
            return !initial.SequenceEqual(desired);
        }

        private static PolicyChoice[] TryRead(bool local)
        {
            try { return local ? RdpGraphicsPolicies.ReadLocal() : RdpGraphicsPolicies.ReadEffective(); }
            catch (Exception ex)
            {
                Program.Log("Could not read " + (local ? "local" : "effective") + " RDP graphics policy: 0x" + ex.HResult.ToString("X8"));
                return null;
            }
        }

        private static string ChoiceText(PolicyChoice choice)
        {
            switch (choice)
            {
                case PolicyChoice.On: return Strings.T("policy_enabled");
                case PolicyChoice.Off: return Strings.T("policy_disabled");
                case PolicyChoice.Default: return Strings.T("policy_unconfigured");
                default: return Strings.T("graphics_keep");
            }
        }

        private static async Task<int> ApplyLocalAsync(string[] values)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EnableChildSessions.exe");
            if (!File.Exists(path) || !string.Equals(FileVersionInfo.GetVersionInfo(path).FileVersion,
                FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion, StringComparison.Ordinal))
                throw new InvalidOperationException(Strings.T("graphics_helper_missing"));
            var info = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--set-rdp-graphics " + Strings.Language + " " + string.Join(" ", values),
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            using (Process started = Process.Start(info))
            {
                if (started == null) throw new InvalidOperationException(Strings.T("graphics_helper_missing"));
                return await Task.Run(delegate { started.WaitForExit(); return started.ExitCode; });
            }
        }

        private static Label MakeLabel(string text)
        {
            return new Label { AutoSize = true, Text = text, MaximumSize = new Size(610, 0), Margin = new Padding(4, 5, 4, 5) };
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
