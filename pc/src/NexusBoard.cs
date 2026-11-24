using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NexusBoard
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        public const string Version = "2.0.1";
        public const string UpdateArg = "/update";

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static void Main(string[] args)
        {
            string[] cliVerbs = { "--add", "--plan", "--start", "--doing", "--done", "--block", "--blocked",
                "--todo", "--drop", "--dropped", "--progress", "--note", "--log", "--mode", "--clear",
                "--open", "--autostart", "--path", "--help", "-h", "--list", "--json", "--selftest", "--chat-list", "--chat", "--chat-say" };
            if (args.Length >= 1 && Array.IndexOf(cliVerbs, args[0]) >= 0)
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

            if (args.Length >= 2 && args[0] == UpdateArg)
            {
                try
                {
                    string target = args[1];
                    string self = Application.ExecutablePath;
                    for (int i = 0; i < 40; i++)
                    {
                        try
                        {
                            File.Copy(self, target, true);
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(500);
                        }
                    }
                    System.Diagnostics.Process.Start(target);
                }
                catch { }
                return;
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            try { ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true; } catch { }

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
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--theme" && i + 1 < args.Length) shotTheme = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length) shotLang = args[++i];
                else if (args[i] == "--view" && i + 1 < args.Length) shotView = args[++i];
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

            Application.Run(new MainForm());
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

    class Api
    {
        public string Base;
        public string Key = "";

        public Api(string baseUrl)
        {
            Base = baseUrl.TrimEnd('/');
        }

        string Get(string path)
        {
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("User-Agent", "NexusBoardPC/" + Program.Version);
                return wc.DownloadString(Base + path);
            }
        }

        string Post(string path, string json)
        {
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("Content-Type", "application/json");
                wc.Headers.Add("User-Agent", "NexusBoardPC/" + Program.Version);
                return wc.UploadString(Base + path, json);
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
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Path_(rect, Radius))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
                using (var p = new Pen(BorderColor)) g.DrawPath(p, path);
            }
            base.OnPaint(e);
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
        List<ChatMsg> chatMsgs = new List<ChatMsg>();
        long chatLast = 0;
        Dictionary<string, object> train;

        Label lblTitle, lblSub;
        FlowLayoutPanel tabs, list;
        FlatButton btnRefresh, btnMenu, btnUpdate, btnKey;
        Panel chatBar;
        TextBox chatInput;
        FlatButton chatSend;
        string updateVersion = "";
        System.Windows.Forms.Timer timer;
        ContextMenuStrip menu;

        public MainForm()
        {
            settings = Settings.Load();
            L.Lang = settings.Lang == "en" ? "en" : "ru";
            th = Theme.Get(settings.Theme);
            api = new Api(settings.Base);
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

            Shown += (s, e) =>
            {
                ApplyTheme();
                if (string.IsNullOrEmpty(boardId)) CreateBoard(); else LoadBoard();
                CheckUpdate();
            };

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) LoadBoard();
                if (e.Control && e.KeyCode == Keys.N) EditTask(null);
                if (e.Control && e.Alt && e.KeyCode == Keys.S) OpenSettings();
            };

            timer = new System.Windows.Forms.Timer { Interval = 10000 };
            timer.Tick += (s, e) => { if (chatView) LoadChat(false); else if (trainView) LoadTrain(); else LoadBoard(); };
            timer.Start();
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
            btnRefresh.Click += (s, e) => LoadBoard();
            header.Controls.Add(btnRefresh);

            btnKey = new FlatButton { Text = "КЛЮЧ", Font = FontCache.Get("Segoe UI Semibold", 8.5f, FontStyle.Bold), Size = new Size(58, 40) };
            btnKey.Click += (s, e) => ShowConnect();
            header.Controls.Add(btnKey);

            btnUpdate = new FlatButton { Text = "↓", Size = new Size(46, 40) };
            btnUpdate.Click += Click_Update;
            header.Controls.Add(btnUpdate);

            var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.Transparent };
            Controls.Add(bar);
            bar.BringToFront();
            tabs = new FlowLayoutPanel { Location = new Point(30, 6), Height = 44, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
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

            list = new FlowLayoutPanel
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

            Resize += (s, e) => LayoutHeader();
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
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexusboard-selftest.txt"),
                    "SELFTEST OK themes=" + Theme.All.Length + " lang=" + L.Lang);
            }
            catch { }
        }

        void BuildMenu()
        {
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
            menu.BackColor = th.Panel;
            menu.ForeColor = th.Text;
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(th));
            foreach (ToolStripItem it in menu.Items)
            {
                it.ForeColor = th.Text;
                it.BackColor = th.Panel;
            }
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
            RenderTabs();
            if (chatView) RenderChat(); else if (trainView) RenderTraining(); else RenderTasks();
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
            tabs.Controls.Add(TabChip(L.T("tab_board"), "__board", !trainView && !chatView));
            tabs.Controls.Add(TabChip(L.T("tab_train"), "__train", trainView));
            tabs.Controls.Add(TabChip(L.T("tab_chat"), "__chat", chatView));
            if (trainView || chatView) return;
            tabs.Controls.Add(TabChip(L.T("all"), "", activeSection == ""));
            foreach (var s in sections) tabs.Controls.Add(TabChip(s.Name, s.Id, activeSection == s.Id));
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
                if (id == "__train") { trainView = true; chatView = false; Render(); LoadTrain(); return; }
                if (id == "__chat") { chatView = true; trainView = false; Render(); LoadChat(true); return; }
                if (id == "__board") { trainView = false; chatView = false; Render(); LoadBoard(); return; }
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

        void RenderTasks()
        {
            list.SuspendLayout();
            ClearControls(list);
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
                    Width = Math.Max(380, list.Width - 60),
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
                Width = Math.Max(420, list.Width - 58),
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
                    BeginInvoke((Action)(() =>
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
                    }));
                }
                catch
                {
                    BeginInvoke((Action)(() => lblSub.Text = L.T("offline")));
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
            api.Key = settings.Key;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var data = api.Train();
                    BeginInvoke((Action)(() =>
                    {
                        train = data;
                        lastSync = DateTime.Now;
                        Render();
                    }));
                }
                catch
                {
                    BeginInvoke((Action)(() => lblSub.Text = L.T("offline")));
                }
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
                int bubbleWidth = Math.Min(width, 640);
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
                    Text = (mine ? L.T("chat_me") : L.T("chat_ai")) + " · " + DateTimeOffset.FromUnixTimeMilliseconds(m.Ts).ToLocalTime().ToString("HH:mm"),
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
            try
            {
                if (reset)
                {
                    chatMsgs.Clear();
                    chatLast = 0;
                }
                var snap = api.ChatList(boardId, chatLast);
                if (snap == null) return;
                if (snap.ContainsKey("messages") && snap["messages"] is object[])
                {
                    foreach (var o in (object[])snap["messages"])
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        var msg = new ChatMsg
                        {
                            Id = Str(d, "id"),
                            From = Str(d, "from"),
                            Text = Str(d, "text"),
                            Ts = Convert.ToInt64(d.ContainsKey("ts") ? d["ts"] : 0L)
                        };
                        if (chatMsgs.Exists(x => x.Id == msg.Id)) continue;
                        chatMsgs.Add(msg);
                        if (msg.Ts > chatLast) chatLast = msg.Ts;
                    }
                }
                if (chatMsgs.Count > 300) chatMsgs = chatMsgs.GetRange(chatMsgs.Count - 300, 300);
                if (chatView) RenderChat();
                lblSub.Text = chatLast > 0
                    ? (L.Lang == "en" ? "chat · replies come from the AI" : "чат · отвечает ИИ")
                    : (L.Lang == "en" ? "chat" : "чат");
            }
            catch { }
        }

        void SendChat()
        {
            string text = (chatInput.Text ?? "").Trim();
            if (text.Length == 0 || string.IsNullOrEmpty(boardId)) return;
            chatInput.Text = "";
            try
            {
                api.ChatSend(boardId, "owner", text);
                LoadChat(false);
            }
            catch
            {
                chatInput.Text = text;
            }
        }

        void RenderTraining()
        {
            if (string.IsNullOrEmpty(settings.Key))
            {
                ClearControls(list);
                var hint = new Round { Radius = 18, Fill = th.Card, BorderColor = th.Line, Size = new Size(560, 108) };
                hint.Controls.Add(new Label
                {
                    Text = L.Lang == "en"
                        ? "Training needs the site access key.\nSettings (Ctrl+Alt+S) → field «Site key»."
                        : "Для обучения нужен ключ доступа к сайту.\nНастройки (Ctrl+Alt+S) → поле «Ключ сайта».",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = th.Dim,
                    BackColor = Color.Transparent,
                    Font = FontCache.Get("Segoe UI", 10f, FontStyle.Regular)
                });
                list.Controls.Add(hint);
                return;
            }
            ClearControls(list);
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            int w = Math.Max(600, list.ClientSize.Width - 60);

            if (string.IsNullOrEmpty(settings.Key))
            {
                var card = Card(w, 110);
                card.Controls.Add(Abs(Txt(L.T("tr_nokey"), th, 10f, false, th.Dim), 20, 22));
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
            string stage = train.ContainsKey("stage") ? Str(train, "stage") : "";
            string unit = train.ContainsKey("unit") ? Str(train, "unit") : "";
            double pct = train.ContainsKey("pct") ? Num(train, "pct") : 0;
            string step = "—";
            if (train.ContainsKey("step"))
                step = Convert.ToString(train["step"]) + " / " + (train.ContainsKey("total") ? Convert.ToString(train["total"]) : "?") + "  ·  " + pct.ToString("0") + "%";

            var head = Card(w, 168);
            head.Controls.Add(Abs(Txt(alive ? L.T("tr_running") : L.T("tr_dead"), th, 9.5f, true,
                alive ? th.Accent : Color.FromArgb(214, 132, 52)), 22, 18));
            head.Controls.Add(Abs(Txt(run, th, 20f, true, th.Text), 22, 42));
            head.Controls.Add(Abs(Txt((stage + (string.IsNullOrEmpty(unit) ? "" : " · " + unit)).Trim(), th, 9.5f, false, th.Dim), 22, 84));
            var bar = new Bar { Value = (int)Math.Round(pct), Location = new Point(22, 112), Size = new Size(w - 44, 8) };
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
            metrics.Controls.Add(Metric(L.T("tr_rate"), train.ContainsKey("rate") ? Str(train, "rate") : "—", mw));
            metrics.Controls.Add(Metric(L.T("tr_step"), train.ContainsKey("step") ? Convert.ToString(train["step"]) : "—", mw));
            list.Controls.Add(metrics);

            var chain = Card(w, 0);
            chain.AutoSize = false;
            chain.Height = 128;
            chain.Controls.Add(Abs(Txt(L.T("tr_chain"), th, 8.5f, true, th.Dim), 20, 14));
            chain.Controls.Add(Abs(Txt(Join(train, "chainLines", 6), th, 8.5f, false, th.Dim), 20, 38));
            list.Controls.Add(chain);

            var log = Card(w, 150);
            log.Controls.Add(Abs(Txt(L.T("tr_log"), th, 8.5f, true, th.Dim), 20, 14));
            var mono = Txt(Join(train, "chainLines", 10), th, 8.5f, false, th.Dim);
            mono.Font = FontCache.Get("Consolas", 8.5f, FontStyle.Regular);
            log.Controls.Add(Abs(mono, 20, 38));
            list.Controls.Add(log);
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

                    BeginInvoke((Action)(() =>
                    {
                        tasks = parsedTasks;
                        sections = parsedSections;
                        lastSync = DateTime.Now;
                        Render();
                    }));
                }
                catch
                {
                    BeginInvoke((Action)(() => lblSub.Text = L.T("offline")));
                }
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
            using (var dlg = new TaskDialog(api, boardId, th, sections, t))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadBoard();
            }
        }

        void NewSection()
        {
            string name = Prompt.Show(this, L.T("newsect"), L.T("sectname"), th);
            if (string.IsNullOrEmpty(name)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { api.AddSection(boardId, name); } catch { }
                BeginInvoke((Action)LoadBoard);
            });
        }

        public void ShotLook(string theme, string lang, string view)
        {
            if (view == "chat")
            {
                chatView = true;
            }
            if (!string.IsNullOrEmpty(theme))
            {
                th = Theme.Get(theme);
                settings.Theme = th.Name;
            }
            if (!string.IsNullOrEmpty(lang))
            {
                L.Lang = lang == "en" ? "en" : "ru";
                settings.Lang = L.Lang;
            }
            settings.Save();
            ApplyTheme();
            if (chatView) { Render(); LoadChat(true); } else LoadBoard();
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
            string body = L.T("connect_body") + Environment.NewLine + Environment.NewLine + (boardId.Length == 0 ? "—" : boardId);
            var r = MessageBox.Show(this, body, L.T("connect"), MessageBoxButtons.OKCancel);
            if (r == DialogResult.OK && boardId.Length > 0)
            {
                try { Clipboard.SetText(boardId); } catch { }
                lblSub.Text = L.T("copied");
            }
        }

        void OpenSettings()
        {
            using (var dlg = new SettingsDialog(settings, th, boardId))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    api = new Api(settings.Base);
                    boardId = settings.Board;
                    th = Theme.Get(settings.Theme);
                    L.Lang = settings.Lang;
                    BuildMenu();
                    ApplyTheme();
                    if (string.IsNullOrEmpty(boardId)) CreateBoard(); else LoadBoard();
                }
            }
        }

        void CheckUpdate()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string raw = api.CheckUpdate();
                if (string.IsNullOrEmpty(raw)) return;
                try
                {
                    var d = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                    if (d == null) return;
                    string ver = d.ContainsKey("versionName") ? Convert.ToString(d["versionName"]) : "";
                    if (ver != Program.Version && !string.IsNullOrEmpty(ver))
                    {
                        updateVersion = ver;
                        bool already = settings.AutoUpdatedTo == ver;
                        BeginInvoke((Action)(() =>
                        {
                            btnUpdate.Visible = true;
                            btnUpdate.Text = "↓";
                            LayoutHeader();
                            var tip = new ToolTip();
                            tip.SetToolTip(btnUpdate, L.T("update") + " " + ver);
                            if (!already)
                            {
                                settings.AutoUpdatedTo = ver;
                                settings.Save();
                                AutoInstallUpdate();
                            }
                        }));
                    }
                }
                catch { }
            });
        }

        void AutoInstallUpdate()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NexusBoard-new.exe");
                try
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    using (var wc = new WebClient()) wc.DownloadFile(settings.Base.TrimEnd('/') + "/app/pc/NexusBoard.exe", tmp);
                    var info = new FileInfo(tmp);
                    if (!info.Exists || info.Length < 20000) return;
                    try
                    {
                        BeginInvoke((Action)(() => { lblSub.Text = (L.Lang == "en" ? "updating… " : "обновляюсь… "); }));
                    }
                    catch { }
                    System.Diagnostics.Process.Start(tmp, Program.UpdateArg + " \"" + Application.ExecutablePath + "\"");
                    try { BeginInvoke((Action)(() => Close())); } catch { }
                }
                catch
                {
                    // сеть/антивирус — тихо оставляем текущую версию, кнопка ↓ остаётся
                }
            });
        }

        void Click_Update(object s, EventArgs e)
        {
            if (string.IsNullOrEmpty(updateVersion))
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string raw = api.CheckUpdate();
                    string fresh = "";
                    try
                    {
                        var d = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                        if (d != null && d.ContainsKey("versionName")) fresh = Convert.ToString(d["versionName"]);
                    }
                    catch { }
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (!string.IsNullOrEmpty(fresh) && fresh != Program.Version)
                            {
                                updateVersion = fresh;
                                lblSub.Text = (L.Lang == "en" ? "new version " : "новая версия ") + fresh;
                                AutoInstallUpdate();
                            }
                            else
                            {
                                lblSub.Text = L.Lang == "en" ? "latest version installed" : "установлена последняя версия";
                            }
                        }));
                    }
                    catch { }
                });
                return;
            }
            try
            {
                string url = settings.Base.TrimEnd('/') + "/app/pc/NexusBoard.exe";
                string tmp = Path.Combine(Path.GetTempPath(), "NexusBoard-new.exe");
                using (var wc = new WebClient()) wc.DownloadFile(url, tmp);
                string self = Application.ExecutablePath;
                System.Diagnostics.Process.Start(tmp, Program.UpdateArg + " \"" + self + "\"");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L.T("err"));
            }
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
        bool deleted;

        public bool Deleted { get { return deleted; } }

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
            cbSection = new ComboBox { Location = new Point(20, y), Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            cbSection.Items.Add("—");
            foreach (var s in sections) cbSection.Items.Add(s.Name);
            cbSection.SelectedIndex = 0;
            Controls.Add(cbSection);

            cbStatus = new ComboBox { Location = new Point(300, y), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
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

            var ok = new FlatButton { Text = L.T("save"), Location = new Point(380, y - 6), Size = new Size(120, 36), BackColor = th.Accent, ForeColor = th.AccentText };
            ok.Click += (snd, e) => Save();
            Controls.Add(ok);

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
                    try { api.UpdateTask(boardId, task.Id, new Dictionary<string, object> { { "status", "done" }, { "progress", 100 } }); } catch { }
                    deleted = true;
                    DialogResult = DialogResult.OK;
                    Close();
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

            try
            {
                if (task == null)
                {
                    api.AddTask(boardId, title, note, section, status, prog);
                }
                else
                {
                    api.UpdateTask(boardId, task.Id, new Dictionary<string, object>
                    {
                        { "title", title }, { "note", note }, { "status", status }, { "progress", prog }, { "section", section }
                    });
                }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L.T("err"));
            }
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
                try
                {
                    var api = new Api(url.Text.Trim());
                    string id = api.NewBoard();
                    if (!string.IsNullOrEmpty(id)) key.Text = id;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, L.T("err"));
                }
            };
            Controls.Add(fresh);

            var save = new FlatButton { Text = L.T("save"), Location = new Point(420, 370), Size = new Size(180, 38), BackColor = th.Accent, ForeColor = th.AccentText };
            save.Click += (snd, e) =>
            {
                settings.Base = url.Text.Trim();
                settings.Board = key.Text.Trim();
                settings.Key = nkey.Text.Trim();
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
