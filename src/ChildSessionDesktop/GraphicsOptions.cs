using System;
using Microsoft.Win32;

namespace ChildSessionDesktop
{
    internal static class GraphicsOptions
    {
        // Wallpaper, full-window drag, menu animations, cursor shadow.
        internal const int FluidPerformanceFlags = 0x01 | 0x02 | 0x04 | 0x20;

        internal static string DescribePolicy(string name)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services"))
                {
                    object value = key == null ? null : key.GetValue(name);
                    if (value is int) return Strings.T((int)value == 1 ? "policy_enabled" : "policy_disabled");
                    return Strings.T("policy_unconfigured");
                }
            }
            catch (UnauthorizedAccessException) { return Strings.T("policy_unreadable"); }
            catch (System.Security.SecurityException) { return Strings.T("policy_unreadable"); }
        }
    }
}
