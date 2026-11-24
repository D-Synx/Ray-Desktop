using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ChildSessionDesktop
{
    internal static class MaintenanceClient
    {
        internal static int Run(MaintenanceProfile profile)
        {
            return Run(profile.ConnectionFile());
        }

        internal static int Run(string connectionFile)
        {
            string path = Path.Combine(Path.GetTempPath(), "RayMaintenance-" + Guid.NewGuid().ToString("N") + ".rdp");
            bool created = false;
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    using (var writer = new StreamWriter(stream, Encoding.Unicode))
                        writer.Write(connectionFile);
                }

                var start = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "mstsc.exe"),
                    Arguments = "\"" + path + "\" /public /prompt",
                    UseShellExecute = false
                };
                using (var client = Process.Start(start))
                {
                    if (client == null) throw new InvalidOperationException(Strings.T("cli_maintenance_start_failed"));
                    Program.Log("Maintenance: Remote Desktop client started; connection state is shown by Windows.");
                    client.WaitForExit();
                    Program.Log("Maintenance: Remote Desktop client exited (" + client.ExitCode + ").");
                    return client.ExitCode;
                }
            }
            finally
            {
                if (created)
                {
                    try { File.Delete(path); }
                    catch (IOException) { Program.Log("Maintenance: Could not remove temporary RDP profile."); }
                    catch (UnauthorizedAccessException) { Program.Log("Maintenance: Could not remove temporary RDP profile."); }
                }
            }
        }
    }
}
