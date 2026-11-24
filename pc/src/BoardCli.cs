using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NexusBoard.Cli
{
    public class TaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Mode { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }
        public int Progress { get; set; }
        public int Priority { get; set; }
        public string Source { get; set; }
        public string Created { get; set; }
        public string Updated { get; set; }

        public TaskItem()
        {
            Id = "";
            Title = "";
            Mode = "task";
            Status = "todo";
            Note = "";
            Source = "ai";
            Created = Now();
            Updated = Now();
            Priority = 3;
        }

        public static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }

    public class Board
    {
        public string Mode { get; set; }
        public int NextId { get; set; }
        public string Updated { get; set; }
        public List<TaskItem> Tasks { get; set; }

        public Board()
        {
            Mode = "idle";
            NextId = 1;
            Updated = TaskItem.Now();
            Tasks = new List<TaskItem>();
        }
    }

    public static class Store
    {
        public static string Root
        {
            get
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(exe);
                string up = Path.GetFullPath(Path.Combine(dir, "..", "data"));
                string local = Path.Combine(dir, "data");
                string root = File.Exists(Path.Combine(up, "tasks.json")) ? up : local;
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string TasksFile { get { return Path.Combine(Root, "tasks.json"); } }
        public static string ActivityFile { get { return Path.Combine(Root, "activity.ndjson"); } }
        public static string LockFile { get { return Path.Combine(Root, "tasks.lock"); } }

        static FileStream Acquire()
        {
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    return new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    Thread.Sleep(40);
                }
            }
            throw new IOException("не удалось взять блокировку tasks.lock");
        }

        public static Board Load()
        {
            if (!File.Exists(TasksFile)) return new Board();
            Exception last = null;
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    string json = File.ReadAllText(TasksFile, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(json)) return new Board();
                    Board b = new JavaScriptSerializer().Deserialize<Board>(json);
                    if (b == null) return new Board();
                    if (b.Tasks == null) b.Tasks = new List<TaskItem>();
                    return b;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(40);
                }
            }
            // Файл битый: сохраняем его рядом с датой, чтобы он не пропал при
            // первом же сохранении, и начинаем с чистой доски.
            try
            {
                string bad = TasksFile + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(TasksFile, bad, true);
                Console.Error.WriteLine("tasks.json не прочитан (" +
                    (last == null ? "?" : last.Message) + "), копия: " + bad);
            }
            catch (Exception) { }
            return new Board();
        }

        public static void Save(Board b)
        {
            b.Updated = TaskItem.Now();
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;
            string json = ser.Serialize(b);
            string tmp = TasksFile + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(TasksFile))
            {
                try { File.Replace(tmp, TasksFile, null); }
                catch (Exception) { File.Copy(tmp, TasksFile, true); try { File.Delete(tmp); } catch { } }
            }
            else
            {
                File.Move(tmp, TasksFile);
            }
        }

        public static T Mutate<T>(Func<Board, T> fn)
        {
            using (Acquire())
            {
                Board b = Load();
                T result = fn(b);
                Save(b);
                return result;
            }
        }

        public static void Log(string kind, string text)
        {
            try
            {
                string line = new JavaScriptSerializer().Serialize(new Dictionary<string, string>
                {
                    { "ts", TaskItem.Now() },
                    { "kind", kind },
                    { "text", text }
                });
                using (var fs = new FileStream(ActivityFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    sw.WriteLine(line);
                }
            }
            catch (Exception) { }
        }

        public static List<string> LastActivity(int count)
        {
            var result = new List<string>();
            try
            {
                if (!File.Exists(ActivityFile)) return result;
                string[] lines = File.ReadAllLines(ActivityFile, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0 && result.Count < count; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try
                    {
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(lines[i]);
                        string ts = d.ContainsKey("ts") ? Convert.ToString(d["ts"]) : "";
                        string text = d.ContainsKey("text") ? Convert.ToString(d["text"]) : "";
                        result.Add(ShortTime(ts) + "  " + text);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return result;
        }

        static string ShortTime(string iso)
        {
            DateTime dt;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                if (dt.Date == DateTime.Today) return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
                return dt.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
            }
            return iso;
        }
    }

    public static class Lang
    {
        public static string Current = "ru";

        public static bool IsRu { get { return Current != "en"; } }

        public static string UiFile { get { return Path.Combine(Store.Root, "ui.json"); } }

        static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            { "app.subtitle", "доска задач ИИ" },
            { "col.todo", "В ПЛАНЕ" },
            { "col.doing", "В РАБОТЕ" },
            { "col.blocked", "БЛОК" },
            { "col.done", "ГОТОВО" },
            { "mode.night", "НОЧЬ" },
            { "mode.auto", "АВТОНОМ" },
            { "mode.task", "ЗАДАЧА" },
            { "mode.idle", "ОЖИДАНИЕ" },
            { "status.todo", "ПЛАН" },
            { "status.doing", "В РАБОТЕ" },
            { "status.blocked", "БЛОК" },
            { "status.done", "ГОТОВО" },
            { "status.dropped", "СНЯТО" },
            { "stats.planned", "в плане" },
            { "stats.doing", "в работе" },
            { "stats.closed", "закрыто" },
            { "empty.free", "свободно" },
            { "empty.done", "пока пусто" },
            { "footer.last", "последнее:" },
            { "footer.empty", "лента пуста" },
            { "footer.updated", "обновлено" },
            { "time.now", "только что" },
            { "time.min", "мин назад" },
            { "time.hour", "ч назад" },
            { "meta.you", "ты" },
            { "log.lang", "язык интерфейса" },
            { "log.task", "задача" },
            { "log.mode", "режим →" },
            { "log.moved", "ты переставил" },
            { "log.cleared", "удалено задач:" }
        };

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "app.subtitle", "AI task board" },
            { "col.todo", "PLANNED" },
            { "col.doing", "IN PROGRESS" },
            { "col.blocked", "BLOCKED" },
            { "col.done", "DONE" },
            { "mode.night", "NIGHT" },
            { "mode.auto", "AUTONOMOUS" },
            { "mode.task", "TASK" },
            { "mode.idle", "IDLE" },
            { "status.todo", "PLAN" },
            { "status.doing", "IN PROGRESS" },
            { "status.blocked", "BLOCKED" },
            { "status.done", "DONE" },
            { "status.dropped", "DROPPED" },
            { "stats.planned", "planned" },
            { "stats.doing", "in progress" },
            { "stats.closed", "closed" },
            { "empty.free", "free" },
            { "empty.done", "nothing yet" },
            { "footer.last", "last:" },
            { "footer.empty", "feed is empty" },
            { "footer.updated", "updated" },
            { "time.now", "just now" },
            { "time.min", "min ago" },
            { "time.hour", "h ago" },
            { "meta.you", "you" },
            { "log.lang", "interface language" },
            { "log.task", "task" },
            { "log.mode", "mode →" },
            { "log.moved", "you moved" },
            { "log.cleared", "tasks removed:" }
        };

        public static string T(string key)
        {
            string v;
            if ((IsRu ? Ru : En).TryGetValue(key, out v)) return v;
            if (Ru.TryGetValue(key, out v)) return v;
            return key;
        }

        public static void Load()
        {
            Current = "ru";
            try
            {
                if (!File.Exists(UiFile)) return;
                string json = File.ReadAllText(UiFile, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (d == null || !d.ContainsKey("lang")) return;
                string v = (Convert.ToString(d["lang"]) ?? "").Trim().ToLowerInvariant();
                Current = v == "en" ? "en" : "ru";
            }
            catch (Exception) { Current = "ru"; }
        }

        public static bool Save()
        {
            try
            {
                File.WriteAllText(UiFile, "{\"lang\":\"" + Current + "\"}", new UTF8Encoding(false));
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Set(string lang)
        {
            Current = (lang ?? "").Trim().ToLowerInvariant() == "en" ? "en" : "ru";
            Save();
            Store.Log("lang", T("log.lang") + " → " + Current);
        }

        public static void Toggle()
        {
            Set(IsRu ? "en" : "ru");
        }
    }

    public static class Ui
    {
        public const string MutexName = "Local\\NexusTasksUI_v1";
        public const string ShowEventName = "Local\\NexusTasksShow_v1";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "NexusBoard";

        public static string ExePath
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        public static bool IsRunning()
        {
            try
            {
                using (Mutex.OpenExisting(MutexName)) return true;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
            catch (Exception) { return false; }
        }

        public static Mutex Acquire()
        {
            bool created;
            Mutex m = new Mutex(true, MutexName, out created);
            return created ? m : null;
        }

        public static bool Ensure()
        {
            if (IsRunning()) return false;
            try
            {
                var psi = new ProcessStartInfo(ExePath);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = Path.GetDirectoryName(ExePath);
                Process.Start(psi);
            }
            catch (Exception) { return false; }
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(75);
                if (IsRunning()) return true;
            }
            return true;
        }

        public static void SignalShow()
        {
            try
            {
                using (EventWaitHandle e = EventWaitHandle.OpenExisting(ShowEventName)) e.Set();
            }
            catch (Exception) { }
        }

        public static string AutostartState()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return "выкл";
                    object v = k.GetValue(RunValue);
                    if (v == null) return "выкл";
                    return "вкл → " + Convert.ToString(v);
                }
            }
            catch (Exception ex) { return "ошибка: " + ex.Message; }
        }

        public static bool SetAutostart(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    if (on) k.SetValue(RunValue, "\"" + ExePath + "\"");
                    else k.DeleteValue(RunValue, false);
                }
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    public static class Cli
    {
        static Dictionary<string, string> Parse(string[] args)
        {
            var d = new Dictionary<string, string>();
            var order = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--"))
                {
                    string key = a.Substring(2).ToLowerInvariant();
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        d[key] = args[i + 1];
                        i++;
                    }
                    else d[key] = "";
                }
                else order.Add(a);
            }
            d["_positional"] = string.Join("\u0001", order);
            return d;
        }

        static string Get(Dictionary<string, string> d, string key)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        static TaskItem Find(Board b, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in b.Tasks) if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        static NexusBoard.Settings ChatSettings()
        {
            return NexusBoard.Settings.Load();
        }

        static string ChatBase(NexusBoard.Settings s)
        {
            string b = string.IsNullOrEmpty(s.Base) ? "https://board.nexus-your-heper.ru" : s.Base;
            return b.TrimEnd('/');
        }

        static string HttpJson(string url, string postJson)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
            }
            catch { }
            using (var wc = new System.Net.WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("User-Agent", "NexusBoardPC/chat");
                if (postJson != null)
                {
                    wc.Headers.Add("Content-Type", "application/json");
                    return wc.UploadString(url, postJson);
                }
                return wc.DownloadString(url);
            }
        }

        static int ChatListCmd(bool json)
        {
            var s = ChatSettings();
            if (string.IsNullOrEmpty(s.Board)) { Out("ERR нет ключа доски: открой приложение → Ключ"); return 2; }
            string raw;
            try { raw = HttpJson(ChatBase(s) + "/api/c/list?board=" + s.Board + "&since=0&limit=50", null); }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
            if (json) { Out(raw); return 0; }
            var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
            if (o == null || !o.ContainsKey("messages")) { Out("ERR пустой ответ"); return 1; }
            var arr = o["messages"] as object[];
            if (arr == null || arr.Length == 0) { Out("чат пуст"); return 0; }
            foreach (var item in arr)
            {
                var m = item as Dictionary<string, object>;
                if (m == null) continue;
                string who = Convert.ToString(m.ContainsKey("from") ? m["from"] : "") == "ai" ? "ИИ" : "ты";
                long ts = m.ContainsKey("ts") ? Convert.ToInt64(m["ts"]) : 0;
                string when = ts > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("HH:mm") : "";
                string text = Convert.ToString(m.ContainsKey("text") ? m["text"] : "").Replace("\r", " ").Replace("\n", " ");
                Out(when + " " + who + ": " + text);
            }
            return 0;
        }

        static int ChatSayCmd(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { Out("ERR пустой текст"); return 2; }
            var s = ChatSettings();
            if (string.IsNullOrEmpty(s.Board)) { Out("ERR нет ключа доски"); return 2; }
            var payload = new Dictionary<string, object> { { "board", s.Board }, { "from", "ai" }, { "text", text } };
            try
            {
                string raw = HttpJson(ChatBase(s) + "/api/c/send", new JavaScriptSerializer().Serialize(payload));
                var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                bool ok = o != null && o.ContainsKey("ok") && Convert.ToBoolean(o["ok"]);
                Out(ok ? "SENT" : "ERR " + raw);
                return ok ? 0 : 1;
            }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
        }

        public static int Run(string[] args)
        {
            var d = Parse(args);
            string cmd = "";
            for (int i = 0; i < args.Length; i++) { if (args[i].StartsWith("--")) { cmd = args[i].Substring(2).ToLowerInvariant(); break; } }

            string[] autoUi = { "add", "plan", "start", "doing", "done", "block", "blocked", "drop", "dropped", "todo", "progress", "note", "log", "mode", "clear", "open" };
            if (Array.IndexOf(autoUi, cmd) >= 0) try { Ui.Ensure(); } catch { }

            switch (cmd)
            {
                case "chat-list":
                case "chat":
                    return ChatListCmd(d.ContainsKey("json"));

                case "chat-say":
                    {
                        string text = Get(d, "chat-say");
                        if (string.IsNullOrWhiteSpace(text)) text = Get(d, "_positional").Replace("", " ");
                        return ChatSayCmd(text);
                    }

                case "help":
                case "h":
                case "?":
                    Out(HelpText());
                    return 0;

                case "add":
                case "plan":
                    {
                        string title = Get(d, "add") ?? Get(d, "plan");
                        if (string.IsNullOrWhiteSpace(title)) title = Get(d, "_positional").Replace("\u0001", " ");
                        if (string.IsNullOrWhiteSpace(title)) { Out("ERR пустой заголовок"); return 2; }
                        string mode = Get(d, "mode") ?? "task";
                        string note = Get(d, "note") ?? "";
                        int prio = IntOf(Get(d, "prio"), 3);
                        var item = Store.Mutate(b =>
                        {
                            var t = new TaskItem();
                            t.Id = "t" + b.NextId;
                            b.NextId++;
                            t.Title = title.Trim();
                            t.Mode = mode.Trim().ToLowerInvariant();
                            t.Note = note;
                            t.Priority = prio < 1 ? 1 : (prio > 5 ? 5 : prio);
                            t.Source = Get(d, "source") ?? "ai";
                            b.Tasks.Add(t);
                            return t;
                        });
                        Store.Log("add", Lang.T("log.task") + " " + item.Id + ": " + item.Title);
                        Out("OK " + item.Id);
                        return 0;
                    }

                case "start":
                case "doing":
                    return SetStatus(d, cmd, "doing", 5);

                case "done":
                    return SetStatus(d, cmd, "done", 100);

                case "block":
                case "blocked":
                    return SetStatus(d, cmd, "blocked", -1);

                case "drop":
                case "dropped":
                    return SetStatus(d, cmd, "dropped", -1);

                case "todo":
                    return SetStatus(d, cmd, "todo", 0);

                case "progress":
                    {
                        string id = Get(d, "progress");
                        int val = IntOf(Get(d, "to"), IntOf(Get(d, "_positional"), -1));
                        if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --progress t3 --to 40"); return 2; }
                        bool ok = Store.Mutate(b =>
                        {
                            var t = Find(b, id);
                            if (t == null) return false;
                            t.Progress = val < 0 ? 0 : (val > 100 ? 100 : val);
                            t.Updated = TaskItem.Now();
                            if (!string.IsNullOrEmpty(Get(d, "note"))) t.Note = Get(d, "note");
                            return true;
                        });
                        if (!ok) { Out("ERR нет задачи " + id); return 3; }
                        Store.Log("progress", id + " → " + val + "%" + (Get(d, "note") != null ? ": " + Get(d, "note") : ""));
                        Out("OK " + id + " " + val + "%");
                        return 0;
                    }

                case "note":
                    {
                        string id = Get(d, "note");
                        string text = Get(d, "_positional");
                        if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --note t3 \"текст\""); return 2; }
                        bool ok = Store.Mutate(b =>
                        {
                            var t = Find(b, id);
                            if (t == null) return false;
                            t.Note = text.Replace("\u0001", " ");
                            t.Updated = TaskItem.Now();
                            return true;
                        });
                        if (!ok) { Out("ERR нет задачи " + id); return 3; }
                        Store.Log("note", id + ": " + text);
                        Out("OK " + id);
                        return 0;
                    }

                case "log":
                    {
                        string text = Get(d, "log");
                        if (string.IsNullOrWhiteSpace(text)) text = Get(d, "_positional").Replace("\u0001", " ");
                        Store.Log("log", text);
                        Out("OK");
                        return 0;
                    }

                case "mode":
                    {
                        string m = Get(d, "mode");
                        if (m == null) m = Get(d, "_positional");
                        m = (m ?? "").Trim().ToLowerInvariant();
                        if (m != "night" && m != "auto" && m != "idle" && m != "task")
                        {
                            Out("ERR режим: night | auto | task | idle");
                            return 2;
                        }
                        Store.Mutate(b => { b.Mode = m; return 0; });
                        Store.Log("mode", Lang.T("log.mode") + " " + m);
                        Out("OK режим " + m);
                        return 0;
                    }

                case "lang":
                    {
                        string v = Get(d, "lang");
                        if (v == null) v = Get(d, "_positional");
                        v = (v ?? "").Replace("\u0001", " ").Trim().ToLowerInvariant();
                        if (v == "ru" || v == "en")
                        {
                            Lang.Set(v);
                            Out("OK lang " + Lang.Current);
                            return 0;
                        }
                        if (v.Length > 0) { Out("ERR язык: ru | en"); return 2; }
                        Out("lang: " + Lang.Current);
                        return 0;
                    }

                case "list":
                    {
                        Board b = Store.Load();
                        if (d.ContainsKey("json"))
                        {
                            var ser = new JavaScriptSerializer();
                            ser.MaxJsonLength = 8 * 1024 * 1024;
                            Out(ser.Serialize(new { mode = b.Mode, tasks = b.Tasks }));
                            return 0;
                        }
                        Out("режим: " + b.Mode + "   задач: " + b.Tasks.Count);
                        foreach (var t in b.Tasks)
                        {
                            string p = t.Progress > 0 ? " " + t.Progress + "%" : "";
                            Out(string.Format("{0,-5} {1,-8} {2,-6}{3}  {4}", t.Id, t.Status, t.Mode, p, t.Title));
                            if (!string.IsNullOrWhiteSpace(t.Note)) Out("        └ " + t.Note);
                        }
                        return 0;
                    }

                case "clear":
                    {
                        string scope = Get(d, "clear");
                        int n = Store.Mutate(b =>
                        {
                            int before = b.Tasks.Count;
                            if (scope == "all") b.Tasks.Clear();
                            else b.Tasks.RemoveAll(t => t.Status == "done" || t.Status == "dropped");
                            return before - b.Tasks.Count;
                        });
                        Store.Log("clear", Lang.T("log.cleared") + " " + n);
                        Out("OK удалено " + n);
                        return 0;
                    }

                case "path":
                    Out(Store.TasksFile);
                    return 0;

                case "open":
                    Out(Ui.IsRunning() ? "OK окно открыто" : "OK окно запущено");
                    Ui.SignalShow();
                    return 0;

                case "autostart":
                    {
                        string v = (Get(d, "autostart") ?? Get(d, "_positional")).Replace("\u0001", " ").Trim().ToLowerInvariant();
                        if (v == "on" || v == "вкл")
                        {
                            Out(Ui.SetAutostart(true) ? "OK автозапуск вкл" : "ERR не смог записать реестр");
                        }
                        else if (v == "off" || v == "выкл")
                        {
                            Out(Ui.SetAutostart(false) ? "OK автозапуск выкл" : "ERR не смог записать реестр");
                        }
                        else
                        {
                            Out("автозапуск: " + Ui.AutostartState());
                            Out("окно: " + (Ui.IsRunning() ? "запущено" : "не запущено"));
                        }
                        return 0;
                    }

                default:
                    Out(HelpText());
                    return 1;
            }
        }

        static int SetStatus(Dictionary<string, string> d, string key, string status, int progress)
        {
            string id = Get(d, key);
            if (string.IsNullOrEmpty(id)) id = Get(d, "_positional").Replace("\u0001", " ").Trim();
            if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --" + key + " t3"); return 2; }
            string note = Get(d, "note");
            int to = IntOf(Get(d, "to"), progress);
            bool ok = Store.Mutate(b =>
            {
                var t = Find(b, id);
                if (t == null) return false;
                t.Status = status;
                if (to >= 0) t.Progress = to > 100 ? 100 : to;
                if (!string.IsNullOrEmpty(note)) t.Note = note;
                t.Updated = TaskItem.Now();
                return true;
            });
            if (!ok) { Out("ERR нет задачи " + id); return 3; }
            Store.Log(status, id + (note != null ? ": " + note : ""));
            Out("OK " + id + " → " + status);
            return 0;
        }

        static int IntOf(string s, int fallback)
        {
            int v;
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        static void Out(string s)
        {
            Console.WriteLine(s);
        }

        static string HelpText()
        {
            return string.Join("\n", new[]
            {
                "Nexus Board — доска задач ИИ",
                "",
                "GUI:            NexusBoard.exe            (двойной клик)",
                "Окно поднимается само при первой же задаче из CLI.",
                "",
                "Поставить задачу:",
                "  --add \"текст\" [--mode night|auto|task] [--note \"...\"] [--prio 1-5]",
                "  --start t3        взять в работу",
                "  --progress t3 --to 40 [--note \"...\"]",
                "  --note t3 \"текст\"",
                "  --done t3         закрыть",
                "  --block t3 --note \"почему\"",
                "  --todo t3         вернуть в план",
                "  --drop t3         снять",
                "  --log \"запись в ленту\"",
                "  --mode night|auto|task|idle",
                "  --list [--json]   список задач",
                "  --clear [--clear done|all]",
                "  --open            показать/запустить окно",
                "  --autostart on|off|status",
                "  --lang ru|en      язык интерфейса",
                "  --chat-list [--json]   переписка с владельцем",
                "  --chat-say \"текст\"     ответить владельцу в чат",
                "  --path            путь к tasks.json",
                "  --help",
                "",
                "Данные: " + Store.TasksFile,
                "Язык:   " + Lang.UiFile,
                "",
                "--- English ---",
                "Nexus Board — AI task board",
                "",
                "GUI:            NexusBoard.exe            (double click)",
                "The window opens itself on the first CLI task.",
                "",
                "Create and update tasks:",
                "  --add \"text\" [--mode night|auto|task] [--note \"...\"] [--prio 1-5]",
                "  --start t3        take into work",
                "  --progress t3 --to 40 [--note \"...\"]",
                "  --note t3 \"text\"",
                "  --done t3         close",
                "  --block t3 --note \"why\"",
                "  --todo t3         back to plan",
                "  --drop t3         drop",
                "  --log \"feed entry\"",
                "  --mode night|auto|task|idle",
                "  --list [--json]   task list",
                "  --clear [--clear done|all]",
                "  --open            show/start the window",
                "  --autostart on|off|status",
                "  --lang ru|en      interface language",
                "  --chat-list [--json]   chat history",
                "  --chat-say \"text\"      reply to the owner in chat",
                "  --path            path to tasks.json",
                "  --help",
                "",
                "Data: " + Store.TasksFile,
                "Language: " + Lang.UiFile
            });
        }
    }

}
