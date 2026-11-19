using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class RayMaintenanceLocale
{
    private const string Prefix = "--admin-account=";
    private const string Title = "Ray Maintenance Locale";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            if (args.Length != 1 || !args[0].StartsWith(Prefix, StringComparison.Ordinal))
                throw new ArgumentException(@"Utilisation : RayMaintenanceLocale.exe --admin-account=DOMAINE\utilisateur");
            string account = args[0].Substring(Prefix.Length);
            if (account.Length > 254 || !Regex.IsMatch(account,
                @"\A(?:[A-Za-z0-9._-]+\\[A-Za-z0-9._-]+|[A-Za-z0-9._-]+@[A-Za-z0-9.-]+)\z"))
                throw new ArgumentException(@"Compte attendu : DOMAINE\utilisateur, .\utilisateur ou utilisateur@domaine.");

            if (account.StartsWith(@".\", StringComparison.Ordinal))
                account = Environment.MachineName + account.Substring(1);

            using (var identity = WindowsIdentity.GetCurrent())
            {
                bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                if (!elevated)
                {
                    if (MessageBox.Show("Compte attendu : " + account + "\r\nSélectionne ce compte dans l’invite Windows.",
                        Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                        return 1223;

                    using (Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = Prefix + account,
                        WorkingDirectory = Environment.SystemDirectory,
                        UseShellExecute = true,
                        Verb = "runas"
                    })) { }
                    return 0;
                }

                var expected = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
                if (!expected.Equals(identity.User))
                    throw new UnauthorizedAccessException("Compte attendu : " + account +
                        "\r\nCompte utilisé par Windows : " + identity.Name);

                Application.Run(CreateWindow(identity.Name));
                return 0;
            }
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223) return 1223;
            return ShowError(ex);
        }
        catch (Exception ex)
        {
            return ShowError(ex);
        }
    }

    private static int ShowError(Exception ex)
    {
        MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }

    private static Form CreateWindow(string identity)
    {
        var form = new Form { Text = Title, StartPosition = FormStartPosition.CenterScreen,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximizeBox = false };
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = Environment.MachineName + " — " + identity, AutoSize = true });
        AddTool(panel, "Gestion de l’ordinateur", "mmc.exe", "compmgmt.msc");
        AddTool(panel, "Services", "mmc.exe", "services.msc");
        AddTool(panel, "Observateur d’événements", "mmc.exe", "eventvwr.msc");
        AddTool(panel, "Gestionnaire de périphériques", "mmc.exe", "devmgmt.msc");
        AddTool(panel, "Invite de commandes", "cmd.exe", null);
        form.Controls.Add(panel);
        return form;
    }

    private static void AddTool(FlowLayoutPanel panel, string label, string file, string snapIn)
    {
        var button = new Button { Text = label, Size = new Size(280, 32) };
        button.Click += delegate
        {
            try
            {
                string directory = Environment.SystemDirectory;
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(directory, file),
                    Arguments = snapIn == null ? "/d" : "\"" + Path.Combine(directory, snapIn) + "\"",
                    WorkingDirectory = directory,
                    UseShellExecute = false
                })) { }
            }
            catch (Exception ex) { ShowError(ex); }
        };
        panel.Controls.Add(button);
    }
}
