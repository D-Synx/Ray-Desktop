using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ChildSessionDesktop;
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

            Strings.SetLanguage(Strings.ExtractLanguage(ref args));
            try
            {
                bool enabled;
                if (!WTSIsChildSessionsEnabled(out enabled))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                if (args.Length != 1 || args[0] != "--enable-child-sessions")
                {
                    MessageBox.Show(
                        Strings.T("setup_child_status", Strings.T(enabled ? "policy_enabled" : "policy_disabled")),
                        "RayXDesktop");
                    return args.Length == 0 ? 0 : 2;
                }

                if (enabled)
                {
                    MessageBox.Show(Strings.T("setup_child_already_enabled"), "RayXDesktop");
                    return 0;
                }

                if (MessageBox.Show(
                    Strings.T("setup_child_confirm"),
                    "RayXDesktop", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return 3;

                if (!WTSEnableChildSessions(true))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(Strings.T("setup_child_enable_failed", error));
                }

                if (!WTSIsChildSessionsEnabled(out enabled) || !enabled)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(Strings.T("setup_child_verify_failed", error));
                }

                MessageBox.Show(
                    Strings.T("setup_child_enabled"),
                    "RayXDesktop",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message + Environment.NewLine + Environment.NewLine + Strings.T("setup_child_error_help"),
                    Strings.T("err_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }
        private static int SetGraphicsPolicy(string[] args)
        {
            PolicyChoice[] changes;
            if (!RdpGraphicsPolicies.TryParse(args, out changes) || Strings.NormalizeLanguage(args[1]) == null) return 2;
            Strings.SetLanguage(args[1]);
            try
            {
                return RdpGraphicsPolicies.SaveLocal(changes) ? 0 : 4;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.T("setup_graphics_save_failed") + Environment.NewLine + ex.Message,
                    "RayXDesktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
