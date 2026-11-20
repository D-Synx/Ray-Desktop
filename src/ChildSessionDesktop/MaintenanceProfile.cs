using System;
using System.Text.RegularExpressions;

namespace ChildSessionDesktop
{
    internal sealed class MaintenanceProfile
    {
        private readonly string host;
        private readonly string account;

        private MaintenanceProfile(string host, string account)
        {
            this.host = host;
            this.account = account;
        }

        internal static MaintenanceProfile Parse(string[] args)
        {
            string host = null;
            string account = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--maintenance-host=", StringComparison.Ordinal))
                {
                    if (host != null) throw Usage();
                    host = arg.Substring("--maintenance-host=".Length);
                }
                else if (arg.StartsWith("--admin-account=", StringComparison.Ordinal))
                {
                    if (account != null) throw Usage();
                    account = arg.Substring("--admin-account=".Length);
                }
            }

            if (host == null && account == null) return null;
            if (host == null || account == null) throw Usage();

            foreach (string arg in args)
                if (!arg.StartsWith("--maintenance-host=", StringComparison.Ordinal) &&
                    !arg.StartsWith("--admin-account=", StringComparison.Ordinal))
                    throw Usage();

            const string label = @"[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?";
            if (host.Length > 253 ||
                !Regex.IsMatch(host, @"\A(?:" + label + @"\.)+[A-Za-z](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z") ||
                host.Equals("localhost.localdomain", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Split('.')[0].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the DNS name of a separate maintenance host.");

            if (account.Length > 254 ||
                !Regex.IsMatch(account, @"\A(?:[A-Za-z0-9._-]+\\[A-Za-z0-9._-]+|[A-Za-z0-9._-]+@[A-Za-z0-9.-]+)\z"))
                throw new ArgumentException("Use DOMAIN\\user or user@domain for the maintenance account.");

            return new MaintenanceProfile(host, account);
        }

        private static ArgumentException Usage()
        {
            return new ArgumentException("Use --maintenance-host=host.example --admin-account=DOMAIN\\user together.");
        }

        internal string ConnectionFile()
        {
            return CreateDirectConnectionFile(host, account);
        }

        internal static string CreateDirectConnectionFile(string host, string account)
        {
            if (host.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                account.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Invalid address or account in the RDP profile.");
            return string.Join("\r\n", new[]
            {
                "full address:s:" + host,
                "username:s:" + account,
                "screen mode id:i:1",
                "prompt for credentials:i:1",
                "authentication level:i:1",
                "enablecredsspsupport:i:1",
                "redirectclipboard:i:0",
                "redirectprinters:i:0",
                "redirectcomports:i:0",
                "redirectsmartcards:i:0",
                "redirectwebauthn:i:0",
                "drivestoredirect:s:",
                "devicestoredirect:s:",
                ""
            });
        }
    }
}
