using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace ChildSessionDesktop
{
    internal sealed class DesktopPreferences
    {
        private readonly string keyPath;

        internal DesktopPreferences(string keyPath = @"Software\RayXDesktop")
        {
            this.keyPath = keyPath;
        }

        internal bool LogEdgeEnabled { get { return ReadFlag(keyPath, "LogEdgeEnabled", true); } }

        internal bool SetLogEdgeEnabled(bool enabled)
        {
            return WriteFlag(keyPath, "LogEdgeEnabled", enabled);
        }

        internal bool UsesCertificateWarning(RdpEndpoint endpoint)
        {
            return ReadFlag(keyPath + @"\RdpCertificateWarnings", endpoint.Display, false);
        }

        internal bool SetCertificateWarning(RdpEndpoint endpoint, bool enabled)
        {
            return WriteFlag(keyPath + @"\RdpCertificateWarnings", endpoint.Display, enabled ? (bool?)true : null);
        }

        private static bool ReadFlag(string path, string name, bool defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(path))
                {
                    object value = key == null ? null : key.GetValue(name);
                    return value is int && ((int)value == 0 || (int)value == 1)
                        ? (int)value == 1 : defaultValue;
                }
            }
            catch (IOException) { return defaultValue; }
            catch (UnauthorizedAccessException) { return defaultValue; }
            catch (SecurityException) { return defaultValue; }
        }

        private static bool WriteFlag(string path, string name, bool? value)
        {
            try
            {
                if (value.HasValue)
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(path))
                    {
                        if (key == null) return false;
                        key.SetValue(name, value.Value ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
                else
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(path, true))
                        if (key != null) key.DeleteValue(name, false);
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }
    }
}
