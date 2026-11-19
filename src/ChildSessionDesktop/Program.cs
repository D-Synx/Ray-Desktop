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
        private static bool logInitialized;
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

            if (Array.Exists(args, IsLaunchCredential))
            {
                Environment.ExitCode = LaunchWithCredentials(args);
                return;
            }

            // 多语言：--lang=zh / --lang=en 覆盖（缺省按系统语言，见 Strings.SetLanguage）。
            // 同时支持 "--lang=en"（等号）与 "--lang en"（空格）两种写法。
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
                bool dynamicMode = args != null && Array.IndexOf(args, "--dynamic") >= 0;
                bool createdNew;
                singleInstance = new Mutex(true, @"Local\RayDesktop_SingleInstance", out createdNew);
                if (!createdNew)
                {
                    Log("Another Ray Desktop instance is already running; this instance will exit.");
                    MessageBox.Show(Strings.T("msg_already_running"), "Ray Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new DesktopForm(dynamicMode));
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                MessageBox.Show(ex.ToString(), Strings.T("startup_fail_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsLaunchCredential(string argument)
        {
            return argument.StartsWith("--account=", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--password:", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--password=", StringComparison.OrdinalIgnoreCase);
        }

        private static int LaunchWithCredentials(string[] args)
        {
            const string title = "Ray Desktop";
            string account = null;
            string password = null;
            foreach (string argument in args)
            {
                if (argument.StartsWith("--account=", StringComparison.OrdinalIgnoreCase) && account == null)
                    account = argument.Substring("--account=".Length);
                else if (IsLaunchCredential(argument) && argument.StartsWith("--password", StringComparison.OrdinalIgnoreCase) && password == null)
                    password = argument.Substring("--password:".Length);
            }

            int separator = account == null ? -1 : account.IndexOf('\\');
            if (args.Length != 2 || separator <= 0 || separator != account.LastIndexOf('\\')
                || separator == account.Length - 1 || string.IsNullOrEmpty(password))
            {
                MessageBox.Show(@"Usage : Raydesktop.exe --account=DOMAINE\Utilisateur --password:MOT_DE_PASSE",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            string domain = account.Substring(0, separator);
            string user = account.Substring(separator + 1);
            try
            {
                using (var secret = new System.Security.SecureString())
                {
                    foreach (char character in password)
                        secret.AppendChar(character);
                    secret.MakeReadOnly();

                    var startup = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = false,
                        Domain = domain,
                        UserName = user,
                        Password = secret,
                        LoadUserProfile = true
                    };
                    using (var process = System.Diagnostics.Process.Start(startup))
                    {
                        if (process == null)
                            throw new InvalidOperationException();
                    }
                }
                return 0;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                MessageBox.Show("Windows : " + error.Message + " (" + error.NativeErrorCode + ")",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            catch (Exception)
            {
                MessageBox.Show("Impossible de lancer Ray Desktop avec ce compte.",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        /// <summary>日志目录：优先 %LOCALAPPDATA%\RayDesktop\Logs（用户始终可写），失败时回退 exe 同目录。</summary>
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
                                mutex.WaitOne();
                                ownsMutex = true;
                            }
                            catch (AbandonedMutexException)
                            {
                                // The previous process exited while logging; this process now owns the mutex.
                                ownsMutex = true;
                            }

                            string path = Path.Combine(LogDirectory, "child-session.log");
                            if (!logInitialized)
                            {
                                string existing = string.Empty;
                                if (File.Exists(path))
                                {
                                    using (var reader = new StreamReader(path, Encoding.UTF8, true))
                                        existing = reader.ReadToEnd();
                                }
                                existing = existing.Replace("\0", string.Empty);
                                byte[] preamble = new UTF8Encoding(true).GetPreamble();
                                byte[] content = Encoding.UTF8.GetBytes(existing);
                                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                                {
                                    stream.Write(preamble, 0, preamble.Length);
                                    stream.Write(content, 0, content.Length);
                                }
                                logInitialized = true;
                            }

                            // 日志体积治理：超过 5MB 归档为 .1 并重建，避免长期高频轮询持续膨胀（P2-3 兜底）。
                            const long MaxLogBytes = 5 * 1024 * 1024;
                            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                            {
                                string archived = path + ".1";
                                if (File.Exists(archived)) File.Delete(archived);
                                File.Move(path, archived);
                                logInitialized = false;
                            }

                            string cleanMessage = (message ?? string.Empty).Replace("\0", string.Empty);
                            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " +
                                cleanMessage + Environment.NewLine, new UTF8Encoding(false));
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
