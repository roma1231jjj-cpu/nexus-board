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
            PushBoard(b);
        }

        static string ApiBase()
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:8788/api/health");
                req.Timeout = 600;
                req.ReadWriteTimeout = 600;
                using (var r = req.GetResponse()) { }
                return "http://127.0.0.1:8788";
            }
            catch (Exception) { }
            var st = NexusBoard.Settings.Load();
            string b = string.IsNullOrEmpty(st.Base) ? "https://board.nexus-your-heper.ru" : st.Base;
            return b.TrimEnd('/');
        }

        public static string BoardId()
        {
            try
            {
                var st = NexusBoard.Settings.Load();
                string id = (st.Board ?? "").Trim().ToLowerInvariant();
                return System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-f0-9]{16}$") ? id : "";
            }
            catch (Exception) { return ""; }
        }

        public static string HttpJsonTimeout(string url, string postJson, int ms)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
            }
            catch { }
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Timeout = ms;
            req.ReadWriteTimeout = ms;
            req.UserAgent = "NexusBoardPC/cli";
            if (postJson != null)
            {
                req.Method = "POST";
                req.ContentType = "application/json";
                byte[] raw = Encoding.UTF8.GetBytes(postJson);
                req.ContentLength = raw.Length;
                using (var st = req.GetRequestStream()) st.Write(raw, 0, raw.Length);
            }
            using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static string BoardUrl(string pathAndQuery)
        {
            string id = BoardId();
            if (string.IsNullOrEmpty(id)) return null;
            string sep = pathAndQuery.Contains("?") ? "&" : "?";
            return ApiBase() + pathAndQuery + sep + "b=" + id;
        }

        static void PushBoard(Board b)
        {
            try
            {
                string url = Store.BoardUrl("/api/b/sync");
                if (url == null) return;
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                string payload = ser.Serialize(new Dictionary<string, object>
                {
                    { "tasks", b.Tasks },
                    { "mode", b.Mode }
                });
                Store.HttpJsonTimeout(url, payload, 6000);
            }
            catch (Exception) { }
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

        public static bool NoUi
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("NEXUSBOARD_NO_UI");
                if (string.IsNullOrEmpty(v)) return false;
                v = v.Trim().ToLowerInvariant();
                return v == "1" || v == "true" || v == "yes" || v == "on";
            }
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
        static string ArgText(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string t = v.Trim();
            if (t.StartsWith("@"))
            {
                string f = t.Substring(1);
                try { return File.ReadAllText(f, Encoding.UTF8); }
                catch (Exception e) { throw new IOException("не прочитать " + f + ": " + e.Message); }
            }
            return v;
        }

        static Dictionary<string, object> BodyOf(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new Dictionary<string, object>();
            string t = ArgText(raw);
            try
            {
                var o = new JavaScriptSerializer().DeserializeObject(t) as Dictionary<string, object>;
                if (o != null) return o;
            }
            catch (Exception) { }
            return new Dictionary<string, object> { { "text", t } };
        }

        static Dictionary<string, object> SourceOf(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string path = raw.Trim();
            int lines = 40;
            int i = path.LastIndexOf(':');
            if (i > 2)
            {
                int n;
                if (int.TryParse(path.Substring(i + 1), out n) && n > 0)
                {
                    lines = Math.Min(400, n);
                    path = path.Substring(0, i);
                }
            }
            return new Dictionary<string, object> { { "path", path }, { "lines", lines } };
        }

        static int ViewCmd(string cmd, Dictionary<string, string> d)
        {
            string url = Store.BoardUrl("/api/b/view/set");
            if (url == null) { Out("ERR нет ключа доски: открой приложение → Ключ"); return 2; }
            string id = cmd == "view-del" ? Get(d, "view-del")
                : cmd == "view-set" ? (Get(d, "view-set") ?? Get(d, "id"))
                : Get(d, "id");
            string addTitle = cmd == "view-add" ? Get(d, "view-add") : null;
            if (cmd == "view-del")
            {
                if (string.IsNullOrWhiteSpace(id)) { Out("ERR нужен id: --view-del <id>"); return 2; }
                try
                {
                    string r = Store.HttpJsonTimeout(Store.BoardUrl("/api/b/view/del"), new JavaScriptSerializer().Serialize(
                        new Dictionary<string, object> { { "id", id.Trim() } }), 8000);
                    var o = new JavaScriptSerializer().DeserializeObject(r) as Dictionary<string, object>;
                    bool ok = o != null && o.ContainsKey("ok") && Convert.ToBoolean(o["ok"]);
                    Out(ok ? "OK удалён " + id : "ERR " + r);
                    return ok ? 0 : 1;
                }
                catch (Exception e) { Out("ERR " + e.Message); return 1; }
            }
            var payload = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(id)) payload["id"] = id.Trim();
            string kind = Get(d, "kind");
            if (!string.IsNullOrWhiteSpace(kind)) payload["kind"] = kind.Trim();
            string title = Get(d, "title") ?? addTitle;
            if (!string.IsNullOrWhiteSpace(title)) payload["title"] = title.Trim();
            string order = Get(d, "order");
            if (!string.IsNullOrWhiteSpace(order)) payload["order"] = IntOf(order, 0);
            string size = Get(d, "size");
            if (!string.IsNullOrWhiteSpace(size)) payload["size"] = size.Trim();
            string bodyRaw = Get(d, "body");
            if (bodyRaw != null) payload["body"] = BodyOf(bodyRaw);
            var src = SourceOf(Get(d, "source"));
            if (src != null) payload["source"] = src;
            if (!payload.ContainsKey("kind") && !payload.ContainsKey("id")) payload["kind"] = "text";
            try
            {
                string r = Store.HttpJsonTimeout(url, new JavaScriptSerializer().Serialize(payload), 10000);
                var o = new JavaScriptSerializer().DeserializeObject(r) as Dictionary<string, object>;
                bool ok = o != null && o.ContainsKey("ok") && Convert.ToBoolean(o["ok"]);
                if (!ok) { Out("ERR " + r); return 1; }
                var blk = o["block"] as Dictionary<string, object>;
                Out("OK " + (blk != null && blk.ContainsKey("id") ? Convert.ToString(blk["id"]) : ""));
                return 0;
            }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
        }

        static int ViewListCmd(bool json)
        {
            string url = Store.BoardUrl("/api/b/views");
            if (url == null) { Out("ERR нет ключа доски: открой приложение → Ключ"); return 2; }
            string raw;
            try { raw = Store.HttpJsonTimeout(url, null, 10000); }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
            if (json) { Out(raw); return 0; }
            var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
            if (o == null || !o.ContainsKey("blocks")) { Out("ERR " + raw); return 1; }
            var list = o["blocks"] as object[];
            if (list == null || list.Length == 0) { Out("разделов нет"); return 0; }
            foreach (var it in list)
            {
                var b = it as Dictionary<string, object>;
                if (b == null) continue;
                Out(Convert.ToString(b["id"]) + "  " + Convert.ToString(b["kind"]) + "  " + Convert.ToString(b["title"]));
            }
            return 0;
        }

        static int CodePushCmd(Dictionary<string, string> d)
        {
            string id = Get(d, "code-push");
            string file = Get(d, "file");
            string content = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(file)) content = File.ReadAllText(file.Trim(), Encoding.UTF8);
                else content = Console.In.ReadToEnd();
            }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
            if (string.IsNullOrWhiteSpace(content)) { Out("ERR пустой текст: --file <путь> или stdin"); return 2; }
            string title = Get(d, "title") ?? "код";
            string lang = Get(d, "lang") ?? "diff";
            string filename = Get(d, "name") ?? (string.IsNullOrWhiteSpace(file) ? "" : Path.GetFileName(file.Trim()));
            if (string.IsNullOrWhiteSpace(id))
            {
                string url = Store.BoardUrl("/api/b/view/set");
                if (url == null) { Out("ERR нет ключа доски"); return 2; }
                var payload = new Dictionary<string, object>
                {
                    { "kind", "code" },
                    { "title", title },
                    { "size", "wide" },
                    { "body", new Dictionary<string, object> { { "lang", lang }, { "filename", filename }, { "content", content } } }
                };
                try
                {
                    string r = Store.HttpJsonTimeout(url, new JavaScriptSerializer().Serialize(payload), 15000);
                    var o = new JavaScriptSerializer().DeserializeObject(r) as Dictionary<string, object>;
                    var blk = o != null && o.ContainsKey("block") ? o["block"] as Dictionary<string, object> : null;
                    Out("OK " + (blk != null ? Convert.ToString(blk["id"]) : ""));
                    return 0;
                }
                catch (Exception e) { Out("ERR " + e.Message); return 1; }
            }
            string saveUrl = Store.BoardUrl("/api/b/code/save");
            var body = new Dictionary<string, object> { { "id", id.Trim() }, { "content", content }, { "title", title } };
            string ifv = Get(d, "if-version");
            if (!string.IsNullOrWhiteSpace(ifv)) body["ifVersion"] = IntOf(ifv, 0);
            try
            {
                string r = Store.HttpJsonTimeout(saveUrl, new JavaScriptSerializer().Serialize(body), 15000);
                var o = new JavaScriptSerializer().DeserializeObject(r) as Dictionary<string, object>;
                bool ok = o != null && o.ContainsKey("ok") && Convert.ToBoolean(o["ok"]);
                if (!ok) { Out("ERR " + r); return 1; }
                Out("OK v" + Convert.ToString(o["version"]) + " sha=" + Convert.ToString(o["sha"]));
                return 0;
            }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
        }

        static int CodePullCmd(Dictionary<string, string> d)
        {
            string id = Get(d, "code-pull");
            if (string.IsNullOrWhiteSpace(id)) { Out("ERR нужен id: --code-pull <id> [--out файл]"); return 2; }
            string url = Store.BoardUrl("/api/b/code?id=" + id.Trim());
            if (url == null) { Out("ERR нет ключа доски"); return 2; }
            try
            {
                string raw = Store.HttpJsonTimeout(url, null, 15000);
                var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                var code = o != null && o.ContainsKey("code") ? o["code"] as Dictionary<string, object> : null;
                if (code == null) { Out("ERR " + raw); return 1; }
                string content = Convert.ToString(code["content"]);
                string outFile = Get(d, "out");
                if (!string.IsNullOrWhiteSpace(outFile))
                {
                    File.WriteAllText(outFile.Trim(), content, new UTF8Encoding(false));
                    Out("OK " + outFile.Trim() + " v" + Convert.ToString(code["version"]));
                }
                else
                {
                    Out(content);
                }
                return 0;
            }
            catch (Exception e) { Out("ERR " + e.Message); return 1; }
        }

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

        static int ChatListCmd(bool json, string board)
        {
            var s = ChatSettings();
            string target = string.IsNullOrEmpty(board) ? s.Board : board.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(target)) { Out("ERR нет ключа доски: открой приложение → Ключ"); return 2; }
            string raw;
            try { raw = HttpJson(ChatBase(s) + "/api/c/list?board=" + target + "&since=0&limit=50", null); }
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

        static int ChatSayCmd(string text, string board)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { Out("ERR пустой текст"); return 2; }
            var s = ChatSettings();
            string target = string.IsNullOrEmpty(board) ? s.Board : board.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(target)) { Out("ERR нет ключа доски"); return 2; }
            var payload = new Dictionary<string, object> { { "board", target }, { "from", "ai" }, { "text", text } };
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

        static bool NoUiFor(string[] args)
        {
            if (Ui.NoUi) return true;
            for (int i = 0; i < args.Length; i++) if (args[i] == "--no-ui") return true;
            return false;
        }

        public static int Run(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            var d = Parse(args);
            string cmd = "";
            for (int i = 0; i < args.Length; i++) { if (args[i].StartsWith("--")) { cmd = args[i].Substring(2).ToLowerInvariant(); break; } }

            if (cmd == "open" && !NoUiFor(args))
            {
                try { Ui.Ensure(); } catch { }
                try { Ui.SignalShow(); } catch { }
            }

            switch (cmd)
            {
                case "chat-list":
                case "chat":
                    return ChatListCmd(d.ContainsKey("json"), Get(d, "board"));

                case "chat-say":
                    {
                        string text = Get(d, "chat-say");
                        if (string.IsNullOrWhiteSpace(text)) text = Get(d, "_positional").Replace("", " ");
                        return ChatSayCmd(text, Get(d, "board"));
                    }

                case "view-list":
                    return ViewListCmd(d.ContainsKey("json"));

                case "view-add":
                case "view-set":
                case "view-del":
                    return ViewCmd(cmd, d);

                case "code-push":
                    return CodePushCmd(d);

                case "code-pull":
                    return CodePullCmd(d);

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
                "Окно открывает только --open (или двойной клик).",
                "",
                "Разделы, которые ИИ рисует сам:",
                "  --view-add \"Обучение\" --kind progress --body \"{\\\"value\\\":82}\"",
                "  --view-set <id> --body \"@file.json\"   [--title ...] [--order 10] [--size md|wide]",
                "  --view-del <id> / --view-list [--json]",
                "  --code-push [<id>] --title \"Патч\" --file E:\\patch.diff [--lang diff]",
                "  --code-pull <id> [--out E:\\patch.diff]",
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
                "  --board <16 hex>  к какой доске (по умолчанию — своя)",
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
