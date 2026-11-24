using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NexusBoardSetup
{
    static class Program
    {
        static string AppDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusBoard"); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();

            if (args.Length > 0 && args[0] == "/uninstall")
            {
                Uninstall();
                return;
            }

            try
            {
                Directory.CreateDirectory(AppDir);
                string exe = Path.Combine(AppDir, "NexusBoard.exe");
                string self = Path.Combine(AppDir, "uninstall.exe");

                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("NexusBoard.exe"))
                using (var f = File.Create(exe))
                {
                    s.CopyTo(f);
                }

                try
                {
                    File.Copy(Application.ExecutablePath, self, true);
                }
                catch { }

                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nexus Board.lnk"), exe);
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus Board.lnk"), exe);

                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NexusBoard"))
                {
                    key.SetValue("DisplayName", "Nexus Board");
                    key.SetValue("DisplayVersion", "1.2.0");
                    key.SetValue("Publisher", "Nexus x АутSquad");
                    key.SetValue("InstallLocation", AppDir);
                    key.SetValue("DisplayIcon", exe);
                    key.SetValue("UninstallString", "\"" + self + "\" /uninstall");
                    key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }

                MessageBox.Show(
                    "Nexus Board установлен.\n\nЯрлык на рабочем столе и в меню «Пуск».\nДоска создастся при первом запуске — ключ покажется в шапке.",
                    "Nexus Board",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                Process.Start(exe);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось установить: " + ex.Message, "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void Shortcut(string path, string target)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Nexus Board" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        static void Uninstall()
        {
            try
            {
                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nexus Board.lnk");
                string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus Board.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
                if (File.Exists(start)) File.Delete(start);
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NexusBoard", false);

                string self = Application.ExecutablePath;
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusBoard");
                if (Directory.Exists(cfg)) Directory.Delete(cfg, true);

                foreach (var f in Directory.GetFiles(AppDir))
                {
                    if (string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(f); } catch { }
                }

                var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + AppDir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                MessageBox.Show("Nexus Board удалён.", "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Удаление не завершено: " + ex.Message, "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
