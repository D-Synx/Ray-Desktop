using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal static class Program
    {
        private static readonly object LogLock = new object();
        private static Mutex singleInstance;
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                Log(e.Exception.ToString());
                MessageBox.Show(e.Exception.ToString(), Strings.T("err_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log(Convert.ToString(e.ExceptionObject));
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string lang = null;
            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (string.Equals(args[i], "--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    lang = args[i + 1];
                else if (args[i].StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                    lang = args[i].Substring("--lang=".Length);
            }
            Strings.SetLanguage(lang);

            try
            {
                if (args != null && args.Length > 0 && string.Equals(args[0], "--clipboard-agent", StringComparison.OrdinalIgnoreCase))
                {
                    uint parentSessionId = 0;
                    for (int i = 1; i + 1 < args.Length; i++)
                        if (string.Equals(args[i], "--parent-session", StringComparison.OrdinalIgnoreCase))
                            uint.TryParse(args[i + 1], out parentSessionId);
                    if (ClipboardFileRelay.ShouldRunChildAgent(parentSessionId))
                    {
                        Log("Child-session file clipboard agent started");
                        Application.Run(new ClipboardAgentContext());
                    }
                    return;
                }
                var maintenance = MaintenanceProfile.Parse(args);
                if (maintenance != null)
                {
                    Environment.ExitCode = MaintenanceClient.Run(maintenance);
                    return;
                }
                string account = null;
                string password = null;
                if (Array.Exists(args, ConnectionArguments.IsCredentialOption)
                    && !ConnectionArguments.TryParse(args, out account, out password))
                {
                    MessageBox.Show(Strings.T("msg_account_usage"),
                        "RayXDesktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Environment.ExitCode = 2;
                    return;
                }
                bool dynamicMode = Array.IndexOf(args, "--dynamic") >= 0;
                bool createdNew;
                singleInstance = new Mutex(true, @"Local\RayDesktop_SingleInstance", out createdNew);
                if (!createdNew)
                {
                    Log("Another RayXDesktop instance is already running; this instance will exit.");
                    MessageBox.Show(Strings.T("msg_already_running"), "RayXDesktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new DesktopForm(dynamicMode, account, password));
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                MessageBox.Show(ex.ToString(), Strings.T("startup_fail_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static string LogDirectory
        {
            get
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData), "RayDesktop", "Logs");
                    Directory.CreateDirectory(dir);
                    return dir;
                }
                catch
                {
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
            }
        }

        internal static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    using (var mutex = new Mutex(false, @"Global\RayDesktopLog"))
                    {
                        bool ownsMutex = false;
                        try
                        {
                            try
                            {
                                ownsMutex = mutex.WaitOne(50);
                                if (!ownsMutex) return;
                            }
                            catch (AbandonedMutexException)
                            {
                                // The previous process exited while logging; this process now owns the mutex.
                                ownsMutex = true;
                            }

                            string path = Path.Combine(LogDirectory, "child-session.log");
                            const long MaxLogBytes = 5 * 1024 * 1024;
                            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                            {
                                string archived = path + ".1";
                                if (File.Exists(archived)) File.Delete(archived);
                                File.Move(path, archived);
                            }

                            string cleanMessage = (message ?? string.Empty).Replace("\0", string.Empty);
                            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " +
                                cleanMessage + Environment.NewLine, Encoding.UTF8);
                        }
                        finally
                        {
                            if (ownsMutex) mutex.ReleaseMutex();
                        }
                    }
                }
            }
            catch { }
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSGetChildSessionId(out uint sessionId);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSLogoffSession(IntPtr server, uint sessionId,
            [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQuerySessionInformation(IntPtr hServer, uint sessionId, int infoClass,
            out IntPtr ppBuffer, out uint pBytesReturned);

        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("kernel32.dll")]
        internal static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentProcessId();
    }
}
