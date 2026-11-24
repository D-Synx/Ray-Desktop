using System;

namespace ChildSessionDesktop
{
    internal static class RdpFailure
    {
        internal static string LoginMessageKey(int code)
        {
            switch (unchecked((uint)code))
            {
                case 0:
                case 0xC000006D: return "remote_logon_invalid";
                case 0xFFFFFFFF:
                case 0xFFFFFFFA:
                case 0xC000006E: return "remote_logon_denied";
                case 1:
                case 0xC0000224: return "remote_logon_expired";
                default: return null;
            }
        }

        internal static string DisconnectHint(int reason, uint? extendedReason, bool loginCompleted, bool byIp)
        {
            // The same numeric value means different things in the ActiveX and server error code fields.
            if (extendedReason == 0x107U) return Strings.T("remote_licensing_interrupted");
            if (!IsUnspecifiedDisconnect(reason, extendedReason)) return "";
            if (loginCompleted) return Strings.T("remote_ended_after_login");
            return Strings.T(byIp ? "remote_ended_early_ip" : "remote_ended_early");
        }

        internal static bool CanRetryWithCertificateWarning(int reason, uint? extendedReason, bool loginCompleted, bool warningRequested)
        {
            return !loginCompleted && !warningRequested && IsUnspecifiedDisconnect(reason, extendedReason);
        }

        private static bool IsUnspecifiedDisconnect(int reason, uint? extendedReason)
        {
            return reason == 0x107 && extendedReason.GetValueOrDefault() == 0;
        }
    }
}
