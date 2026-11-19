using System;
using System.IO;
using System.Text;
using ChildSessionDesktop;

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

        private static int Main()
        {
            TestToMillimeters();
            TestComputeLetterbox();
            TestLockAspectRect();
            TestClipboardParse();
            TestReconnectPolicy();
            Console.WriteLine("RayDesktopTests: " + _passed + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }
    }
}
