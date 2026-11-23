using System;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using ChildSessionDesktop;
using RayXDesktop.Configuration;

namespace RayDesktopTests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool cond, string name)
        {
            if (cond) { _passed++; }
            else { _failed++; Console.WriteLine("FAIL: " + name); }
        }

        // ---------- Geometry.ToMillimeters ----------
        private static void TestToMillimeters()
        {
            Check(Geometry.ToMillimeters(1920, 96) == 508, "ToMillimeters(1920,96)=508");
            Check(Geometry.ToMillimeters(100, 96) == 26, "ToMillimeters(100,96)=26");
            Check(Geometry.ToMillimeters(1920, 144) == 339, "ToMillimeters(1920,144)=339");
            Check(Geometry.ToMillimeters(0, 96) == 10, "ToMillimeters(0,96)=10 (floor 10)");
            Check(Geometry.ToMillimeters(100, 0) == 26, "ToMillimeters(100,0) fallback dpi=96");
            Check(Geometry.ToMillimeters(100, -1) == 26, "ToMillimeters(100,-1) fallback dpi=96");
        }

        // ---------- Geometry.ComputeLetterbox ----------
        private static void TestComputeLetterbox()
        {
            int dw, dh, dx, dy;
            Geometry.ComputeLetterbox(1920, 1080, 1920, 1080, out dw, out dh, out dx, out dy);
            Check(dw == 1920 && dh == 1080 && dx == 0 && dy == 0, "Letterbox same size");

            Geometry.ComputeLetterbox(2560, 1440, 1920, 1080, out dw, out dh, out dx, out dy);
            Check(dw == 1920 && dh == 1080 && dx == 320 && dy == 180, "Letterbox bigger client centered");

            Geometry.ComputeLetterbox(960, 1080, 1920, 1080, out dw, out dh, out dx, out dy);
            Check(dw == 960 && dh == 540 && dx == 0 && dy == 270, "Letterbox scale down");

            Geometry.ComputeLetterbox(1280, 720, 2560, 1440, out dw, out dh, out dx, out dy);
            Check(dw == 1280 && dh == 720 && dx == 0 && dy == 0, "Letterbox half-res 1:1");

            Geometry.ComputeLetterbox(800, 600, 0, 0, out dw, out dh, out dx, out dy);
            Check(dw == 800 && dh == 600 && dx == 0 && dy == 0, "Letterbox no session size");
        }

        private static bool RectEq(Rect r, int l, int t, int rt, int b)
        {
            return r.Left == l && r.Top == t && r.Right == rt && r.Bottom == b;
        }

        private static void TestLockAspectRect()
        {
            Rect anchor = new Rect { Left = 100, Top = 100, Right = 600, Bottom = 400 };
            Rect result;
            const double aspect = 16.0 / 9.0;

            Rect lockHigh = new Rect { Left = 100, Top = 100, Right = 700, Bottom = 300 };
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszLeft, out result) && RectEq(result, 293, 100, 600, 300), "WmszLeft anchors left");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszRight, out result) && RectEq(result, 100, 100, 407, 300), "WmszRight anchors right");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszTop, out result) && RectEq(result, 100, 200, 407, 400), "WmszTop anchors top");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszTopLeft, out result) && RectEq(result, 293, 200, 600, 400), "WmszTopLeft anchors topleft");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszTopRight, out result) && RectEq(result, 100, 200, 407, 400), "WmszTopRight anchors topright");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszBottom, out result) && RectEq(result, 100, 100, 407, 300), "WmszBottom anchors bottom");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszBottomLeft, out result) && RectEq(result, 293, 100, 600, 300), "WmszBottomLeft anchors bottomleft");
            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, Geometry.WmszBottomRight, out result) && RectEq(result, 100, 100, 407, 300), "WmszBottomRight anchors bottomright");

            Check(Geometry.LockAspectRect(lockHigh, anchor, 8, 32, aspect, 99, out result) && RectEq(result, 100, 100, 407, 300), "default dir = bottomright");

            Rect lockWide = new Rect { Left = 100, Top = 100, Right = 600, Bottom = 500 };
            Check(Geometry.LockAspectRect(lockWide, anchor, 8, 32, aspect, Geometry.WmszBottom, out result) && RectEq(result, 100, 100, 600, 409), "WmszBottom lock-wide");

            Check(!Geometry.LockAspectRect(new Rect { Left = 100, Top = 100, Right = 250, Bottom = 300 }, anchor, 8, 32, aspect, Geometry.WmszRight, out result), "Lock too-small drag returns false");
        }

        // ---------- ClipboardProtocol.Parse ----------
        private static byte[] BuildMessage(Guid id, string text, string[] paths)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(ClipboardProtocol.Magic);
                w.Write(ClipboardProtocol.Version);
                w.Write(id.ToByteArray());
                w.Write(text);
                w.Write(paths.Length);
                foreach (var p in paths) w.Write(p);
                return ms.ToArray();
            }
        }

        private static void TestClipboardParse()
        {
            Guid id = new Guid("10203040506070809000a0b0c0d0e0f0");
            string[] paths = new[] { @"C:\a.txt", @"C:\b dir\x.txt" };

            byte[] ok = BuildMessage(id, "hello", paths);
            using (var ms = new MemoryStream(ok))
            using (var r = new BinaryReader(ms, Encoding.UTF8))
            {
                Guid outId; string[] outPaths; string outText;
                Check(ClipboardProtocol.Parse(r, out outId, out outPaths, out outText), "Parse valid message");
                Check(outId == id, "Parse id round-trip");
                Check(outText == "hello", "Parse text");
                Check(outPaths.Length == 2 && outPaths[0] == paths[0] && outPaths[1] == paths[1], "Parse paths");
            }

            using (var ms = new MemoryStream(ok))
            using (var r = new BinaryReader(ms, Encoding.UTF8))
            {
                r.ReadInt32(); r.ReadInt32(); r.ReadBytes(16); r.ReadString(); int n = r.ReadInt32();
                byte[] bad = BuildMessage(id, "hello", new string[0]);
                bad[0] = 0x00;
                using (var ms2 = new MemoryStream(bad))
                using (var r2 = new BinaryReader(ms2, Encoding.UTF8))
                {
                    Guid oi; string[] op; string ot;
                    Check(!ClipboardProtocol.Parse(r2, out oi, out op, out ot), "Parse bad magic rejected");
                }
            }

            {
                byte[] bad = BuildMessage(id, "t", new string[0]);
                bad[4] = 0x7F;
                using (var ms = new MemoryStream(bad))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    Guid oi; string[] op; string ot;
                    Check(!ClipboardProtocol.Parse(r, out oi, out op, out ot), "Parse bad version rejected");
                }
            }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(ClipboardProtocol.Magic); w.Write(ClipboardProtocol.Version);
                w.Write(id.ToByteArray()); w.Write("t"); w.Write(-1);
                byte[] bad = ms.ToArray();
                using (var ms2 = new MemoryStream(bad))
                using (var r = new BinaryReader(ms2, Encoding.UTF8))
                {
                    Guid oi; string[] op; string ot;
                    Check(!ClipboardProtocol.Parse(r, out oi, out op, out ot), "Parse negative count rejected");
                }
            }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(ClipboardProtocol.Magic); w.Write(ClipboardProtocol.Version);
                w.Write(id.ToByteArray()); w.Write("t"); w.Write(99999);
                byte[] bad = ms.ToArray();
                using (var ms2 = new MemoryStream(bad))
                using (var r = new BinaryReader(ms2, Encoding.UTF8))
                {
                    Guid oi; string[] op; string ot;
                    Check(!ClipboardProtocol.Parse(r, out oi, out op, out ot), "Parse oversized count rejected");
                }
            }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(ClipboardProtocol.Magic); w.Write(ClipboardProtocol.Version);
                w.Write(new byte[8]); w.Write("t"); w.Write(0);
                byte[] bad = ms.ToArray();
                using (var ms2 = new MemoryStream(bad))
                using (var r = new BinaryReader(ms2, Encoding.UTF8))
                {
                    Guid oi; string[] op; string ot;
                    Check(!ClipboardProtocol.Parse(r, out oi, out op, out ot), "Parse short id rejected");
                }
            }
        }

        private static void TestReconnectPolicy()
        {
            Check(!ReconnectPolicy.ShouldAutoReconnect(1), "local disconnect(1) no auto-reconnect");
            Check(ReconnectPolicy.ShouldAutoReconnect(0xC07), "remote disconnect auto-reconnect");
            Check(ReconnectPolicy.ShouldAutoReconnect(0), "fatal/other reason auto-reconnect");

            var p = new ReconnectPolicy();
            p.Enter();
            Check(p.InProgress && p.Attempts == 0 && !p.GaveUp, "enter starts in-progress, attempts 0");

            p.OnAttempt();   // attempts=1
            p.Enter();
            Check(p.Attempts == 1, "enter is idempotent (keeps attempts)");

            p.OnReconnected();
            p.Enter();
            Check(p.InProgress && p.Attempts == 0, "fresh enter attempts 0");
            for (int i = 1; i <= 6; i++) p.OnAttempt();
            Check(p.Attempts == 6 && p.InProgress && !p.GaveUp, "attempt 6 still in progress, not gave up");
            p.OnAttempt();
            Check(p.Attempts == 7 && p.GaveUp && !p.InProgress, "attempt 7 gave up, exits in-progress");

            int after = p.Attempts;
            p.OnAttempt();
            Check(p.Attempts == after, "on-attempt after gaveup is no-op");

            p.Enter();
            p.OnAttempt();
            p.OnReconnected();
            Check(!p.InProgress && p.Attempts == 0 && !p.GaveUp, "reconnected resets");

            p.Enter();
            p.OnAttempt();
            p.OnManualReconnect();
            Check(!p.InProgress && p.Attempts == 0 && !p.GaveUp, "manual reconnect resets");
        }

        private static void TestConnectionArguments()
        {
            const string localAccount = @"--account=.\Administrateur";
            string account, password;
            Check(!Array.Exists(new string[0], ConnectionArguments.IsCredentialOption), "no arguments use current account");
            Check(ConnectionArguments.IsCredentialOption(localAccount), "account-only shortcut selects explicit sign-in");
            Check(ConnectionArguments.TryParse(new[] { localAccount, "--lang=fr", "--dynamic" }, out account, out password)
                && account == @".\Administrateur" && password == null, "account only asks for password");
            Check(ConnectionArguments.TryParse(new[] { @"--account=LYCEE\Agent", "--password:Ex%ample!" }, out account, out password)
                && account == @"LYCEE\Agent" && password == "Ex%ample!", "legacy arguments preserve password characters");
            Check(!ConnectionArguments.TryParse(new[] { "--password=example" }, out account, out password), "password needs an account");
            Check(!ConnectionArguments.TryParse(new[] { localAccount, "--password:" }, out account, out password), "empty explicit password is invalid");
            Check(!ConnectionArguments.TryParse(new[] { localAccount, @"--account=.\Autre" }, out account, out password), "duplicate account is invalid");
            Check(!ConnectionArguments.TryParse(new[] { localAccount, "--password:one", "--password=two" }, out account, out password), "duplicate password is invalid");
            Check(!ConnectionArguments.TryParse(new[] { "--account=Administrateur" }, out account, out password), "unqualified account is invalid");
            Check(!ConnectionArguments.TryParse(new[] { localAccount, "--maintenance-host=example.org" }, out account, out password), "mixed connection modes are invalid");
        }

        private static void TestFileTransfer()
        {
            string root = Path.Combine(Path.GetTempPath(), "RayTransferTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source");
                string target = Path.Combine(root, "target");
                string project = Path.Combine(source, "Projet");
                Directory.CreateDirectory(Path.Combine(project, "Sous-dossier"));
                Directory.CreateDirectory(target);
                string pdf = Path.Combine(project, "Sous-dossier", "exemple.txt");
                File.WriteAllText(pdf, "contenu");
                var report = new TransferResult();
                FileTransfer.Copy(new[] { project }, target, false, CancellationToken.None, delegate { }, report);
                Check(report.Copied == 1 && report.Failures.Count == 0
                    && File.ReadAllText(Path.Combine(target, "Projet", "Sous-dossier", "exemple.txt")) == "contenu",
                    "transfer copies directory contents");
                var same = new TransferResult();
                FileTransfer.Copy(new[] { project }, target, false, CancellationToken.None, delegate { }, same);
                Check(same.Skipped == 1 && same.Copied == 0, "transfer skips existing folders by default");
                var keep = new TransferResult();
                FileTransfer.Copy(new[] { project }, target, true, CancellationToken.None, delegate { }, keep);
                Check(keep.Copied == 1 && Directory.Exists(Path.Combine(target, "Projet (2)")), "transfer can keep both folders");
                var recursive = new TransferResult();
                FileTransfer.Copy(new[] { project }, Path.Combine(project, "Sous-dossier"), true, CancellationToken.None, delegate { }, recursive);
                Check(recursive.Failures.Count == 1 && recursive.Copied == 0, "transfer refuses a copy into the source tree");
                string junction = Path.Combine(root, "raccourci");
                var makeJunction = new ProcessStartInfo("cmd.exe", "/d /c mklink /j \"" + junction + "\" \"" + project + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (var process = Process.Start(makeJunction))
                {
                    process.WaitForExit();
                    Check(process.ExitCode == 0, "create an NTFS junction for the transfer regression");
                }
                try
                {
                    var linkedSource = new TransferResult();
                    FileTransfer.Copy(new[] { Path.Combine(junction, "Sous-dossier", "exemple.txt") },
                        target, false, CancellationToken.None, delegate { }, linkedSource);
                    Check(linkedSource.Failures.Count == 1 && linkedSource.Copied == 0,
                        "transfer rejects sources below a junction");
                    bool rejected = false;
                    try
                    {
                        FileTransfer.Copy(new[] { project }, Path.Combine(junction, "Sous-dossier"),
                            true, CancellationToken.None, delegate { }, new TransferResult());
                    }
                    catch (IOException ex) { rejected = ex.Message == Strings.T("files_reparse"); }
                    Check(rejected, "transfer rejects a destination inside the source reached through a junction");
                }
                finally { if (Directory.Exists(junction)) Directory.Delete(junction); }
                using (var stop = new CancellationTokenSource())
                {
                    bool cancelled = false;
                    try
                    {
                        FileTransfer.Copy(new[] { pdf }, target, false, stop.Token, delegate { stop.Cancel(); }, new TransferResult());
                    }
                    catch (OperationCanceledException) { cancelled = true; }
                    Check(cancelled && !File.Exists(Path.Combine(target, "exemple.txt"))
                        && Directory.GetFileSystemEntries(target, ".rayx-*.tmp").Length == 0, "cancellation leaves no partial file");
                }
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestRemoteConnections()
        {
            RdpEndpoint endpoint;
            Check(RdpEndpoint.TryForAccount("", @"PC-EXEMPLE\Maintenance", out endpoint)
                && endpoint.Host == "PC-EXEMPLE" && endpoint.Port == 3389, "qualified account can supply the host");
            Check(RdpEndpoint.TryForAccount("192.0.2.15:3390", @"PC-EXEMPLE\Maintenance", out endpoint)
                && endpoint.Address != null && endpoint.Port == 3390, "IPv4 with explicit port");
            Check(RdpEndpoint.TryParse("[2001:db8::15]:3391", 3389, out endpoint)
                && endpoint.Address != null && endpoint.Display == "[2001:db8::15]:3391", "IPv6 bracketed port");
            Check(!RdpEndpoint.TryParse("127.0.0.1:3390", 3389, out endpoint), "IPv4 local address rejected");
            Check(!RdpEndpoint.TryParse("[::ffff:127.0.0.1]", 3389, out endpoint), "mapped IPv6 loopback rejected");
            Check(!RdpEndpoint.TryParse("focus.example:65536", 3389, out endpoint), "invalid RDP port rejected");
            Check(!RdpEndpoint.TryForAccount("", @"PC-EXEMPLE\", out endpoint), "incomplete account rejected");

            var imported = RdpImport.Parse(new StringReader(
                "full address:s:192.0.2.22\nusername:s:operator\ndomain:s:PC-EXEMPLE\nserver port:i:3398\n"));
            Check(imported.Endpoint.Port == 3398 && imported.Account == @"PC-EXEMPLE\operator"
                && !imported.HasProtectedPassword, "direct file imports address, port and account");
            Check(RejectsRdpFile("full address:s:PC-E1\nfull address:s:PC-E2"), "duplicate RDP target rejected");
            Check(RejectsRdpFile("full address:s:PC-E1\ngatewayusagemethod:i:1"), "gateway profile is not silently changed");

            byte[] placeholder = Encoding.Unicode.GetBytes("test-only!" + '\0');
            byte[] protectedValue = ProtectedData.Protect(placeholder, null, DataProtectionScope.CurrentUser);
            imported = RdpImport.Parse(new StringReader("full address:s:PC-EXEMPLE\nusername:s:PC-EXEMPLE\\operator\npassword 51:b:"
                + BitConverter.ToString(protectedValue).Replace("-", "")));
            Check(imported.HasProtectedPassword && imported.TakePassword() == "test-only!" && !imported.HasProtectedPassword,
                "Windows-protected file password is consumed once");
            imported = RdpImport.Parse(new StringReader("full address:s:PC-EXEMPLE\nusername:s:PC-EXEMPLE\\operator\npassword 51:b:010203"));
            Check(imported.TakePassword() == null, "unreadable protected password falls back to an interactive prompt");
        }

        private static bool RejectsRdpFile(string content)
        {
            try { RdpImport.Parse(new StringReader(content)); }
            catch (InvalidDataException) { return true; }
            return false;
        }

        private static void TestRemoteDisplaySizing()
        {
            Check(Geometry.RemoteDisplaySize(1643, 1380, false) == new Size(1642, 1380), "split remote viewport uses even width");
            Check(Geometry.RemoteDisplaySize(3440, 1440, false) == new Size(2560, 1440), "large remote viewport has a resolution cap");
            Check(Geometry.RemoteDisplaySize(3440, 1440, true) == new Size(1600, 900), "smooth profile caps remote resolution");
            Check(Geometry.RemoteDisplaySize(459, 320, false) == new Size(640, 480), "small remote viewport keeps usable remote resolution");
        }

        private static void TestGraphicsAndRdpDiagnostics()
        {
            PolicyChoice[] changes;
            Check(RdpGraphicsPolicies.TryParse(new[] { "--set-rdp-graphics", "fr", "on", "keep", "default" }, out changes)
                && changes[0] == PolicyChoice.On && changes[1] == PolicyChoice.Keep && changes[2] == PolicyChoice.Default,
                "graphics helper changes only requested rows");
            Check(!RdpGraphicsPolicies.TryParse(new[] { "--set-rdp-graphics", "fr", "on", "keep", "HKLM" }, out changes),
                "graphics helper rejects unknown commands");
            Check(!RdpGraphicsPolicies.TryParse(new[] { "--set-rdp-graphics", "en", "keep", "keep", "keep" }, out changes),
                "graphics helper rejects empty changes");
            Check(RdpGraphicsPolicies.Argument(PolicyChoice.Off) == "off", "explicit disable differs from resetting to Windows default");
            Check(RdpFailure.DisconnectHint(0x107, 0, false, true) == Strings.T("remote_ended_early_ip"),
                "ActiveX 263 alone is not a server licensing code");
            Check(RdpFailure.DisconnectHint(0x3, 0x107, false, false) == Strings.T("remote_licensing_interrupted"),
                "server's extended 0x107 is a licensing interruption");
            Check(RdpFailure.CanRetryWithCertificateWarning(0x107, 0, false, false) &&
                RdpFailure.CanRetryWithCertificateWarning(0x107, null, false, false),
                "early unspecified disconnect offers one interactive certificate retry");
            Check(!RdpFailure.CanRetryWithCertificateWarning(0x107, 0, true, false) &&
                !RdpFailure.CanRetryWithCertificateWarning(0x107, 0, false, true),
                "no certificate retry after sign-in or after an interactive attempt");
            Check(!RdpFailure.CanRetryWithCertificateWarning(0x107, 0x107, false, false) &&
                !RdpFailure.CanRetryWithCertificateWarning(0x104, 0, false, false),
                "certificate retry is not offered for a specific disconnect reason");
            Check(RdpFailure.LoginMessageKey(unchecked((int)0xC000006E)) == "remote_logon_denied"
                && RdpFailure.LoginMessageKey(-2) == null, "sign-in restrictions are recognized without treating progress as an error");
        }

        private static void TestDesktopPreferences()
        {
            string folder = Path.Combine(Path.GetTempPath(), "RayXDesktop.Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "data.ini");
            try
            {
                var preferences = new DesktopPreferences(path);
                RdpEndpoint host, sameHost, otherPort;
                RdpEndpoint.TryParse("PC-EXEMPLE", RdpEndpoint.DefaultPort, out host);
                RdpEndpoint.TryParse("pc-exemple:3389", RdpEndpoint.DefaultPort, out sameHost);
                RdpEndpoint.TryParse("pc-exemple:3390", RdpEndpoint.DefaultPort, out otherPort);
                Check(preferences.LogEdgeEnabled && !preferences.UsesCertificateWarning(host) && preferences.ReconnectAllAtStartup,
                    "new users see the log edge and all saved sessions use strict server authentication at startup");
                Check(preferences.AllowsFileTransfer(true) && preferences.AllowsFileTransfer(false)
                    && !preferences.LocalRdpClipboard.HasValue, "new settings preserve the previous file and clipboard behavior");
                Check(!preferences.ToolbarPinned && preferences.SetToolbarPinned(true)
                    && new DesktopPreferences(path).ToolbarPinned, "pinning the toolbar remains enabled after reopening");
                Check(preferences.SetToolbarPinned(false) && !new DesktopPreferences(path).ToolbarPinned,
                    "unpinning the toolbar clears the saved choice");
                Check(preferences.SetCertificateWarning(host, true) && preferences.SetLogEdgeEnabled(false) &&
                    preferences.SetFluidGraphics(true) && preferences.SetDynamicDisplay(true),
                    "save all view settings and the per-address mode in data.ini");
                Check(preferences.SetSession(host, @"DOM\Élodie", true) &&
                    preferences.SetSession(sameHost, @"dom\élodie", false) && preferences.SetSession(otherPort, @"DOM\Second", true),
                    "save and update sessions without duplicating the same address and account");
                var reopened = new DesktopPreferences(path);
                var profiles = new System.Collections.Generic.List<SavedSession>(reopened.SavedSessions);
                Check(reopened.UsesCertificateWarning(sameHost) && !reopened.UsesCertificateWarning(otherPort) &&
                    !reopened.LogEdgeEnabled && reopened.FluidGraphics && reopened.DynamicDisplay,
                    "stored choices survive reopening and are scoped to the address and port");
                Check(profiles.Count == 2 && profiles[0].Account == @"dom\élodie" && !profiles[0].ShareClipboard &&
                    profiles[1].Endpoint.Port == 3390 && profiles[1].ShareClipboard, "saved sessions retain order, account, port and clipboard preference");
                Check(reopened.SetFileTransfer(true, false) && reopened.SetLocalRdpClipboard(true), "save outbound files and the local clipboard independently");
                var transferSettings = new DesktopPreferences(path);
                Check(!transferSettings.AllowsFileTransfer(true) && transferSettings.AllowsFileTransfer(false) &&
                    transferSettings.LocalRdpClipboard == true, "disabling send leaves receive enabled after reopening");
                Check(reopened.SetFileTransfer(true, true) && reopened.SetFileTransfer(false, false) &&
                    reopened.SetLocalRdpClipboard(false), "enable send and explicitly disable receive and the local native clipboard");
                transferSettings = new DesktopPreferences(path);
                Check(transferSettings.AllowsFileTransfer(true) && !transferSettings.AllowsFileTransfer(false) &&
                    transferSettings.LocalRdpClipboard == false, "disabling receive leaves send enabled after reopening");
                const string examplePassword = "Test-only-Éclair! 327_";
                Check(reopened.SetSessionPassword(host, @"DOM\Élodie", examplePassword) &&
                    !File.ReadAllText(path).Contains(examplePassword), "password is written only as DPAPI-protected data");
                Check(reopened.SetStartupSession(host, @"DOM\Élodie", true) &&
                    reopened.SetStartupSession(otherPort, @"DOM\Second", true) && reopened.SetReconnectAllAtStartup(false),
                    "save multiple selected startup sessions and turn off the global option");
                Check(reopened.SetSession(sameHost, @"dom\élodie", false), "updating clipboard preference preserves credential state");
                var withPassword = new DesktopPreferences(path);
                Check(withPassword.ReadSessionPassword(sameHost, @"dom\élodie") == examplePassword &&
                    withPassword.ReadSessionPassword(otherPort, @"DOM\Second") == null &&
                    withPassword.ReconnectsAtStartup(host, @"DOM\Élodie") && withPassword.ReconnectsAtStartup(otherPort, @"DOM\Second") &&
                    !withPassword.ReconnectAllAtStartup, "DPAPI password and multiple startup choices survive reopening");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check(!withPassword.SetSessionPassword(sameHost, @"dom\élodie", null) &&
                        withPassword.HasSessionPassword(sameHost, @"dom\élodie"),
                        "failed removal leaves the saved password available for a second removal attempt");
                }
                Check(reopened.SetStartupSession(host, @"DOM\Élodie", false) &&
                    !reopened.ReconnectsAtStartup(host, @"DOM\Élodie") && reopened.ReconnectsAtStartup(otherPort, @"DOM\Second"),
                    "disabling one startup session preserves the other");
                string savedIni = File.ReadAllText(path);
                string wrongIdentity = savedIni.Replace("Account=dom\\élodie", "Account=DOM\\Different");
                File.WriteAllText(path, wrongIdentity, Encoding.UTF8);
                Check(new DesktopPreferences(path).ReadSessionPassword(sameHost, @"DOM\Different") == null,
                    "moving a protected password to another account fails safely");
                File.WriteAllText(path, savedIni, Encoding.UTF8);
                Check(reopened.SetSessionPassword(host, @"DOM\Élodie", null) &&
                    !new DesktopPreferences(path).HasSessionPassword(sameHost, @"dom\élodie"), "forget removes the encrypted password");
                Check(reopened.SetCertificateWarning(sameHost, false) && !new DesktopPreferences(path).UsesCertificateWarning(host),
                    "forget the interactive mode and restore strict authentication");
                Check(reopened.RemoveSession(host, @"DOM\ÉLODIE") &&
                    new System.Collections.Generic.List<SavedSession>(new DesktopPreferences(path).SavedSessions).Count == 1,
                    "removing a saved session survives reopening");
                File.AppendAllText(path, "\r\n[Session.9]\r\nAddress=localhost\r\nAccount=ignored\r\n[Session.10]\r\nAccount=PC\\ignored\r\nPassword=ignored\r\n");
                Check(new System.Collections.Generic.List<SavedSession>(new DesktopPreferences(path).SavedSessions).Count == 1,
                    "malformed and local targets in edited data.ini are ignored");
            }
            finally { Directory.Delete(folder, true); }
        }

        private static void TestLogTail()
        {
            string path = Path.GetTempFileName();
            try
            {
                const string tail = "nouvelle entrée\r\nfin\r\n";
                int tailBytes = Encoding.UTF8.GetByteCount(tail);
                File.WriteAllText(path, new string('x', 256) + "é\r\n" + tail, Encoding.UTF8);
                Check(LogPane.ReadRecentText(path, tailBytes + 4) == tail && LogPane.ReadRecentText(path, tailBytes) == tail,
                    "log tail skips partial lines and preserves complete UTF-8 entries");
                using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    Check(LogPane.ReadRecentText(path, tailBytes) == tail, "activity log can be read while a writer holds it");
                File.WriteAllText(path, "rotation\r\n", Encoding.UTF8);
                Check(LogPane.ReadRecentText(path) == "rotation\r\n", "activity log reloads the current file after rotation");
            }
            finally { File.Delete(path); }
        }

        private static void TestToolbarPlacement()
        {
            using (var host = new Form { ClientSize = new Size(960, 540) })
            {
                var viewport = new Panel { Dock = DockStyle.Fill };
                var sidebar = new Panel { Dock = DockStyle.Left, Width = 204 };
                var band = new Panel { Dock = DockStyle.Top, Visible = false };
                host.Controls.Add(viewport);
                host.Controls.Add(sidebar);
                host.Controls.Add(band);
                using (var toolbar = new ToolbarControl(host, band))
                {
                    toolbar.ToggleDock = delegate { toolbar.SetDocked(!toolbar.IsDocked); };
                    host.Show();
                    toolbar.SetDocked(true);
                    Check(toolbar.IsDocked && band.Visible && Math.Abs(toolbar.Left * 2 + toolbar.Width - band.Width) <= 1
                        && viewport.Top == band.Bottom && sidebar.Top == band.Bottom,
                        "docked toolbar centers in the band above both the desktop and session list");
                    host.ClientSize = new Size(320, 440);
                    AccessibleObject buttons = toolbar.AccessibilityObject;
                    Rectangle toolbarBounds = toolbar.RectangleToScreen(toolbar.ClientRectangle);
                    bool allFit = toolbar.Width <= band.Width;
                    AccessibleObject undock = null;
                    for (int i = 0; i < buttons.GetChildCount(); i++)
                    {
                        var button = buttons.GetChild(i);
                        allFit &= toolbarBounds.Contains(button.Bounds);
                        if (button.Name == Strings.T("toolbar_float")) undock = button;
                    }
                    Check(allFit, "every toolbar button fits at the minimum window width");
                    if (undock != null) undock.DoDefaultAction();
                    Check(undock != null && !toolbar.IsDocked && !band.Visible && viewport.Top == 0
                        && toolbar.FindForm().Owner == host, "the toolbar button undocks and returns the space to the desktop");
                    buttons = toolbar.AccessibilityObject;
                    AccessibleObject dock = null;
                    for (int i = 0; i < buttons.GetChildCount(); i++)
                    {
                        var button = buttons.GetChild(i);
                        if (button.Name == Strings.T("toolbar_dock")) dock = button;
                    }
                    if (dock != null) dock.DoDefaultAction();
                    Check(dock != null && toolbar.IsDocked && viewport.Top == band.Bottom,
                        "the same toolbar docks again from the floating window");
                }
            }
        }

        [STAThread]
        private static int Main()
        {
            TestToMillimeters();
            TestComputeLetterbox();
            TestLockAspectRect();
            TestClipboardParse();
            TestReconnectPolicy();
            TestConnectionArguments();
            TestFileTransfer();
            TestRemoteConnections();
            TestRemoteDisplaySizing();
            TestGraphicsAndRdpDiagnostics();
            TestDesktopPreferences();
            TestToolbarPlacement();
            TestLogTail();
            Console.WriteLine("RayDesktopTests: " + _passed + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }
    }
}
