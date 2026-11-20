using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace ChildSessionDesktop
{
    internal sealed class SavedSession
    {
        internal readonly RdpEndpoint Endpoint;
        internal readonly string Account;
        internal readonly bool ShareClipboard;

        internal SavedSession(RdpEndpoint endpoint, string account, bool shareClipboard)
        {
            Endpoint = endpoint;
            Account = account;
            ShareClipboard = shareClipboard;
        }
    }

    internal sealed class DesktopPreferences
    {
        private readonly string path;
        private readonly Dictionary<string, bool> certificateWarnings = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SavedSession> sessions = new List<SavedSession>();
        private bool cannotRead;

        internal DesktopPreferences(string path = null)
        {
            this.path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.ini");
            LogEdgeEnabled = true;

            try
            {
                Load(File.ReadAllLines(this.path, Encoding.UTF8));
            }
            catch (FileNotFoundException)
            {
                if (path == null)
                {
                    MigrateRegistry();
                    Save();
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { cannotRead = true; }
            catch (UnauthorizedAccessException) { cannotRead = true; }
            catch (SecurityException) { cannotRead = true; }
        }

        internal bool LogEdgeEnabled { get; private set; }
        internal bool FluidGraphics { get; private set; }
        internal bool DynamicDisplay { get; private set; }
        internal IEnumerable<SavedSession> SavedSessions { get { return sessions.AsReadOnly(); } }

        internal bool SetLogEdgeEnabled(bool enabled)
        {
            LogEdgeEnabled = enabled;
            return Save();
        }

        internal bool SetFluidGraphics(bool enabled)
        {
            FluidGraphics = enabled;
            return Save();
        }

        internal bool SetDynamicDisplay(bool enabled)
        {
            DynamicDisplay = enabled;
            return Save();
        }

        internal bool UsesCertificateWarning(RdpEndpoint endpoint)
        {
            return certificateWarnings.ContainsKey(endpoint.Display);
        }

        internal bool SetCertificateWarning(RdpEndpoint endpoint, bool enabled)
        {
            if (enabled) certificateWarnings[endpoint.Display] = true;
            else certificateWarnings.Remove(endpoint.Display);
            return Save();
        }

        internal bool SetSession(RdpEndpoint endpoint, string account, bool shareClipboard)
        {
            if (!ValidAccount(account)) return false;
            var item = new SavedSession(endpoint, account, shareClipboard);
            int index = FindSession(endpoint, account);
            if (index < 0) sessions.Add(item);
            else sessions[index] = item;
            return Save();
        }

        internal bool RemoveSession(RdpEndpoint endpoint, string account)
        {
            int index = FindSession(endpoint, account);
            if (index >= 0) sessions.RemoveAt(index);
            return Save();
        }

        private int FindSession(RdpEndpoint endpoint, string account)
        {
            return sessions.FindIndex(item =>
                string.Equals(item.Endpoint.Display, endpoint.Display, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Account, account, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ValidAccount(string account)
        {
            return !string.IsNullOrWhiteSpace(account) && account.Length <= 255 && account.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0;
        }

        private void Load(IEnumerable<string> lines)
        {
            var records = new SortedDictionary<int, Dictionary<string, string>>();
            string section = "";
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (string.Equals(section, "General", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(key, "ShowLogEdge", StringComparison.OrdinalIgnoreCase)) LogEdgeEnabled = value != "0";
                    else if (string.Equals(key, "FluidGraphics", StringComparison.OrdinalIgnoreCase)) FluidGraphics = value == "1";
                    else if (string.Equals(key, "DynamicDisplay", StringComparison.OrdinalIgnoreCase)) DynamicDisplay = value == "1";
                }
                else if (string.Equals(section, "CertificateWarnings", StringComparison.OrdinalIgnoreCase))
                {
                    RdpEndpoint endpoint;
                    if (value == "1" && RdpEndpoint.TryParse(key, RdpEndpoint.DefaultPort, out endpoint))
                        certificateWarnings[endpoint.Display] = true;
                }
                else
                {
                    int index;
                    if (!section.StartsWith("Session.", StringComparison.OrdinalIgnoreCase) ||
                        !int.TryParse(section.Substring(8), NumberStyles.None, CultureInfo.InvariantCulture, out index)) continue;
                    Dictionary<string, string> record;
                    if (!records.TryGetValue(index, out record))
                    {
                        record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        records.Add(index, record);
                    }
                    record[key] = value;
                }
            }

            foreach (var record in records.Values)
            {
                string address, account, clipboard;
                RdpEndpoint endpoint;
                if (!record.TryGetValue("Address", out address) || !record.TryGetValue("Account", out account) ||
                    !ValidAccount(account) || !RdpEndpoint.TryForAccount(address, account, out endpoint) ||
                    string.IsNullOrWhiteSpace(address) || FindSession(endpoint, account) >= 0) continue;
                record.TryGetValue("ShareClipboard", out clipboard);
                sessions.Add(new SavedSession(endpoint, account, clipboard != "0"));
            }
        }

        private void MigrateRegistry()
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\RayXDesktop"))
                {
                    if (root == null) return;
                    LogEdgeEnabled = !Equals(root.GetValue("LogEdgeEnabled"), 0);
                    using (var warnings = root.OpenSubKey("RdpCertificateWarnings"))
                    {
                        if (warnings != null)
                        {
                            foreach (string key in warnings.GetValueNames())
                            {
                                RdpEndpoint endpoint;
                                if (Equals(warnings.GetValue(key), 1) && RdpEndpoint.TryParse(key, RdpEndpoint.DefaultPort, out endpoint))
                                    certificateWarnings[endpoint.Display] = true;
                            }
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        // The temporary file is written completely before replacing data.ini, including on portable Windows volumes.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string source, string destination, uint flags);

        private bool Save()
        {
            if (cannotRead) return false;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("[General]");
                    writer.WriteLine("ShowLogEdge=" + (LogEdgeEnabled ? "1" : "0"));
                    writer.WriteLine("FluidGraphics=" + (FluidGraphics ? "1" : "0"));
                    writer.WriteLine("DynamicDisplay=" + (DynamicDisplay ? "1" : "0"));
                    writer.WriteLine();
                    writer.WriteLine("[CertificateWarnings]");
                    foreach (string endpoint in certificateWarnings.Keys) writer.WriteLine(endpoint + "=1");
                    for (int index = 0; index < sessions.Count; index++)
                    {
                        SavedSession item = sessions[index];
                        writer.WriteLine();
                        writer.WriteLine("[Session." + index.ToString(CultureInfo.InvariantCulture) + "]");
                        writer.WriteLine("Address=" + item.Endpoint.Display);
                        writer.WriteLine("Account=" + item.Account);
                        writer.WriteLine("ShareClipboard=" + (item.ShareClipboard ? "1" : "0"));
                    }
                }
                const uint ReplaceExisting = 1, WriteThrough = 8;
                return MoveFileEx(temporary, path, ReplaceExisting | WriteThrough);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
            }
        }
    }
}
