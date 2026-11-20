using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ChildSessionDesktop
{
    internal sealed class RdpEndpoint
    {
        internal const int DefaultPort = 3389;
        internal string Host { get; private set; }
        internal int Port { get; private set; }
        internal IPAddress Address { get; private set; }

        private RdpEndpoint(string host, int port, IPAddress address)
        {
            Host = host;
            Port = port;
            Address = address;
        }

        internal string Display
        {
            get
            {
                if (Port == DefaultPort) return Host;
                return (Host.IndexOf(':') >= 0 ? "[" + Host + "]" : Host) + ":" + Port.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal static bool TryParse(string value, int defaultPort, out RdpEndpoint endpoint)
        {
            endpoint = null;
            string host = (value ?? "").Trim();
            if (host.Length == 0 || host.Length > 270) return false;
            int port = defaultPort;
            IPAddress address;
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int end = host.IndexOf(']');
                if (end < 0 || (end + 1 < host.Length &&
                    (host[end + 1] != ':' || !TryParsePort(host.Substring(end + 2), out port)))) return false;
                host = host.Substring(1, end - 1);
                if (!IPAddress.TryParse(host, out address) || host.IndexOf(':') < 0) return false;
            }
            else if (!IPAddress.TryParse(host, out address))
            {
                int separator = host.IndexOf(':');
                if (separator >= 0)
                {
                    if (!TryParsePort(host.Substring(separator + 1), out port)) return false;
                    host = host.Substring(0, separator);
                }
                if (!IPAddress.TryParse(host, out address))
                {
                    if (Uri.CheckHostName(host) != UriHostNameType.Dns) return false;
                    host = host.TrimEnd('.');
                    if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            if (address != null)
            {
                if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
                if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                    return false;
                host = address.ToString();
            }
            endpoint = new RdpEndpoint(host, port, address);
            return true;
        }

        internal static bool TryParsePort(string value, out int port)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port >= 1 && port <= 65535;
        }

        internal static bool TryForAccount(string address, string account, out RdpEndpoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrWhiteSpace(account)) return false;
            int separator = account.IndexOf('\\');
            if (separator >= 0 && (separator == 0 || separator == account.Length - 1 || separator != account.LastIndexOf('\\')))
                return false;
            if (string.IsNullOrWhiteSpace(address) && separator > 0) address = account.Substring(0, separator);
            return TryParse(address, DefaultPort, out endpoint);
        }

        internal async Task<bool> ResolvesRemotelyAsync()
        {
            IPAddress[] addresses = Address == null ? await Dns.GetHostAddressesAsync(Host) : new[] { Address };
            if (addresses.Length == 0) return false;
            NetworkInterface[] networks = NetworkInterface.GetAllNetworkInterfaces();
            foreach (IPAddress address in addresses)
            {
                IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
                if (IPAddress.IsLoopback(normalized) || normalized.Equals(IPAddress.Any) || normalized.Equals(IPAddress.IPv6Any)) return false;
                foreach (NetworkInterface network in networks)
                {
                    foreach (UnicastIPAddressInformation local in network.GetIPProperties().UnicastAddresses)
                    {
                        if (local.Address.Equals(normalized) || (local.Address.IsIPv4MappedToIPv6 && local.Address.MapToIPv4().Equals(normalized)))
                            return false;
                    }
                }
            }
            return true;
        }
    }

    internal sealed class RdpImport
    {
        private const int MaximumBytes = 262144;
        private byte[] protectedPassword;
        internal RdpEndpoint Endpoint { get; private set; }
        internal string Account { get; private set; }
        internal bool HasProtectedPassword { get { return protectedPassword != null; } }

        internal static RdpImport Read(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaximumBytes) throw new InvalidDataException(Strings.T("rdp_import_invalid"));
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return Parse(reader);
            }
        }

        internal static RdpImport Parse(TextReader reader)
        {
            string address = null, user = null, domain = null, password = null, line;
            int port = RdpEndpoint.DefaultPort;
            bool separatePort = false;
            while ((line = reader.ReadLine()) != null)
            {
                int first = line.IndexOf(':');
                if (first < 0 || line.Length < first + 3 || line[first + 2] != ':') continue;
                string key = line.Substring(0, first).Trim().ToLowerInvariant();
                char type = char.ToLowerInvariant(line[first + 1]);
                string value = line.Substring(first + 3).Trim();
                switch (key)
                {
                    case "full address":
                        if (type != 's' || address != null) throw InvalidProfile();
                        address = value;
                        break;
                    case "username":
                        if (type != 's' || user != null) throw InvalidProfile();
                        user = value;
                        break;
                    case "domain":
                        if (type != 's' || domain != null) throw InvalidProfile();
                        domain = value;
                        break;
                    case "server port":
                        if (type != 'i' || separatePort || !RdpEndpoint.TryParsePort(value, out port)) throw InvalidProfile();
                        separatePort = true;
                        break;
                    case "password 51":
                        if (type != 'b' || password != null) throw InvalidProfile();
                        password = value;
                        break;
                    case "remoteapplicationmode":
                        if (value != "0") throw UnsupportedProfile();
                        break;
                    case "gatewayusagemethod":
                        // 0 never uses a gateway. 4 bypasses it even when a gateway is specified.
                        if (value != "0" && value != "4") throw UnsupportedProfile();
                        break;
                    case "loadbalanceinfo":
                        if (value.Length != 0) throw UnsupportedProfile();
                        break;
                }
            }

            RdpEndpoint endpoint;
            if (!RdpEndpoint.TryParse(address, port, out endpoint)) throw InvalidProfile();
            string account = user ?? "";
            if (account.Length > 0 && !string.IsNullOrEmpty(domain) && account.IndexOf('\\') < 0 && account.IndexOf('@') < 0)
                account = domain + "\\" + account;
            if (account.Length > 255) throw InvalidProfile();
            return new RdpImport { Endpoint = endpoint, Account = account, protectedPassword = DecodeHex(password) };
        }

        private static InvalidDataException InvalidProfile()
        {
            return new InvalidDataException(Strings.T("rdp_import_invalid"));
        }

        private static InvalidDataException UnsupportedProfile()
        {
            return new InvalidDataException(Strings.T("rdp_import_direct_only"));
        }

        private static byte[] DecodeHex(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 32768 || (text.Length & 1) != 0) return null;
            var bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; ++i)
                if (!byte.TryParse(text.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
                    return null;
            return bytes;
        }

        internal string TakePassword()
        {
            byte[] blob = protectedPassword;
            protectedPassword = null;
            if (blob == null) return null;
            byte[] plain = null;
            try
            {
                plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                if (plain.Length == 0 || (plain.Length & 1) != 0) return null;
                string password = Encoding.Unicode.GetString(plain).TrimEnd('\0');
                return password.Length == 0 || password.IndexOf('\0') >= 0 ? null : password;
            }
            catch (CryptographicException) { return null; }
            finally
            {
                Array.Clear(blob, 0, blob.Length);
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }
    }
}
