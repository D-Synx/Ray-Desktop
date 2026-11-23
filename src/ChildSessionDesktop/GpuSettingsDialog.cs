using System;
using System.Drawing;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal static class GpuSettingsDialog
    {
        internal static void Show(Form owner, GpuPreview preview, Action<PreviewEngine> save)
        {
            using (var form = new Form
            {
                Text = Strings.T("gpu_title"),
                StartPosition = FormStartPosition.CenterParent,
                MinimumSize = new Size(460, 340),
                ClientSize = new Size(650, 440),
                ShowInTaskbar = false,
                MinimizeBox = false
            })
            {
                var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
                choices.Items.AddRange(new object[] { Strings.T("gpu_auto"), Strings.T("gpu_native"),
                    Strings.T("gpu_d3d"), Strings.T("gpu_vk") });
                choices.SelectedIndex = (int)preview.Preferred;
                choices.AccessibleName = Strings.T("gpu_title");
                var settings = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 39, Padding = new Padding(8) };
                var apply = new Button { AutoSize = true, Text = Strings.T("graphics_apply") };
                var diagnostics = new Button { AutoSize = true, Text = Strings.T("gpu_probe") };
                settings.Controls.AddRange(new Control[] { choices, apply, diagnostics });

                var details = new TextBox
                {
                    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical, AccessibleName = Strings.T("gpu_probe")
                };
                Func<string> current = () => Strings.T("gpu_scope") + Environment.NewLine + Environment.NewLine +
                    Strings.T("gpu_selected", EngineLabel(preview.Preferred)) + Environment.NewLine + preview.Status() +
                    Environment.NewLine + Environment.NewLine + Strings.T("gpu_unmeasured");
                details.Text = current();
                apply.Click += delegate
                {
                    var selected = (PreviewEngine)choices.SelectedIndex;
                    preview.Select(selected);
                    save(selected);
                    details.Text = current();
                };
                diagnostics.Click += async delegate
                {
                    diagnostics.Enabled = false;
                    details.Text = current() + Environment.NewLine + Environment.NewLine + Strings.T("gpu_testing");
                    try
                    {
                        Task<string> hardware = preview.ProbeAsync();
                        Task<string> drivers = Task.Run((Func<string>)ReadWindowsDrivers);
                        await Task.WhenAll(hardware, drivers);
                        if (!form.IsDisposed)
                            details.Text = current() + Environment.NewLine + Environment.NewLine +
                                await drivers + Environment.NewLine + await hardware;
                    }
                    catch (Exception ex)
                    {
                        Program.Log("GPU diagnostic failed: " + ex.GetType().Name);
                        if (!form.IsDisposed) details.Text = current() + Environment.NewLine + Strings.T("gpu_probe_failed");
                    }
                    finally { if (!form.IsDisposed) diagnostics.Enabled = true; }
                };
                var close = new Button { Dock = DockStyle.Bottom, Height = 30,
                    Text = Strings.T("graphics_close"), DialogResult = DialogResult.Cancel };
                form.CancelButton = close;
                form.Controls.Add(details);
                form.Controls.Add(close);
                form.Controls.Add(settings);
                form.ShowDialog(owner);
            }
        }

        private static string EngineLabel(PreviewEngine engine)
        {
            if (engine == PreviewEngine.Direct3D) return Strings.T("gpu_d3d");
            if (engine == PreviewEngine.Vulkan) return Strings.T("gpu_vk");
            return GpuPreview.EngineName(engine);
        }

        private static string ReadWindowsDrivers()
        {
            var result = new StringBuilder();
            try
            {
                using (var search = new ManagementObjectSearcher(new ManagementScope(),
                    new ObjectQuery("SELECT Name, DriverVersion FROM Win32_VideoController"),
                    new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = true }))
                using (var adapters = search.Get())
                {
                    foreach (ManagementObject adapter in adapters)
                        using (adapter)
                            result.AppendLine(Strings.T("gpu_driver", adapter["Name"], adapter["DriverVersion"]));
                }
            }
            catch (ManagementException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (UnauthorizedAccessException) { }
            return result.Length == 0 ? Strings.T("gpu_no_drivers") : result.ToString();
        }
    }
}
