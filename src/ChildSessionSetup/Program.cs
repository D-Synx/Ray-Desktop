using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RayXDesktop.Configuration;

namespace ChildSessionSetup
{
    internal static class Program
    {
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSEnableChildSessions([MarshalAs(UnmanagedType.Bool)] bool enable);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0 && args[0] == "--set-rdp-graphics")
                return SetGraphicsPolicy(args);

            try
            {
                bool enabled;
                if (!WTSIsChildSessionsEnabled(out enabled))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                if (args.Length != 1 || args[0] != "--enable-child-sessions")
                {
                    MessageBox.Show(
                        "Child Sessions: " + (enabled ? "enabled" : "disabled") +
                        "\r\nUse --enable-child-sessions only with authorization.",
                        "RayXDesktop Maintenance");
                    return args.Length == 0 ? 0 : 2;
                }

                if (enabled)
                {
                    MessageBox.Show("Child Sessions are already enabled.", "RayXDesktop Maintenance");
                    return 0;
                }

                if (MessageBox.Show(
                    "Enable Child Sessions for this computer? Confirm only if authorized.",
                    "RayXDesktop Maintenance", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return 3;

                if (!WTSEnableChildSessions(true))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException("WTSEnableChildSessions failed, Windows error code " + error + ".");
                }

                if (!WTSIsChildSessionsEnabled(out enabled) || !enabled)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException("Windows did not confirm Child Sessions are enabled, error code " + error + ".");
                }

                MessageBox.Show(
                    "Child Sessions are enabled. Save your work, sign out of Windows, then sign back in to use the isolated desktop.\r\n\r\nIf a Windows credential prompt appears when connecting now, cancel it and do not enter a blank password; after signing out and back in, the child session will connect automatically using your current sign-in.",
                    "Isolated Windows Desktop Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message + Environment.NewLine + Environment.NewLine + "Verify this Windows version supports Child Sessions and that administrator action is allowed.",
                    "Could Not Enable Child Sessions",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }
        private static int SetGraphicsPolicy(string[] args)
        {
            PolicyChoice[] changes;
            if (!RdpGraphicsPolicies.TryParse(args, out changes)) return 2;
            bool french = args[1] == "fr";
            try
            {
                return RdpGraphicsPolicies.SaveLocal(changes) ? 0 : 4;
            }
            catch (Exception ex)
            {
                MessageBox.Show((french ? "Impossible d’enregistrer la stratégie graphique locale."
                    : "Could not save the local graphics policy.") + Environment.NewLine + ex.Message,
                    "RayXDesktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
