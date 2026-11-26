const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const { execFile } = require("node:child_process");

const ROOT = path.resolve(__dirname, "..");
const DATA = process.env.NEXUS_DATA_DIR || path.join(ROOT, "data");
const TASKS = path.join(DATA, "tasks.json");
const ACT = path.join(DATA, "activity.ndjson");
const BOARDS = process.env.NEXUS_BOARDS_DIR || path.join(__dirname, "boards");
const CHAT_DIR = process.env.NEXUS_CHAT_DIR || path.join(__dirname, "chat");
const LOGS = process.env.NEXUS_LOGS_DIR || path.join(__dirname, "logs");
const APP_DIR = process.env.NEXUS_APP_DIR || path.join(__dirname, "app");
const INDEX = path.join(__dirname, "index.html");
const TRAIN_HTML = path.join(__dirname, "train.html");
const PORT = Number(process.env.NEXUS_SITE_PORT || 8788);
const HOST = process.env.NEXUS_SITE_HOST || "127.0.0.1";
const PROXY_PORT = Number(process.env.NEXUS_PROXY_PORT || 8789);
const PROXY_TARGET = process.env.NEXUS_PROXY_TARGET || "http://127.0.0.1:8790";
// путь к каталогу обучения задаётся снаружи: self-host не должен знать чужие диски
const FINETUNE = process.env.NEXUS_FINETUNE_DIR || process.env.NEXUS_FINETUNE || "D:\\finetune";
const BOARD_LOG = "board-hits.log";
const ERROR_LOG = "errors.log";
const LOG_LIMIT = 5 * 1024 * 1024;
const MAX_BODY = 2 * 1024 * 1024;
const MAX_BOARDS = Number(process.env.NEXUS_MAX_BOARDS || 2000);
const MAX_CHATS = Number(process.env.NEXUS_MAX_CHATS || 2000);
const NEW_BOARDS_PER_DAY = 20;

let TOKEN = (process.env.NEXUS_SITE_TOKEN || "").trim();
if (!TOKEN) {
  try {
    TOKEN = fs.readFileSync(path.join(__dirname, "token.txt"), "utf8").trim();
  } catch {}
}

let started = false;
let logWarned = false;

// ---------- журналы: каталог при старте, ротация, без шума на каждый запрос ----------

function ensureLogs() {
  try {
    fs.mkdirSync(LOGS, { recursive: true });
    return true;
  } catch (e) {
    if (!logWarned) {
      logWarned = true;
      console.error("каталог логов недоступен:", e.message);
    }
    return false;
  }
}

function appendLog(file, line) {
  if (!ensureLogs()) return false;
  try {
    fs.appendFileSync(path.join(LOGS, file), line + "\n");
    return true;
  } catch (e) {
    if (!logWarned) {
      logWarned = true;
      console.error("запись в лог не удалась:", e.message);
    }
    return false;
  }
}

function rotateLog(file) {
  const p = path.join(LOGS, file);
  try {
    const st = fs.statSync(p);
    if (st.size <= LOG_LIMIT) return;
    const old = p + ".1";
    try {
      fs.rmSync(old, { force: true });
    } catch {}
    fs.renameSync(p, old);
  } catch {}
}

function sanitizeLine(v) {
  return String(v == null ? "" : v).replace(/[\r\n]+/g, " ").slice(0, 600);
}

function logError(where, e) {
  const text = e && (e.stack || e.message) ? String(e.stack || e.message) : String(e);
  const row = `[${new Date().toISOString()}] ${sanitizeLine(where)}: ${text.replace(/[\r\n]+/g, " | ").slice(0, 1800)}`;
  console.error(row);
  rotateLog(ERROR_LOG);
  appendLog(ERROR_LOG, row);
}

function boardHit(req, url, bid) {
  rotateLog(BOARD_LOG);
  appendLog(
    BOARD_LOG,
    [
      new Date().toISOString(),
      sanitizeLine(req.method),
      sanitizeLine(url),
      "board=" + bid,
      "ua=" + sanitizeLine(String(req.headers["user-agent"] || "")).slice(0, 60),
    ].join(" "),
  );
}

ensureLogs();
rotateLog(BOARD_LOG);

// ---------- сравнение секретов и cookie ----------

// timingSafeEqual требует одинаковую длину: выравниваем буферы, длину проверяем отдельно
function safeEqual(a, b) {
  const x = Buffer.from(String(a == null ? "" : a), "utf8");
  const y = Buffer.from(String(b == null ? "" : b), "utf8");
  if (!x.length || !y.length) return false;
  const len = Math.max(x.length, y.length);
  const px = Buffer.alloc(len);
  const py = Buffer.alloc(len);
  x.copy(px);
  y.copy(py);
  const same = crypto.timingSafeEqual(px, py);
  return same && x.length === y.length;
}

function cookieValue(req, name) {
  const raw = req.headers.cookie || "";
  for (const part of raw.split(";")) {
    const [k, ...v] = part.trim().split("=");
    if (k !== name) continue;
    try {
      return decodeURIComponent(v.join("="));
    } catch {
      return v.join("=");
    }
  }
  return "";
}

function isHttps(req) {
  const proto = String(req.headers["x-forwarded-proto"] || "").split(",")[0].trim().toLowerCase();
  return proto === "https" || Boolean(req.socket && req.socket.encrypted);
}

// HttpOnly — токен не читается из JS; Secure ставим на https (снаружи всегда туннель)
function authCookie(req, value) {
  const parts = [
    "nxt=" + encodeURIComponent(value),
    "Path=/",
    "Max-Age=31536000",
    "HttpOnly",
    "SameSite=Lax",
  ];
  if (isHttps(req)) parts.push("Secure");
  return parts.join("; ");
}

function allowed(req, query) {
  if (!TOKEN) return true;
  const ck = cookieValue(req, "nxt");
  if (ck && safeEqual(ck, TOKEN)) return true;
  const qk = query.get("k") || "";
  return Boolean(qk) && safeEqual(qk, TOKEN);
}

// ---------- ограничители ----------

const RATE_LIMITS = {
  b: [120, 60000],
  c: [60, 60000],
  new: [10, 60000],
};
const rateBuckets = new Map();
const newBoards = new Map();

function clientIp(req) {
  // CF-Connecting-IP ставит Cloudflare сам: X-Forwarded-For клиент может подделать
  const cf = String(req.headers["cf-connecting-ip"] || "").trim();
  const fwd = String(req.headers["x-forwarded-for"] || "").split(",")[0].trim();
  const raw = cf || fwd || (req.socket && req.socket.remoteAddress) || "?";
  return raw.replace(/^::ffff:/, "").slice(0, 64) || "?";
}

// скользящее окно по секундам: массив меток времени на пару bucket|ip
function rateLimited(bucket, ip) {
  const cfg = RATE_LIMITS[bucket];
  if (!cfg) return false;
  const [limit, win] = cfg;
  const key = bucket + "|" + ip;
  const now = Date.now();
  let arr = rateBuckets.get(key);
  if (!arr) {
    arr = [];
    rateBuckets.set(key, arr);
  }
  while (arr.length && now - arr[0] > win) arr.shift();
  arr.push(now);
  if (arr.length > limit + 64) arr.splice(0, arr.length - limit - 64);
  return arr.length > limit;
}

function boardQuotaExceeded(ip) {
  const day = new Date().toISOString().slice(0, 10);
  const rec = newBoards.get(ip);
  if (!rec || rec.day !== day) {
    newBoards.set(ip, { day, count: 1 });
    return false;
  }
  rec.count += 1;
  return rec.count > NEW_BOARDS_PER_DAY;
}

setInterval(() => {
  const now = Date.now();
  const day = new Date().toISOString().slice(0, 10);
  for (const [key, arr] of rateBuckets) {
    while (arr.length && now - arr[0] > 120000) arr.shift();
    if (!arr.length) rateBuckets.delete(key);
  }
  for (const [ip, rec] of newBoards) if (rec.day !== day) newBoards.delete(ip);
}, 60000).unref();

// ---------- файлы досок и чатов ----------

function countFiles(dir, ext) {
  try {
    let n = 0;
    for (const name of fs.readdirSync(dir)) {
      if (name.endsWith(ext) && !name.startsWith("_")) n += 1;
    }
    return n;
  } catch {
    return 0;
  }
}

const boardFile = (id) => path.join(BOARDS, id + ".json");
const validBoardId = (id) => /^[a-f0-9]{16}$/.test(String(id || ""));

// очередь по ключу файла: два POST не теряют обновления друг друга
const locks = new Map();
function withLock(key, fn) {
  const prev = locks.get(key) || Promise.resolve();
  const run = prev.then(() => fn());
  const tail = run.then(
    () => {},
    () => {},
  );
  locks.set(key, tail);
  tail.then(() => {
    if (locks.get(key) === tail) locks.delete(key);
  });
  return run;
}

function storageFull(message) {
  const e = new Error(message || "storage full");
  e.code = "E_STORAGE_FULL";
  return e;
}

function writeJsonAtomic(target, data, pretty) {
  fs.mkdirSync(path.dirname(target), { recursive: true });
  const tmp = target + "." + crypto.randomUUID() + ".tmp";
  try {
    fs.writeFileSync(tmp, JSON.stringify(data, null, pretty ? 2 : 0), "utf8");
    fs.renameSync(tmp, target);
  } catch (e) {
    try {
      fs.rmSync(tmp, { force: true });
    } catch {}
    throw e;
  }
}

function loadBoard(id) {
  try {
    const d = JSON.parse(fs.readFileSync(boardFile(id), "utf8"));
    if (!d || typeof d !== "object" || Array.isArray(d)) throw new Error("bad board shape");
    return d;
  } catch (e) {
    return { id, created: new Date().toISOString(), updated: "", tasks: [], sections: [], blocks: [] };
  }
}

function saveBoard(data) {
  data.updated = new Date().toISOString();
  writeJsonAtomic(boardFile(data.id), data, true);
}

function trySave(res, data) {
  try {
    saveBoard(data);
    return true;
  } catch (e) {
    logError("save " + sanitizeLine(data && data.id), e);
    json(res, 500, { ok: false, error: "save failed" });
    return false;
  }
}

function parseJson(raw) {
  try {
    const payload = raw ? JSON.parse(raw) : {};
    if (!payload || typeof payload !== "object" || Array.isArray(payload)) return { ok: false, payload: {} };
    return { ok: true, payload };
  } catch {
    return { ok: false, payload: {} };
  }
}

function readBody(req, limit) {
  return new Promise((resolve, reject) => {
    const max = limit || MAX_BODY;
    let size = 0;
    let done = false;
    const chunks = [];
    const finish = (fn, v) => {
      if (done) return;
      done = true;
      fn(v);
    };
    req.on("data", (c) => {
      if (done) return;
      size += c.length;
      if (size > max) {
        const e = new Error("payload too large");
        e.code = "E_TOO_LARGE";
        finish(reject, e);
        return;
      }
      chunks.push(c);
    });
    req.on("end", () => finish(resolve, Buffer.concat(chunks).toString("utf8")));
    req.on("error", (e) => finish(reject, e));
  });
}

// Тело читается до вызова обработчика: при переполнении обработчик НЕ получает пустой объект
function withBody(req, res, handler) {
  readBody(req)
    .then((raw) => handler(raw))
    .catch((e) => {
      if (e && e.code === "E_TOO_LARGE") {
        if (!res.headersSent) json(res, 413, { ok: false, error: "payload too large" });
        if (res.writableEnded) {
          req.destroy();
        } else {
          res.on("finish", () => req.destroy());
        }
        return;
      }
      if (e && e.code === "E_STORAGE_FULL") {
        if (!res.headersSent) json(res, 507, { ok: false, error: e.message || "storage full" });
        return;
      }
      logError("handler", e);
      if (!res.headersSent) json(res, 500, { ok: false, error: "internal error" });
    });
}

function drain(req) {
  try {
    req.resume();
  } catch {}
}

function json(res, code, body) {
  if (res.headersSent) return res.end();
  const buf = Buffer.from(JSON.stringify(body), "utf8");
  res.writeHead(code, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "Content-Length": buf.length,
  });
  res.end(buf);
}

// ---------- доска владельца и лента ----------

function readTasks() {
  try {
    const raw = fs.readFileSync(TASKS, "utf8");
    const d = JSON.parse(raw);
    const mtime = fs.statSync(TASKS).mtime.toISOString();
    return { ok: true, file: mtime, mode: d.Mode || d.mode || "idle", updated: d.Updated || d.updated || "", tasks: d.Tasks || d.tasks || [] };
  } catch (e) {
    return { ok: false, error: String(e.message || e), mode: "idle", updated: "", tasks: [] };
  }
}

const VIEW_KINDS = ["metric", "progress", "status", "text", "log", "links", "chart", "code", "tasks", "activity", "note"];

const LOG_ROOTS = [LOGS, path.join(FINETUNE, "runs", "logs")].map((p) => path.resolve(p));

// kind:"log" — только журналы из разрешённых каталогов. Иначе любой мог
// прочитать token.txt, boards/*.json или keystore через source.path.
function safeLogFile(raw) {
  const p = String(raw || "").trim();
  if (!p || p.includes("\0")) return null;
  let full;
  try {
    full = path.resolve(p);
  } catch {
    return null;
  }
  const inside = LOG_ROOTS.some((root) => full === root || full.startsWith(root + path.sep));
  if (!inside) return null;
  if (!/\.(log|txt|out|err|jsonl)$/i.test(full)) return null;
  const base = path.basename(full).toLowerCase();
  if (/(token|secret|credential|settings|keystore|\.key$|board)/.test(base)) return null;
  return full;
}

function tailLines(file, count) {
  let fd = null;
  try {
    const st = fs.statSync(file);
    if (!st.isFile() || st.size > 64 * 1024 * 1024) return { error: "not a text file" };
    const want = Math.max(1, Math.min(400, Number(count) || 40));
    const len = Math.min(st.size, 131072);
    const buf = Buffer.alloc(len);
    fd = fs.openSync(file, "r");
    fs.readSync(fd, buf, 0, len, st.size - len);
    let start = 0;
    // не начинаем посреди многобайтного символа
    if (st.size > len) while (start < 4 && start < buf.length && (buf[start] & 0xc0) === 0x80) start++;
    let text = buf.slice(start).toString("utf8");
    if (st.size > len) {
      const nl = text.indexOf("\n");
      if (nl >= 0) text = text.slice(nl + 1);
    }
    const lines = text.split(/\r?\n/);
    return { lines: lines.slice(-want), size: st.size, mtime: st.mtime.toISOString() };
  } catch (e) {
    return { error: String(e.message || e) };
  } finally {
    if (fd !== null) {
      try {
        fs.closeSync(fd);
      } catch {}
    }
  }
}

function bodySize(o) {
  try {
    return JSON.stringify(o).length;
  } catch {
    return 0;
  }
}

function safeHttpUrl(raw) {
  try {
    const u = new URL(String(raw || "").trim());
    return u.protocol === "http:" || u.protocol === "https:" ? u.toString() : "";
  } catch {
    return "";
  }
}

function cleanView(payload, prev) {
  const p = payload && typeof payload === "object" ? payload : {};
  const base = prev || {};
  const prevBody = base.body && typeof base.body === "object" ? base.body : {};
  let kind = String(p.kind || base.kind || "text")
    .toLowerCase()
    .replace(/[^a-z]/g, "")
    .slice(0, 16);
  if (!kind) kind = "text";
  const id =
    String(p.id || base.id || "")
      .replace(/[^a-zA-Z0-9_-]/g, "")
      .slice(0, 32) || "v" + crypto.randomBytes(4).toString("hex");
  let body = p.body !== undefined ? p.body : base.body;
  if (typeof body === "string") {
    try {
      body = JSON.parse(body);
    } catch {
      body = { text: String(body).slice(0, 8000) };
    }
  }
  if (!body || typeof body !== "object" || Array.isArray(body)) body = {};
  const size = ["sm", "md", "wide"].includes(String(p.size || base.size)) ? String(p.size || base.size) : "sm";
  const out = {
    id,
    kind: VIEW_KINDS.includes(kind) ? kind : "text",
    title: String(p.title != null ? p.title : base.title || "").slice(0, 80),
    order: Number(p.order != null ? p.order : base.order || 0) || 0,
    size,
    updated: new Date().toISOString(),
  };
  if (out.kind === "code") {
    const content = String(body.content != null ? body.content : prevBody.content || "").slice(0, 200000);
    out.body = {
      lang: String(body.lang || prevBody.lang || "text").slice(0, 16),
      filename: String(body.filename || prevBody.filename || "").slice(0, 120),
      content,
      version: Number(prevBody.version) || 1,
      sha: crypto.createHash("sha256").update(content).digest("hex").slice(0, 16),
    };
  } else {
    // ссылки принимаем только http/https: javascript:, file: и UNC через ПК
    // открывали бы чужой код или уводили NTLM-хеш
    if (out.kind === "links") {
      const items = Array.isArray(body.items) ? body.items : [];
      out.body = {
        ...body,
        items: items
          .slice(0, 50)
          .map((it) => {
            const row = it && typeof it === "object" ? it : {};
            return {
              label: String(row.label || "").slice(0, 160),
              url: safeHttpUrl(row.url),
              note: String(row.note || "").slice(0, 200),
            };
          })
          .filter((it) => it.url),
      };
    } else if (out.kind === "log") {
      const safe = safeLogFile(body.path);
      out.body = { ...body };
      if (safe) out.body.path = safe;
      else {
        delete out.body.path;
        out.body.error = "log path is outside allowed directories";
      }
    } else {
      out.body = body;
    }
    if (bodySize(out.body) > 65536) out.body = { text: JSON.stringify(out.body).slice(0, 60000), truncated: true };
  }
  const src = p.source && typeof p.source === "object" ? p.source : base.source;
  if (src && src.path) {
    const safe = out.kind === "log" ? safeLogFile(src.path) : null;
    if (safe) {
      out.source = { path: safe.slice(0, 400), lines: Math.max(1, Math.min(400, Number(src.lines) || 40)) };
      if (!out.body.path) out.body.path = safe;
    } else if (out.kind !== "log") {
      out.source = { path: String(src.path).slice(0, 400), lines: Math.max(1, Math.min(400, Number(src.lines) || 40)) };
    }
  }
  if (!out.title) out.title = out.kind;
  return out;
}

const MODES = ["auto", "task", "night", "idle"];
const STATUSES = ["todo", "doing", "blocked", "done"];

const syncTask = (t) => ({
  Id: String((t && (t.Id || t.id)) || "").slice(0, 40),
  Title: String((t && (t.Title || t.title)) || "").slice(0, 300),
  Note: String((t && (t.Note || t.note)) || "").slice(0, 2000),
  Mode: MODES.includes(String(t && (t.Mode || t.mode))) ? String(t.Mode || t.mode) : "task",
  Status: STATUSES.includes(String(t && (t.Status || t.status))) ? String(t.Status || t.status) : "todo",
  Progress: Math.max(0, Math.min(100, Number(t && (t.Progress != null ? t.Progress : t.progress)) || 0)),
  Section: String((t && (t.Section || t.section)) || "").slice(0, 24),
  Priority: Math.max(1, Math.min(5, Number(t && (t.Priority != null ? t.Priority : t.priority)) || 3)),
  Source: String((t && (t.Source || t.source)) || "ai").slice(0, 16),
  Created: String((t && (t.Created || t.created)) || new Date().toISOString()).slice(0, 32),
  Updated: String((t && (t.Updated || t.updated)) || new Date().toISOString()).slice(0, 32),
});

// progress принимаем только конечным числом 0..100, иначе 400 — никаких молчаливых нулей
function strictProgress(v) {
  if (v === undefined || v === null) return { ok: true, value: null };
  if (typeof v === "string" && !v.trim()) return { ok: false };
  const n = Number(v);
  if (!Number.isFinite(n) || n < 0 || n > 100) return { ok: false };
  return { ok: true, value: n };
}

function ownerSettings() {
  try {
    const file = path.join(process.env.APPDATA || "", "NexusBoard", "settings.json");
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
  } catch {
    return null;
  }
}

function ownerBoard() {
  const st = ownerSettings();
  const id = st && String(st.Board || "");
  if (!id || !validBoardId(id)) return null;
  try {
    return JSON.parse(fs.readFileSync(boardFile(id), "utf8"));
  } catch {
    return null;
  }
}

function boardBlocks(board) {
  const blocks = Array.isArray(board && board.blocks) ? board.blocks : [];
  return blocks
    .map((b) => {
      const copy = JSON.parse(JSON.stringify(b));
      if (copy.kind === "log") {
        const p = (copy.source && copy.source.path) || (copy.body && copy.body.path);
        const safe = safeLogFile(p);
        if (safe) copy.tail = tailLines(safe, (copy.source && copy.source.lines) || (copy.body && copy.body.lines) || 40);
        else copy.tail = { error: "log path is outside allowed directories" };
      }
      return copy;
    })
    .sort((a, b) => (a.order || 0) - (b.order || 0));
}

// ---------- прогресс обучения ----------

function tailText(file, bytes) {
  let fd = null;
  try {
    const st = fs.statSync(file);
    if (!st.isFile()) return { text: "", mtime: 0, size: 0 };
    const len = Math.min(st.size, bytes);
    const buf = Buffer.alloc(len);
    fd = fs.openSync(file, "r");
    fs.readSync(fd, buf, 0, len, st.size - len);
    let start = 0;
    // режем хвост по границе UTF-8, иначе первый символ превращается в мусор
    if (st.size > len) while (start < 4 && start < buf.length && (buf[start] & 0xc0) === 0x80) start++;
    return { text: buf.slice(start).toString("utf8"), mtime: st.mtimeMs, size: st.size };
  } catch {
    return { text: "", mtime: 0, size: 0 };
  } finally {
    if (fd !== null) {
      try {
        fs.closeSync(fd);
      } catch {}
    }
  }
}

function secs(hms) {
  const s = String(hms == null ? "" : hms).trim();
  if (!s || s.includes("?")) return 0;
  const p = s.split(":").map(Number);
  if (!p.length || p.some(Number.isNaN)) return 0;
  if (p.length === 3) return p[0] * 3600 + p[1] * 60 + p[2];
  if (p.length === 2) return p[0] * 60 + p[1];
  return p[0] || 0;
}

function rateText(num, unit) {
  const n = String(num == null ? "" : num).trim();
  const u = String(unit == null ? "" : unit).trim();
  if (!n || n === "?" || !u || u === "?") return null;
  const low = u.toLowerCase();
  if (low === "s/it") return n + " с/шаг";
  if (low === "it/s") return n + " итер/с";
  if (low === "examples/s") return n + " прим/с";
  if (low === "прим/с" || low === "с/шаг" || low === "итер/с") return n + " " + low;
  return n + " " + u;
}

const PROGRESS_RE = /(\d+)\s*\/\s*(\d+)\s*\[([\d:]+)\s*<\s*([\d:]+|\?)(?:,\s*([\d.]+|\?)\s*([A-Za-zА-Яа-я/]+))?\]/;
const NUM_RE = "-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?";
const EVAL_LOSS_RE = new RegExp("eval_loss['\"]?\\s*[:=]\\s*['\"]?(" + NUM_RE + ")", "i");
const PLAIN_LOSS_RE = new RegExp("(?<![_a-zA-Z])loss['\"]?\\s*[:=]\\s*['\"]?(" + NUM_RE + ")", "i");

function readTrain() {
  const logPath = path.join(FINETUNE, "runs", "logs", "marathon.log");
  const chainPath = path.join(FINETUNE, "runs", "logs", "chain_v6.log");
  const { text, mtime } = tailText(logPath, 400000);
  const lines = text.split(/[\r\n]+/).filter(Boolean);

  let step = null;
  let total = null;
  let elapsed = null;
  let remain = null;
  let rate = null;
  let stage = null;
  let unit = null;

  for (let i = lines.length - 1; i >= 0; i--) {
    const l = lines[i];
    const m = l.match(PROGRESS_RE);
    if (!m) continue;
    if (l.includes("Tokenizing")) {
      stage = "tokenization";
      unit = "примеров";
      step = Number(m[1]);
      total = Number(m[2]);
      elapsed = m[3];
      remain = m[4];
      rate = rateText(m[5], m[6]) || rateText(m[5], "examples/s");
      break;
    }
    stage = "training";
    unit = "шагов";
    step = Number(m[1]);
    total = Number(m[2]);
    elapsed = m[3];
    remain = m[4];
    rate = rateText(m[5], m[6]);
    break;
  }

  if (!stage && lines.some((l) => l.includes("марафон"))) stage = "start";
  if (!stage) stage = "build";

  const losses = [];
  let plainLoss = null;
  for (const l of lines) {
    const e = l.match(EVAL_LOSS_RE);
    if (e) {
      const v = Number(e[1]);
      if (Number.isFinite(v)) losses.push(v);
      continue;
    }
    const p = l.match(PLAIN_LOSS_RE);
    if (p) {
      const v = Number(p[1]);
      if (Number.isFinite(v)) plainLoss = v;
    }
  }

  let run = null;
  let runAt = 0;
  try {
    const dir = path.join(FINETUNE, "models", "adapters");
    for (const name of fs.readdirSync(dir)) {
      const st = fs.statSync(path.join(dir, name));
      if (st.isDirectory() && st.mtimeMs > runAt) {
        runAt = st.mtimeMs;
        run = name;
      }
    }
  } catch {}

  let phase = null;
  let phaseAt = null;
  let chainLines = [];
  const chain = tailText(chainPath, 20000);
  if (chain.text) {
    chainLines = chain.text.split(/\r?\n/).filter(Boolean).slice(-8);
    const last = chainLines[chainLines.length - 1] || "";
    const m = last.match(/^\[([^\]]+)\]\s*(.*)$/);
    if (m) {
      phaseAt = m[1];
      phase = m[2];
    } else {
      phase = last;
    }
  }

  const alive = mtime > 0 && Date.now() - mtime < 180000;
  const pct = total ? Math.round((Number(step || 0) / total) * 1000) / 10 : null;
  const noSource = !mtime && !chain.text;

  return {
    ok: !noSource,
    error: noSource ? "логи обучения не найдены" : undefined,
    alive,
    stage,
    unit,
    run,
    step,
    total,
    pct,
    elapsed,
    remain: remain || null,
    remainSeconds: remain && remain !== "?" ? secs(remain) : null,
    rate,
    loss: losses.length ? losses[losses.length - 1] : Number.isFinite(plainLoss) ? plainLoss : null,
    lossKind: losses.length ? "eval" : Number.isFinite(plainLoss) ? "train" : null,
    losses: losses.slice(-30),
    phase,
    phaseAt,
    chainLines,
    logAge: mtime ? Math.round((Date.now() - mtime) / 1000) : null,
    now: new Date().toISOString(),
  };
}

function readActivity(limit) {
  try {
    const lines = fs.readFileSync(ACT, "utf8").split("\n").filter(Boolean);
    const out = [];
    for (let i = lines.length - 1; i >= 0 && out.length < limit; i--) {
      try {
        out.push(JSON.parse(lines[i]));
      } catch {}
    }
    return out;
  } catch {
    return [];
  }
}

// ---------- вход и страницы ----------

function gatePage() {
  const html = `<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Nexus — вход по ключу</title>
<style>
:root{color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;min-height:100vh;display:grid;place-items:center;background:
radial-gradient(900px 600px at 12% 8%,rgba(139,92,246,.30),transparent 60%),
radial-gradient(760px 520px at 88% 78%,rgba(37,99,235,.22),transparent 62%),#0A0812;
color:#EDEAF6;font:16px/1.5 "Segoe UI",system-ui,sans-serif;padding:24px}
.card{width:100%;max-width:440px;padding:30px 28px 26px;border-radius:22px;
background:linear-gradient(160deg,rgba(28,22,44,.92),rgba(14,11,24,.94));
border:1px solid rgba(167,139,250,.22);box-shadow:0 26px 70px -30px rgba(0,0,0,.9)}
h1{margin:0 0 6px;font-size:20px;letter-spacing:.2px}
p{margin:0 0 20px;color:#A9A2C0;font-size:14px}
label{display:block;font-size:12px;text-transform:uppercase;letter-spacing:.14em;color:#8B84A3;margin-bottom:8px}
input{width:100%;padding:13px 14px;border-radius:12px;border:1px solid rgba(167,139,250,.28);
background:rgba(10,8,18,.85);color:#EDEAF6;font:15px "Segoe UI",sans-serif;outline:none}
input:focus{border-color:#8B5CF6;box-shadow:0 0 0 3px rgba(139,92,246,.18)}
button{margin-top:14px;width:100%;padding:13px 16px;border:0;border-radius:12px;cursor:pointer;
background:linear-gradient(180deg,#8B5CF6,#6D3BE0);color:#fff;font:600 15px "Segoe UI",sans-serif}
button:hover{filter:brightness(1.08)}
.hint{margin-top:14px;font-size:12.5px;color:#7C7594}
</style></head><body>
<form class="card" id="f" autocomplete="off">
  <h1>Nexus — закрытый доступ</h1>
  <p>Вставь ключ доступа, он запомнится на год.</p>
  <label for="k">Ключ</label>
  <input id="k" name="k" type="password" spellcheck="false" autofocus>
  <button type="submit">Открыть</button>
  <div class="hint">Или открой ссылку вида <code>/?k=ТОКЕН</code> — тот же результат.</div>
</form>
<script>
document.getElementById("f").addEventListener("submit", function (e) {
  e.preventDefault();
  var v = document.getElementById("k").value.trim();
  if (v) location.href = "/?k=" + encodeURIComponent(v);
});
if (location.search.indexOf("k=") === -1 && location.hash) {
  var m = location.hash.match(/k=([0-9a-f]+)/i);
  if (m) location.href = "/?k=" + m[1];
}
</script></body></html>`;
  return html;
}

// Редирект только на собственный путь без токена; //host, scheme и бэкслэши отбрасываются
function safeRedirectPath(parsed) {
  let p = String(parsed.pathname || "/");
  if (!p.startsWith("/") || p.startsWith("//") || p.includes("://") || p.includes("\\")) p = "/";
  const q = new URLSearchParams();
  for (const [key, value] of parsed.searchParams) {
    if (key === "k") continue;
    const v = String(value);
    if (v.startsWith("//") || v.includes("://") || v.includes("\\") || /[\r\n]/.test(v)) continue;
    q.append(key, v);
  }
  const s = q.toString();
  return s ? p + "?" + s : p;
}

function boardUrlFor(req, host, trainHost) {
  let base;
  if (trainHost) base = "https://board." + host.replace(/^train\./, "");
  else {
    const proto = isHttps(req) ? "https" : "http";
    base = proto + "://" + (host || HOST + ":" + PORT);
  }
  return TOKEN ? base + "/?k=" + encodeURIComponent(TOKEN) : base + "/";
}

function sendFile(res, file, type) {
  let buf;
  try {
    buf = fs.readFileSync(file);
  } catch (e) {
    return json(res, 500, { ok: false, error: String(e.message || e) });
  }
  res.writeHead(200, { "Content-Type": type, "Cache-Control": "no-store", "Content-Length": buf.length });
  return res.end(buf);
}

function sendTrain(req, res, host, trainHost) {
  let html;
  try {
    html = fs.readFileSync(TRAIN_HTML, "utf8");
  } catch (e) {
    return json(res, 500, { ok: false, error: String(e.message || e) });
  }
  // ключ подставляем на сервере: cookie HttpOnly, JS его больше не видит
  html = html.replace('"__NEXUS_BOARD_URL__"', JSON.stringify(boardUrlFor(req, host, trainHost)));
  res.writeHead(200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
  return res.end(html);
}

const APP_TYPES = {
  ".apk": "application/vnd.android.package-archive",
  ".exe": "application/octet-stream",
  ".zip": "application/zip",
  ".json": "application/json; charset=utf-8",
};

function streamAppFile(res, file, name) {
  let stat;
  try {
    stat = fs.statSync(file);
  } catch {
    stat = null;
  }
  if (!stat || !stat.isFile()) {
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
    return res.end("not found");
  }
  const ext = path.extname(name).toLowerCase();
  const disp = ext === ".json" ? "inline" : "attachment";
  res.writeHead(200, {
    "Content-Type": APP_TYPES[ext] || "application/octet-stream",
    "Content-Length": stat.size,
    "Content-Disposition": `${disp}; filename="${path.basename(name)}"`,
    "Cache-Control": "no-store",
  });
  const stream = fs.createReadStream(file);
  stream.on("error", (e) => {
    logError("stream " + sanitizeLine(name), e);
    try {
      res.destroy();
    } catch {}
  });
  res.on("close", () => {
    try {
      stream.destroy();
    } catch {}
  });
  return stream.pipe(res);
}

// ---------- обработчик запросов ----------

const server = http.createServer((req, res) => {
  try {
    handle(req, res);
  } catch (e) {
    logError("request", e);
    if (!res.headersSent) json(res, 500, { ok: false, error: "internal error" });
    else res.end();
  }
});

function handle(req, res) {
  let parsed;
  try {
    parsed = new URL(req.url, `http://${req.headers.host || HOST}`);
  } catch {
    return json(res, 400, { ok: false, error: "bad url" });
  }
  const url = parsed.pathname;
  const host = String(req.headers.host || "").toLowerCase();
  const trainHost = host.startsWith("train.");

  const isBoardApi = url.startsWith("/api/b/");
  const isChatApi = url.startsWith("/api/c/");
  const apiPass =
    isBoardApi ||
    isChatApi ||
    url === "/api/views" ||
    url === "/api/code" ||
    url === "/api/tasks" ||
    url === "/api/activity" ||
    url === "/api/train";
  // Личные доски авторизуются своим ключом (k= или cookie); остальные
  // /api/* отдают данные проекта и требуют ключ сайта. Раньше они были
  // открыты всем — задачи и лента читались без авторизации.
  const globalApi =
    url === "/api/views" || url === "/api/code" || url === "/api/tasks" ||
    url === "/api/activity" || url === "/api/train";
  const apiAuthOk = !TOKEN
    || safeEqual(cookieValue(req, "nxt"), TOKEN)
    || safeEqual(String(parsed.searchParams.get("k") || ""), TOKEN);

  if (url === "/api/health") return json(res, 200, { ok: true, port: PORT });

  // ограничители ставим до любой работы с диском
  if (isBoardApi || isChatApi) {
    const ip = clientIp(req);
    const bucket = url === "/api/b/new" ? "new" : isChatApi ? "c" : "b";
    if (rateLimited(bucket, ip)) return json(res, 429, { ok: false, error: "rate limit" });
  }

  if (globalApi && !apiAuthOk) {
    // API-ключ приходит в query (k=) и не редиректится: иначе клиент,
    // не хранящий cookie, получал 302 и «нет связи».
    return json(res, 401, { ok: false, error: "unauthorized" });
  }

  if (!apiPass) {
    // вход по ключу: cookie + 302 на свой путь, токен в Location не попадает
    const qk = parsed.searchParams.get("k") || "";
    if (TOKEN && qk && safeEqual(qk, TOKEN)) {
      res.writeHead(302, {
        "Set-Cookie": authCookie(req, TOKEN),
        Location: safeRedirectPath(parsed),
        "Cache-Control": "no-store",
      });
      return res.end();
    }
    const gated = !apiPass && !url.startsWith("/app/") && !url.startsWith("/api/app/");
    if (gated && !allowed(req, parsed.searchParams)) {
      res.writeHead(401, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
      return res.end(gatePage());
    }
  }

  // ---------- личные доски: без аккаунтов и ключей ----------
  if (isBoardApi) {
    const bid = String(parsed.searchParams.get("b") || req.headers["x-board"] || "").trim();
    const boardKey = String(parsed.searchParams.get("k") || cookieValue(req, "nxt") || "").trim();

    if (url === "/api/b/new" && req.method === "POST") {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        let key = String(p.payload.key || "").trim();
        if (!key) {
          const q = String(parsed.searchParams.get("k") || "").trim();
          if (q && !(TOKEN && safeEqual(q, TOKEN))) key = q;
        }
        if (!key) {
          const ck = cookieValue(req, "nxt");
          if (ck && !(TOKEN && safeEqual(ck, TOKEN))) key = ck;
        }
        if (key) {
          if (!/^[\p{L}\p{N}_-]{8,64}$/u.test(key)) {
            return json(res, 400, { ok: false, error: "bad key: 8-64 letters, digits, _ or -" });
          }
          if (TOKEN && safeEqual(key, TOKEN)) {
            return json(res, 400, { ok: false, error: "board key must differ from the site token" });
          }
        }
        if (boardQuotaExceeded(clientIp(req))) {
          return json(res, 429, { ok: false, error: "too many boards from this ip" });
        }
        if (countFiles(BOARDS, ".json") >= MAX_BOARDS) {
          return json(res, 507, { ok: false, error: "board storage full" });
        }
        const id = crypto.randomBytes(8).toString("hex");
        const fresh = { id, created: new Date().toISOString(), updated: "", tasks: [], sections: [], blocks: [] };
        if (key) fresh.key = key;
        if (!trySave(res, fresh)) return;
        return json(res, 200, { ok: true, id });
      });
    }

    if (!validBoardId(bid)) return json(res, 400, { ok: false, error: "bad board id" });

    boardHit(req, url, bid);

    if (!fs.existsSync(boardFile(bid))) {
      return json(res, 404, { ok: false, error: "board not found" });
    }

    const probe = loadBoard(bid);
    if (probe.key && !safeEqual(boardKey, probe.key)) {
      return json(res, 401, { ok: false, error: "bad key" });
    }

    if (url === "/api/b/seed" && req.method === "POST") {
      // Шаблон с задачами владельца больше не подмешивается: пустая доска
      // остаётся пустой, иначе клиент после /clear снова получал чужие задачи.
      drain(req);
      return json(res, 200, { ok: true, seeded: false, tasks: 0 });
    }

    if (url === "/api/b/tasks" && req.method === "GET") {
      const data = loadBoard(bid);
      return json(res, 200, {
        ok: true,
        id: data.id,
        updated: data.updated || "",
        tasks: data.tasks || [],
        sections: data.sections || [],
      });
    }

    if (url === "/api/b/section" && req.method === "GET") {
      const data = loadBoard(bid);
      return json(res, 200, { ok: true, sections: data.sections || [] });
    }

    if (url === "/api/b/activity" && req.method === "GET") {
      const owner = ownerBoard();
      if (!owner || owner.id !== bid) return json(res, 403, { ok: false, error: "owner board only" });
      try {
        const raw = fs
          .readFileSync(ACT, "utf8")
          .trim()
          .split("\n")
          .map((x) => x.replace("\r", ""));
        const rows = raw
          .slice(-40)
          .map((l) => {
            try {
              return JSON.parse(l);
            } catch {
              return null;
            }
          })
          .filter(Boolean)
          .slice(-20)
          .reverse();
        return json(res, 200, { ok: true, items: rows });
      } catch {
        return json(res, 200, { ok: true, items: [] });
      }
    }

    const viewsOf = (data) => {
      data.blocks = Array.isArray(data.blocks) ? data.blocks : [];
      return data.blocks;
    };
    const findView = (data, id) => viewsOf(data).find((b) => b && b.id === id);

    if (url === "/api/b/views" && req.method === "GET") {
      const data = loadBoard(bid);
      const blocks = viewsOf(data).map((b) => {
        const copy = JSON.parse(JSON.stringify(b));
        if (copy.kind === "log") {
          const p = (copy.source && copy.source.path) || (copy.body && copy.body.path);
          const safe = safeLogFile(p);
          if (safe) copy.tail = tailLines(safe, (copy.source && copy.source.lines) || (copy.body && copy.body.lines) || 40);
          else copy.tail = { error: "log path is outside allowed directories" };
        }
        return copy;
      });
      blocks.sort((a, b) => (a.order || 0) - (b.order || 0));
      const version = crypto.createHash("sha1").update(JSON.stringify(blocks)).digest("hex").slice(0, 12);
      return json(res, 200, { ok: true, version, blocks });
    }

    if (url === "/api/b/view/set" && req.method === "POST") {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        return withLock(boardFile(bid), () => {
          const data = loadBoard(bid);
          const blocks = viewsOf(data);
          const id = String(p.payload.id || "").replace(/[^a-zA-Z0-9_-]/g, "");
          const prev = id ? findView(data, id) : null;
          if (!prev && blocks.length >= 40) return json(res, 400, { ok: false, error: "too many blocks" });
          const block = cleanView(p.payload, prev);
          if (prev) {
            blocks[blocks.indexOf(prev)] = block;
          } else {
            blocks.push(block);
          }
          if (!trySave(res, data)) return;
          return json(res, 200, { ok: true, block });
        });
      });
    }

    if (url === "/api/b/view/del" && req.method === "POST") {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        return withLock(boardFile(bid), () => {
          const data = loadBoard(bid);
          const blocks = viewsOf(data);
          const id = String(p.payload.id || "");
          const before = blocks.length;
          data.blocks = blocks.filter((b) => !b || b.id !== id);
          if (data.blocks.length === before) return json(res, 404, { ok: false, error: "block not found" });
          if (!trySave(res, data)) return;
          return json(res, 200, { ok: true, blocks: data.blocks });
        });
      });
    }

    if (url === "/api/b/sync" && req.method === "POST") {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        const payload = p.payload;
        if (!Array.isArray(payload.tasks)) return json(res, 400, { ok: false, error: "tasks[] required" });
        return withLock(boardFile(bid), () => {
          const data = loadBoard(bid);
          viewsOf(data);
          // Слияние, а не замена: CLI с пустым локальным кэшем больше не
          // стирает задачи, созданные с телефона или сайта.
          const byId = new Map((data.tasks || []).filter((t) => t && t.Id).map((t) => [t.Id, t]));
          for (const t of payload.tasks.slice(0, 400).map(syncTask)) {
            if (t.Title) byId.set(t.Id, { ...(byId.get(t.Id) || {}), ...t });
          }
          data.tasks = [...byId.values()];
          if (Array.isArray(payload.sections)) {
            data.sections = payload.sections
              .slice(0, 20)
              .map((sec, i) => ({
                id: String((sec && sec.id) || "s" + i)
                  .replace(/[^a-zA-Z0-9_-]/g, "")
                  .slice(0, 24),
                name: String((sec && sec.name) || "").slice(0, 60),
                order: Number((sec && sec.order) != null ? sec.order : i) || i,
              }))
              .filter((sec) => sec.name);
          }
          if (payload.mode != null) {
            const mode = String(payload.mode).trim();
            if (!MODES.includes(mode)) return json(res, 400, { ok: false, error: "bad mode" });
            data.mode = mode;
            delete data.Mode;
          }
          if (!trySave(res, data)) return;
          return json(res, 200, {
            ok: true,
            tasks: data.tasks.length,
            sections: (data.sections || []).length,
            blocks: viewsOf(data).length,
            updated: data.updated,
          });
        });
      });
    }

    if (url === "/api/b/code" && req.method === "GET") {
      const data = loadBoard(bid);
      const id = String(parsed.searchParams.get("id") || "");
      const b = findView(data, id);
      if (!b || !b.body || typeof b.body.content !== "string") {
        return json(res, 404, { ok: false, error: "code block not found" });
      }
      const content = b.body.content;
      return json(res, 200, {
        ok: true,
        code: {
          id: b.id,
          title: b.title || "",
          lang: b.body.lang || "text",
          filename: b.body.filename || "",
          content,
          version: Number(b.body.version) || 1,
          sha: crypto.createHash("sha256").update(content).digest("hex").slice(0, 16),
          updated: b.updated || "",
        },
      });
    }

    if (url === "/api/b/code/save" && req.method === "POST") {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        return withLock(boardFile(bid), () => {
          const data = loadBoard(bid);
          const id = String(p.payload.id || "");
          const b = findView(data, id);
          if (!b || !b.body || typeof b.body.content !== "string") {
            return json(res, 404, { ok: false, error: "code block not found" });
          }
          const current = Number(b.body.version) || 1;
          const currentSha = crypto.createHash("sha256").update(b.body.content).digest("hex").slice(0, 16);
          const ifVersion = p.payload.ifVersion != null ? Number(p.payload.ifVersion) : null;
          if (ifVersion != null && ifVersion !== current) {
            return json(res, 409, {
              ok: false,
              error: "conflict",
              version: current,
              sha: currentSha,
              content: b.body.content,
            });
          }
          const content = String(p.payload.content == null ? "" : p.payload.content).slice(0, 200000);
          if (!content.trim()) return json(res, 400, { ok: false, error: "content required" });
          const sha = crypto.createHash("sha256").update(content).digest("hex").slice(0, 16);
          const version = current + 1;
          b.body.history = (Array.isArray(b.body.history) ? b.body.history : [])
            .concat([{ version: current, sha: currentSha, ts: new Date().toISOString() }])
            .slice(-5);
          b.body.content = content;
          b.body.version = version;
          b.body.sha = sha;
          if (p.payload.title) b.title = String(p.payload.title).slice(0, 80);
          b.updated = new Date().toISOString();
          if (!trySave(res, data)) return;
          return json(res, 200, { ok: true, version, sha, updated: b.updated });
        });
      });
    }

    if (
      (url === "/api/b/add" ||
        url === "/api/b/update" ||
        url === "/api/b/delete" ||
        url === "/api/b/clear" ||
        url === "/api/b/mode" ||
        url === "/api/b/section/add" ||
        url === "/api/b/section/set" ||
        url === "/api/b/section/del") &&
      req.method === "POST"
    ) {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        const payload = p.payload;
        return withLock(boardFile(bid), () => {
          const data = loadBoard(bid);
          data.tasks = Array.isArray(data.tasks) ? data.tasks : [];
          data.sections = Array.isArray(data.sections) ? data.sections : [];

          if (url === "/api/b/section/add") {
            if (data.sections.length >= 20) return json(res, 400, { ok: false, error: "too many sections" });
            const name = String(payload.name || "").trim().slice(0, 60);
            if (!name) return json(res, 400, { ok: false, error: "name required" });
            const sid =
              String(payload.id || "")
                .trim()
                .replace(/[^a-zA-Z0-9_-]/g, "")
                .slice(0, 24) || "s" + crypto.randomBytes(3).toString("hex");
            if (data.sections.some((s) => s.id === sid)) return json(res, 409, { ok: false, error: "section exists" });
            const sec = { id: sid, name, order: data.sections.length };
            data.sections.push(sec);
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, section: sec, sections: data.sections });
          }

          if (url === "/api/b/section/set") {
            const sid = String(payload.id || "").trim();
            const found = data.sections.find((s) => s.id === sid);
            if (!found) return json(res, 404, { ok: false, error: "section not found" });
            const name = String(payload.name || "").trim().slice(0, 60);
            if (!name) return json(res, 400, { ok: false, error: "name required" });
            found.name = name;
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, section: found, sections: data.sections });
          }

          if (url === "/api/b/section/del") {
            const sid = String(payload.id || "").trim();
            const before = data.sections.length;
            data.sections = data.sections.filter((s) => s.id !== sid);
            if (data.sections.length === before) return json(res, 404, { ok: false, error: "section not found" });
            // задачи не остаются без раздела: уводим в первый оставшийся или в пустой
            const ordered = data.sections.slice().sort((a, b) => (Number(a.order) || 0) - (Number(b.order) || 0));
            const fallback = ordered.length ? ordered[0].id : "";
            let moved = 0;
            data.tasks.forEach((x) => {
              if (x && x.Section === sid) {
                x.Section = fallback;
                moved += 1;
              }
            });
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, sections: data.sections, moved, fallback });
          }

          if (url === "/api/b/clear") {
            data.tasks = [];
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, tasks: 0 });
          }

          if (url === "/api/b/delete") {
            const id = String(payload.id || payload.Id || "").trim();
            if (!id) return json(res, 400, { ok: false, error: "id required" });
            const before = data.tasks.length;
            data.tasks = data.tasks.filter((x) => !x || x.Id !== id);
            if (data.tasks.length === before) return json(res, 404, { ok: false, error: "task not found" });
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, tasks: data.tasks.length });
          }

          if (url === "/api/b/mode") {
            const mode = String(payload.mode || "").trim();
            if (!MODES.includes(mode)) return json(res, 400, { ok: false, error: "bad mode" });
            data.mode = mode;
            delete data.Mode;
            data.updated = new Date().toISOString();
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, mode });
          }

          if (url === "/api/b/add") {
            if (data.tasks.length >= 200) return json(res, 400, { ok: false, error: "board full" });
            const title = String(payload.title || "").trim().slice(0, 300);
            if (!title) return json(res, 400, { ok: false, error: "title required" });
            const pr = strictProgress(payload.progress);
            if (!pr.ok) return json(res, 400, { ok: false, error: "bad progress" });
            const mode = String(payload.mode || "task").trim();
            if (payload.mode != null && !MODES.includes(mode)) {
              return json(res, 400, { ok: false, error: "bad mode" });
            }
            const task = {
              Id: "b" + (data.tasks.length + 1) + "-" + crypto.randomBytes(3).toString("hex"),
              Title: title,
              Note: String(payload.note || "").slice(0, 2000),
              Mode: MODES.includes(mode) ? mode : "task",
              Status: STATUSES.includes(String(payload.status)) ? String(payload.status) : "todo",
              Progress: pr.value == null ? 0 : pr.value,
              Section: String(payload.section || payload.Section || "").slice(0, 24),
              Created: new Date().toISOString(),
              Updated: new Date().toISOString(),
            };
            data.tasks.push(task);
            if (!trySave(res, data)) return;
            return json(res, 200, { ok: true, task });
          }

          // update
          const id = String(payload.id || payload.Id || "");
          const task = data.tasks.find((x) => x && x.Id === id);
          if (!task) return json(res, 404, { ok: false, error: "task not found" });
          if (payload.title != null) {
            const title = String(payload.title).trim().slice(0, 300);
            if (!title) return json(res, 400, { ok: false, error: "title required" });
            task.Title = title;
          }
          if (payload.note != null) task.Note = String(payload.note).slice(0, 2000);
          if (payload.status != null) {
            const status = String(payload.status);
            if (!STATUSES.includes(status)) return json(res, 400, { ok: false, error: "bad status" });
            task.Status = status;
          }
          if (payload.mode != null) {
            const mode = String(payload.mode).trim();
            if (!MODES.includes(mode)) return json(res, 400, { ok: false, error: "bad mode" });
            task.Mode = mode;
          }
          if (payload.progress != null) {
            const pr = strictProgress(payload.progress);
            if (!pr.ok || pr.value == null) return json(res, 400, { ok: false, error: "bad progress" });
            task.Progress = pr.value;
          }
          if (payload.section != null || payload.Section != null) {
            task.Section = String(payload.section != null ? payload.section : payload.Section).slice(0, 24);
          }
          task.Updated = new Date().toISOString();
          if (!trySave(res, data)) return;
          return json(res, 200, { ok: true, task });
        });
      });
    }

    drain(req);
    return json(res, 404, { ok: false, error: "unknown board endpoint" });
  }

  // ---------- чат доски ----------
  if (isChatApi) {
    const queryId = String(parsed.searchParams.get("board") || "").trim();
    const chatKey = String(parsed.searchParams.get("k") || cookieValue(req, "nxt") || "").trim();
    const chatAuthorized = (id) => {
      try {
        const b = loadBoard(id);
        return !b.key || safeEqual(chatKey, b.key);
      } catch {
        return false;
      }
    };

    const loadChat = (id) => {
      try {
        const d = JSON.parse(fs.readFileSync(path.join(CHAT_DIR, id + ".json"), "utf8"));
        d.messages = Array.isArray(d.messages) ? d.messages : [];
        return d;
      } catch {
        return { board: id, messages: [] };
      }
    };
    const saveChat = (data) => {
      data.updated = new Date().toISOString();
      writeJsonAtomic(path.join(CHAT_DIR, data.board + ".json"), data, false);
    };
    const pushMessage = (id, from, text) => {
      const file = path.join(CHAT_DIR, id + ".json");
      if (!fs.existsSync(file) && countFiles(CHAT_DIR, ".json") >= MAX_CHATS) {
        throw storageFull("chat storage full");
      }
      const data = loadChat(id);
      const msg = {
        id: crypto.randomBytes(8).toString("hex"),
        from,
        text,
        ts: Date.now(),
      };
      data.messages.push(msg);
      if (data.messages.length > 500) data.messages = data.messages.slice(-500);
      saveChat(data);
      return msg;
    };

    if (url === "/api/c/send" && req.method === "POST") {
      return withBody(req, res, (raw) => {
        const p = parseJson(raw);
        if (!p.ok) return json(res, 400, { ok: false, error: "bad json" });
        const id = String(p.payload.board || queryId || "").trim();
        if (!validBoardId(id)) return json(res, 400, { ok: false, error: "bad board id" });
        // отправителя не подделываем: только ai или owner, иначе 400
        const from = String(p.payload.from == null ? "" : p.payload.from)
          .trim()
          .toLowerCase();
        if (from !== "ai" && from !== "owner") return json(res, 400, { ok: false, error: "bad from" });
        const text = String(p.payload.text == null ? "" : p.payload.text).trim().slice(0, 4000);
        if (!text) return json(res, 400, { ok: false, error: "text required" });
        if (!fs.existsSync(boardFile(id))) return json(res, 404, { ok: false, error: "board not found" });
        if (!chatAuthorized(id)) return json(res, 401, { ok: false, error: "bad key" });
        return withLock(path.join(CHAT_DIR, id + ".json"), () => {
          const msg = pushMessage(id, from, text);
          return json(res, 200, { ok: true, id: msg.id, ts: msg.ts });
        });
      });
    }

    if (url === "/api/c/list" && req.method === "GET") {
      if (!validBoardId(queryId)) return json(res, 400, { ok: false, error: "bad board id" });
      if (!fs.existsSync(boardFile(queryId))) return json(res, 404, { ok: false, error: "board not found" });
      if (!chatAuthorized(queryId)) return json(res, 401, { ok: false, error: "bad key" });
      const data = loadChat(queryId);
      const since = Number(parsed.searchParams.get("since") || 0) || 0;
      const limit = Math.max(1, Math.min(500, Number(parsed.searchParams.get("limit") || 200) || 200));
      let list = since > 0 ? data.messages.filter((m) => m.ts > since) : data.messages;
      if (list.length > limit) list = list.slice(-limit);
      const last = data.messages.length ? data.messages[data.messages.length - 1].ts : 0;
      return json(res, 200, { ok: true, messages: list, last, count: data.messages.length });
    }

    drain(req);
    return json(res, 404, { ok: false, error: "unknown chat endpoint" });
  }

  if (url === "/api/app/latest") {
    const file = path.join(APP_DIR, "latest.json");
    let body = { ok: false };
    try {
      body = JSON.parse(fs.readFileSync(file, "utf8"));
      body.ok = true;
    } catch {}
    return json(res, 200, body);
  }

  if (url === "/api/app/pc") {
    const file = path.join(APP_DIR, "pc", "latest.json");
    let body = { ok: false };
    try {
      body = JSON.parse(fs.readFileSync(file, "utf8"));
      body.ok = true;
    } catch {}
    return json(res, 200, body);
  }

  if (url.startsWith("/app/")) {
    let name;
    try {
      name = decodeURIComponent(url.slice("/app/".length));
    } catch {
      name = "";
    }
    if (!/^(pc\/)?[A-Za-z0-9._-]+\.(apk|exe|zip|json)$/.test(name)) {
      res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
      return res.end("not found");
    }
    const resolved = path.resolve(path.join(APP_DIR, name));
    if (!resolved.startsWith(path.resolve(APP_DIR) + path.sep)) {
      res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
      return res.end("not found");
    }
    return streamAppFile(res, resolved, name);
  }

  if (trainHost && url === "/") return sendTrain(req, res, host, trainHost);

  if (url === "/api/views") {
    const board = ownerBoard();
    if (!board) return json(res, 200, { ok: false, blocks: [], error: "no board key" });
    return json(res, 200, { ok: true, version: board.updated || "", blocks: boardBlocks(board) });
  }

  if (url === "/api/code") {
    const board = ownerBoard();
    const id = String(parsed.searchParams.get("id") || "");
    const b = (board && Array.isArray(board.blocks) ? board.blocks : []).find((x) => x && x.id === id);
    if (!b || !b.body || typeof b.body.content !== "string") {
      return json(res, 404, { ok: false, error: "code block not found" });
    }
    return json(res, 200, {
      ok: true,
      code: {
        id: b.id,
        title: b.title || "",
        lang: b.body.lang || "text",
        filename: b.body.filename || "",
        content: b.body.content,
        version: Number(b.body.version) || 1,
      },
    });
  }

  if (url === "/api/tasks") return json(res, 200, readTasks());
  if (url === "/api/activity") return json(res, 200, readActivity(40));
  if (url === "/api/train") return json(res, 200, readTrain());

  if (url === "/train" || url === "/train.html") return sendTrain(req, res, host, trainHost);

  if (url === "/" || url === "/index.html") return sendFile(res, INDEX, "text/html; charset=utf-8");

  res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
  return res.end("not found");
}

server.listen(PORT, HOST, () => {
  started = true;
  console.log(`Nexus board site: http://${HOST}:${PORT}  (данные: ${DATA})`);
  if (process.argv.includes("--open")) {
    execFile("cmd", ["/c", "start", "", `http://${HOST}:${PORT}`], { windowsHide: true }, () => {});
  }
});

server.on("error", (e) => {
  if (e && e.code === "EADDRINUSE") {
    console.error(`порт ${PORT} уже занят: останови старый экземпляр или задай NEXUS_SITE_PORT`);
  } else {
    console.error("сервер не поднялся:", e.message);
  }
  // старт не удался — процесс не должен висеть полуживым
  process.exit(1);
});

// ---------- прокси к прогресс-хабу ----------

const proxy = http.createServer((req, res) => {
  let parsed;
  try {
    parsed = new URL(req.url, `http://${req.headers.host || HOST}`);
  } catch {
    res.writeHead(400, { "Content-Type": "text/plain; charset=utf-8" });
    return res.end("bad url");
  }

  const qk = parsed.searchParams.get("k") || "";
  if (TOKEN && qk && safeEqual(qk, TOKEN)) {
    res.writeHead(302, {
      "Set-Cookie": authCookie(req, TOKEN),
      Location: safeRedirectPath(parsed),
      "Cache-Control": "no-store",
    });
    return res.end();
  }

  if (!allowed(req, parsed.searchParams)) {
    res.writeHead(401, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
    return res.end(gatePage());
  }

  const up = new URL(PROXY_TARGET);
  const preq = http.request(
    {
      hostname: up.hostname,
      port: up.port || 80,
      path: req.url,
      method: req.method,
      timeout: 60000,
      headers: Object.assign({}, req.headers, { host: up.host }),
    },
    (pres) => {
      res.writeHead(pres.statusCode || 502, pres.headers);
      pres.pipe(res);
    },
  );
  preq.on("timeout", () => {
    preq.destroy(new Error("таймаут прогресс-хаба"));
  });
  preq.on("error", (e) => {
    logError("proxy upstream", e);
    if (!res.headersSent) {
      res.writeHead(502, { "Content-Type": "text/plain; charset=utf-8" });
      res.end("прогресс-хаб не отвечает: " + e.message);
    } else {
      res.destroy();
    }
  });
  req.pipe(preq);
});

proxy.on("error", (e) => {
  if (e && e.code === "EADDRINUSE") {
    console.error(`прокси: порт ${PROXY_PORT} занят (EADDRINUSE), вход по ключу на этом порту не поднят`);
    return;
  }
  logError("proxy", e);
  console.error("прокси остановлен:", e && e.message);
  try {
    proxy.close();
  } catch {}
});

proxy.listen(PROXY_PORT, HOST, () => {
  console.log(`прогресс обучения через ключ: http://${HOST}:${PROXY_PORT} → ${PROXY_TARGET}`);
});

process.on("uncaughtException", (e) => {
  logError("uncaughtException", e);
  if (!started) process.exit(1);
});

process.on("unhandledRejection", (e) => {
  logError("unhandledRejection", e);
  if (!started) process.exit(1);
});
