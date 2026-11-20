using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ChildSessionDesktop
{
    internal sealed class SavedSession
    {
        internal readonly RdpEndpoint Endpoint;
        internal readonly string Account;
        internal readonly bool ShareClipboard;
        internal string ProtectedPassword;
        internal bool ReconnectAtStartup;

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
            ReconnectAllAtStartup = true;
            SendFilesEnabled = ReceiveFilesEnabled = true;

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
        internal bool ReconnectAllAtStartup { get; private set; }
        private bool SendFilesEnabled { get; set; }
        private bool ReceiveFilesEnabled { get; set; }
        internal bool? LocalRdpClipboard { get; private set; }
        internal IEnumerable<SavedSession> SavedSessions { get { return sessions.AsReadOnly(); } }

        internal bool SetReconnectAllAtStartup(bool enabled)
        {
            ReconnectAllAtStartup = enabled;
            return Save();
        }

        internal bool AllowsFileTransfer(bool outbound)
        {
            return outbound ? SendFilesEnabled : ReceiveFilesEnabled;
        }

        internal bool SetFileTransfer(bool outbound, bool enabled)
        {
            if (outbound) SendFilesEnabled = enabled;
            else ReceiveFilesEnabled = enabled;
            return Save();
        }

        internal bool SetLocalRdpClipboard(bool enabled)
        {
            LocalRdpClipboard = enabled;
            return Save();
        }

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
            else
            {
                item.ProtectedPassword = sessions[index].ProtectedPassword;
                item.ReconnectAtStartup = sessions[index].ReconnectAtStartup;
                sessions[index] = item;
            }
            return Save();
        }

        internal bool HasSessionPassword(RdpEndpoint endpoint, string account)
        {
            int index = FindSession(endpoint, account);
            return index >= 0 && sessions[index].ProtectedPassword != null;
        }

        internal bool ReconnectsAtStartup(RdpEndpoint endpoint, string account)
        {
            int index = FindSession(endpoint, account);
            return index >= 0 && sessions[index].ReconnectAtStartup;
        }

        internal bool SetStartupSession(RdpEndpoint endpoint, string account, bool enabled)
        {
            int index = FindSession(endpoint, account);
            if (index < 0) return false;
            sessions[index].ReconnectAtStartup = enabled;
            return Save();
        }

        private static byte[] PasswordEntropy(RdpEndpoint endpoint, string account)
        {
            return Encoding.UTF8.GetBytes("RayXDesktop/RDP/1\0" + endpoint.Display.ToUpperInvariant() + "\0" + account.ToUpperInvariant());
        }

        internal bool SetSessionPassword(RdpEndpoint endpoint, string account, string password)
        {
            int index = FindSession(endpoint, account);
            if (index < 0) return false;
            if (password == null)
            {
                sessions[index].ProtectedPassword = null;
                return Save();
            }
            if (password.Length == 0 || password.Length > 1024 || password.IndexOf('\0') >= 0) return false;

            byte[] plain = Encoding.Unicode.GetBytes(password);
            try
            {
                byte[] encrypted = ProtectedData.Protect(plain, PasswordEntropy(endpoint, account), DataProtectionScope.CurrentUser);
                string previous = sessions[index].ProtectedPassword;
                sessions[index].ProtectedPassword = Convert.ToBase64String(encrypted);
                if (Save()) return true;
                sessions[index].ProtectedPassword = previous;
                return false;
            }
            catch (CryptographicException) { return false; }
            catch (SecurityException) { return false; }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        internal string ReadSessionPassword(RdpEndpoint endpoint, string account)
        {
            int index = FindSession(endpoint, account);
            if (index < 0 || sessions[index].ProtectedPassword == null) return null;

            byte[] plain = null;
            try
            {
                byte[] encrypted = Convert.FromBase64String(sessions[index].ProtectedPassword);
                plain = ProtectedData.Unprotect(encrypted, PasswordEntropy(endpoint, account), DataProtectionScope.CurrentUser);
                if (plain.Length == 0 || plain.Length > 2048 || (plain.Length & 1) != 0) return null;
                string password = Encoding.Unicode.GetString(plain);
                return password.IndexOf('\0') < 0 ? password : null;
            }
            catch (FormatException) { return null; }
            catch (CryptographicException) { return null; }
            catch (SecurityException) { return null; }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
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
                    else if (string.Equals(key, "ReconnectAllAtStartup", StringComparison.OrdinalIgnoreCase)) ReconnectAllAtStartup = value != "0";
                    else if (string.Equals(key, "SendFiles", StringComparison.OrdinalIgnoreCase)) SendFilesEnabled = value != "0";
                    else if (string.Equals(key, "ReceiveFiles", StringComparison.OrdinalIgnoreCase)) ReceiveFilesEnabled = value != "0";
                    else if (string.Equals(key, "LocalRdpClipboard", StringComparison.OrdinalIgnoreCase) && (value == "1" || value == "0"))
                        LocalRdpClipboard = value == "1";
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
                var item = new SavedSession(endpoint, account, clipboard != "0");
                string encrypted, startup;
                if (record.TryGetValue("PasswordDpapi", out encrypted) && encrypted.Length > 0 && encrypted.Length <= 16384)
                    item.ProtectedPassword = encrypted;
                item.ReconnectAtStartup = record.TryGetValue("ReconnectAtStartup", out startup) && startup == "1";
                sessions.Add(item);
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
                    writer.WriteLine("ReconnectAllAtStartup=" + (ReconnectAllAtStartup ? "1" : "0"));
                    writer.WriteLine("SendFiles=" + (SendFilesEnabled ? "1" : "0"));
                    writer.WriteLine("ReceiveFiles=" + (ReceiveFilesEnabled ? "1" : "0"));
                    if (LocalRdpClipboard.HasValue) writer.WriteLine("LocalRdpClipboard=" + (LocalRdpClipboard.Value ? "1" : "0"));
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
                        if (item.ReconnectAtStartup) writer.WriteLine("ReconnectAtStartup=1");
                        if (item.ProtectedPassword != null) writer.WriteLine("PasswordDpapi=" + item.ProtectedPassword);
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
