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

        public const string BusyText = "tasks.json занят другим процессом, попробуй ещё раз";

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
            return LoadFrom(TasksFile);
        }

        public static Board LoadFrom(string file)
        {
            if (!File.Exists(file)) return new Board();

            string json = null;
            Exception busy = null;
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    json = File.ReadAllText(file, Encoding.UTF8);
                    busy = null;
                    break;
                }
                catch (IOException ex) { busy = ex; }
                catch (UnauthorizedAccessException ex) { busy = ex; }
                Thread.Sleep(40);
            }
            if (busy != null) throw new IOException(BusyText, busy);
            if (json == null) throw new IOException(BusyText);
            if (string.IsNullOrWhiteSpace(json)) return new Board();

            Board b = null;
            Exception broken = null;
            try
            {
                b = new JavaScriptSerializer().Deserialize<Board>(json);
                if (b == null) broken = new InvalidDataException("пустой объект");
            }
            catch (Exception ex) { broken = ex; }
            if (broken != null)
            {
                Quarantine(file, broken);
                return new Board();
            }
            if (b.Tasks == null) b.Tasks = new List<TaskItem>();
            return b;
        }

        static void Quarantine(string file, Exception ex)
        {
            string bad = file + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                File.Copy(file, bad, true);
            }
            catch (Exception copy)
            {
                throw new IOException("tasks.json повреждён (" + ex.Message +
                    "), копию сделать не удалось: " + copy.Message + "; файл не изменён", copy);
            }
            try
            {
                if (string.Equals(file, TasksFile, StringComparison.OrdinalIgnoreCase))
                    Console.Error.WriteLine("tasks.json не прочитан (" + ex.Message + "), копия: " + bad);
            }
            catch (Exception) { }
        }

        public static void Save(Board b)
        {
            WriteFile(b, TasksFile);
            PushBoard(b);
        }

        public static void WriteFile(Board b, string file)
        {
            b.Updated = TaskItem.Now();
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;
            string json = ser.Serialize(b);
            string dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(file))
            {
                try { File.Replace(tmp, file, null); }
                catch (Exception) { File.Copy(tmp, file, true); try { File.Delete(tmp); } catch { } }
            }
            else
            {
                File.Move(tmp, file);
            }
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
            string url = ApiBase() + pathAndQuery + sep + "b=" + id;
            try
            {
                string k = (NexusBoard.Settings.Load().Key ?? "").Trim();
                if (k.Length > 0) url += "&k=" + Uri.EscapeDataString(k);
            }
            catch (Exception) { }
            return url;
        }

        public static bool Offline;

        static void PushBoard(Board b)
        {
            if (Offline) return;
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
                try
                {
                    Save(b);
                }
                catch (IOException ex) { throw new IOException(BusyText, ex); }
                catch (UnauthorizedAccessException ex) { throw new IOException(BusyText, ex); }
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

    public class RemoteError : Exception
    {
        public int Status { get; private set; }

        public RemoteError(string message, int status) : base(message)
        {
            Status = status;
        }
    }

    public static class Remote
    {
        public const int TimeoutMs = 15000;

        static bool _loaded;
        static string _base;
        static string _board;
        static string _key;

        static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;
            _base = "";
            _board = "";
            _key = "";
            try
            {
                NexusBoard.Settings s = NexusBoard.Settings.Load();
                if (s == null) return;
                string b = (s.Base ?? "").Trim().TrimEnd('/');
                string board = (s.Board ?? "").Trim().ToLowerInvariant();
                string key = (s.Key ?? "").Trim();
                if (b.Length == 0 || key.Length == 0) return;
                if (!System.Text.RegularExpressions.Regex.IsMatch(board, "^[a-f0-9]{16}$")) return;
                _base = b;
                _board = board;
                _key = key;
            }
            catch (Exception)
            {
                _base = "";
                _board = "";
                _key = "";
            }
        }

        public static bool Enabled
        {
            get { Ensure(); return _base.Length > 0; }
        }

        public static string BoardId
        {
            get { Ensure(); return _board; }
        }

        public static string BaseUrl
        {
            get { Ensure(); return _base; }
        }

        static string Url(string path)
        {
            Ensure();
            string sep = path.Contains("?") ? "&" : "?";
            return _base + path + sep + "b=" + _board + "&k=" + Uri.EscapeDataString(_key);
        }

        static string Ser(object o)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;
            return ser.Serialize(o);
        }

        static Dictionary<string, object> Parse(string raw)
        {
            try
            {
                var o = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                if (o != null) return o;
            }
            catch (Exception) { }
            throw new RemoteError("ERR сервер ответил не по контракту", 0);
        }

        static string ServerError(string body)
        {
            try
            {
                var o = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                if (o != null && o.ContainsKey("error")) return Convert.ToString(o["error"]);
            }
            catch (Exception) { }
            return "";
        }

        /// <summary>Один запрос к доске. notFound — текст для 404; null — общий.</summary>
        static string Call(string path, string postJson, string notFound)
        {
            if (!Enabled) throw new RemoteError("ERR доска не подключена: нет адреса, ключа или id доски в настройках", 0);
            string url = Url(path);
            System.Net.HttpWebRequest req;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            }
            catch (Exception)
            {
                throw new RemoteError("ERR нет связи с сервером", 0);
            }
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.UserAgent = "NexusBoardPC/cli";
            try
            {
                if (postJson != null)
                {
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    byte[] raw = Encoding.UTF8.GetBytes(postJson);
                    req.ContentLength = raw.Length;
                    using (var st = req.GetRequestStream()) st.Write(raw, 0, raw.Length);
                }
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (RemoteError) { throw; }
            catch (System.Net.WebException ex)
            {
                var resp = ex.Response as System.Net.HttpWebResponse;
                if (resp == null) throw new RemoteError("ERR нет связи с сервером", 0);
                int code = (int)resp.StatusCode;
                string body = "";
                try
                {
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                }
                catch (Exception) { }
                string err = ServerError(body);
                if (code == 401) throw new RemoteError("ERR неверный ключ доски", 401);
                if (code == 404 && err.IndexOf("board not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new RemoteError("ERR доски " + BoardId + " нет на сервере", 404);
                if (code == 404) throw new RemoteError(string.IsNullOrEmpty(notFound) ? "ERR сервер не нашёл данные" : notFound, 404);
                if (code == 429) throw new RemoteError("ERR сервер занят (лимит запросов), повтори через минуту — повторный --sync дублей не создаёт", 429);
                if (err.Length > 0) throw new RemoteError("ERR " + err, code);
                throw new RemoteError("ERR сервер ответил " + code, code);
            }
            catch (Exception)
            {
                throw new RemoteError("ERR нет связи с сервером", 0);
            }
        }

        public static string Text(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return "";
            return Convert.ToString(d[key]);
        }

        public static int Num(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return 0;
            int v;
            if (int.TryParse(Convert.ToString(d[key]), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        public static List<Dictionary<string, object>> ArrayOf(Dictionary<string, object> o, string key)
        {
            var list = new List<Dictionary<string, object>>();
            if (o == null || !o.ContainsKey(key)) return list;
            var arr = o[key] as object[];
            if (arr == null) return list;
            foreach (var it in arr)
            {
                var d = it as Dictionary<string, object>;
                if (d != null) list.Add(d);
            }
            return list;
        }

        public static Dictionary<string, object> Tasks()
        {
            var o = Parse(Call("/api/b/tasks", null, "ERR доски " + BoardId + " нет на сервере"));
            if (!o.ContainsKey("tasks")) throw new RemoteError("ERR сервер ответил без задач", 0);
            return o;
        }

        public static List<Dictionary<string, object>> Items()
        {
            return ArrayOf(Tasks(), "tasks");
        }

        public static List<TaskItem> ToItems(Dictionary<string, object> board)
        {
            var list = new List<TaskItem>();
            foreach (var d in ArrayOf(board, "tasks"))
            {
                var t = new TaskItem();
                t.Id = Text(d, "Id");
                t.Title = Text(d, "Title");
                t.Note = Text(d, "Note");
                t.Mode = Text(d, "Mode");
                t.Status = Text(d, "Status");
                t.Progress = Num(d, "Progress");
                t.Source = Text(d, "Source");
                t.Created = Text(d, "Created");
                t.Updated = Text(d, "Updated");
                list.Add(t);
            }
            return list;
        }

        public static Dictionary<string, object> Add(string title, string note, string mode, int prio, string section)
        {
            return Add(title, note, mode, prio, section, "todo", 0);
        }

        public static Dictionary<string, object> Add(string title, string note, string mode, int prio, string section, string status, int progress)
        {
            string m = string.IsNullOrEmpty(mode) ? "task" : mode.Trim();
            if (m != "auto" && m != "task" && m != "night") m = "task";
            string st = string.IsNullOrEmpty(status) ? "todo" : status.Trim();
            if (st != "todo" && st != "doing" && st != "blocked" && st != "done") st = "todo";
            int pr = progress < 0 ? 0 : (progress > 100 ? 100 : progress);
            var body = new Dictionary<string, object>
            {
                { "title", title }, { "note", note ?? "" }, { "mode", m },
                { "status", st }, { "progress", pr }, { "section", section ?? "" }
            };
            var o = Parse(Call("/api/b/add", Ser(body), "ERR доски " + BoardId + " нет на сервере"));
            var t = o.ContainsKey("task") ? o["task"] as Dictionary<string, object> : null;
            if (t == null) throw new RemoteError("ERR сервер не вернул задачу", 0);
            return t;
        }

        public static Dictionary<string, object> Update(string id, Dictionary<string, object> fields, string notFound)
        {
            var body = new Dictionary<string, object>();
            if (fields != null) foreach (var kv in fields) body[kv.Key] = kv.Value;
            body["id"] = id;
            var o = Parse(Call("/api/b/update", Ser(body),
                notFound ?? ("ERR задачи " + id + " нет на доске")));
            return o.ContainsKey("task") ? o["task"] as Dictionary<string, object> : null;
        }

        public static Dictionary<string, object> Update(string id, Dictionary<string, object> fields)
        {
            return Update(id, fields, null);
        }

        public static void Delete(string id)
        {
            Call("/api/b/delete", Ser(new Dictionary<string, object> { { "id", id } }),
                "ERR задачи " + id + " нет на доске");
        }

        public static void Clear()
        {
            Call("/api/b/clear", "{}", null);
        }

        public static List<Dictionary<string, object>> Sections()
        {
            return ArrayOf(Parse(Call("/api/b/section", null, "ERR доски " + BoardId + " нет на сервере")), "sections");
        }

        public static Dictionary<string, object> SectionAdd(string name)
        {
            var o = Parse(Call("/api/b/section/add", Ser(new Dictionary<string, object> { { "name", name } }), null));
            return o.ContainsKey("section") ? o["section"] as Dictionary<string, object> : null;
        }

        public static Dictionary<string, object> SectionSet(string id, string name)
        {
            var o = Parse(Call("/api/b/section/set", Ser(new Dictionary<string, object>
            {
                { "id", id }, { "name", name }
            }), "ERR раздела " + id + " нет на доске"));
            return o.ContainsKey("section") ? o["section"] as Dictionary<string, object> : null;
        }

        public static void SectionDel(string id)
        {
            Call("/api/b/section/del", Ser(new Dictionary<string, object> { { "id", id } }),
                "ERR раздела " + id + " нет на доске");
        }

        /// <summary>Первая задача в плане — для --start без id.</summary>
        public static string NextId()
        {
            var items = Items();
            foreach (var d in items) if (Text(d, "Status") == "todo") return Text(d, "Id");
            foreach (var d in items)
            {
                string st = Text(d, "Status");
                if (st != "done" && st != "dropped") return Text(d, "Id");
            }
            return "";
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
            Mutex m;
            try { m = new Mutex(true, MutexName, out created); }
            catch (Exception) { return null; }
            if (created) return m;
            m.Dispose();
            return null;
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

        static readonly string[] CommandVerbs =
        {
            "add", "plan", "start", "doing", "done", "block", "blocked", "todo", "drop", "dropped",
            "progress", "note", "log", "mode", "clear", "open", "autostart", "path",
            "help", "h", "?", "list", "selftest", "sync",
            "chat-list", "chat", "chat-say", "lang",
            "view-add", "view-set", "view-del", "view-list", "code-push", "code-pull"
        };

        static readonly HashSet<string> ValueFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "plan", "start", "doing", "done", "block", "blocked", "todo", "drop", "dropped",
            "progress", "note", "log", "mode", "autostart", "lang", "board", "chat-say",
            "view-add", "view-set", "view-del", "code-push", "code-pull",
            "to", "id", "kind", "title", "order", "size", "body", "source", "file", "name",
            "out", "if-version", "prio", "section"
        };

        static readonly HashSet<string> OptionalValueFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "clear"
        };

        static bool IsVerb(string key)
        {
            return Array.IndexOf(CommandVerbs, key) >= 0;
        }

        public static bool ShouldRunAsCli(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            for (int i = 0; i < args.Length; i++)
            {
                string k = KeyOf(args[i]);
                if (k == null || k.Length == 0) return false;
                if (k == "board" || k == "json" || k == "no-ui" || k == "local" || k == "offline")
                {
                    if (k == "board" && i + 1 < args.Length && KeyOf(args[i + 1]) == null) i++;
                    continue;
                }
                return IsVerb(k);
            }
            return false;
        }

        static string KeyOf(string a)
        {
            if (string.IsNullOrEmpty(a)) return null;
            if (a.StartsWith("--")) return a.Substring(2).Trim().ToLowerInvariant();
            if (a.Length >= 2 && a[0] == '-' && !char.IsDigit(a[1])) return a.Substring(1).Trim().ToLowerInvariant();
            return null;
        }

        static Dictionary<string, string> Parse(string[] args, out string cmd)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            cmd = "";
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string key = KeyOf(a);
                if (key == null)
                {
                    order.Add(a);
                    continue;
                }
                if (key.Length == 0)
                {
                    order.Add(a);
                    continue;
                }
                if (ValueFlags.Contains(key))
                {
                    string val = "";
                    if (i + 1 < args.Length) val = args[++i];
                    d[key] = val;
                }
                else if (OptionalValueFlags.Contains(key))
                {
                    string val = "";
                    if (i + 1 < args.Length && KeyOf(args[i + 1]) == null) val = args[++i];
                    d[key] = val;
                }
                else d[key] = "";

                if (cmd.Length == 0 && IsVerb(key)) cmd = key;
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
            string target, err;
            if (!ChatTarget(s, board, out target, out err)) { Out(err); return 2; }
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

        static bool ChatTarget(NexusBoard.Settings s, string board, out string target, out string err)
        {
            err = null;
            string given = (board ?? "").Trim().ToLowerInvariant();
            if (given.Length > 0)
            {
                if (!IsBoardId(given))
                {
                    target = null;
                    err = "ERR --board: нужно 16 hex-символов 0-9a-f, например 0123456789abcdef";
                    return false;
                }
                target = given;
                return true;
            }
            string own = (s.Board ?? "").Trim().ToLowerInvariant();
            if (own.Length == 0)
            {
                target = null;
                err = "ERR нет ключа доски: открой приложение → Ключ";
                return false;
            }
            if (!IsBoardId(own))
            {
                target = null;
                err = "ERR ключ доски в настройках неверный: нужно 16 hex-символов 0-9a-f";
                return false;
            }
            target = own;
            return true;
        }

        static int ChatSayCmd(string text, string board)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { Out("ERR пустой текст"); return 2; }
            var s = ChatSettings();
            string target, err;
            if (!ChatTarget(s, board, out target, out err)) { Out(err); return 2; }
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

        static bool NoUiFor(Dictionary<string, string> d)
        {
            if (Ui.NoUi) return true;
            return d.ContainsKey("no-ui");
        }

        static bool IsBoardId(string id)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^[a-f0-9]{16}$");
        }

        public static int Run(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            try { Lang.Load(); } catch { }
            try
            {
                return RunCore(args);
            }
            catch (IOException ex) { Out("ERR " + ex.Message); return 1; }
            catch (UnauthorizedAccessException ex) { Out("ERR " + ex.Message); return 1; }
        }

        static int RunCore(string[] args)
        {
            string cmd;
            var d = Parse(args, out cmd);

            bool forceLocal = d.ContainsKey("local") || d.ContainsKey("offline");
            bool remote = Remote.Enabled && !forceLocal;
            Store.Offline = forceLocal || Remote.Enabled;

            if (cmd == "open" && !NoUiFor(d))
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

                case "selftest":
                    return SelfTestCmd();

                case "sync":
                    return SyncCmd(remote);

                case "add":
                case "plan":
                    {
                        string title = Get(d, "add") ?? Get(d, "plan");
                        if (string.IsNullOrWhiteSpace(title)) title = Get(d, "_positional").Replace("\u0001", " ");
                        if (string.IsNullOrWhiteSpace(title)) { Out("ERR пустой заголовок"); return 2; }
                        string mode = (Get(d, "mode") ?? "task").Trim().ToLowerInvariant();
                        if (mode != "night" && mode != "auto" && mode != "task" && mode != "idle")
                        {
                            Out("ERR режим: night | auto | task | idle");
                            return 2;
                        }
                        string note = Get(d, "note") ?? "";
                        int prio = IntOf(Get(d, "prio"), 3);
                        if (remote)
                        {
                            try
                            {
                                var rt = Remote.Add(title.Trim(), note, mode, prio, Get(d, "section") ?? "");
                                Out("OK " + Remote.Text(rt, "Id"));
                                return 0;
                            }
                            catch (RemoteError e) { Out(e.Message); return 1; }
                        }
                        var item = Store.Mutate(b =>
                        {
                            var t = new TaskItem();
                            t.Id = "t" + b.NextId;
                            b.NextId++;
                            t.Title = title.Trim();
                            t.Mode = mode;
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
                    return SetStatus(d, cmd, "doing", 5, remote, true);

                case "done":
                    return SetStatus(d, cmd, "done", 100, remote, false);

                case "block":
                case "blocked":
                    return SetStatus(d, cmd, "blocked", -1, remote, false);

                case "drop":
                case "dropped":
                    return SetStatus(d, cmd, "dropped", -1, remote, false);

                case "todo":
                    return SetStatus(d, cmd, "todo", 0, remote, false);

                case "progress":
                    {
                        string id = Get(d, "progress");
                        if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --progress t3 --to 40"); return 2; }
                        string toRaw = Get(d, "to");
                        if (toRaw == null)
                        {
                            Out("ERR укажи --to 0..100: --progress " + id + " --to 40");
                            return 1;
                        }
                        int raw;
                        if (!int.TryParse(toRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out raw))
                        {
                            Out("ERR --to: нужно число 0..100");
                            return 1;
                        }
                        int val = raw < 0 ? 0 : (raw > 100 ? 100 : raw);
                        string note = Get(d, "note");
                        if (remote)
                        {
                            try
                            {
                                var fields = new Dictionary<string, object> { { "progress", val } };
                                if (!string.IsNullOrEmpty(note)) fields["note"] = note;
                                var rt = Remote.Update(id, fields, "ERR задачи " + id + " нет на доске");
                                if (rt != null) val = Remote.Num(rt, "Progress");
                                Out("OK " + id + " " + val + "%");
                                return 0;
                            }
                            catch (RemoteError e) { Out(e.Message); return 1; }
                        }
                        bool ok = Store.Mutate(b =>
                        {
                            var t = Find(b, id);
                            if (t == null) return false;
                            t.Progress = val;
                            t.Updated = TaskItem.Now();
                            if (!string.IsNullOrEmpty(note)) t.Note = note;
                            return true;
                        });
                        if (!ok) { Out("ERR нет задачи " + id); return 3; }
                        Store.Log("progress", id + " → " + val + "%" + (note != null ? ": " + note : ""));
                        Out("OK " + id + " " + val + "%");
                        return 0;
                    }

                case "note":
                    {
                        string id = Get(d, "note");
                        string text = Get(d, "_positional");
                        if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --note t3 \"текст\""); return 2; }
                        string noteText = text.Replace("\u0001", " ");
                        if (remote)
                        {
                            try
                            {
                                Remote.Update(id, new Dictionary<string, object> { { "note", noteText } },
                                    "ERR задачи " + id + " нет на доске");
                                Out("OK " + id);
                                return 0;
                            }
                            catch (RemoteError e) { Out(e.Message); return 1; }
                        }
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
                        Out(remote ? "OK (запись в локальную ленту)" : "OK");
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
                        if (remote)
                        {
                            Out("OK режим " + m + " (только локальная доска: на сервере режима нет)");
                            return 0;
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
                        if (remote) return RemoteListCmd(d);
                        Board b = Store.Load();
                        if (d.ContainsKey("json"))
                        {
                            var ser = new JavaScriptSerializer();
                            ser.MaxJsonLength = 8 * 1024 * 1024;
                            Out(ser.Serialize(new { mode = b.Mode, tasks = b.Tasks }));
                            return 0;
                        }
                        Out("режим: " + b.Mode + "   задач: " + b.Tasks.Count);
                        PrintTasks(b.Tasks);
                        return 0;
                    }

                case "clear":
                    {
                        string scope = Get(d, "clear");
                        if (remote) return RemoteClearCmd(scope);
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
                    if (remote) Out("активно: сервер " + Remote.BaseUrl + " доска " + Remote.BoardId);
                    else if (Store.Offline) Out("активно: локальный файл (--local)");
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

        static void PrintTasks(List<TaskItem> tasks)
        {
            foreach (var t in tasks)
            {
                string p = t.Progress > 0 ? " " + t.Progress + "%" : "";
                Out(string.Format("{0,-5} {1,-8} {2,-6}{3}  {4}", t.Id, t.Status, t.Mode, p, t.Title));
                if (!string.IsNullOrWhiteSpace(t.Note)) Out("        └ " + t.Note);
            }
        }

        static int RemoteListCmd(Dictionary<string, string> d)
        {
            try
            {
                var board = Remote.Tasks();
                if (d.ContainsKey("json"))
                {
                    if (!board.ContainsKey("mode")) board["mode"] = "server";
                    var ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 8 * 1024 * 1024;
                    Out(ser.Serialize(board));
                    return 0;
                }
                var tasks = Remote.ToItems(board);
                Out("режим: сервер   задач: " + tasks.Count);
                PrintTasks(tasks);
                return 0;
            }
            catch (RemoteError e) { Out(e.Message); return 1; }
        }

        static int RemoteClearCmd(string scope)
        {
            try
            {
                var items = Remote.Items();
                if (scope == "all")
                {
                    int before = items.Count;
                    Remote.Clear();
                    Out("OK удалено " + before);
                    return 0;
                }
                int n = 0;
                foreach (var it in items)
                {
                    string st = Remote.Text(it, "Status");
                    if (st != "done" && st != "dropped") continue;
                    string id = Remote.Text(it, "Id");
                    if (string.IsNullOrEmpty(id)) continue;
                    try { Remote.Delete(id); n++; }
                    catch (RemoteError e) { if (e.Status != 404) throw; }
                }
                Out("OK удалено " + n);
                return 0;
            }
            catch (RemoteError e) { Out(e.Message); return 1; }
        }

        static string TaskKey(string title, string note)
        {
            return (title ?? "").Trim() + "\u0001" + (note ?? "").Trim();
        }

        static int SyncCmd(bool remote)
        {
            if (!remote)
            {
                if (Store.Offline)
                {
                    Out("OK локальная доска, синхронизировать не с чем (--local)");
                    return 0;
                }
                Store.Mutate(b => 0);
                Out("OK синхронизировано");
                return 0;
            }

            Board local;
            try { local = Store.Load(); }
            catch (IOException e) { Out("ERR " + e.Message); return 1; }

            List<Dictionary<string, object>> items;
            try { items = Remote.Items(); }
            catch (RemoteError e) { Out(e.Message); return 1; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in items) seen.Add(TaskKey(Remote.Text(it, "Title"), Remote.Text(it, "Note")));

            int added = 0;
            int total = local.Tasks.Count;
            foreach (var t in local.Tasks)
            {
                if (t.Status == "dropped") continue;
                string k = TaskKey(t.Title, t.Note);
                if (seen.Contains(k)) continue;
                try
                {
                    Remote.Add(t.Title, t.Note, t.Mode, t.Priority, "", t.Status, t.Progress);
                    seen.Add(k);
                    added++;
                }
                catch (RemoteError e) { Out(e.Message); return 1; }
            }
            Out("SYNCED " + added + "/" + total);
            return 0;
        }

        static int SetStatus(Dictionary<string, string> d, string key, string status, int progress, bool remote, bool autoPick)
        {
            string id = Get(d, key);
            if (string.IsNullOrEmpty(id)) id = Get(d, "_positional").Replace("\u0001", " ").Trim();
            string note = Get(d, "note");
            int to = IntOf(Get(d, "to"), progress);

            if (remote)
            {
                try
                {
                    if (string.IsNullOrEmpty(id) && autoPick) id = Remote.NextId();
                    if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --" + key + " t3"); return 2; }
                    var fields = new Dictionary<string, object> { { "status", status } };
                    if (to >= 0) fields["progress"] = to > 100 ? 100 : to;
                    if (!string.IsNullOrEmpty(note)) fields["note"] = note;
                    Remote.Update(id, fields, "ERR задачи " + id + " нет на доске");
                    Out("OK " + id + " → " + status);
                    return 0;
                }
                catch (RemoteError e) { Out(e.Message); return 1; }
            }

            if (string.IsNullOrEmpty(id) && autoPick)
            {
                Board cur = Store.Load();
                foreach (var t in cur.Tasks) if (t.Status == "todo") { id = t.Id; break; }
                if (string.IsNullOrEmpty(id))
                    foreach (var t in cur.Tasks) if (t.Status != "done" && t.Status != "dropped") { id = t.Id; break; }
            }
            if (string.IsNullOrEmpty(id)) { Out("ERR укажи id: --" + key + " t3"); return 2; }
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

        static int SelfTestCmd()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nexusboard-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Out("SELFTEST FAIL: временная папка — " + ex.Message);
                return 1;
            }
            try
            {
                string file = Path.Combine(dir, "tasks.json");
                var b = new Board();
                b.Mode = "night";
                b.NextId = 3;
                b.Tasks.Add(new TaskItem { Id = "t1", Title = "первая", Mode = "task", Status = "todo", Note = "заметка", Progress = 40, Priority = 2 });
                b.Tasks.Add(new TaskItem { Id = "t2", Title = "вторая", Mode = "night", Status = "doing" });
                Store.WriteFile(b, file);

                Board r = Store.LoadFrom(file);
                if (r.Tasks.Count != 2 || r.NextId != 3 || r.Mode != "night"
                    || r.Tasks[0].Title != "первая" || r.Tasks[0].Progress != 40 || r.Tasks[0].Note != "заметка"
                    || r.Tasks[1].Mode != "night" || r.Tasks[1].Status != "doing")
                {
                    Out("SELFTEST FAIL: чтение/запись tasks.json");
                    return 1;
                }

                string broken = Path.Combine(dir, "broken.json");
                File.WriteAllText(broken, "{\"tasks\": [ {\"id\": ", new UTF8Encoding(false));
                Board empty = Store.LoadFrom(broken);
                string[] bads = Directory.GetFiles(dir, "broken.json.bad-*");
                if (empty == null || empty.Tasks.Count != 0 || bads.Length != 1 || !File.Exists(broken))
                {
                    Out("SELFTEST FAIL: битый json не отложен в .bad");
                    return 1;
                }

                string locked = Path.Combine(dir, "locked.json");
                Store.WriteFile(b, locked);
                using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    bool busy = false;
                    try { Store.LoadFrom(locked); }
                    catch (IOException) { busy = true; }
                    if (!busy)
                    {
                        Out("SELFTEST FAIL: занятый файл отдан как пустая доска");
                        return 1;
                    }
                }

                Out("SELFTEST OK");
                return 0;
            }
            catch (Exception ex)
            {
                Out("SELFTEST FAIL: " + ex.Message);
                return 1;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        public static string HelpText()
        {
            return string.Join("\n", new[]
            {
                "Nexus Board — доска задач ИИ",
                "",
                "GUI:            NexusBoard.exe            (двойной клик)",
                "Окно поднимает только --open или двойной клик: CLI-команды окно не открывают.",
                "",
                "Разделы, которые ИИ рисует сам:",
                "  --view-add \"Обучение\" --kind progress --body \"{\\\"value\\\":82}\"",
                "  --view-set <id> --body \"@file.json\"   [--title ...] [--order 10] [--size md|wide]",
                "  --view-del <id> / --view-list [--json]",
                "  --code-push [<id>] --title \"Патч\" --file E:\\patch.diff [--lang diff]",
                "  --code-pull <id> [--out E:\\patch.diff]",
                "",
                "Поставить задачу:",
                "  --add \"текст\" [--mode night|auto|task|idle] [--note \"...\"] [--prio 1-5]",
                "  --start t3        взять в работу",
                "  --progress t3 --to 40 [--note \"...\"]   (--to обязателен, 0..100)",
                "  --note t3 \"текст\"",
                "  --done t3         закрыть",
                "  --block t3 --note \"почему\"",
                "  --todo t3         вернуть в план",
                "  --drop t3         снять",
                "  --log \"запись в ленту\"",
                "  --mode night|auto|task|idle",
                "  --list [--json]   список задач",
                "  --clear [done|all]  убрать закрытые или все задачи",
                "  --open            показать/запустить окно",
                "  --autostart on|off|status",
                "  --lang ru|en      язык интерфейса",
                "  --chat-list [--json]   переписка с владельцем",
                "  --chat-say \"текст\"     ответить владельцу в чат",
                "  --board <16 hex>  к какой доске (по умолчанию — своя)",
                "  --json            ответ в JSON (--list, --chat-list, --view-list)",
                "  --local           только локальный data/tasks.json, без сервера",
                "  --offline         то же, что --local",
                "  --no-ui           не поднимать окно ни при каких условиях",
                "  --selftest        самотест чтения/записи tasks.json",
                "  --path            путь к tasks.json и активный источник данных",
                "  --help, -h, --?   эта справка",
                "",
                "Хранилище: настройки Base + Key + Board есть → все команды идут на серверную доску",
                "(та же, что в окне, на телефоне и на сайте). Локальный data/tasks.json — только с --local.",
                "server не настроен → работаем с локальным файлом, как раньше.",
                "  --sync            залить на сервер локальные задачи, которых там нет (Title+Note)",
                "                    → SYNCED добавлено/всего, повторный прогон даёт SYNCED 0/N",
                "",
                "Данные: " + Store.TasksFile,
                "Язык:   " + Lang.UiFile,
                "",
                "--- English ---",
                "Nexus Board — AI task board",
                "",
                "GUI:            NexusBoard.exe            (double click)",
                "The window opens only with --open or a double click: CLI commands never raise it.",
                "",
                "Create and update tasks:",
                "  --add \"text\" [--mode night|auto|task|idle] [--note \"...\"] [--prio 1-5]",
                "  --start t3        take into work",
                "  --progress t3 --to 40 [--note \"...\"]   (--to required, 0..100)",
                "  --note t3 \"text\"",
                "  --done t3         close",
                "  --block t3 --note \"why\"",
                "  --todo t3         back to plan",
                "  --drop t3         drop",
                "  --log \"feed entry\"",
                "  --mode night|auto|task|idle",
                "  --list [--json]   task list",
                "  --clear [done|all]  drop closed tasks or all tasks",
                "  --open            show/start the window",
                "  --autostart on|off|status",
                "  --lang ru|en      interface language",
                "  --chat-list [--json]   chat history",
                "  --chat-say \"text\"      reply to the owner in chat",
                "  --board <16 hex>  target board (own board by default)",
                "  --json            JSON output (--list, --chat-list, --view-list)",
                "  --local           local data/tasks.json only, no server",
                "  --offline         same as --local",
                "  --no-ui           never raise the window",
                "  --selftest        self test of tasks.json read/write",
                "  --path            path to tasks.json and the active data source",
                "  --help, -h, --?   this help",
                "",
                "Storage: when settings have Base + Key + Board, every command goes to the server board",
                "(the same one the window, the phone and the site use). Local data/tasks.json — only with --local.",
                "Without a configured server the local file is used, as before.",
                "  --sync            upload local tasks missing on the server (Title+Note)",
                "                    → SYNCED added/total, a second run prints SYNCED 0/N",
                "",
                "Data: " + Store.TasksFile,
                "Language: " + Lang.UiFile
            });
        }
    }

}
