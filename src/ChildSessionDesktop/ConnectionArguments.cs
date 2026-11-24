using System;

namespace ChildSessionDesktop
{
    internal static class ConnectionArguments
    {
        internal static bool IsCredentialOption(string argument)
        {
            return argument.StartsWith("--account=", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--password:", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--password=", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryParse(string[] args, out string account, out string password)
        {
            account = null;
            password = null;
            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                if (argument.Equals("--lang", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    i++;
                    continue;
                }

                if (argument.StartsWith("--account=", StringComparison.OrdinalIgnoreCase) && account == null)
                    account = argument.Substring("--account=".Length);
                else if ((argument.StartsWith("--password:", StringComparison.OrdinalIgnoreCase)
                    || argument.StartsWith("--password=", StringComparison.OrdinalIgnoreCase)) && password == null)
                    password = argument.Substring("--password:".Length);
                else if (!argument.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase) && argument != "--dynamic")
                    return false;
            }

            int separator = account == null ? -1 : account.IndexOf('\\');
            return separator > 0 && separator == account.LastIndexOf('\\')
                && separator < account.Length - 1 && (password == null || password.Length > 0);
        }
    }
}
