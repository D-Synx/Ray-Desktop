using System;
using Microsoft.Win32;

namespace ChildSessionDesktop
{
    internal static class ClipboardRestrictions
    {
        private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

        internal static bool AllowsFiles(bool outbound, string childSid)
        {
            using (var machine = Registry.LocalMachine.OpenSubKey(PolicyPath))
            using (var current = Registry.CurrentUser.OpenSubKey(PolicyPath))
            using (var child = string.IsNullOrEmpty(childSid) ? null : Registry.Users.OpenSubKey(childSid + "\\" + PolicyPath))
            {
                string direction = outbound ? "CSClipLevel" : "SCClipLevel";
                return !Blocks(machine, direction) && !Blocks(current, direction) && !Blocks(child, direction);
            }
        }

        internal static bool AllowsText(bool outbound)
        {
            using (var machine = Registry.LocalMachine.OpenSubKey(PolicyPath))
            using (var current = Registry.CurrentUser.OpenSubKey(PolicyPath))
            {
                string direction = outbound ? "CSClipLevel" : "SCClipLevel";
                return !BlocksText(machine, direction) && !BlocksText(current, direction);
            }
        }

        private static bool BlocksText(RegistryKey key, string direction)
        {
            return key != null && (IsEnabled(key, "fDisableClip") || Convert.ToInt32(key.GetValue(direction, 1)) == 0);
        }

        private static bool Blocks(RegistryKey key, string direction)
        {
            if (key == null) return false;
            // Directional clipboard limit values 0 through 4 never include files.
            return IsEnabled(key, "fDisableClip") || IsEnabled(key, "fDisableCdm") || key.GetValue(direction) != null;
        }

        private static bool IsEnabled(RegistryKey key, string name)
        {
            return Convert.ToInt32(key.GetValue(name, 0)) != 0;
        }
    }
}
