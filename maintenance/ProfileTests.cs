using System;
using ChildSessionDesktop;

internal static class ProfileTests
{
    private static void Require(bool condition, string test)
    {
        if (!condition) throw new Exception(test);
    }

    private static void Reject(params string[] args)
    {
        try { MaintenanceProfile.Parse(args); }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid options accepted.");
    }

    private static void Main()
    {
        const string host = "--maintenance-host=pc.example.test";
        const string account = @"--admin-account=IT\maint";
        Require(MaintenanceProfile.Parse(new[] { "--dynamic", "--lang=en" }) == null, "Child mode changed.");
        string profile = MaintenanceProfile.Parse(new[] { host, account }).ConnectionFile();
        Require(profile.Contains("full address:s:pc.example.test\r\n"), "Host missing.");
        Require(profile.Contains(@"username:s:IT\maint"), "Account missing.");
        Require(profile.Contains("prompt for credentials:i:1"), "Credential prompt missing.");
        Require(profile.Contains("authentication level:i:1"), "Certificate check missing.");
        Require(profile.Contains("redirectclipboard:i:0") && profile.Contains("drivestoredirect:s:\r\n"), "Redirection enabled.");
        Require(!profile.Contains("password"), "Password field present.");
        string warningProfile = MaintenanceProfile.CreateDirectConnectionFile("192.0.2.15", @"PC\maint", true);
        Require(warningProfile.Contains("authentication level:i:2\r\n") && !warningProfile.Contains("authentication level:i:1"),
            "Interactive certificate review was not selected for this connection.");
        Require(warningProfile.Contains("prompt for credentials:i:1\r\n") && warningProfile.Contains("enablecredsspsupport:i:1\r\n") &&
            !warningProfile.Contains("password"), "Interactive certificate review changed credential handling.");
        Require(MaintenanceProfile.Parse(new[] { host, "--admin-account=maint@it.example.test" }) != null, "UPN rejected.");
        Reject(host);
        Reject(account);
        Reject(host, account, account);
        Reject(host, account, "--password=example");
        Reject(host, "--admin-account=IT\\maint\r\nredirectclipboard:i:1");
        Reject("--maintenance-host=127.0.0.1", account);
        Reject("--maintenance-host=localhost", account);
        Reject("--maintenance-host=localhost.localdomain", account);
        Reject("--maintenance-host=pc.example.test /admin", account);
        Console.WriteLine("Maintenance profile tests passed.");
    }
}
