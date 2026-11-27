using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("Nexus Board")]
[assembly: System.Reflection.AssemblyProduct("Nexus Board")]
[assembly: System.Reflection.AssemblyCompany("Nexus x AutSquad")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.6")]
[assembly: System.Reflection.AssemblyVersion("2.2.6")]
[assembly: System.Reflection.AssemblyInformationalVersion("2.2.6")]

namespace NexusBoard
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        public const string Version = "2.2.6";
        public const int VersionCode = 22;
        public static bool OpenKeyOnStart = false;
        public const string UpdateArg = "/update";

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static void Main(string[] args)
        {
            if (NexusBoard.Cli.Cli.ShouldRunAsCli(args))
            {
                AttachConsole(-1);
                try
                {
                    int code = NexusBoard.Cli.Cli.Run(args);
                    Environment.ExitCode = code;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERR " + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            // неизвестный флаг раньше поднимал окно и висел — печатаем справку
            if (args.Length >= 1 && args[0].StartsWith("--") &&
                args[0] != "--shot" && args[0] != "--shotkey" && args[0] != "--theme" &&
                args[0] != "--lang" && args[0] != "--view" && args[0] != "--open-key")
            {
                AttachConsole(-1);
                Console.WriteLine(NexusBoard.Cli.Cli.HelpText());
                Environment.ExitCode = 1;
                return;
            }

            if (args.Length >= 2 && args[0] == UpdateArg)
            {
                try
                {
                    string target = args[1];
                    string self = Application.ExecutablePath;
                    bool copied = false;
                    for (int i = 0; i < 40; i++)
                    {
                        try
                        {
                            File.Copy(self, target, true);
                            copied = true;
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(500);
                        }
                    }
                    if (!copied)
                    {
                        System.Windows.Forms.MessageBox.Show(
                            "Не удалось заменить " + target + " — обновление не установлено.",
                            "Nexus Board", System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning);
                        return;
                    }
                    System.Diagnostics.Process.Start(target);
                    // временный exe обновления больше не нужен; удаляем его
                    // после выхода текущего процесса
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(
                            "cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + self + "\"");
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "Nexus Board",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
                return;
            }

            ServicePointManager.DefaultConnectionLimit = 8;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            }
            catch
            {
                try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
            }

            if (args.Length >= 1 && args[0] == "/selftest")
            {
                try
                {
                    var f = new MainForm();
                    f.SelfCheck();
                    Console.WriteLine("SELFTEST OK");
                }
                catch (Exception ex)
                {
                    try
                    {
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexusboard-selftest.txt"),
                            "SELFTEST FAIL " + ex);
                    }
                    catch { }
                }
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexusboard-crash.log"),
                        DateTime.Now.ToString("s") + " UNHANDLED " + e.ExceptionObject + Environment.NewLine);
                }
                catch { }
            };
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexusboard-crash.log"),
                        DateTime.Now.ToString("s") + " THREAD " + e.Exception + Environment.NewLine);
                }
                catch { }
                MessageBox.Show(e.Exception.ToString(), "Nexus Board");
                Application.Exit();
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string shot = null, shotTheme = null, shotLang = null, shotView = null;
            bool openKey = args.Length > 0 && args[0] == "--open-key";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--theme" && i + 1 < args.Length) shotTheme = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length) shotLang = args[++i];
                else if (args[i] == "--view" && i + 1 < args.Length) shotView = args[++i];
            }
            OpenKeyOnStart = openKey;
            string shotKey = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--shotkey") shotKey = args[i + 1];
            if (shotKey != null)
            {
                string thName = "midnight";
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--theme") thName = args[i + 1];
                L.Lang = Array.IndexOf(args, "en") >= 0 ? "en" : "ru";
                var dlg = new KeyDialog(Theme.Get(thName), "0123456789abcdef");
                dlg.Shown += (s2, e2) =>
                {
                    var tmr = new System.Windows.Forms.Timer { Interval = 900 };
                    tmr.Tick += (s3, e3) =>
                    {
                        tmr.Stop();
                        try
                        {
                            var r = dlg.Bounds;
                            using (var bmp = new Bitmap(r.Width, r.Height))
                            {
                                using (var g = Graphics.FromImage(bmp))
                                {
                                    IntPtr hdc = g.GetHdc();
                                    bool ok = PrintWindow(dlg.Handle, hdc, 2);
                                    g.ReleaseHdc(hdc);
                                }
                                bmp.Save(shotKey, System.Drawing.Imaging.ImageFormat.Png);
                            }
                            System.IO.File.WriteAllText(shotKey + ".ok", "ok");
                        }
                        catch (Exception ex) { try { System.IO.File.WriteAllText(shotKey + ".err", ex.Message); } catch { } }
                        dlg.Close();
                    };
                    tmr.Start();
                };
                Application.Run(dlg);
                return;
            }
            if (shot != null)
            {
                var f = new MainForm();
                f.ShotLook(shotTheme, shotLang, shotView);
                try { Cursor.Position = new Point(0, 0); } catch { }
                var worker = new System.Threading.Thread(() =>
                {
                    System.Threading.Thread.Sleep(3000);
                    try
                    {
                        f.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                try { f.Activate(); SetForegroundWindow(f.Handle); } catch { }
                                System.Threading.Thread.Sleep(400);
                                var r = f.Bounds;
                                using (var bmp = new Bitmap(r.Width, r.Height))
                                {
                                    bool ok = false;
                                    try
                                    {
                                        using (var g = Graphics.FromImage(bmp))
                                        {
                                            IntPtr hdc = g.GetHdc();
                                            ok = PrintWindow(f.Handle, hdc, 2);
                                            g.ReleaseHdc(hdc);
                                        }
                                    }
                                    catch { ok = false; }
                                    bool blank = true;
                                    if (ok)
                                    {
                                        Color first = bmp.GetPixel(4, 4);
                                        for (int x = 4; x < bmp.Width - 4 && blank; x += 37)
                                            for (int y = 4; y < bmp.Height - 4 && blank; y += 29)
                                                if (bmp.GetPixel(x, y) != first) blank = false;
                                    }
                                    if (!ok || blank)
                                    {
                                        using (var g = Graphics.FromImage(bmp))
                                            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
                                    }
                                    var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(shot));
                                    if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                                    bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                                }
                                System.IO.File.WriteAllText(shot + ".ok", "ok");
                            }
                            catch (Exception ex) { try { System.IO.File.WriteAllText(shot + ".err", ex.Message); } catch { } }
                            f.Close();
                        }));
                    }
                    catch { try { f.Close(); } catch { } }
                });
                worker.IsBackground = true;
                worker.Start();
                Application.Run(f);
                Environment.Exit(0);
                return;
            }

            var instance = NexusBoard.Cli.Ui.Acquire();
            if (instance == null)
            {
                NexusBoard.Cli.Ui.SignalShow();
                return;
            }
            Application.Run(new MainForm());
            GC.KeepAlive(instance);
        }
    }


    class CodeDialog : Form
    {
        readonly Api api;
        readonly string board;
        readonly string id;
        readonly Theme th;
        TextBox box;
        Label meta;
        Button pullBtn;
        int version = 1;

        public CodeDialog(Api api, string board, Theme th, string id, string title)
        {
            this.api = api;
            this.board = board;
            this.th = th;
            this.id = id;
            Text = string.IsNullOrEmpty(title) ? "Nexus · код" : "Nexus · " + title;
            Width = 980;
            Height = 720;
            MinimumSize = new Size(640, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = th.Back;
            ForeColor = th.Text;
            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);

            box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsTab = true,
                BorderStyle = BorderStyle.None,
                BackColor = th.Name == "frost" ? Color.FromArgb(248, 249, 253) : Color.FromArgb(12, 10, 20),
                ForeColor = th.Text,
                Font = FontCache.Get("Consolas", 10f, FontStyle.Regular),
                Dock = DockStyle.Fill
            };
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 8), BackColor = th.Back };
            host.Controls.Add(box);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = th.Back };
            meta = new Label
            {
                AutoSize = false,
                Width = 420,
                Height = 40,
                Location = new Point(18, 16),
                ForeColor = th.Dim,
                BackColor = Color.Transparent,
                Font = FontCache.Get("Consolas", 9f, FontStyle.Regular),
                Text = "…"
            };
            bar.Controls.Add(meta);
            pullBtn = MkButton("забрать", 470, (s2, e2) => Pull());
            bar.Controls.Add(pullBtn);
            bar.Controls.Add(MkButton("сохранить", 610, (s2, e2) => Save()));
            bar.Controls.Add(MkButton("закрыть", 750, (s2, e2) => Close()));

            Controls.Add(host);
            Controls.Add(bar);
            Shown += (s2, e2) => Pull();
        }

        Button MkButton(string text, int x, EventHandler click)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(130, 36),
                Location = new Point(x, 14),
                FlatStyle = FlatStyle.Flat,
                BackColor = th.Card,
                ForeColor = th.Text,
                Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = th.Line;
            b.Click += click;
            return b;
        }

        bool UiAlive { get { return !IsDisposed && IsHandleCreated; } }

        void Ui(Action action)
        {
            try
            {
                if (!UiAlive) return;
                BeginInvoke(action);
            }
            catch { }
        }

        void Pull()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var o = api.CodeGet(board, id);
                    var code = o != null && o.ContainsKey("code") ? o["code"] as Dictionary<string, object> : null;
                    if (code == null) return;
                    string content = Convert.ToString(code["content"]);
                    int ver = Convert.ToInt32(code["version"]);
                    string sha = Convert.ToString(code["sha"]);
                    Ui(() =>
                    {
                        version = ver;
                        box.Text = content;
                        meta.Text = "v" + ver + "  sha=" + sha;
                        pullBtn.Text = "забрать v" + ver;
                    });
                }
                catch (Exception ex)
                {
                    Ui(() => meta.Text = "ERR " + ex.Message);
                }
            });
        }

        void Save()
        {
            string content = box.Text;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string raw;
                try { raw = api.CodeSave(board, id, content, version); }
                catch (Exception ex)
                {
                    Ui(() => meta.Text = "ERR " + ex.Message);
                    return;
                }
                var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                bool ok = o != null && o.ContainsKey("ok") && Convert.ToBoolean(o["ok"]);
                if (ok)
                {
                    int ver = Convert.ToInt32(o["version"]);
                    string sha = Convert.ToString(o["sha"]);
                    Ui(() =>
                    {
                        version = ver;
                        meta.Text = "сохранено: v" + ver + "  sha=" + sha;
                        pullBtn.Text = "забрать v" + ver;
                    });
                    return;
                }
                string err = o != null && o.ContainsKey("error") ? Convert.ToString(o["error"]) : raw;
                if (err == "conflict")
                {
                    string fresh = o.ContainsKey("content") ? Convert.ToString(o["content"]) : "";
                    int ver = o.ContainsKey("version") ? Convert.ToInt32(o["version"]) : version;
                    Ui(() =>
                    {
                        MessageBox.Show(this,
                            "На сервере уже более новая версия (v" + ver + ").\nСейчас подтяну её — свою правку скопируй заранее.",
                            "Nexus · конфликт версий", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        version = ver;
                        if (!string.IsNullOrEmpty(fresh)) box.Text = fresh;
                        meta.Text = "конфликт: подтянул v" + ver;
                        pullBtn.Text = "забрать v" + ver;
                    });
                    return;
                }
                Ui(() => meta.Text = "ERR " + err);
            });
        }
    }

    static class FontCache
    {
        static readonly System.Collections.Generic.Dictionary<string, Font> map =
            new System.Collections.Generic.Dictionary<string, Font>();

        public static Font Get(string name, float size, FontStyle style)
        {
            string key = name + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            Font f;
            if (!map.TryGetValue(key, out f))
            {
                f = new Font(name, size, style);
                map[key] = f;
            }
            return f;
        }
    }

    class Theme
    {
        public string Name;
        public Color Back, Back2, Panel, Card, Line, Text, Dim, Accent, AccentText, Chip;

        public static readonly Theme[] All =
        {
            new Theme
            {
                Name = "midnight",
                Back = Color.FromArgb(13, 11, 22), Back2 = Color.FromArgb(24, 18, 40), Panel = Color.FromArgb(23, 20, 36),
                Card = Color.FromArgb(31, 27, 47), Line = Color.FromArgb(52, 46, 76),
                Text = Color.FromArgb(240, 238, 250), Dim = Color.FromArgb(150, 143, 180),
                Accent = Color.FromArgb(139, 108, 255), AccentText = Color.White, Chip = Color.FromArgb(58, 50, 88)
            },
            new Theme
            {
                Name = "frost",
                Back = Color.FromArgb(246, 248, 253), Back2 = Color.FromArgb(233, 240, 252), Panel = Color.White,
                Card = Color.FromArgb(255, 255, 255), Line = Color.FromArgb(214, 223, 238),
                Text = Color.FromArgb(22, 30, 48), Dim = Color.FromArgb(108, 122, 148),
                Accent = Color.FromArgb(37, 99, 235), AccentText = Color.White, Chip = Color.FromArgb(226, 236, 252)
            },
            new Theme
            {
                Name = "graphite",
                Back = Color.FromArgb(26, 29, 30), Back2 = Color.FromArgb(34, 38, 40), Panel = Color.FromArgb(38, 41, 42),
                Card = Color.FromArgb(45, 48, 49), Line = Color.FromArgb(62, 66, 67),
                Text = Color.FromArgb(236, 239, 238), Dim = Color.FromArgb(146, 154, 152),
                Accent = Color.FromArgb(84, 214, 168), AccentText = Color.FromArgb(14, 24, 20), Chip = Color.FromArgb(56, 62, 61)
            },
        };

        public static Theme Get(string name)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Name == name) return All[i];
            return All[0];
        }
    }

    class Settings
    {
        public string Base = "https://board.nexus-your-heper.ru";
        public string Board = "";
        public string Key = "";
        public string Theme = "midnight";
        public string Lang = "ru";
        public string AutoUpdatedTo = "";

        static string File_
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusBoard");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        public static Settings Load()
        {
            try
            {
                string raw = File.ReadAllText(File_, Encoding.UTF8);
                var s = new JavaScriptSerializer().Deserialize<Settings>(raw);
                if (s != null) return s;
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(File_, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
            }
            catch { }
        }
    }

    static class L
    {
        static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>
        {
            { "app", new[] { "Nexus Board", "Nexus Board" } },
            { "all", new[] { "Всё", "All" } },
            { "today", new[] { "сегодня", "today" } },
            { "refresh", new[] { "Обновить", "Refresh" } },
            { "add", new[] { "+ Задача", "+ Task" } },
            { "theme", new[] { "Тема", "Theme" } },
            { "settings", new[] { "Настройки", "Settings" } },
            { "loading", new[] { "Загрузка…", "Loading…" } },
            { "empty", new[] { "Пока пусто. Добавь задачу или подключи ИИ по ключу.", "Nothing yet. Add a task or connect an AI with the key." } },
            { "key", new[] { "ключ", "key" } },
            { "server", new[] { "Адрес сервера", "Server URL" } },
            { "sync", new[] { "Ключ синхронизации доски", "Board sync key" } },
            { "sync_hint", new[] { "Один и тот же ключ на ПК и телефоне — одна доска.", "Same key on PC and phone means one shared board." } },
            { "newboard", new[] { "Создать новую доску", "Create a new board" } },
            { "copied", new[] { "Ключ скопирован", "Key copied" } },
            { "save", new[] { "Сохранить", "Save" } },
            { "cancel", new[] { "Отмена", "Cancel" } },
            { "title", new[] { "Заголовок", "Title" } },
            { "note", new[] { "Заметка", "Note" } },
            { "section", new[] { "Раздел", "Section" } },
            { "status", new[] { "Статус", "Status" } },
            { "progress", new[] { "Готово, %", "Progress, %" } },
            { "delete", new[] { "Удалить", "Delete" } },
            { "newsect", new[] { "Новый раздел", "New section" } },
            { "sectname", new[] { "Название раздела", "Section name" } },
            { "update", new[] { "Доступна версия", "Version available" } },
            { "doupdate", new[] { "Обновить", "Update" } },
            { "later", new[] { "Позже", "Later" } },
            { "offline", new[] { "нет связи с сервером", "server unreachable" } },
            { "about", new[] { "Доска задач, которую заполняет твой ИИ. Ключ общий с телефоном.", "A task board your own AI fills in. The key is shared with the phone." } },
            { "todo", new[] { "дальше", "next" } },
            { "doing", new[] { "сейчас", "now" } },
            { "blocked", new[] { "ждёт", "waiting" } },
            { "done", new[] { "готово", "done" } },
            { "copy", new[] { "Копировать", "Copy" } },
            { "err", new[] { "Ошибка", "Error" } },
            { "tab_board", new[] { "Доска", "Board" } },
            { "tab_train", new[] { "Обучение", "Training" } },
            { "tab_views", new[] { "Разделы", "Sections" } },
            { "tab_chat", new[] { "Чат", "Chat" } },
            { "chat_hint", new[] { "Написать ИИ…", "Message the AI…" } },
            { "chat_send", new[] { "Отправить", "Send" } },
            { "chat_empty", new[] { "Пока пусто. Напиши первым — я отвечу здесь.", "Nothing yet. Say hi — I will answer here." } },
            { "chat_me", new[] { "ты", "you" } },
            { "chat_ai", new[] { "ИИ", "AI" } },
            { "tr_running", new[] { "идёт обучение", "training in progress" } },
            { "tr_dead", new[] { "обучение остановлено", "training is not running" } },
            { "tr_loss", new[] { "loss", "loss" } },
            { "tr_eta", new[] { "осталось", "eta" } },
            { "tr_rate", new[] { "скорость", "rate" } },
            { "tr_step", new[] { "шаг", "step" } },
            { "tr_chain", new[] { "Цепочка", "Chain" } },
            { "tr_log", new[] { "Хвост лога", "Log tail" } },
            { "tr_nokey", new[] { "Добавь ключ Nexus в настройках, чтобы видеть обучение.", "Add a Nexus key in settings to see training." } },
            { "connect", new[] { "Как подключить", "How to connect" } },
            { "nexus_key", new[] { "Ключ Nexus (включает вкладку «Обучение»)", "Nexus key (unlocks the Training tab)" } },
            { "nexus_hint", new[] { "необязательно", "optional" } },
            { "connect_body", new[] { "1. Это личная доска, ключ ниже.\n2. Тот же ключ на телефоне (долгое нажатие по заголовку) — одна доска на двух устройствах.\n3. ИИ заполняет её запросами POST /api/b/add?b=КЛЮЧ (title, note, status, progress).\n4. Вкладки-разделы: POST /api/b/section/add?b=КЛЮЧ.\n5. Данные идут через сайт, поэтому работает в любой сети.", "1. This is a personal board; its key is below.\n2. The same key on the phone (long-press the title) gives one board on both devices.\n3. An AI fills it with POST /api/b/add?b=KEY (title, note, status, progress).\n4. Tabs: POST /api/b/section/add?b=KEY.\n5. Data goes through the website, so it works on any network." } },
        };

        public static string Lang = "ru";

        public static string T(string key)
        {
            if (!D.ContainsKey(key)) return key;
            return Lang == "en" ? D[key][1] : D[key][0];
        }
    }

    class TimeoutWebClient : WebClient
    {
        public int TimeoutMs = 8000;

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest r = base.GetWebRequest(address);
            if (r != null) r.Timeout = TimeoutMs;
            return r;
        }
    }

    class Api
    {
        public string Base;
        public string Key = "";

        public Api(string baseUrl)
        {
            Base = baseUrl.TrimEnd('/');
        }

        string WithKey(string path)
        {
            if (string.IsNullOrEmpty(Key)) return path;
            if (path.Contains("k=")) return path;
            if (!path.StartsWith("/api/b/") && !path.StartsWith("/api/c/")) return path;
            return path + (path.Contains("?") ? "&" : "?") + "k=" + Uri.EscapeDataString(Key);
        }

        string Get(string path)
        {
            using (var wc = new TimeoutWebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("User-Agent", "NexusBoardPC/" + Program.Version);
                return wc.DownloadString(Base + WithKey(path));
            }
        }

        string Post(string path, string json)
        {
            using (var wc = new TimeoutWebClient { TimeoutMs = 15000 })
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("Content-Type", "application/json");
                wc.Headers.Add("User-Agent", "NexusBoardPC/" + Program.Version);
                return wc.UploadString(Base + WithKey(path), json);
            }
        }

        public string NewBoard()
        {
            var o = new JavaScriptSerializer().DeserializeObject(Post("/api/b/new", "{}")) as Dictionary<string, object>;
            return o != null && o.ContainsKey("id") ? Convert.ToString(o["id"]) : "";
        }

        public Dictionary<string, object> Train()
        {
            string path = "/api/train" + (string.IsNullOrEmpty(Key) ? "" : "?k=" + Key);
            return new JavaScriptSerializer().DeserializeObject(Get(path)) as Dictionary<string, object>;
        }

        public Dictionary<string, object> Snapshot(string board)
        {
            return new JavaScriptSerializer().DeserializeObject(Get("/api/b/tasks?b=" + board)) as Dictionary<string, object>;
        }

        public void AddTask(string board, string title, string note, string section, string status, int progress)
        {
            var d = new Dictionary<string, object>
            {
                { "title", title }, { "note", note }, { "section", section },
                { "status", status }, { "progress", progress }, { "mode", "task" }
            };
            Post("/api/b/add?b=" + board, new JavaScriptSerializer().Serialize(d));
        }

        public void UpdateTask(string board, string id, Dictionary<string, object> fields)
        {
            fields["id"] = id;
            Post("/api/b/update?b=" + board, new JavaScriptSerializer().Serialize(fields));
        }

        public void AddSection(string board, string name)
        {
            Post("/api/b/section/add?b=" + board, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "name", name } }));
        }

        /// <summary>Возвращает id созданного раздела (сервер сам его выдаёт) или "".</summary>
        public string AddSectionEx(string board, string name)
        {
            try
            {
                string raw = Post("/api/b/section/add?b=" + board,
                    new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "name", name } }));
                var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                var sec = o != null && o.ContainsKey("section") ? o["section"] as Dictionary<string, object> : null;
                if (sec != null && sec.ContainsKey("id")) return Convert.ToString(sec["id"]);
            }
            catch { }
            return "";
        }

        int PostStatus(string path, string json)
        {
            try
            {
                Post(path, json);
                return 200;
            }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                return r == null ? 0 : (int)r.StatusCode;
            }
            catch { return 0; }
        }

        /// <summary>POST /api/b/delete — настоящие удаление задачи. 200 ок, 404 — старый сервер.</summary>
        public int DeleteTask(string board, string id)
        {
            return PostStatus("/api/b/delete?b=" + board,
                new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "id", id } }));
        }

        /// <summary>POST /api/b/section/set — переименование раздела. 404 — маршрута нет.</summary>
        public int SectionSave(string board, string id, string name)
        {
            return PostStatus("/api/b/section/set?b=" + board,
                new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "id", id }, { "name", name } }));
        }

        public int SectionDel(string board, string id)
        {
            return PostStatus("/api/b/section/del?b=" + board,
                new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "id", id } }));
        }

        public Dictionary<string, object> ChatList(string board, long since)
        {
            return new JavaScriptSerializer().DeserializeObject(
                Get("/api/c/list?board=" + board + "&since=" + since + "&limit=200")) as Dictionary<string, object>;
        }

        public void ChatSend(string board, string from, string text)
        {
            var d = new Dictionary<string, object> { { "board", board }, { "from", from }, { "text", text } };
            Post("/api/c/send", new JavaScriptSerializer().Serialize(d));
        }

        public Dictionary<string, object> Views(string board)
        {
            return new JavaScriptSerializer().DeserializeObject(Get("/api/b/views?b=" + board)) as Dictionary<string, object>;
        }

        public Dictionary<string, object> Activity(string board)
        {
            try { return new JavaScriptSerializer().DeserializeObject(Get("/api/b/activity?b=" + board)) as Dictionary<string, object>; }
            catch { return null; }
        }

        public Dictionary<string, object> CodeGet(string board, string id)
        {
            return new JavaScriptSerializer().DeserializeObject(Get("/api/b/code?b=" + board + "&id=" + id)) as Dictionary<string, object>;
        }

        public string CodeSave(string board, string id, string content, int ifVersion)
        {
            var d = new Dictionary<string, object> { { "id", id }, { "content", content }, { "ifVersion", ifVersion } };
            string body = new JavaScriptSerializer().Serialize(d);
            using (var wc = new TimeoutWebClient { TimeoutMs = 15000 })
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("Content-Type", "application/json");
                wc.Headers.Add("User-Agent", "NexusBoardPC/" + Program.Version);
                try
                {
                    return wc.UploadString(Base + WithKey("/api/b/code/save?b=" + board), body);
                }
                catch (WebException e)
                {
                    var resp = e.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
                    }
                    throw;
                }
            }
        }

        public string CheckUpdate()
        {
            try
            {
                return Get("/api/app/pc");
            }
            catch
            {
                return "";
            }
        }
    }

    class TaskItem
    {
        public string Id, Title, Note, Mode, Status, Section;
        public int Progress;
    }

    class SectionItem
    {
        public string Id, Name;
    }

    class ChatMsg
    {
        public string Id, From, Text;
        public long Ts;
    }

    class BufFlow : FlowLayoutPanel
    {
        public BufFlow()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }

    class Round : Panel
    {
        public int Radius = 14;
        public Color Fill = Color.White;
        public Color BorderColor = Color.Gray;

        public Round()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Path_(rect, Radius))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
                using (var p = new Pen(BorderColor)) g.DrawPath(p, path);
            }
        }

        
        public static GraphicsPath Path_(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    class FlatButton : Button
    {
        public Color HoverColor = Color.Empty;

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    class MainForm : Form
    {
        readonly Settings settings;
        Theme th;
        Api api;
        string boardId = "";
        List<TaskItem> tasks = new List<TaskItem>();
        List<SectionItem> sections = new List<SectionItem>();
        string activeSection = "";
        DateTime lastSync = DateTime.MinValue;
        bool owner = false;
        bool trainView = false;
        bool chatView = false;
        bool viewsView = false;
        List<Dictionary<string, object>> blocks = new List<Dictionary<string, object>>();
        List<Dictionary<string, object>> feed = new List<Dictionary<string, object>>();
        int listCardWidth = 680;
        string blocksVersion = "";
        List<ChatMsg> chatMsgs = new List<ChatMsg>();
        long chatLast = 0;
        Dictionary<string, object> train;

        Label lblTitle, lblSub;
        FlowLayoutPanel tabs, list;
        FlatButton btnRefresh, btnMenu, btnUpdate, btnKey;
        Panel chatBar;
        TextBox chatInput;
        FlatButton chatSend;
        string updateVersion = "", updateFile = "", updateSha = "";
        int updateCode;
        System.Windows.Forms.Timer timer, resizeTimer;
        ContextMenuStrip menu, sectionMenu;
        ToolTip tipUpdate;
        EventWaitHandle showWaitEvent;
        RegisteredWaitHandle showWaitRegistered;
        int renderedWidth;
        bool chatReloadPending;
        bool closing;

        public MainForm()
        {
            settings = Settings.Load();
            L.Lang = settings.Lang == "en" ? "en" : "ru";
            th = Theme.Get(settings.Theme);
            api = new Api(settings.Base);
            api.Key = settings.Key;
            boardId = settings.Board;

            Text = "Nexus Board";
            Width = 1120;
            Height = 780;
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);
            DoubleBuffered = true;
            KeyPreview = true;

            BuildUi();

            try
            {
                bool created;
                showWaitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, NexusBoard.Cli.Ui.ShowEventName, out created);
                showWaitRegistered = ThreadPool.RegisterWaitForSingleObject(showWaitEvent, (s, timedOut) =>
                {
                    Ui(() =>
                    {
                        if (!Visible) Show();
                        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                        Activate();
                        Program.SetForegroundWindow(Handle);
                    });
                }, null, Timeout.Infinite, false);
            }
            catch { }

            Shown += (s, e) =>
            {
                ApplyTheme();
                bool brokenKey = false;
                if (!string.IsNullOrEmpty(boardId) && !IsBoardKey(boardId))
                {
                    // ключ из старых настроек не hex — молча в «нет связи» не сидим
                    lblSub.Text = L.Lang == "en" ? "board key is broken — reconnect" : "ключ доски битый — подключи заново";
                    boardId = "";
                    settings.Board = "";
                    settings.Save();
                    brokenKey = true;
                }
                if (brokenKey)
                {
                    ShowConnect();
                    if (string.IsNullOrEmpty(boardId)) CreateBoard();
                }
                else if (string.IsNullOrEmpty(boardId)) CreateBoard();
                else LoadBoard();
                CheckUpdate();
                if (Program.OpenKeyOnStart)
                {
                    var t2 = new System.Windows.Forms.Timer { Interval = 1200 };
                    t2.Tick += (s2, e2) => { t2.Stop(); ShowConnect(); };
                    t2.Start();
                }
            };

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) { RefreshActive(); e.SuppressKeyPress = true; }
                if (e.Control && e.KeyCode == Keys.N && CanAddTask()) { EditTask(null); e.SuppressKeyPress = true; }
                if (e.Control && e.Alt && e.KeyCode == Keys.S) OpenSettings();
            };

            timer = new System.Windows.Forms.Timer { Interval = 10000 };
            timer.Tick += (s, e) =>
            {
                if (chatView) LoadChat(false);
                else if (trainView) LoadTrain();
                else if (viewsView) LoadViews();
                else LoadBoard();
            };
            timer.Start();
        }

        int boardBusy, chatBusy, trainBusy;

        bool UiAlive
        {
            get { return !closing && !IsDisposed && !Disposing && IsHandleCreated; }
        }

        /// <summary>Единственная точка возврата в UI-поток: при закрытом окне просто молчит.</summary>
        void Ui(Action action)
        {
            if (action == null) return;
            try
            {
                if (!UiAlive) return;
                BeginInvoke(action);
            }
            catch { }
        }

        bool HasNexusKey()
        {
            return settings != null && !string.IsNullOrEmpty((settings.Key ?? "").Trim());
        }

        bool CanAddTask()
        {
            return !chatView && !trainView && !viewsView && !string.IsNullOrEmpty(boardId);
        }

        void RefreshActive()
        {
            if (chatView) LoadChat(false);
            else if (trainView) LoadTrain();
            else if (viewsView) LoadViews();
            else LoadBoard();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            try { if (timer != null) timer.Stop(); } catch { }
            try { if (resizeTimer != null) resizeTimer.Stop(); } catch { }
            try
            {
                if (showWaitRegistered != null)
                {
                    showWaitRegistered.Unregister(null);
                    showWaitRegistered = null;
                }
            }
            catch { }
            try
            {
                if (showWaitEvent != null)
                {
                    showWaitEvent.Close();
                    showWaitEvent.Dispose();
                    showWaitEvent = null;
                }
            }
            catch { }
            try { if (tipUpdate != null) { tipUpdate.Dispose(); tipUpdate = null; } } catch { }
            try { if (sectionMenu != null) { sectionMenu.Dispose(); sectionMenu = null; } } catch { }
            try { if (menu != null) { menu.Dispose(); menu = null; } } catch { }
            base.OnFormClosing(e);
        }

        void BuildUi()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            var header = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Color.Transparent };
            Controls.Add(header);

            lblTitle = new Label
            {
                Text = "Nexus Board",
                AutoSize = true,
                Font = FontCache.Get("Segoe UI Semibold", 24f, FontStyle.Regular),
                Location = new Point(34, 24),
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblTitle);

            lblSub = new Label
            {
                AutoSize = true,
                Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular),
                Location = new Point(36, 68),
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblSub);

            btnMenu = new FlatButton { Text = "⋯", Font = FontCache.Get("Segoe UI Semibold", 15f, FontStyle.Regular), Size = new Size(46, 40) };
            btnMenu.Click += (s, e) => ShowMenu();
            header.Controls.Add(btnMenu);

            btnRefresh = new FlatButton { Text = "⟳", Font = FontCache.Get("Segoe UI", 14f, FontStyle.Regular), Size = new Size(46, 40) };
            btnRefresh.Click += (s, e) => RefreshActive();
            header.Controls.Add(btnRefresh);

            btnKey = new FlatButton { Text = "КЛЮЧ", Font = FontCache.Get("Segoe UI Semibold", 8.5f, FontStyle.Bold), Size = new Size(58, 40) };
            btnKey.Click += (s, e) => ShowConnect();
            header.Controls.Add(btnKey);

            btnUpdate = new FlatButton { Text = "↓", Size = new Size(46, 40), Visible = false };
            btnUpdate.Click += Click_Update;
            tipUpdate = new ToolTip();
            header.Controls.Add(btnUpdate);

            var bar = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Color.Transparent, Padding = new Padding(30, 4, 26, 4) };
            Controls.Add(bar);
            bar.BringToFront();
            tabs = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 2)
            };
            tabs.MouseWheel += (s, e) => ScrollTabs(-e.Delta);
            bar.Controls.Add(tabs);

            chatBar = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.Transparent, Visible = false };
            Controls.Add(chatBar);

            chatInput = new TextBox
            {
                Location = new Point(30, 16),
                Height = 38,
                BorderStyle = BorderStyle.FixedSingle,
                Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
            };
            chatInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    SendChat();
                }
            };
            chatBar.Controls.Add(chatInput);

            chatSend = new FlatButton { Text = L.T("chat_send"), Font = FontCache.Get("Segoe UI Semibold", 9.5f, FontStyle.Bold), Size = new Size(122, 38), Location = new Point(0, 16) };
            chatSend.Click += (s, e) => SendChat();
            chatBar.Controls.Add(chatSend);

            list = new BufFlow
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(30, 6, 24, 26),
                BackColor = Color.Transparent
            };
            Controls.Add(list);
            list.BringToFront();

            BuildMenu();

            resizeTimer = new System.Windows.Forms.Timer { Interval = 150 };
            resizeTimer.Tick += (s, e) =>
            {
                resizeTimer.Stop();
                LayoutHeader();
                if (list != null && renderedWidth != list.ClientSize.Width) Render();
            };
            Resize += (s, e) =>
            {
                LayoutHeader();
                if (resizeTimer != null)
                {
                    resizeTimer.Stop();
                    resizeTimer.Start();
                }
            };
            Shown += (s, e) => LayoutHeader();
        }

        static void Trace(string s)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexusboard-trace.txt"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine);
            }
            catch { }
        }

        static List<Dictionary<string, object>> SelfCheckBlocks()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var kind in new[] { "metric", "progress", "status", "text", "log", "links", "code", "tasks", "activity", "unknown_kind" })
            {
                var body = new Dictionary<string, object>();
                if (kind == "metric") { body["value"] = "1"; body["unit"] = "шт"; body["sub"] = "sub"; }
                if (kind == "progress") { body["value"] = 42; body["label"] = "label"; body["eta"] = "eta"; }
                if (kind == "status")
                {
                    body["items"] = new object[]
                    {
                        new Dictionary<string, object> { { "label", "a" }, { "state", "ok" }, { "note", "n" } },
                        new Dictionary<string, object> { { "label", "b" }, { "state", "warn" }, { "note", "n" } }
                    };
                }
                if (kind == "text") body["text"] = "текст\r\nвторая строка";
                if (kind == "links")
                {
                    body["items"] = new object[]
                    {
                        new Dictionary<string, object> { { "label", "l" }, { "url", "https://example.com" }, { "note", "note" } }
                    };
                }
                if (kind == "code") { body["filename"] = "a.diff"; body["content"] = "x"; body["version"] = 1; body["sha"] = "abc"; }
                list.Add(new Dictionary<string, object>
                {
                    { "id", "sc_" + kind }, { "kind", kind }, { "title", kind }, { "order", 0 }, { "size", "sm" }, { "body", body },
                    { "tail", new Dictionary<string, object> { { "lines", new object[] { "log line 1", "log line 2" } } } }
                });
            }
            return list;
        }

        public void SelfCheck()
        {
            Trace("selfcheck start");
            if (timer != null) timer.Stop();
            var savedKey = settings.Key;
            settings.Key = "selftest";
            train = new Dictionary<string, object>
            {
                { "alive", true }, { "run", "ada-v6" }, { "stage", "training" }, { "unit", "шагов" },
                { "pct", 44.7 }, { "step", 3073 }, { "total", 6875 }, { "loss", 0.522 },
                { "remain", "9:35:27" }, { "rate", "9.08 с/шаг" },
                { "chainLines", new object[] { "цепочка: этап 1 завершён", "next_v6s: старт ada-v6", "этап 1/1" } }
            };
            foreach (var lang in new[] { "ru", "en" })
            {
                L.Lang = lang;
                trainView = true;
                ApplyTheme();
                Render();
                trainView = false;
            }
            Trace("langs ok");
            settings.Key = savedKey;
            train = null;
            ApplyTheme();
            Render();
            Trace("reset ok");
            foreach (var th2 in Theme.All)
            {
                th = th2;
                ApplyTheme();
                Render();
            }
            L.Lang = L.Lang == "en" ? "ru" : "en";
            ApplyTheme();
            Render();
            L.Lang = L.Lang == "en" ? "ru" : "en";
            ApplyTheme();
            try
            {
                blocks = SelfCheckBlocks();
                feed = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { { "ts", "2026-11-25T21:58:31" }, { "kind", "add" }, { "text", "selfcheck" } }
                };
                viewsView = true;
                foreach (var th3 in Theme.All)
                {
                    th = th3;
                    ApplyTheme();
                    RenderViews();
                }
                viewsView = false;
                th = Theme.Get(settings.Theme);
                ApplyTheme();
                using (var dlg = new CodeDialog(api, boardId, th, "selftest", "selftest")) { }
                Trace("views+code dialog ok");
            }
            catch (Exception ex)
            {
                Trace("views/code FAIL " + ex.Message);
            }
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexusboard-selftest.txt"),
                    "SELFTEST OK themes=" + Theme.All.Length + " lang=" + L.Lang);
            }
            catch { }
        }

        void BuildMenu()
        {
            if (menu != null)
            {
                try { menu.Dispose(); } catch { }
                menu = null;
            }
            menu = new ContextMenuStrip { Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular), ShowImageMargin = false };
            menu.Opening += (s, e) => StyleMenu();
            menu.Items.Add(new ToolStripMenuItem("＋ " + (L.Lang == "en" ? "New task" : "Новая задача"), null, (s, e) => EditTask(null)));
            menu.Items.Add(new ToolStripMenuItem("＋ " + (L.Lang == "en" ? "New section" : "Новый раздел"), null, (s, e) => NewSection()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(L.T("refresh"), null, (s, e) => LoadBoard()));
            menu.Items.Add(new ToolStripMenuItem(L.T("theme"), null, Click_Theme));
            menu.Items.Add(new ToolStripMenuItem(L.Lang == "en" ? "Русский" : "English", null, Click_Lang));
            menu.Items.Add(new ToolStripMenuItem(L.T("settings"), null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem(L.T("connect"), null, (s, e) => ShowConnect()));
            if (boardId.Length > 0)
            {
                menu.Items.Add(new ToolStripMenuItem(L.Lang == "en" ? "Copy sync key" : "Скопировать ключ", null, (s, e) =>
                {
                    try { Clipboard.SetText(boardId); } catch { }
                }));
            }
        }

        void ShowMenu()
        {
            BuildMenu();
            menu.Show(btnMenu, new Point(0, btnMenu.Height + 4));
        }

        void StyleMenu()
        {
            StyleMenuStrip(menu);
        }

        void StyleMenuStrip(ContextMenuStrip ms)
        {
            if (ms == null) return;
            ms.BackColor = th.Panel;
            ms.ForeColor = th.Text;
            ms.Renderer = new ToolStripProfessionalRenderer(new MenuColors(th));
            foreach (ToolStripItem it in ms.Items)
            {
                it.ForeColor = th.Text;
                it.BackColor = th.Panel;
            }
        }

        void ScrollTabs(int delta)
        {
            if (tabs == null) return;
            try
            {
                var hs = tabs.HorizontalScroll;
                int max = Math.Max(0, hs.Maximum - hs.LargeChange + 1);
                int next = Math.Max(0, Math.Min(max, hs.Value + delta));
                if (next != hs.Value)
                {
                    tabs.HorizontalScroll.Value = next;
                    tabs.PerformLayout();
                }
            }
            catch { }
        }

        void LayoutHeader()
        {
            if (chatBar != null)
            {
                int w = Math.Max(200, chatBar.ClientSize.Width - 30 - 132);
                chatInput.Width = w;
                chatSend.Left = 30 + w + 12;
            }
            btnKey.Text = L.Lang == "en" ? "KEY" : "КЛЮЧ";
            int right = ClientSize.Width - 30;
            btnRefresh.Location = new Point(right - btnRefresh.Width, 26);
            btnMenu.Location = new Point(btnRefresh.Left - btnMenu.Width - 8, 26);
            btnUpdate.Location = new Point(btnMenu.Left - btnUpdate.Width - 8, 26);
            btnKey.Location = new Point(btnUpdate.Left - btnKey.Width - 8, 26);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), th.Back, th.Back2, 65f))
                g.FillRectangle(b, ClientRectangle);

            using (var path = new GraphicsPath())
            {
                int size = Math.Max(Math.Max(Width, Height), 900);
                var glow = new Rectangle(-size / 3, -size / 2, size, size);
                path.AddEllipse(glow);
                // PathGradientBrush на пустом пути GDI+ валит OutOfMemoryException —
                // именно из-за этого окно падало с «Недостаточно памяти»
                try
                {
                    using (var blend = new PathGradientBrush(path))
                    {
                        blend.CenterColor = Color.FromArgb(48, th.Accent);
                        blend.SurroundColors = new[] { Color.FromArgb(0, th.Accent) };
                        g.FillEllipse(blend, glow);
                    }
                }
                catch (OutOfMemoryException)
                {
                    using (var soft = new SolidBrush(Color.FromArgb(22, th.Accent)))
                        g.FillEllipse(soft, glow);
                }
            }

            using (var brush = new SolidBrush(Color.FromArgb(26, th.Accent)))
                g.FillEllipse(brush, Width - 420, Height - 380, 520, 420);
        }

        void ApplyTheme()
        {
            BackColor = th.Back;
            Invalidate();
            foreach (Control c in Controls) PaintCtl(c);
            lblTitle.ForeColor = th.Text;
            lblSub.ForeColor = th.Dim;
            StyleButton(btnMenu);
            StyleButton(btnRefresh);
            StyleButton(btnUpdate);
            StyleButton(btnKey);
            if (chatBar != null)
            {
                chatBar.BackColor = Color.Transparent;
                chatInput.BackColor = th.Card;
                chatInput.ForeColor = th.Text;
                chatSend.BackColor = th.Accent;
                chatSend.ForeColor = th.AccentText;
            }
            btnKey.ForeColor = th.Accent;
            BuildMenu();
            Render();
            LayoutHeader();
        }

        void PaintCtl(Control c)
        {
            if (c is Round || c is FlatButton || c is Bar) return;
            if (c is Label) c.ForeColor = th.Text;
            if (c is Panel || c is FlowLayoutPanel) c.BackColor = Color.Transparent;
            if (c is ContextMenuStrip) { c.BackColor = th.Panel; c.ForeColor = th.Text; }
            foreach (Control k in c.Controls) PaintCtl(k);
        }

        void StyleButton(FlatButton b)
        {
            b.BackColor = th.Card;
            b.ForeColor = th.Text;
            b.FlatAppearance.MouseOverBackColor = th.Chip;
        }

        /// <summary>Clear() не освобождает детей — за сутки копились тысячи контролов и шрифтов.</summary>
        static void ClearControls(Control parent)
        {
            while (parent.Controls.Count > 0)
            {
                var c = parent.Controls[0];
                parent.Controls.RemoveAt(0);
                try { c.Dispose(); } catch { }
            }
        }

        void Render()
        {
            if (trainView && !HasNexusKey()) trainView = false;
            if (list != null) renderedWidth = list.ClientSize.Width;
            RenderTabs();
            if (chatView) RenderChat();
            else if (trainView) RenderTraining();
            else if (viewsView) RenderViews();
            else RenderTasks();
            if (chatBar != null) chatBar.Visible = chatView;
            if (!chatView)
            {
                lblSub.Text = lastSync == DateTime.MinValue
                    ? L.T("loading")
                    : (L.Lang == "en" ? "updated " : "обновлено ") + lastSync.ToString("HH:mm");
            }
            Invalidate();
        }

        void RenderTabs()
        {
            ClearControls(tabs);
            tabs.Controls.Add(TabChip(L.T("tab_board"), "__board", !trainView && !chatView && !viewsView));
            tabs.Controls.Add(TabChip(L.T("tab_views"), "__views", viewsView));
            if (HasNexusKey()) tabs.Controls.Add(TabChip(L.T("tab_train"), "__train", trainView));
            tabs.Controls.Add(TabChip(L.T("tab_chat"), "__chat", chatView));
            if (trainView || chatView || viewsView) return;
            tabs.Controls.Add(TabChip(L.T("all"), "", activeSection == ""));
            foreach (var s in sections) tabs.Controls.Add(SectionChip(s));
        }

        /// <summary>Вкладка раздела с контекстным меню «переименовать / удалить».</summary>
        Control SectionChip(SectionItem sec)
        {
            var chip = TabChip(sec.Name, sec.Id, activeSection == sec.Id);
            chip.Tag = sec;
            foreach (Control c in chip.Controls) c.Tag = sec;
            if (sectionMenu == null) InitSectionMenu();
            if (sectionMenu != null)
            {
                if (sectionMenu.Items.Count > 1)
                {
                    sectionMenu.Items[0].Text = L.Lang == "en" ? "Rename" : "Переименовать";
                    sectionMenu.Items[1].Text = L.T("delete");
                }
                chip.ContextMenuStrip = sectionMenu;
                foreach (Control c in chip.Controls) c.ContextMenuStrip = sectionMenu;
            }
            return chip;
        }

        void InitSectionMenu()
        {
            sectionMenu = new ContextMenuStrip { Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular), ShowImageMargin = false };
            sectionMenu.Items.Add(new ToolStripMenuItem(L.Lang == "en" ? "Rename" : "Переименовать", null,
                (s, e) => { var sec = SectionFromMenu(); if (sec != null) RenameSection(sec); }));
            sectionMenu.Items.Add(new ToolStripMenuItem(L.T("delete"), null,
                (s, e) => { var sec = SectionFromMenu(); if (sec != null) DeleteSection(sec); }));
            sectionMenu.Opening += (s, e) => StyleMenuStrip(sectionMenu);
        }

        SectionItem SectionFromMenu()
        {
            if (sectionMenu == null) return null;
            var c = sectionMenu.SourceControl;
            while (c != null)
            {
                if (c.Tag is SectionItem) return (SectionItem)c.Tag;
                c = c.Parent;
            }
            return null;
        }

        void RenameSection(SectionItem sec)
        {
            if (sec == null || string.IsNullOrEmpty(boardId)) return;
            string name = Prompt.Show(this, L.Lang == "en" ? "Rename section" : "Переименовать раздел", L.T("sectname"), th);
            name = (name ?? "").Trim();
            if (name.Length == 0 || name == sec.Name) return;
            var moveIds = tasks.Where(t => t.Section == sec.Id).Select(t => t.Id).ToList();
            string b = boardId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int code;
                try { code = api.SectionSave(b, sec.Id, name); }
                catch { code = 0; }
                if (code == 404)
                {
                    // старый сервер: переименовать можно только через новый раздел
                    string newId = "";
                    try { newId = api.AddSectionEx(b, name); } catch { }
                    if (!string.IsNullOrEmpty(newId))
                    {
                        foreach (var id in moveIds)
                        {
                            try { api.UpdateTask(b, id, new Dictionary<string, object> { { "section", newId } }); } catch { }
                        }
                        try { api.SectionDel(b, sec.Id); } catch { }
                        code = 200;
                    }
                }
                if (code == 200)
                {
                    Ui(() => LoadBoard());
                    return;
                }
                Ui(() => MessageBox.Show(this,
                    L.Lang == "en" ? "Server refused to rename the section." : "Сервер не дал переименовать раздел.",
                    "Nexus Board"));
            });
        }

        void DeleteSection(SectionItem sec)
        {
            if (sec == null || string.IsNullOrEmpty(boardId)) return;
            string q = L.Lang == "en"
                ? "Delete section \"" + sec.Name + "\"? Tasks stay on the board, they just lose the section."
                : "Удалить раздел «" + sec.Name + "»? Задачи останутся на доске, просто без раздела.";
            if (MessageBox.Show(this, q, "Nexus Board", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string b = boardId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int code;
                try { code = api.SectionDel(b, sec.Id); }
                catch { code = 0; }
                Ui(() =>
                {
                    if (code == 404)
                        MessageBox.Show(this,
                            L.Lang == "en" ? "Server does not support section delete — update the server." : "Сервер не поддерживает удаление раздела — обнови сервер.",
                            "Nexus Board");
                    if (activeSection == sec.Id) activeSection = "";
                    LoadBoard();
                });
            });
        }

        Control TabChip(string text, string id, bool active)
        {
            int w = Math.Max(74, TextRenderer.MeasureText(text, FontCache.Get("Segoe UI", 9.5f, FontStyle.Bold)).Width + 40);
            var chip = new Round
            {
                Radius = 13,
                Fill = active ? th.Accent : Color.FromArgb(Math.Min(255, th.Card.A + 12), th.Card),
                BorderColor = active ? th.Accent : th.Line,
                Size = new Size(w, 40),
                Margin = new Padding(0, 0, 9, 0),
                Cursor = Cursors.Hand
            };
            var lb = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = active ? th.AccentText : th.Text,
                BackColor = Color.Transparent,
                Font = FontCache.Get("Segoe UI", 9.5f, active ? FontStyle.Bold : FontStyle.Regular)
            };
            EventHandler pick = (s, e) =>
            {
                if (id == "__views") { viewsView = true; trainView = false; chatView = false; Render(); LoadViews(); return; }
                if (id == "__train") { viewsView = false; trainView = true; chatView = false; Render(); LoadTrain(); return; }
                if (id == "__chat") { viewsView = false; chatView = true; trainView = false; Render(); LoadChat(true); return; }
                if (id == "__board") { viewsView = false; trainView = false; chatView = false; Render(); LoadBoard(); return; }
                activeSection = id;
                Render();
            };
            lb.Click += pick;
            chip.Click += pick;
            chip.MouseEnter += (s, e) => { if (!active) chip.BorderColor = th.Accent; };
            chip.MouseLeave += (s, e) => { if (!active) chip.BorderColor = th.Line; };
            chip.Controls.Add(lb);
            return chip;
        }


        void LoadViews()
        {
            if (string.IsNullOrEmpty(boardId)) return;
            if (Interlocked.CompareExchange(ref boardBusy, 1, 0) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var snap = api.Views(boardId);
                    var act = api.Activity(boardId);
                    var parsedFeed = new List<Dictionary<string, object>>();
                    if (act != null && act.ContainsKey("items") && act["items"] is object[])
                    {
                        foreach (var o in (object[])act["items"])
                        {
                            var d = o as Dictionary<string, object>;
                            if (d != null) parsedFeed.Add(d);
                        }
                    }
                    var parsed = new List<Dictionary<string, object>>();
                    string ver = "";
                    if (snap != null)
                    {
                        if (snap.ContainsKey("version")) ver = Convert.ToString(snap["version"]);
                        if (snap.ContainsKey("blocks") && snap["blocks"] is object[])
                        {
                            foreach (var o in (object[])snap["blocks"])
                            {
                                var d = o as Dictionary<string, object>;
                                if (d != null) parsed.Add(d);
                            }
                        }
                    }
                    Ui(() =>
                    {
                        blocks = parsed;
                        feed = parsedFeed;
                        blocksVersion = ver;
                        lastSync = DateTime.Now;
                        Render();
                    });
                }
                catch
                {
                    Ui(() => lblSub.Text = L.T("offline"));
                }
                finally { Interlocked.Exchange(ref boardBusy, 0); }
            });
        }

        static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key] as Dictionary<string, object>;
        }

        static object Val(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key];
        }

        static string S(Dictionary<string, object> d, string key)
        {
            object v = Val(d, key);
            return v == null ? "" : Convert.ToString(v);
        }

        static double N(Dictionary<string, object> d, string key)
        {
            object v = Val(d, key);
            if (v == null) return 0;
            double r;
            return double.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        Control ViewCard(Dictionary<string, object> b)
        {
            string kind = S(b, "kind");
            string title = S(b, "title");
            var body = Sub(b, "body");
            if (body == null) body = new Dictionary<string, object>();
            int width = listCardWidth;
            int h = 96;
            if (kind == "text") h = 44 + TextHeight(S(body, "text"), width - 60);
            else if (kind == "log") h = 92 + 16 * 8;
            else if (kind == "status") h = 62 + 26 * CountItems(body, "items");
            else if (kind == "links") h = 58 + 28 * CountItems(body, "items");
            else if (kind == "progress" || kind == "metric") h = 118;
            else if (kind == "code") h = 108;
            else if (kind == "activity") h = 56 + 22 * Math.Max(1, Math.Min(8, feed.Count));

            var card = new Round
            {
                Radius = 16,
                Fill = th.Card,
                BorderColor = th.Line,
                Width = width,
                Height = h,
                Margin = new Padding(0, 0, 0, 12)
            };
            card.Controls.Add(new Label
            {
                Text = title,
                AutoSize = false,
                Width = width - 130,
                Height = 24,
                Location = new Point(24, 16),
                Font = FontCache.Get("Segoe UI Semibold", 11.5f, FontStyle.Regular),
                ForeColor = th.Text,
                BackColor = Color.Transparent
            });
            card.Controls.Add(new Label
            {
                Text = kind.ToUpper(),
                AutoSize = false,
                Width = 100,
                Height = 20,
                Location = new Point(width - 124, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Font = FontCache.Get("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = th.Dim,
                BackColor = Color.Transparent
            });

            if (kind == "metric")
            {
                card.Controls.Add(new Label
                {
                    Text = S(body, "value") + " " + S(body, "unit"),
                    AutoSize = false,
                    Width = width - 60,
                    Height = 40,
                    Location = new Point(24, 42),
                    Font = FontCache.Get("Segoe UI Semibold", 20f, FontStyle.Bold),
                    ForeColor = th.Accent,
                    BackColor = Color.Transparent
                });
                card.Controls.Add(new Label
                {
                    Text = S(body, "sub"),
                    AutoSize = false,
                    Width = width - 60,
                    Height = 22,
                    Location = new Point(24, 84),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular)
                });
            }
            else if (kind == "progress")
            {
                int pct = (int)Math.Max(0, Math.Min(100, N(body, "value")));
                string label = S(body, "label");
                if (!string.IsNullOrEmpty(label))
                {
                    card.Controls.Add(new Label
                    {
                        Text = label,
                        AutoSize = false,
                        Width = width - 160,
                        Height = 20,
                        Location = new Point(24, 44),
                        ForeColor = th.Dim,
                        BackColor = Color.Transparent,
                        Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular)
                    });
                }
                card.Controls.Add(new Round { Radius = 6, Fill = th.Line, BorderColor = th.Line, Location = new Point(24, 70), Size = new Size(width - 150, 10) });
                var fillBar = new Round
                {
                    Radius = 6,
                    Fill = th.Accent,
                    BorderColor = th.Accent,
                    Location = new Point(24, 70),
                    Size = new Size(Math.Max(6, (width - 150) * pct / 100), 10)
                };
                card.Controls.Add(fillBar);
                fillBar.BringToFront();
                card.Controls.Add(new Label
                {
                    Text = pct + "%" + (string.IsNullOrEmpty(S(body, "eta")) ? "" : "  ·  " + S(body, "eta")),
                    AutoSize = false,
                    Width = 120,
                    Height = 22,
                    Location = new Point(width - 138, 64),
                    TextAlign = ContentAlignment.MiddleRight,
                    ForeColor = th.Text,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI Semibold", 10.5f, FontStyle.Bold)
                });
            }
            else if (kind == "status")
            {
                int y = 46;
                var items = Val(body, "items") as object[];
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        var row = it as Dictionary<string, object>;
                        if (row == null) continue;
                        string state = S(row, "state");
                        Color col = state == "ok" ? Color.FromArgb(84, 214, 168)
                            : state == "warn" ? Color.FromArgb(224, 158, 62)
                            : state == "err" ? Color.FromArgb(226, 100, 100) : th.Dim;
                        card.Controls.Add(new Panel { BackColor = col, Location = new Point(26, y + 7), Size = new Size(8, 8) });
                        card.Controls.Add(new Label
                        {
                            Text = S(row, "label"),
                            AutoSize = false,
                            Width = width / 2,
                            Height = 22,
                            Location = new Point(44, y),
                            ForeColor = th.Text,
                            BackColor = Color.Transparent,
                            Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                        });
                        card.Controls.Add(new Label
                        {
                            Text = S(row, "note"),
                            AutoSize = false,
                            Width = width / 2 - 70,
                            Height = 22,
                            Location = new Point(width / 2 + 10, y),
                            TextAlign = ContentAlignment.MiddleRight,
                            ForeColor = th.Dim,
                            BackColor = Color.Transparent,
                            AutoEllipsis = true,
                            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular)
                        });
                        y += 26;
                    }
                }
            }
            else if (kind == "text")
            {
                card.Controls.Add(new Label
                {
                    Text = S(body, "text"),
                    AutoSize = false,
                    Width = width - 52,
                    Height = h - 52,
                    Location = new Point(24, 44),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                });
            }
            else if (kind == "log")
            {
                var tail = Sub(b, "tail");
                string text = "";
                if (tail != null && Val(tail, "lines") is object[])
                {
                    var lines = (object[])Val(tail, "lines");
                    var keep = lines.Skip(Math.Max(0, lines.Length - 10)).Select(x => Convert.ToString(x));
                    text = string.Join(Environment.NewLine, keep);
                }
                else if (tail != null && S(tail, "error") != "")
                {
                    text = S(tail, "error");
                }
                card.Controls.Add(new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.None,
                    BackColor = th.Name == "frost" ? Color.FromArgb(244, 246, 251) : Color.FromArgb(14, 11, 23),
                    ForeColor = th.Dim,
                    Font = FontCache.Get("Consolas", 9f, FontStyle.Regular),
                    Location = new Point(24, 46),
                    Size = new Size(width - 52, h - 66),
                    Text = text
                });
            }
            else if (kind == "links")
            {
                int y = 44;
                var items = Val(body, "items") as object[];
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        var row = it as Dictionary<string, object>;
                        if (row == null) continue;
                        string url = S(row, "url");
                        var link = new LinkLabel
                        {
                            Text = S(row, "label") + (string.IsNullOrEmpty(S(row, "note")) ? "" : "  ·  " + S(row, "note")),
                            AutoSize = false,
                            Width = width - 52,
                            Height = 24,
                            Location = new Point(24, y),
                            LinkColor = th.Accent,
                            ActiveLinkColor = th.Accent,
                            BackColor = Color.Transparent,
                            Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                        };
                        link.LinkClicked += (s2, e2) =>
                        {
                            try
                            {
                                Uri parsed;
                                if (Uri.TryCreate(url, UriKind.Absolute, out parsed) &&
                                    (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                                    System.Diagnostics.Process.Start(parsed.AbsoluteUri);
                            }
                            catch { }
                        };
                        card.Controls.Add(link);
                        y += 28;
                    }
                }
            }
            else if (kind == "code")
            {
                card.Controls.Add(new Label
                {
                    Text = S(body, "filename") + "   v" + S(body, "version") + "   " + S(body, "sha"),
                    AutoSize = false,
                    Width = width - 220,
                    Height = 20,
                    Location = new Point(24, 46),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Consolas", 9f, FontStyle.Regular)
                });
                var open = new FlatButton { Text = "открыть", Size = new Size(120, 34), Location = new Point(width - 148, 44) };
                StyleButton(open);
                open.Click += (s2, e2) => OpenCodeBlock(S(b, "id"), S(b, "title"));
                card.Controls.Add(open);
                card.Controls.Add(new Label
                {
                    Text = L.Lang == "en" ? "code from the PC — the phone pulls changes" : "код с ПК, телефон подтягивает изменения",
                    AutoSize = false,
                    Width = width - 52,
                    Height = 20,
                    Location = new Point(24, 74),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 9f, FontStyle.Regular)
                });
            }
            else if (kind == "tasks")
            {
                int doing = tasks.Count(t => t.Status == "doing");
                int done = tasks.Count(t => t.Status == "done");
                card.Controls.Add(new Label
                {
                    Text = (L.Lang == "en" ? "in progress " : "в работе ") + doing + "   ·   " +
                        (L.Lang == "en" ? "closed " : "закрыто ") + done + "   ·   " +
                        (L.Lang == "en" ? "total " : "всего ") + tasks.Count,
                    AutoSize = false,
                    Width = width - 52,
                    Height = 46,
                    Location = new Point(24, 44),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10.5f, FontStyle.Regular)
                });
            }
            else if (kind == "activity")
            {
                int y = 44;
                int n = 0;
                foreach (var item in feed)
                {
                    if (n >= 8) break;
                    string ts = S(item, "ts");
                    string when = ts.Length >= 16 ? ts.Substring(11, 5) : ts;
                    string kindName = S(item, "kind");
                    card.Controls.Add(new Label
                    {
                        Text = when + "   " + kindName,
                        AutoSize = false,
                        Width = 120,
                        Height = 20,
                        Location = new Point(24, y),
                        ForeColor = th.Dim,
                        BackColor = Color.Transparent,
                        Font = FontCache.Get("Consolas", 9f, FontStyle.Regular)
                    });
                    card.Controls.Add(new Label
                    {
                        Text = S(item, "text"),
                        AutoSize = false,
                        Width = width - 200,
                        Height = 20,
                        Location = new Point(150, y),
                        ForeColor = th.Text,
                        BackColor = Color.Transparent,
                        AutoEllipsis = true,
                        Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular)
                    });
                    y += 22;
                    n++;
                }
                if (n == 0)
                {
                    card.Controls.Add(new Label
                    {
                        Text = L.Lang == "en" ? "no events yet" : "событий пока нет",
                        AutoSize = false,
                        Width = width - 52,
                        Height = 24,
                        Location = new Point(24, 44),
                        ForeColor = th.Dim,
                        BackColor = Color.Transparent,
                        Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                    });
                }
            }
            else
            {
                card.Controls.Add(new Label
                {
                    Text = (L.Lang == "en" ? "kind not supported by this client: " : "клиент не знает такой раздел: ") + kind,
                    AutoSize = false,
                    Width = width - 52,
                    Height = 40,
                    Location = new Point(24, 44),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                });
            }
            return card;
        }

        static int CountItems(Dictionary<string, object> body, string key)
        {
            var items = Val(body, key) as object[];
            if (items == null) return 1;
            return Math.Max(1, Math.Min(10, items.Length));
        }

        int TextHeight(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return 24;
            var size = TextRenderer.MeasureText(text, FontCache.Get("Segoe UI", 10f, FontStyle.Regular), new Size(width, 0), TextFormatFlags.WordBreak);
            return Math.Max(24, Math.Min(320, size.Height));
        }

        void RenderViews()
        {
            list.SuspendLayout();
            ClearControls(list);
            listCardWidth = Math.Max(420, list.ClientSize.Width - 58);
            if (blocks.Count == 0)
            {
                var empty = new Round
                {
                    Radius = 18,
                    Fill = th.Card,
                    BorderColor = th.Line,
                    Width = listCardWidth,
                    Height = 150,
                    Margin = new Padding(0, 0, 0, 12)
                };
                empty.Controls.Add(new Label
                {
                    Text = L.Lang == "en" ? "The AI has not created sections yet" : "ИИ ещё не создал разделы",
                    AutoSize = false,
                    Width = empty.Width - 60,
                    Height = 28,
                    Location = new Point(26, 34),
                    ForeColor = th.Text,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI Semibold", 11f, FontStyle.Regular)
                });
                empty.Controls.Add(new Label
                {
                    Text = L.Lang == "en"
                        ? "Ask in chat: \"add a section\" — it appears here on its own, on the PC and the phone."
                        : "Напиши в чате: «сделай раздел» — он появится здесь сам, и на ПК, и на телефоне.",
                    AutoSize = false,
                    Width = empty.Width - 60,
                    Height = 52,
                    Location = new Point(26, 64),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                });
                list.Controls.Add(empty);
                list.ResumeLayout();
            list.Invalidate(true);
                return;
            }
            foreach (var b in blocks) list.Controls.Add(ViewCard(b));
            list.ResumeLayout();
        }

        void OpenCodeBlock(string id, string title)
        {
            if (string.IsNullOrEmpty(id)) return;
            using (var dlg = new CodeDialog(api, boardId, th, id, title))
            {
                dlg.ShowDialog(this);
            }
            LoadViews();
        }

        void RenderTasks()
        {
            list.SuspendLayout();
            ClearControls(list);
            listCardWidth = Math.Max(420, list.ClientSize.Width - 58);
            var shown = tasks.Where(t => activeSection == "" || t.Section == activeSection).ToList();
            shown.Sort((a, b) =>
            {
                int ra = Rank(a.Status), rb = Rank(b.Status);
                if (ra != rb) return ra.CompareTo(rb);
                return b.Progress.CompareTo(a.Progress);
            });

            if (shown.Count == 0)
            {
                var empty = new Round
                {
                    Radius = 18,
                    Fill = th.Card,
                    BorderColor = th.Line,
                    Width = Math.Min(listCardWidth, Math.Max(380, list.ClientSize.Width - 60)),
                    Height = 140,
                    Margin = new Padding(0, 0, 0, 12)
                };
                var mark = new Label
                {
                    Text = "◇",
                    Font = FontCache.Get("Segoe UI", 30f, FontStyle.Regular),
                    ForeColor = th.Accent,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Width = 60,
                    Height = 60,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(26, 38)
                };
                empty.Controls.Add(mark);
                var txt = new Label
                {
                    Text = L.T("empty"),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Width = empty.Width - 130,
                    Height = 52,
                    Location = new Point(96, 42),
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                };
                empty.Controls.Add(txt);
                list.Controls.Add(empty);
            }

            foreach (var t in shown) list.Controls.Add(TaskCard(t));
            list.ResumeLayout();
            list.Invalidate(true);
        }

        static int Rank(string status)
        {
            switch (status)
            {
                case "doing": return 0;
                case "todo": return 1;
                case "blocked": return 2;
                case "done": return 3;
                default: return 1;
            }
        }

        Color StatusColor(string status)
        {
            if (status == "blocked") return Color.FromArgb(224, 158, 62);
            if (status == "done") return th.Name == "frost" ? Color.FromArgb(22, 163, 74) : Color.FromArgb(84, 214, 168);
            if (status == "doing") return th.Accent;
            return th.Dim;
        }

        Control TaskCard(TaskItem t)
        {
            int h = string.IsNullOrEmpty(t.Note) ? 88 : 118;
            var card = new Round
            {
                Radius = 16,
                Fill = th.Card,
                BorderColor = th.Line,
                Width = listCardWidth,
                Height = h,
                Margin = new Padding(0, 0, 0, 12),
                Cursor = Cursors.Hand
            };

            var stripe = new Panel { BackColor = StatusColor(t.Status), Location = new Point(0, 18), Size = new Size(4, h - 36) };
            card.Controls.Add(stripe);

            var title = new Label
            {
                Text = t.Title,
                AutoSize = false,
                Width = card.Width - 200,
                Height = 26,
                Location = new Point(24, 18),
                Font = FontCache.Get("Segoe UI Semibold", 12.5f, FontStyle.Regular),
                ForeColor = th.Text,
                BackColor = Color.Transparent
            };
            card.Controls.Add(title);

            var state = new Label
            {
                Text = L.T(t.Status).ToUpper(),
                AutoSize = false,
                Width = 120,
                Height = 22,
                Location = new Point(card.Width - 140, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Font = FontCache.Get("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = StatusColor(t.Status),
                BackColor = Color.Transparent
            };
            card.Controls.Add(state);

            if (!string.IsNullOrEmpty(t.Note))
            {
                var note = new Label
                {
                    Text = t.Note,
                    AutoSize = false,
                    Width = card.Width - 70,
                    Height = 20,
                    Location = new Point(24, 48),
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular)
                };
                card.Controls.Add(note);
            }

            var bar = new Bar
            {
                Location = new Point(24, h - 30),
                Size = new Size(card.Width - 140, 6),
                Track = th.Line,
                Fill = StatusColor(t.Status),
                Value = Math.Max(0, Math.Min(100, t.Progress)),
                BackColor = Color.Transparent
            };
            card.Controls.Add(bar);

            var pct = new Label
            {
                Text = t.Progress + "%",
                AutoSize = false,
                Width = 56,
                Height = 20,
                Location = new Point(card.Width - 92, h - 38),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = th.Dim,
                BackColor = Color.Transparent,
                Font = FontCache.Get("Segoe UI Semibold", 9.5f, FontStyle.Regular)
            };
            card.Controls.Add(pct);

            EventHandler open = (s, e) => EditTask(t);
            card.Click += open;
            foreach (Control c in card.Controls) c.Click += open;
            card.MouseEnter += (s, e) => card.BorderColor = th.Accent;
            card.MouseLeave += (s, e) => card.BorderColor = th.Line;

            return card;
        }

        void CreateBoard()
        {
            lblSub.Text = L.T("loading");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string id = api.NewBoard();
                    Ui(() =>
                    {
                        if (!string.IsNullOrEmpty(id))
                        {
                            boardId = id;
                            settings.Board = id;
                            settings.Save();
                            BuildMenu();
                            LoadBoard();
                        }
                        else lblSub.Text = L.T("offline");
                    });
                }
                catch
                {
                    Ui(() => lblSub.Text = L.T("offline"));
                }
            });
        }


        Round Card(int w, int h)
        {
            return new Round
            {
                Radius = 16,
                Fill = th.Card,
                BorderColor = th.Line,
                Size = new Size(w, h),
                Margin = new Padding(0, 0, 0, 12)
            };
        }

        static Label Txt(string text, Theme th, float size, bool bold, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = FontCache.Get("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        void LoadTrain()
        {
            if (string.IsNullOrEmpty(settings.Key)) return;
            if (Interlocked.CompareExchange(ref trainBusy, 1, 0) == 1) return;
            api.Key = settings.Key;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var data = api.Train();
                    Ui(() =>
                    {
                        train = data;
                        lastSync = DateTime.Now;
                        Render();
                    });
                }
                catch
                {
                    Ui(() => lblSub.Text = L.T("offline"));
                }
                finally { Interlocked.Exchange(ref trainBusy, 0); }
            });
        }

        void RenderChat()
        {
            list.SuspendLayout();
            ClearControls(list);
            int width = Math.Max(320, list.ClientSize.Width - 60);

            if (chatMsgs.Count == 0)
            {
                var empty = new Round
                {
                    Radius = 18,
                    Fill = th.Card,
                    BorderColor = th.Line,
                    Size = new Size(Math.Min(width, 520), 92),
                    Margin = new Padding(0, 0, 0, 12)
                };
                var lb = new Label
                {
                    Text = L.T("chat_empty"),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                };
                empty.Controls.Add(lb);
                list.Controls.Add(empty);
            }

            foreach (var m in chatMsgs)
            {
                bool mine = m.From == "owner";
                int bubbleWidth = Math.Max(220, Math.Min(width, 640));
                var bubble = new Round
                {
                    Radius = 16,
                    Fill = mine ? th.Accent : th.Card,
                    BorderColor = mine ? th.Accent : th.Line,
                    Size = new Size(bubbleWidth, 10),
                    Margin = new Padding(mine ? Math.Max(0, width - bubbleWidth) : 0, 0, 0, 10)
                };
                var head = new Label
                {
                    Text = (mine ? L.T("chat_me") : L.T("chat_ai")) + " · " + ChatTime(m.Ts),
                    AutoSize = true,
                    Location = new Point(18, 12),
                    ForeColor = mine ? th.AccentText : th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 8.5f, FontStyle.Bold)
                };
                var body = new Label
                {
                    Text = m.Text,
                    AutoSize = true,
                    MaximumSize = new Size(bubbleWidth - 36, 0),
                    Location = new Point(18, 34),
                    ForeColor = mine ? th.AccentText : th.Text,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                };
                bubble.Controls.Add(head);
                bubble.Controls.Add(body);
                bubble.Height = body.PreferredHeight + 52;
                list.Controls.Add(bubble);
            }

            list.ResumeLayout();
            if (list.Controls.Count > 0)
            {
                list.ScrollControlIntoView(list.Controls[list.Controls.Count - 1]);
            }
        }

        void LoadChat(bool reset)
        {
            if (string.IsNullOrEmpty(boardId)) return;
            if (reset)
            {
                chatMsgs.Clear();
                chatLast = 0;
                if (chatView) RenderChat();
            }
            if (Interlocked.CompareExchange(ref chatBusy, 1, 0) == 1)
            {
                // запрос уже в пути — вернёмся за результатом ещё раз, а не теряем его
                chatReloadPending = true;
                return;
            }
            long since = chatLast;
            string b = boardId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<ChatMsg> fresh = null;
                try
                {
                    var snap = api.ChatList(b, since);
                    if (snap != null)
                    {
                        fresh = new List<ChatMsg>();
                        if (snap.ContainsKey("messages") && snap["messages"] is object[])
                        {
                            foreach (var o in (object[])snap["messages"])
                            {
                                var d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                long ts = 0;
                                try { ts = Convert.ToInt64(d.ContainsKey("ts") ? d["ts"] : 0L); } catch { ts = 0; }
                                fresh.Add(new ChatMsg
                                {
                                    Id = Str(d, "id"),
                                    From = Str(d, "from"),
                                    Text = Str(d, "text"),
                                    Ts = ts
                                });
                            }
                        }
                    }
                }
                catch { fresh = null; }

                Interlocked.Exchange(ref chatBusy, 0);
                if (fresh != null)
                {
                    var got = fresh;
                    Ui(() => ApplyChat(got));
                }
                else
                {
                    Ui(() => lblSub.Text = L.T("offline"));
                }
                if (chatReloadPending)
                {
                    chatReloadPending = false;
                    Ui(() => LoadChat(false));
                }
            });
        }

        void ApplyChat(List<ChatMsg> fresh)
        {
            foreach (var msg in fresh)
            {
                if (!string.IsNullOrEmpty(msg.Id) && chatMsgs.Exists(x => x.Id == msg.Id)) continue;
                if (msg.From == "owner")
                    chatMsgs.RemoveAll(x => x.Id != null && x.Id.StartsWith("local:", StringComparison.Ordinal) && x.Text == msg.Text);
                chatMsgs.Add(msg);
                if (msg.Ts > chatLast) chatLast = msg.Ts;
            }
            if (chatMsgs.Count > 300) chatMsgs = chatMsgs.GetRange(chatMsgs.Count - 300, 300);
            if (chatView) RenderChat();
            lblSub.Text = chatLast > 0
                ? (L.Lang == "en" ? "chat · replies come from the AI" : "чат · отвечает ИИ")
                : (L.Lang == "en" ? "chat" : "чат");
        }

        /// <summary>Битый ts не должен валить окно: без времени — прочерк, а не 03:00 1970.</summary>
        static string ChatTime(long ts)
        {
            if (ts <= 0) return "—";
            try
            {
                long ms = ts < 100000000000L ? ts * 1000 : ts;
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm");
            }
            catch { return "—"; }
        }

        void SendChat()
        {
            string text = (chatInput.Text ?? "").Trim();
            if (text.Length == 0 || string.IsNullOrEmpty(boardId)) return;

            // пузырь появляется сразу, сеть — потом
            var mine = new ChatMsg
            {
                Id = "local:" + Guid.NewGuid().ToString("N"),
                From = "owner",
                Text = text,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            chatMsgs.Add(mine);
            chatInput.Text = "";
            if (chatView) RenderChat();
            chatSend.Enabled = false;

            string b = boardId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = true;
                try { api.ChatSend(b, "owner", text); }
                catch { ok = false; }
                Ui(() =>
                {
                    chatSend.Enabled = true;
                    if (!ok)
                    {
                        chatMsgs.RemoveAll(x => x.Id == mine.Id);
                        if (chatInput.Text.Length == 0) chatInput.Text = text;
                        lblSub.Text = L.T("offline");
                        if (chatView) RenderChat();
                    }
                    LoadChat(false);
                });
            });
        }

        void RenderTraining()
        {
            ClearControls(list);
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            int w = Math.Max(600, list.ClientSize.Width - 60);

            if (!HasNexusKey())
            {
                var card = Card(w, 110);
                card.Controls.Add(Abs(Txt(L.Lang == "en"
                    ? "Training is hidden for regular boards.\nSettings (Ctrl+Alt+S) → field «Nexus key»."
                    : "Обучение видно только с ключом Nexus.\nНастройки (Ctrl+Alt+S) → поле «Ключ Nexus».",
                    th, 10f, false, th.Dim), 20, 22));
                list.Controls.Add(card);
                return;
            }
            if (train == null)
            {
                var card = Card(w, 90);
                card.Controls.Add(Abs(Txt(L.T("loading"), th, 10f, false, th.Dim), 20, 24));
                list.Controls.Add(card);
                return;
            }

            bool alive = Str(train, "alive") == "True" || Str(train, "alive") == "true";
            string run = train.ContainsKey("run") ? Str(train, "run") : "—";
            string stage = TrainText(train.ContainsKey("stage") ? Str(train, "stage") : "");
            string unit = TrainText(train.ContainsKey("unit") ? Str(train, "unit") : "");
            string rate = TrainText(train.ContainsKey("rate") ? Str(train, "rate") : "—");
            double pct = train.ContainsKey("pct") ? Num(train, "pct") : 0;
            string step = "—";
            if (train.ContainsKey("step"))
                step = Convert.ToString(train["step"]) + " / " + (train.ContainsKey("total") ? Convert.ToString(train["total"]) : "?") + "  ·  " + pct.ToString("0") + "%";

            var head = Card(w, 168);
            head.Controls.Add(Abs(Txt(alive ? L.T("tr_running") : L.T("tr_dead"), th, 9.5f, true,
                alive ? th.Accent : Color.FromArgb(214, 132, 52)), 22, 18));
            head.Controls.Add(Abs(Txt(run, th, 20f, true, th.Text), 22, 42));
            head.Controls.Add(Abs(Txt((stage + (string.IsNullOrEmpty(unit) ? "" : " · " + unit)).Trim(), th, 9.5f, false, th.Dim), 22, 84));
            var bar = new Bar
            {
                Value = (int)Math.Round(pct),
                Track = th.Name == "frost" ? Color.FromArgb(222, 230, 244) : th.Line,
                Fill = th.Accent,
                Location = new Point(22, 112),
                Size = new Size(w - 44, 8)
            };
            head.Controls.Add(bar);
            head.Controls.Add(Abs(Txt(step, th, 10f, false, th.Text), 22, 130));
            list.Controls.Add(head);

            var metrics = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4)
            };
            int mw = Math.Max(150, (w - 36) / 4);
            metrics.Controls.Add(Metric(L.T("tr_loss"), train.ContainsKey("loss") ? Num(train, "loss").ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) : "—", mw));
            metrics.Controls.Add(Metric(L.T("tr_eta"), train.ContainsKey("remain") ? Str(train, "remain") : "—", mw));
            metrics.Controls.Add(Metric(L.T("tr_rate"), rate, mw));
            metrics.Controls.Add(Metric(L.T("tr_step"), train.ContainsKey("step") ? Convert.ToString(train["step"]) : "—", mw));
            list.Controls.Add(metrics);

            var chain = Card(w, 0);
            chain.AutoSize = false;
            chain.Height = 128;
            chain.Controls.Add(Abs(Txt(L.T("tr_chain"), th, 8.5f, true, th.Dim), 20, 14));
            var chainText = Txt(Join(train, "chainLines", 6), th, 8.5f, false, th.Dim);
            chainText.Font = FontCache.Get("Consolas", 8.5f, FontStyle.Regular);
            chain.Controls.Add(Abs(chainText, 20, 38));
            list.Controls.Add(chain);

            var log = Card(w, 0);
            log.AutoSize = false;
            log.Height = 168;
            log.Controls.Add(Abs(Txt(L.T("tr_log"), th, 8.5f, true, th.Dim), 20, 14));
            var mono = Txt(LogTail(10), th, 8.5f, false, th.Dim);
            mono.Font = FontCache.Get("Consolas", 8.5f, FontStyle.Regular);
            log.Controls.Add(Abs(mono, 20, 38));
            list.Controls.Add(log);
        }

        /// <summary>Русские единицы сервера («шагов», «с/шаг») в английском интерфейсе.</summary>
        static string TrainText(string s)
        {
            if (string.IsNullOrEmpty(s) || L.Lang != "en") return s;
            return s.Replace("примеров", "examples")
                    .Replace("шагов", "steps")
                    .Replace("прим/с", "ex/s")
                    .Replace("с/шаг", "s/step");
        }

        /// <summary>Хвост лога: logLines/lines/log из ответа, иначе собираем из того, что сервер отдаёт.</summary>
        string LogTail(int tail)
        {
            if (train == null) return "—";
            object v;
            if (train.TryGetValue("logLines", out v) && v is object[]) return Join(train, "logLines", tail);
            if (train.TryGetValue("lines", out v) && v is object[]) return Join(train, "lines", tail);
            if (train.TryGetValue("log", out v) && v is object[]) return Join(train, "log", tail);
            var parts = new List<string>();
            if (train.TryGetValue("losses", out v) && v is object[])
            {
                var arr = (object[])v;
                for (int i = Math.Max(0, arr.Length - tail); i < arr.Length; i++)
                    parts.Add("eval_loss " + Convert.ToString(arr[i], CultureInfo.InvariantCulture));
            }
            if (parts.Count == 0)
            {
                string stage = Str(train, "stage");
                if (!string.IsNullOrEmpty(stage)) parts.Add("stage " + TrainText(stage));
                if (train.ContainsKey("elapsed")) parts.Add("elapsed " + Str(train, "elapsed"));
                if (train.ContainsKey("logAge")) parts.Add("log " + Str(train, "logAge") + " s ago");
            }
            return parts.Count == 0 ? "—" : string.Join(Environment.NewLine, parts.ToArray());
        }

        Control Abs(Control c, int x, int y)
        {
            c.Location = new Point(x, y);
            return c;
        }

        Control Metric(string label, string value, int width)
        {
            var card = new Round { Radius = 14, Fill = th.Card, BorderColor = th.Line, Size = new Size(width, 86), Margin = new Padding(0, 0, 12, 12) };
            card.Controls.Add(Abs(Txt(label.ToUpperInvariant(), th, 8f, true, th.Dim), 16, 14));
            card.Controls.Add(Abs(Txt(value, th, 14f, true, th.Text), 16, 38));
            return card;
        }

        static string Join(Dictionary<string, object> d, string key, int tail)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "—";
            var arr = v as object[];
            if (arr == null) return Convert.ToString(v);
            var parts = new List<string>();
            for (int i = Math.Max(0, arr.Length - tail); i < arr.Length; i++) parts.Add(Convert.ToString(arr[i]));
            return string.Join(Environment.NewLine, parts.ToArray());
        }

        void LoadBoard()
        {
            if (string.IsNullOrEmpty(boardId)) return;
            if (Interlocked.CompareExchange(ref boardBusy, 1, 0) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var snap = api.Snapshot(boardId);
                    var parsedTasks = new List<TaskItem>();
                    var parsedSections = new List<SectionItem>();

                    if (snap != null && snap.ContainsKey("tasks") && snap["tasks"] is object[])
                    {
                        foreach (var o in (object[])snap["tasks"])
                        {
                            var d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            parsedTasks.Add(new TaskItem
                            {
                                Id = Str(d, "Id"),
                                Title = Str(d, "Title"),
                                Note = Str(d, "Note"),
                                Mode = Str(d, "Mode"),
                                Status = Str(d, "Status"),
                                Section = Str(d, "Section"),
                                Progress = (int)Num(d, "Progress")
                            });
                        }
                    }

                    if (snap != null && snap.ContainsKey("sections") && snap["sections"] is object[])
                    {
                        foreach (var o in (object[])snap["sections"])
                        {
                            var d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            parsedSections.Add(new SectionItem { Id = Str(d, "id"), Name = Str(d, "name") });
                        }
                    }

                    Ui(() =>
                    {
                        tasks = parsedTasks;
                        sections = parsedSections;
                        lastSync = DateTime.Now;
                        Render();
                    });
                }
                catch
                {
                    Ui(() => lblSub.Text = L.T("offline"));
                }
                finally { Interlocked.Exchange(ref boardBusy, 0); }
            });
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                double r;
                if (double.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out r)) return r;
            }
            return 0;
        }

        void EditTask(TaskItem t)
        {
            if (string.IsNullOrEmpty(boardId)) return;
            using (var dlg = new TaskDialog(api, boardId, th, sections, t))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.Deleted)
                {
                    string gone = dlg.DeletedId;
                    if (!string.IsNullOrEmpty(gone)) tasks.RemoveAll(x => x.Id == gone);
                    Render();
                }
                LoadBoard();
            }
        }

        void NewSection()
        {
            if (string.IsNullOrEmpty(boardId)) return;
            string name = Prompt.Show(this, L.T("newsect"), L.T("sectname"), th);
            if (string.IsNullOrEmpty(name)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { api.AddSection(boardId, name); } catch { }
                Ui(LoadBoard);
            });
        }

        public void ShotLook(string theme, string lang, string view)
        {
            if (view == "chat")
            {
                chatView = true;
            }
            if (view == "views")
            {
                viewsView = true;
            }
            if (!string.IsNullOrEmpty(theme))
            {
                th = Theme.Get(theme);
                settings.Theme = th.Name;
            }
            if (!string.IsNullOrEmpty(lang))
            {
                L.Lang = lang == "en" ? "en" : "ru";
            }
            // --shot — только снимок: theme/lang в settings.json не пишем, чтобы не затереть выбор владельца
            ApplyTheme();
            if (chatView) { Render(); LoadChat(true); }
            else if (viewsView) { Render(); LoadViews(); }
            else LoadBoard();
        }

        void Click_Theme(object s, EventArgs e)
        {
            int i = 0;
            for (int k = 0; k < Theme.All.Length; k++) if (Theme.All[k].Name == th.Name) i = k;
            th = Theme.All[(i + 1) % Theme.All.Length];
            settings.Theme = th.Name;
            settings.Save();
            ApplyTheme();
        }

        void Click_Lang(object s, EventArgs e)
        {
            L.Lang = L.Lang == "en" ? "ru" : "en";
            settings.Lang = L.Lang;
            settings.Save();
            ApplyTheme();
        }

        void ShowConnect()
        {
            using (var dlg = new KeyDialog(th, boardId))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string key = (dlg.Key ?? "").Trim().ToLowerInvariant();
                if (!IsBoardKey(key))
                {
                    MessageBox.Show(this, L.Lang == "en"
                        ? "Board key must be 16 hex characters: 0-9, a-f."
                        : "Ключ доски — 16 hex-символов: 0-9, a-f.", "Nexus Board",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                settings.Board = key;
                settings.Save();
                boardId = key;
                tasks.Clear();
                sections.Clear();
                activeSection = "";
                // чужая доска не должна оставлять свои сообщения и позицию опроса
                chatMsgs.Clear();
                chatLast = 0;
                chatReloadPending = false;
                blocks.Clear();
                ClearControls(list);
                Render();
                LoadBoard();
                if (chatView) LoadChat(true);
            }
        }

        internal static bool IsBoardKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length != 16) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        void OpenSettings()
        {
            string oldBoard = boardId;
            using (var dlg = new SettingsDialog(settings, th, boardId))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    api = new Api(settings.Base);
                    api.Key = settings.Key;
                    boardId = settings.Board;
                    th = Theme.Get(settings.Theme);
                    L.Lang = settings.Lang;
                    if (!string.Equals(oldBoard, boardId, StringComparison.OrdinalIgnoreCase))
                    {
                        // иначе на экране остаются задачи и чат старой доски
                        tasks.Clear();
                        sections.Clear();
                        blocks.Clear();
                        chatMsgs.Clear();
                        chatLast = 0;
                        activeSection = "";
                        blocksVersion = "";
                        feed.Clear();
                    }
                    BuildMenu();
                    ApplyTheme();
                    if (string.IsNullOrEmpty(boardId)) CreateBoard(); else LoadBoard();
                }
            }
        }

        static int[] VerParts(string v)
        {
            if (string.IsNullOrEmpty(v)) return new int[0];
            var parts = new List<int>();
            var sb = new StringBuilder();
            for (int i = 0; i <= v.Length; i++)
            {
                char c = i < v.Length ? v[i] : '.';
                if (c >= '0' && c <= '9') sb.Append(c);
                else
                {
                    if (sb.Length == 0) continue;
                    int n;
                    if (!int.TryParse(sb.ToString(), out n)) n = 0;
                    parts.Add(n);
                    sb.Length = 0;
                }
            }
            return parts.ToArray();
        }

        /// <summary>true только если серверная версия строго новее локальной — никаких откатов.</summary>
        static bool IsNewerVersion(string remote)
        {
            var r = VerParts(remote);
            if (r.Length == 0) return false;
            var l = VerParts(Program.Version);
            int n = Math.Max(r.Length, l.Length);
            for (int i = 0; i < n; i++)
            {
                int a = i < r.Length ? r[i] : 0;
                int b = i < l.Length ? l[i] : 0;
                if (a != b) return a > b;
            }
            return false;
        }

        void SetUpdateAvailable(bool show, string tip)
        {
            if (btnUpdate == null) return;
            btnUpdate.Visible = show;
            if (tipUpdate == null) tipUpdate = new ToolTip();
            tipUpdate.SetToolTip(btnUpdate, tip ?? "");
            LayoutHeader();
        }

        void CheckUpdate()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string raw;
                try { raw = api.CheckUpdate(); }
                catch { return; }
                if (string.IsNullOrEmpty(raw)) return;
                Dictionary<string, object> d;
                try { d = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>; }
                catch { return; }
                if (d == null) return;

                string ver = d.ContainsKey("versionName") ? Convert.ToString(d["versionName"]) : "";
                int code = d.ContainsKey("versionCode") ? (int)Num(d, "versionCode") : 0;
                string file = d.ContainsKey("file") ? Convert.ToString(d["file"]) : "";
                string sha = d.ContainsKey("sha256") ? Convert.ToString(d["sha256"]) : "";

                bool newer = IsNewerVersion(ver);
                if (!newer && VerParts(ver).Length == 0) newer = code > Program.VersionCode;

                Ui(() =>
                {
                    if (!newer)
                    {
                        updateVersion = "";
                        updateFile = "";
                        updateSha = "";
                        updateCode = 0;
                        SetUpdateAvailable(false, "");
                        return;
                    }
                    updateVersion = ver;
                    updateFile = file;
                    updateSha = sha;
                    updateCode = code;
                    SetUpdateAvailable(true, L.T("update") + " " + ver);
                });
            });
        }

        /// <summary>Скачивание в память + сверка SHA-256. Установка — только после «Да» в диалоге.</summary>
        void AutoInstallUpdate()
        {
            string ver = updateVersion, file = updateFile, sha = updateSha;
            string baseUrl = (settings.Base ?? "").TrimEnd('/');
            if (string.IsNullOrEmpty(ver)) return;
            if (string.IsNullOrEmpty((sha ?? "").Trim()))
            {
                MessageBox.Show(this,
                    L.Lang == "en"
                        ? "The manifest has no checksum — refusing to install the update."
                        : "В манифесте нет контрольной суммы — не ставлю обновление.",
                    "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(file)) file = "pc/NexusBoard.exe";
            string url = baseUrl + "/app/" + file.TrimStart('/');

            SetUpdateAvailable(true, L.T("update") + " " + ver);
            lblSub.Text = (L.Lang == "en" ? "downloading " : "скачиваю ") + ver + "…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NexusBoard-" + ver + ".exe");
                string error = "";
                try
                {
                    byte[] data;
                    using (var wc = new TimeoutWebClient { TimeoutMs = 180000 })
                    {
                        wc.Headers.Add("User-Agent", "NexusBoardPC/" + Program.Version);
                        data = wc.DownloadData(url);
                    }
                    if (data == null || data.Length < 20000)
                    {
                        error = L.Lang == "en" ? "downloaded file looks broken" : "скачался битый файл";
                    }
                    else
                    {
                        string got;
                        using (var sha256 = System.Security.Cryptography.SHA256.Create())
                            got = BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                        string want = sha.Trim().ToLowerInvariant();
                        // строго 64 hex: короткий префикс из манифеста не должен
                        // пропускать поддельный exe
                        bool match = want.Length == 64 && Regex.IsMatch(want, "^[0-9a-f]{64}$") && got == want;
                        if (!match)
                        {
                            error = L.Lang == "en"
                                ? "Checksum mismatch — update cancelled."
                                : "Контрольная сумма не совпала — обновление отменено.";
                        }
                        else
                        {
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                            File.WriteAllBytes(tmp, data);
                        }
                    }
                }
                catch (Exception ex) { error = ex.Message; }

                if (!string.IsNullOrEmpty(error))
                {
                    Ui(() =>
                    {
                        lblSub.Text = L.T("offline");
                        MessageBox.Show(this, error, L.T("err"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                    return;
                }
                Ui(() => AskInstall(tmp, ver));
            });
        }

        void AskInstall(string file, string ver)
        {
            if (closing) return;
            string q = L.Lang == "en"
                ? "Update " + ver + " is downloaded. Install it now? The app will restart."
                : "Скачано обновление " + ver + ", установить сейчас? Приложение перезапустится.";
            if (MessageBox.Show(this, q, "Nexus Board", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                // отказ от установки не должен оставлять десятки мегабайт
                // в %TEMP% до следующей попытки
                try { File.Delete(file); } catch { }
                lblSub.Text = (L.Lang == "en" ? "update " : "обновление ") + ver +
                    (L.Lang == "en" ? " is ready — press ↓" : " готово — нажми ↓");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(file, Program.UpdateArg + " \"" + Application.ExecutablePath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L.T("err"));
                return;
            }
            Close();
        }

        void Click_Update(object s, EventArgs e)
        {
            if (string.IsNullOrEmpty(updateVersion))
            {
                lblSub.Text = L.Lang == "en" ? "checking for updates…" : "проверяю обновления…";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string raw = "";
                    try { raw = api.CheckUpdate(); } catch { }
                    Dictionary<string, object> d = null;
                    try { d = new JavaScriptSerializer().DeserializeObject(raw ?? "") as Dictionary<string, object>; }
                    catch { }
                    string fresh = d != null && d.ContainsKey("versionName") ? Convert.ToString(d["versionName"]) : "";
                    int code = d != null && d.ContainsKey("versionCode") ? (int)Num(d, "versionCode") : 0;
                    string file = d != null && d.ContainsKey("file") ? Convert.ToString(d["file"]) : "";
                    string sha = d != null && d.ContainsKey("sha256") ? Convert.ToString(d["sha256"]) : "";
                    bool newer = IsNewerVersion(fresh);
                    Ui(() =>
                    {
                        if (newer)
                        {
                            updateVersion = fresh;
                            updateFile = file;
                            updateSha = sha;
                            updateCode = code;
                            lblSub.Text = (L.Lang == "en" ? "new version " : "новая версия ") + fresh;
                            SetUpdateAvailable(true, L.T("update") + " " + fresh);
                            AutoInstallUpdate();
                        }
                        else
                        {
                            SetUpdateAvailable(false, "");
                            lblSub.Text = L.Lang == "en" ? "latest version installed" : "установлена последняя версия";
                        }
                    });
                });
                return;
            }
            AutoInstallUpdate();
        }
    }

    class Bar : Panel
    {
        public Color Track = Color.Gray;
        public Color Fill = Color.White;
        public int Value;

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Math.Max(3, Height);
            using (var p = new Pen(Track, h)) g.DrawLine(p, 0, h / 2, Width, h / 2);
            int w = (int)(Width * (Math.Max(0, Math.Min(100, Value)) / 100.0));
            if (w > 0)
            {
                Color c = Fill;
                using (var p = new Pen(c, h)) g.DrawLine(p, 0, h / 2, w, h / 2);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0318)
            {
                using (var g = Graphics.FromHdc(m.WParam))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var e = new PaintEventArgs(g, ClientRectangle);
                    OnPaintBackground(e);
                    OnPaint(e);
                }
                return;
            }
            base.WndProc(ref m);
        }

    }

    class MenuColors : ProfessionalColorTable
    {
        readonly Theme th;

        public MenuColors(Theme t)
        {
            th = t;
        }

        public override Color MenuItemSelected { get { return th.Chip; } }
        public override Color MenuItemBorder { get { return th.Chip; } }
        public override Color MenuBorder { get { return th.Line; } }
        public override Color ToolStripDropDownBackground { get { return th.Panel; } }
        public override Color ImageMarginGradientBegin { get { return th.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return th.Panel; } }
        public override Color ImageMarginGradientEnd { get { return th.Panel; } }
        public override Color SeparatorDark { get { return th.Line; } }
        public override Color SeparatorLight { get { return th.Line; } }
    }

    class TaskDialog : Form
    {
        readonly Api api;
        readonly string boardId;
        readonly Theme th;
        readonly List<SectionItem> sections;
        readonly TaskItem task;

        TextBox txtTitle, txtNote, txtProgress;
        ComboBox cbSection, cbStatus;
        FlatButton okBtn;
        bool deleted;
        string deletedId = "";

        public bool Deleted { get { return deleted; } }

        public string DeletedId { get { return deletedId; } }

        public TaskDialog(Api api, string boardId, Theme th, List<SectionItem> sections, TaskItem task)
        {
            this.api = api;
            this.boardId = boardId;
            this.th = th;
            this.sections = sections;
            this.task = task;

            Text = task == null ? (L.Lang == "en" ? "New task" : "Новая задача") : (L.Lang == "en" ? "Task" : "Задача");
            Width = 620;
            Height = 470;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = th.Back;
            ForeColor = th.Text;
            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);

            int y = 18;
            AddLabel(L.T("title"), 20, y); y += 22;
            txtTitle = AddBox(20, y, 560); y += 44;

            AddLabel(L.T("note"), 20, y); y += 22;
            txtNote = AddBox(20, y, 560);
            txtNote.Multiline = true;
            txtNote.Height = 92;
            y += 104;

            AddLabel(L.T("section"), 20, y);
            AddLabel(L.T("status"), 300, y);
            y += 22;
            cbSection = new ComboBox
            {
                Location = new Point(20, y),
                Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = th.Card,
                ForeColor = th.Text
            };
            cbSection.Items.Add("—");
            foreach (var s in sections) cbSection.Items.Add(s.Name);
            cbSection.SelectedIndex = 0;
            Controls.Add(cbSection);

            cbStatus = new ComboBox
            {
                Location = new Point(300, y),
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = th.Card,
                ForeColor = th.Text
            };
            cbStatus.Items.Add(L.T("todo"));
            cbStatus.Items.Add(L.T("doing"));
            cbStatus.Items.Add(L.T("blocked"));
            cbStatus.Items.Add(L.T("done"));
            cbStatus.SelectedIndex = 0;
            Controls.Add(cbStatus);
            y += 46;

            AddLabel(L.T("progress"), 20, y); y += 22;
            txtProgress = AddBox(20, y, 90);
            txtProgress.Text = "0";

            okBtn = new FlatButton { Text = L.T("save"), Location = new Point(380, y - 6), Size = new Size(120, 36), BackColor = th.Accent, ForeColor = th.AccentText };
            okBtn.Click += (snd, e) => Save();
            Controls.Add(okBtn);

            var cancel = new FlatButton { Text = L.T("cancel"), Location = new Point(506, y - 6), Size = new Size(74, 36), BackColor = th.Card, ForeColor = th.Text };
            cancel.Click += (snd, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            if (task != null)
            {
                txtTitle.Text = task.Title;
                txtNote.Text = task.Note;
                txtProgress.Text = task.Progress.ToString();
                int idx = sections.FindIndex(s => s.Id == task.Section);
                cbSection.SelectedIndex = idx >= 0 ? idx + 1 : 0;
                cbStatus.SelectedIndex = Math.Max(0, cbStatus.Items.IndexOf(L.T(task.Status)));

                var del = new FlatButton { Text = L.T("delete"), Location = new Point(20, y - 6), Size = new Size(110, 36), BackColor = th.Card, ForeColor = Color.FromArgb(220, 90, 90) };
                del.Click += (s, e) =>
                {
                    string ask = L.Lang == "en" ? "Delete this task?" : "Удалить задачу?";
                    if (MessageBox.Show(this, ask, "Nexus Board", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    del.Enabled = false;
                    string id = task.Id;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        int code;
                        try { code = api.DeleteTask(boardId, id); }
                        catch { code = 0; }
                        try
                        {
                            if (IsDisposed) return;
                            BeginInvoke((Action)(() =>
                            {
                                del.Enabled = true;
                                if (code == 200)
                                {
                                    deleted = true;
                                    deletedId = id;
                                    DialogResult = DialogResult.OK;
                                    Close();
                                    return;
                                }
                                if (code == 404)
                                {
                                    MessageBox.Show(this,
                                        L.Lang == "en" ? "Server does not support delete — update the server." : "Сервер не поддерживает удаление — обнови сервер.",
                                        "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    return;
                                }
                                MessageBox.Show(this,
                                    L.Lang == "en" ? "Delete failed." : "Удалить не получилось.", L.T("err"));
                            }));
                        }
                        catch { }
                    });
                };
                Controls.Add(del);
            }
        }

        void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = th.Dim });
        }

        TextBox AddBox(int x, int y, int w)
        {
            var box = new TextBox { Location = new Point(x, y), Width = w, BorderStyle = BorderStyle.FixedSingle, BackColor = th.Card, ForeColor = th.Text };
            Controls.Add(box);
            return box;
        }

        void Save()
        {
            string title = txtTitle.Text.Trim();
            if (title.Length == 0) return;
            string note = txtNote.Text.Trim();
            int prog;
            if (!int.TryParse(txtProgress.Text.Trim(), out prog)) prog = 0;
            prog = Math.Max(0, Math.Min(100, prog));
            string[] keys = { "todo", "doing", "blocked", "done" };
            string status = keys[Math.Max(0, cbStatus.SelectedIndex)];
            string section = cbSection.SelectedIndex > 0 && cbSection.SelectedIndex - 1 < sections.Count ? sections[cbSection.SelectedIndex - 1].Id : "";

            bool isNew = task == null;
            string id = isNew ? "" : task.Id;
            if (okBtn != null) okBtn.Enabled = false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = "";
                try
                {
                    if (isNew)
                    {
                        api.AddTask(boardId, title, note, section, status, prog);
                    }
                    else
                    {
                        api.UpdateTask(boardId, id, new Dictionary<string, object>
                        {
                            { "title", title }, { "note", note }, { "status", status }, { "progress", prog }, { "section", section }
                        });
                    }
                }
                catch (Exception ex) { err = ex.Message; }

                try
                {
                    if (IsDisposed) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (okBtn != null) okBtn.Enabled = true;
                        if (string.IsNullOrEmpty(err))
                        {
                            DialogResult = DialogResult.OK;
                            Close();
                            return;
                        }
                        MessageBox.Show(this, err, L.T("err"));
                    }));
                }
                catch { }
            });
        }
    }

    class KeyDialog : Form
    {
        public string Key = "";
        readonly Theme th;

        public KeyDialog(Theme theme, string current)
        {
            th = theme;
            Text = L.Lang == "en" ? "Board key" : "Ключ доски";
            Width = 560;
            Height = 268;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = th.Back;
            ForeColor = th.Text;
            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);

            Controls.Add(new Label
            {
                Text = L.Lang == "en"
                    ? "Paste the key from another device — phone or PC. Both ways work: the phone can join the PC board and the PC can join the phone board."
                    : "Вставь ключ с другого устройства — телефона или ПК. Работает в обе стороны: телефон подключается к доске ПК, ПК — к доске телефона.",
                Location = new Point(20, 18),
                Size = new Size(500, 62),
                ForeColor = th.Dim
            });

            var box = new TextBox
            {
                Location = new Point(20, 88),
                Width = 380,
                Text = current ?? "",
                BackColor = th.Card,
                ForeColor = th.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = FontCache.Get("Segoe UI", 12f, FontStyle.Regular)
            };
            Controls.Add(box);

            var paste = new FlatButton { Text = L.Lang == "en" ? "Paste" : "Вставить", Location = new Point(410, 86), Size = new Size(110, 30), BackColor = th.Card, ForeColor = th.Text };
            paste.Click += (s, e) =>
            {
                try
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip)) box.Text = Normalize(clip);
                }
                catch { }
            };
            Controls.Add(paste);

            var copy = new FlatButton { Text = L.Lang == "en" ? "Copy" : "Копировать", Location = new Point(20, 132), Size = new Size(150, 30), BackColor = th.Card, ForeColor = th.Text };
            copy.Click += (s, e) => { try { Clipboard.SetText(box.Text.Trim()); } catch { } };
            Controls.Add(copy);

            var hint = new Label
            {
                Text = L.Lang == "en" ? "16 characters: 0-9, a-f" : "16 символов: 0-9, a-f",
                Location = new Point(184, 138),
                AutoSize = true,
                ForeColor = th.Dim
            };
            Controls.Add(hint);

            var ok = new FlatButton { Text = L.Lang == "en" ? "Connect" : "Подключить", Location = new Point(300, 176), Size = new Size(140, 34), BackColor = th.Accent, ForeColor = th.AccentText };
            ok.Click += (s, e) => { Key = Normalize(box.Text); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);

            var cancel = new FlatButton { Text = L.Lang == "en" ? "Cancel" : "Отмена", Location = new Point(450, 176), Size = new Size(90, 34), BackColor = th.Card, ForeColor = th.Text };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = null;
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Key = Normalize(box.Text); DialogResult = DialogResult.OK; Close(); } };
        }

        static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string v = raw.Trim().ToLowerInvariant();
            int at = v.IndexOf("k=", StringComparison.Ordinal);
            if (at >= 0) v = v.Substring(at + 2);
            var sb = new StringBuilder();
            foreach (char c in v)
            {
                if (sb.Length >= 16) break;
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) sb.Append(c);
            }
            return sb.ToString();
        }
    }

    class SettingsDialog : Form
    {
        readonly Settings settings;
        readonly Theme th;
        public string BoardId;

        public SettingsDialog(Settings s, Theme th, string boardId)
        {
            settings = s;
            this.th = th;
            BoardId = boardId;

            Text = L.T("settings");
            Width = 640;
            Height = 470;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = th.Back;
            ForeColor = th.Text;
            Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);

            int y = 20;
            Controls.Add(new Label { Text = L.T("server"), Location = new Point(20, y), AutoSize = true, ForeColor = th.Dim });
            y += 24;
            var url = new TextBox { Location = new Point(20, y), Width = 580, Text = settings.Base, BackColor = th.Card, ForeColor = th.Text, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(url);
            y += 44;

            Controls.Add(new Label { Text = L.T("sync"), Location = new Point(20, y), AutoSize = true, ForeColor = th.Dim });
            y += 24;
            var key = new TextBox { Location = new Point(20, y), Width = 420, Text = BoardId, BackColor = th.Card, ForeColor = th.Text, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(key);

            var copy = new FlatButton { Text = L.T("copy"), Location = new Point(450, y - 2), Size = new Size(150, 28), BackColor = th.Card, ForeColor = th.Text };
            copy.Click += (snd, e) => { try { Clipboard.SetText(key.Text.Trim()); } catch { } };
            Controls.Add(copy);
            y += 30;

            Controls.Add(new Label { Text = L.T("sync_hint"), Location = new Point(20, y), AutoSize = true, ForeColor = th.Dim });
            y += 34;

            Controls.Add(new Label { Text = L.T("nexus_key"), Location = new Point(20, y), AutoSize = true, ForeColor = th.Dim });
            y += 24;
            var nkey = new TextBox { Location = new Point(20, y), Width = 420, Text = settings.Key, BackColor = th.Card, ForeColor = th.Text, BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(nkey);
            Controls.Add(new Label { Text = L.T("nexus_hint"), Location = new Point(450, y + 4), AutoSize = true, ForeColor = th.Dim });
            y += 40;

            var fresh = new FlatButton { Text = L.T("newboard"), Location = new Point(20, y), Size = new Size(230, 34), BackColor = th.Card, ForeColor = th.Text };
            fresh.Click += (snd, e) =>
            {
                string baseUrl = NormalizeBase(url.Text);
                if (baseUrl == null)
                {
                    MessageBox.Show(this, BadUrlText(), "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                fresh.Enabled = false;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string id = "";
                    string err = "";
                    try { id = new Api(baseUrl).NewBoard(); }
                    catch (Exception ex) { err = ex.Message; }
                    try
                    {
                        if (IsDisposed) return;
                        BeginInvoke((Action)(() =>
                        {
                            fresh.Enabled = true;
                            if (!string.IsNullOrEmpty(id))
                            {
                                url.Text = baseUrl;
                                key.Text = id;
                                return;
                            }
                            MessageBox.Show(this, string.IsNullOrEmpty(err) ? L.T("offline") : err, L.T("err"));
                        }));
                    }
                    catch { }
                });
            };
            Controls.Add(fresh);

            var save = new FlatButton { Text = L.T("save"), Location = new Point(420, 370), Size = new Size(180, 38), BackColor = th.Accent, ForeColor = th.AccentText };
            save.Click += (snd, e) =>
            {
                string baseUrl = NormalizeBase(url.Text);
                if (baseUrl == null)
                {
                    MessageBox.Show(this, BadUrlText(), "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string board = (key.Text ?? "").Trim().ToLowerInvariant();
                if (board.Length > 0 && !MainForm.IsBoardKey(board))
                {
                    MessageBox.Show(this, L.Lang == "en"
                        ? "Board key must be 16 hex characters: 0-9, a-f."
                        : "Ключ доски — 16 hex-символов: 0-9, a-f.",
                        "Nexus Board", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                settings.Base = baseUrl;
                settings.Board = board;
                settings.Key = (nkey.Text ?? "").Trim();
                settings.Save();
                BoardId = settings.Board;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);

            var close = new FlatButton { Text = L.T("cancel"), Location = new Point(310, 370), Size = new Size(100, 38), BackColor = th.Card, ForeColor = th.Text };
            close.Click += (snd, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);
        }

        static string BadUrlText()
        {
            return L.Lang == "en"
                ? "Server URL must be http:// or https:// without spaces, e.g. https://board.example.ru"
                : "Адрес сервера — http:// или https:// без пробелов, например https://board.example.ru";
        }

        /// <summary>Возвращает нормализованный адрес без хвостового слэша или null, если он неверный.</summary>
        internal static string NormalizeBase(string raw)
        {
            string v = (raw ?? "").Trim();
            if (v.Length == 0) return null;
            if (v.IndexOf(' ') >= 0 || v.IndexOf('\t') >= 0 || v.IndexOf('\n') >= 0) return null;
            if (v.IndexOf("://", StringComparison.Ordinal) < 0) v = "https://" + v;
            Uri uri;
            if (!Uri.TryCreate(v, UriKind.Absolute, out uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
            if (string.IsNullOrEmpty(uri.Host)) return null;
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }
    }

    static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label, Theme th)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.Width = 480;
                dlg.Height = 190;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = th.Back;
                dlg.ForeColor = th.Text;
                dlg.Font = FontCache.Get("Segoe UI", 9.5f, FontStyle.Regular);

                dlg.Controls.Add(new Label { Text = label, Location = new Point(18, 16), AutoSize = true, ForeColor = th.Dim });
                var box = new TextBox { Location = new Point(18, 44), Width = 424, BackColor = th.Card, ForeColor = th.Text, BorderStyle = BorderStyle.FixedSingle };
                dlg.Controls.Add(box);

                var ok = new FlatButton { Text = L.T("save"), Location = new Point(330, 92), Size = new Size(112, 34), BackColor = th.Accent, ForeColor = th.AccentText };
                ok.Click += (snd, e) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                dlg.Controls.Add(ok);

                var cancel = new FlatButton { Text = L.T("cancel"), Location = new Point(238, 92), Size = new Size(86, 34), BackColor = th.Card, ForeColor = th.Text };
                cancel.Click += (snd, e) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };
                dlg.Controls.Add(cancel);

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                return dlg.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : "";
            }
        }
    }
}
