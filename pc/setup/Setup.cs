using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NexusBoardSetup
{
    static class Program
    {
        const string ExeName = "NexusBoard";
        const string AppExe = "NexusBoard.exe";
        const string SelfName = "uninstall.exe";
        const string FallbackVersion = "2.1.1";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "NexusBoard";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NexusBoard";

        static bool Ru = true;

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
        {
            { "title", new[] { "Nexus Board", "Nexus Board" } },
            { "installed", new[] {
                "Nexus Board установлен.\n\nЯрлык на рабочем столе и в меню «Пуск».\nДоска создастся при первом запуске — ключ покажется в шапке.",
                "Nexus Board is installed.\n\nShortcuts are on the desktop and in the Start menu.\nThe board appears on first launch — the key is shown in the header." } },
            { "fail", new[] { "Не удалось установить: ", "Install failed: " } },
            { "nopayload", new[] { "В установщике нет NexusBoard.exe.", "NexusBoard.exe is missing from the installer." } },
            { "running", new[] { "Nexus Board запущен. Закрыть его и продолжить?", "Nexus Board is running. Close it and continue?" } },
            { "stillrun", new[] {
                "Не удалось закрыть Nexus Board. Закрой его и запусти установку снова.",
                "Could not close Nexus Board. Close it and run the installer again." } },
            { "confirm", new[] { "Удалить Nexus Board и его данные?", "Remove Nexus Board and its data?" } },
            { "all", new[] { "Удалить всё", "Remove all" } },
            { "apponly", new[] { "Только программу", "App only" } },
            { "cancel", new[] { "Отмена", "Cancel" } },
            { "removed", new[] { "Nexus Board удалён.", "Nexus Board has been removed." } },
            { "removefail", new[] { "Удаление не завершено: ", "Uninstall did not finish: " } },
            { "shortcut", new[] { "Nexus Board — доска задач ИИ", "Nexus Board — AI task board" } }
        };

        static string T(string key)
        {
            string[] v;
            if (!S.TryGetValue(key, out v)) return key;
            return Ru ? v[0] : v[1];
        }

        static string LocalRoot
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        static string AppDir
        {
            get { return Path.Combine(LocalRoot, "NexusBoard"); }
        }

        static string DataDir
        {
            get { return Path.Combine(AppDir, "data"); }
        }

        static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusBoard"); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

            if (args.Length > 0 && (args[0] == "/uninstall" || args[0] == "--uninstall" || args[0] == "/u"))
            {
                Uninstall();
                return;
            }

            Install();
        }

        static void Install()
        {
            try
            {
                if (IsAppRunning())
                {
                    if (MessageBox.Show(T("running"), T("title"), MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                    {
                        return;
                    }
                    CloseApp();
                    if (IsAppRunning())
                    {
                        MessageBox.Show(T("stillrun"), T("title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                Directory.CreateDirectory(AppDir);
                string exe = Path.Combine(AppDir, AppExe);
                string self = Path.Combine(AppDir, SelfName);

                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(AppExe))
                {
                    if (s == null)
                    {
                        MessageBox.Show(T("nopayload"), T("title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    using (FileStream f = File.Create(exe)) s.CopyTo(f);
                }

                try
                {
                    if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(Application.ExecutablePath, self, true);
                    }
                }
                catch (Exception) { }

                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nexus Board.lnk"), exe);
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus Board.lnk"), exe);

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Nexus Board");
                        key.SetValue("DisplayVersion", DetectVersion(exe));
                        key.SetValue("Publisher", "Nexus x АутSquad");
                        key.SetValue("InstallLocation", AppDir);
                        key.SetValue("DisplayIcon", exe);
                        key.SetValue("UninstallString", "\"" + self + "\" /uninstall");
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }

                MessageBox.Show(T("installed"), T("title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start(exe);
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("fail") + ex.Message, T("title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string DetectVersion(string exe)
        {
            try
            {
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(exe);
                    string v = (vi.FileVersion ?? "").Trim();
                    if (v.Length == 0 || v.StartsWith("0.0.0")) v = (vi.ProductVersion ?? "").Trim();
                    if (v.Length > 0 && !v.StartsWith("0.0.0")) return v;
                }
            }
            catch (Exception) { }
            return FallbackVersion;
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
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { T("shortcut") });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception) { }
        }

        static bool IsAppRunning()
        {
            try { return Process.GetProcessesByName(ExeName).Length > 0; }
            catch (Exception) { return false; }
        }

        static void CloseApp()
        {
            Process[] ps;
            try { ps = Process.GetProcessesByName(ExeName); }
            catch (Exception) { return; }

            for (int i = 0; i < ps.Length; i++)
            {
                try
                {
                    if (!ps[i].HasExited) ps[i].CloseMainWindow();
                }
                catch (Exception) { }
            }
            for (int i = 0; i < 25 && IsAppRunning(); i++) Thread.Sleep(200);
            if (!IsAppRunning()) return;

            try { ps = Process.GetProcessesByName(ExeName); }
            catch (Exception) { return; }
            for (int i = 0; i < ps.Length; i++)
            {
                try { ps[i].Kill(); }
                catch (Exception) { }
            }
            for (int i = 0; i < 25 && IsAppRunning(); i++) Thread.Sleep(200);
        }

        static void Uninstall()
        {
            CloseApp();

            int choice;
            using (AskForm dlg = new AskForm(T("title"), T("confirm"), T("all"), T("apponly"), T("cancel")))
            {
                dlg.ShowDialog();
                choice = dlg.Choice;
            }
            if (choice == 0) return;

            bool full = choice == 1;

            try
            {
                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nexus Board.lnk");
                string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus Board.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
                if (File.Exists(start)) File.Delete(start);

                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); }
                catch (Exception) { }

                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    {
                        if (k != null) k.DeleteValue(RunValue, false);
                    }
                }
                catch (Exception) { }

                if (full)
                {
                    try
                    {
                        if (Directory.Exists(ConfigDir)) Directory.Delete(ConfigDir, true);
                    }
                    catch (Exception) { }
                }

                RunCleanup(full);
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("removefail") + ex.Message, T("title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(T("removed"), T("title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static void RunCleanup(bool full)
        {
            string temp = Path.GetTempPath();
            string script = Path.Combine(temp, "nexusboard-uninstall.cmd");
            string exe = Path.Combine(AppDir, AppExe);
            string self = Path.Combine(AppDir, SelfName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("ping 127.0.0.1 -n 3 >nul");
            sb.AppendLine("set /a n=0");
            sb.AppendLine(":retry");
            sb.AppendLine("set /a n+=1");
            if (full)
            {
                sb.AppendLine("rmdir /s /q \"" + AppDir + "\" >nul 2>&1");
                sb.AppendLine("rmdir /s /q \"" + DataDir + "\" >nul 2>&1");
                sb.AppendLine("rmdir /s /q \"" + ConfigDir + "\" >nul 2>&1");
                sb.AppendLine("if exist \"" + AppDir + "\" if %n% LSS 20 (ping 127.0.0.1 -n 2 >nul & goto retry)");
            }
            else
            {
                sb.AppendLine("del /f /q \"" + exe + "\" >nul 2>&1");
                sb.AppendLine("del /f /q \"" + self + "\" >nul 2>&1");
                sb.AppendLine("rmdir \"" + AppDir + "\" >nul 2>&1");
                sb.AppendLine("if exist \"" + exe + "\" if %n% LSS 20 (ping 127.0.0.1 -n 2 >nul & goto retry)");
            }
            sb.AppendLine("del /f /q \"%~f0\" >nul 2>&1");

            File.WriteAllText(script, sb.ToString(), Encoding.Default);

            var psi = new ProcessStartInfo("cmd.exe", "/c \"\"" + script + "\"\"");
            psi.WorkingDirectory = temp;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }
    }

    sealed class AskForm : Form
    {
        public int Choice;

        public AskForm(string title, string question, string all, string appOnly, string cancel)
        {
            Choice = 0;
            Text = title;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 150);

            Label label = new Label();
            label.Text = question;
            label.SetBounds(16, 18, 428, 44);
            Controls.Add(label);

            Controls.Add(Mk(all, 16, 96, 140, 1));
            Controls.Add(Mk(appOnly, 164, 96, 150, 2));
            Button b3 = Mk(cancel, 322, 96, 122, 0);
            b3.DialogResult = DialogResult.Cancel;
            Controls.Add(b3);
            CancelButton = b3;
        }

        Button Mk(string text, int x, int y, int w, int choice)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 30);
            b.Click += (s, e) =>
            {
                Choice = choice;
                DialogResult = DialogResult.OK;
                Close();
            };
            return b;
        }
    }
}
