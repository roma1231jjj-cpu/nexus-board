const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const { execFile } = require("node:child_process");

const ROOT = path.resolve(__dirname, "..");
const DATA = path.join(ROOT, "data");
const TASKS = path.join(DATA, "tasks.json");
const ACT = path.join(DATA, "activity.ndjson");
const INDEX = path.join(__dirname, "index.html");
const PORT = Number(process.env.NEXUS_SITE_PORT || 8788);
const HOST = process.env.NEXUS_SITE_HOST || "127.0.0.1";

let TOKEN = (process.env.NEXUS_SITE_TOKEN || "").trim();
if (!TOKEN) {
  try {
    TOKEN = fs.readFileSync(path.join(__dirname, "token.txt"), "utf8").trim();
  } catch {}
}

function cookieValue(req, name) {
  const raw = req.headers.cookie || "";
  for (const part of raw.split(";")) {
    const [k, ...v] = part.trim().split("=");
    if (k === name) return v.join("=");
  }
  return "";
}

function allowed(req, query) {
  if (!TOKEN) return true;
  if (cookieValue(req, "nxt") === TOKEN) return true;
  return query.get("k") === TOKEN;
}

function readTasks() {
  try {
    const raw = fs.readFileSync(TASKS, "utf8");
    const d = JSON.parse(raw);
    const mtime = fs.statSync(TASKS).mtime.toISOString();
    return { ok: true, file: mtime, mode: d.Mode || "idle", updated: d.Updated || "", tasks: d.Tasks || [] };
  } catch (e) {
    return { ok: false, error: String(e.message || e), mode: "idle", updated: "", tasks: [] };
  }
}

const VIEW_KINDS = ["metric", "progress", "status", "text", "log", "links", "chart", "code", "tasks", "activity", "note"];

function tailLines(file, count) {
  try {
    const st = fs.statSync(file);
    if (!st.isFile() || st.size > 64 * 1024 * 1024) return { error: "not a text file" };
    const want = Math.max(1, Math.min(400, Number(count) || 40));
    const len = Math.min(st.size, 131072);
    const buf = Buffer.alloc(len);
    const fd = fs.openSync(file, "r");
    fs.readSync(fd, buf, 0, len, st.size - len);
    fs.closeSync(fd);
    let text = buf.toString("utf8");
    if (st.size > len) {
      const nl = text.indexOf("\n");
      if (nl >= 0) text = text.slice(nl + 1);
    }
    const lines = text.split(/\r?\n/);
    return { lines: lines.slice(-want), size: st.size, mtime: st.mtime.toISOString() };
  } catch (e) {
    return { error: String(e.message || e) };
  }
}

function bodySize(o) {
  try {
    return JSON.stringify(o).length;
  } catch (e) {
    return 0;
  }
}

function cleanView(payload, prev) {
  const p = payload && typeof payload === "object" ? payload : {};
  const base = prev || {};
  const prevBody = base.body && typeof base.body === "object" ? base.body : {};
  let kind = String(p.kind || base.kind || "text").toLowerCase().replace(/[^a-z]/g, "").slice(0, 16);
  if (!kind) kind = "text";
  const id = String(p.id || base.id || "").replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 32) ||
    "v" + crypto.randomBytes(4).toString("hex");
  let body = p.body !== undefined ? p.body : base.body;
  if (typeof body === "string") {
    try {
      body = JSON.parse(body);
    } catch (e) {
      body = { text: String(body).slice(0, 8000) };
    }
  }
  if (!body || typeof body !== "object" || Array.isArray(body)) body = {};
  if (bodySize(body) > 65536) body = { text: JSON.stringify(body).slice(0, 60000), truncated: true };
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
    out.body = body;
  }
  const src = p.source && typeof p.source === "object" ? p.source : base.source;
  if (src && src.path) {
    out.source = { path: String(src.path).slice(0, 400), lines: Math.max(1, Math.min(400, Number(src.lines) || 40)) };
    if (out.kind === "log" && !out.body.path) out.body.path = out.source.path;
  }
  if (!out.title) out.title = out.kind;
  return out;
}

const syncTask = (t) => ({
  Id: String((t && (t.Id || t.id)) || "").slice(0, 40),
  Title: String((t && (t.Title || t.title)) || "").slice(0, 300),
  Note: String((t && (t.Note || t.note)) || "").slice(0, 2000),
  Mode: ["auto", "task", "night"].includes(String(t && (t.Mode || t.mode))) ? String(t.Mode || t.mode) : "task",
  Status: ["todo", "doing", "blocked", "done"].includes(String(t && (t.Status || t.status)))
    ? String(t.Status || t.status) : "todo",
  Progress: Math.max(0, Math.min(100, Number(t && (t.Progress != null ? t.Progress : t.progress)) || 0)),
  Section: String((t && (t.Section || t.section)) || "").slice(0, 24),
  Priority: Math.max(1, Math.min(5, Number(t && (t.Priority != null ? t.Priority : t.priority)) || 3)),
  Source: String((t && (t.Source || t.source)) || "ai").slice(0, 16),
  Created: String((t && (t.Created || t.created)) || new Date().toISOString()).slice(0, 32),
  Updated: String((t && (t.Updated || t.updated)) || new Date().toISOString()).slice(0, 32),
});

function ownerSettings() {
  try {
    const file = path.join(process.env.APPDATA || "", "NexusBoard", "settings.json");
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
  } catch (e) {
    return null;
  }
}

function ownerBoard() {
  const st = ownerSettings();
  const id = st && String(st.Board || "");
  if (!id || !/^[a-f0-9]{16}$/.test(id)) return null;
  try {
    return JSON.parse(fs.readFileSync(path.join(__dirname, "boards", id + ".json"), "utf8"));
  } catch (e) {
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
        if (p) copy.tail = tailLines(p, (copy.source && copy.source.lines) || (copy.body && copy.body.lines) || 40);
      }
      return copy;
    })
    .sort((a, b) => (a.order || 0) - (b.order || 0));
}

const FINETUNE = process.env.NEXUS_FINETUNE || "D:\\finetune";

function tailText(file, bytes) {
  try {
    const st = fs.statSync(file);
    const len = Math.min(st.size, bytes);
    const buf = Buffer.alloc(len);
    const fd = fs.openSync(file, "r");
    fs.readSync(fd, buf, 0, len, st.size - len);
    fs.closeSync(fd);
    return { text: buf.toString("utf8"), mtime: st.mtimeMs, size: st.size };
  } catch {
    return { text: "", mtime: 0, size: 0 };
  }
}

function secs(hms) {
  const p = String(hms).split(":").map(Number);
  if (p.some(Number.isNaN)) return 0;
  if (p.length === 3) return p[0] * 3600 + p[1] * 60 + p[2];
  if (p.length === 2) return p[0] * 60 + p[1];
  return p[0] || 0;
}

function readTrain() {
  const logPath = path.join(FINETUNE, "runs", "logs", "marathon.log");
  const chainPath = path.join(FINETUNE, "runs", "logs", "chain_v6.log");
  const { text, mtime } = tailText(logPath, 400000);
  const lines = text.split(/[\r\n]+/).filter(Boolean);

  let step = null, total = null, elapsed = null, remain = null, rate = null;
  let stage = null, unit = null;
  for (let i = lines.length - 1; i >= 0; i--) {
    const l = lines[i];
    if (l.includes("Tokenizing")) {
      const m = l.match(/(\d+)\/(\d+) \[([\d:]+)<([\d:]+),\s*([\d.]+)\s*examples\/s\]/);
      if (m) {
        stage = "tokenization";
        unit = "примеров";
        step = Number(m[1]); total = Number(m[2]);
        elapsed = m[3]; remain = m[4];
        rate = m[5] + " прим/с";
        break;
      }
      continue;
    }
    const m = l.match(/(\d+)\/(\d+) \[([\d:]+)<([\d:]+),\s*([\d.]+)\s*s\/it\]/);
    if (m) {
      stage = "training";
      unit = "шагов";
      step = Number(m[1]); total = Number(m[2]);
      elapsed = m[3]; remain = m[4];
      rate = m[5] + " с/шаг";
      break;
    }
  }
  if (!stage && lines.some(l => l.includes("марафон"))) stage = "start";
  if (!stage) stage = "build";

  const losses = [];
  for (const l of lines) {
    const m = l.match(/['"]?eval_loss['"]?\s*:\s*['"]?([\d.]+)/);
    if (m) losses.push(Number(m[1]));
  }

  let run = null, runAt = 0;
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

  let phase = null, phaseAt = null, chainLines = [];
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
  const pct = step && total ? Math.round((step / total) * 1000) / 10 : null;

  return {
    ok: true,
    alive,
    stage,
    unit,
    run,
    step,
    total,
    pct,
    elapsed,
    remain,
    remainSeconds: remain ? secs(remain) : null,
    rate,
    loss: losses.length ? losses[losses.length - 1] : null,
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

function json(res, code, body) {
  const buf = Buffer.from(JSON.stringify(body), "utf8");
  res.writeHead(code, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    "Content-Length": buf.length,
  });
  res.end(buf);
}

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

function readBody(req, limit) {
  return new Promise((resolve) => {
    let size = 0;
    const chunks = [];
    req.on("data", (c) => {
      size += c.length;
      if (size > (limit || 65536)) {
        resolve("");
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", () => resolve(""));
  });
}

const server = http.createServer((req, res) => {
  const parsed = new URL(req.url, `http://${req.headers.host || HOST}`);
  const url = parsed.pathname;
  const host = String(req.headers.host || "").toLowerCase();
  const trainHost = host.startsWith("train.");
  if (url !== "/api/health" && !url.startsWith("/api/b/") && !url.startsWith("/api/c/") && !url.startsWith("/api/app/") && !url.startsWith("/app/") && !allowed(req, parsed.searchParams)) {
    res.writeHead(401, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
    return res.end(gatePage());
  }
  if (url !== "/api/health" && parsed.searchParams.get("k") === TOKEN && TOKEN && cookieValue(req, "nxt") !== TOKEN) {
    res.writeHead(302, {
      "Set-Cookie": `nxt=${TOKEN}; Path=/; Max-Age=31536000; SameSite=Lax`,
      Location: req.url,
    });
    return res.end();
  }
  if (url.startsWith("/hw/")) {
    const name = decodeURIComponent(url.slice("/hw/".length));
    if (!/^[A-Za-z0-9._-]+\.png$/.test(name)) {
      res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
      return res.end("not found");
    }
    const img = path.join(__dirname, "hw", name);
    let stat = null;
    try {
      stat = fs.statSync(img);
    } catch (e) {
    }
    if (!stat) {
      res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
      return res.end("not found");
    }
    res.writeHead(200, {
      "Content-Type": "image/png",
      "Content-Length": stat.size,
      "Cache-Control": "no-store",
    });
    return fs.createReadStream(img).pipe(res);
  }

  // ---------- личные доски: без аккаунтов и ключей ----------
  // каждая установка приложения получает свой id и пустую доску,
  // заполняет её свой ИИ через простой POST
  if (url.startsWith("/api/b/")) {
    const body = () => readBody(req);
    const bid = (parsed.searchParams.get("b") || req.headers["x-board"] || "").toString();
    try {
      fs.appendFileSync(path.join(__dirname, "logs", "board-hits.log"),
        [new Date().toISOString(), req.method, url, "board=" + bid,
          "ua=" + String(req.headers["user-agent"] || "").slice(0, 60)].join(" ") + "\n");
    } catch (e) {}
    const okId = /^[a-f0-9]{16}$/.test(bid);
    const store = () => path.join(__dirname, "boards");
    const file = (id) => path.join(store(), (id || bid) + ".json");

    const load = (id) => {
      try {
        return JSON.parse(fs.readFileSync(file(id), "utf8"));
      } catch (e) {
        return { id: id || bid, created: new Date().toISOString(), updated: "", tasks: [] };
      }
    };
    const save = (data) => {
      data.updated = new Date().toISOString();
      fs.mkdirSync(store(), { recursive: true });
      const target = file(data.id);
      const tmp = target + ".tmp";
      fs.writeFileSync(tmp, JSON.stringify(data, null, 2), "utf8");
      fs.renameSync(tmp, target);
    };

    if (url === "/api/b/new" && req.method === "POST") {
      const id = crypto.randomBytes(8).toString("hex");
      save({ id, created: new Date().toISOString(), updated: "", tasks: [], sections: [] });
      return json(res, 200, { ok: true, id });
    }

    if (!okId) return json(res, 400, { ok: false, error: "bad board id" });

    if (url === "/api/b/seed" && req.method === "POST") {
      const data = load();
      data.tasks = Array.isArray(data.tasks) ? data.tasks : [];
      if (data.tasks.length) return json(res, 200, { ok: true, seeded: false, tasks: data.tasks.length });
      let tpl = null;
      try {
        tpl = JSON.parse(fs.readFileSync(path.join(store(), "_template.json"), "utf8"));
      } catch (e) {}
      if (!tpl) return json(res, 200, { ok: true, seeded: false, tasks: 0 });
      data.tasks = (tpl.tasks || []).map((x) => Object.assign({}, x, { Id: "b" + Math.random().toString(16).slice(2, 8) }));
      data.sections = tpl.sections || [];
      save(data);
      return json(res, 200, { ok: true, seeded: true, tasks: data.tasks.length });
    }

    if (url === "/api/b/tasks" && req.method === "GET") {
      const data = load();
      return json(res, 200, {
        ok: true,
        id: data.id,
        updated: data.updated || "",
        tasks: data.tasks || [],
        sections: data.sections || [],
      });
    }

    if (url === "/api/b/section" && req.method === "GET") {
      const data = load();
      return json(res, 200, { ok: true, sections: data.sections || [] });
    }

    const viewsOf = (data) => {
      data.blocks = Array.isArray(data.blocks) ? data.blocks : [];
      return data.blocks;
    };
    const findView = (data, id) => viewsOf(data).find((b) => b && b.id === id);

    if (url === "/api/b/views" && req.method === "GET") {
      const data = load();
      const blocks = viewsOf(data).map((b) => {
        const copy = JSON.parse(JSON.stringify(b));
        if (copy.kind === "log") {
          const p = (copy.source && copy.source.path) || (copy.body && copy.body.path);
          if (p) copy.tail = tailLines(p, (copy.source && copy.source.lines) || (copy.body && copy.body.lines) || 40);
        }
        return copy;
      });
      blocks.sort((a, b) => (a.order || 0) - (b.order || 0));
      const version = crypto.createHash("sha1").update(JSON.stringify(blocks)).digest("hex").slice(0, 12);
      return json(res, 200, { ok: true, version, blocks });
    }

    if (url === "/api/b/view/set" && req.method === "POST") {
      return body().then((raw) => {
        let payload = {};
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch (e) {
          return json(res, 400, { ok: false, error: "bad json" });
        }
        const data = load();
        const blocks = viewsOf(data);
        const id = String(payload.id || "").replace(/[^a-zA-Z0-9_-]/g, "");
        const prev = id ? findView(data, id) : null;
        if (!prev && blocks.length >= 40) return json(res, 400, { ok: false, error: "too many blocks" });
        const block = cleanView(payload, prev);
        if (prev) {
          const i = blocks.indexOf(prev);
          blocks[i] = block;
        } else {
          blocks.push(block);
        }
        save(data);
        return json(res, 200, { ok: true, block });
      });
    }

    if (url === "/api/b/view/del" && req.method === "POST") {
      return body().then((raw) => {
        let payload = {};
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch (e) {
          return json(res, 400, { ok: false, error: "bad json" });
        }
        const data = load();
        const blocks = viewsOf(data);
        const id = String(payload.id || "");
        const before = blocks.length;
        data.blocks = blocks.filter((b) => !b || b.id !== id);
        if (data.blocks.length === before) return json(res, 404, { ok: false, error: "block not found" });
        save(data);
        return json(res, 200, { ok: true, blocks: data.blocks });
      });
    }

    if (url === "/api/b/sync" && req.method === "POST") {
      return body().then((raw) => {
        let payload = {};
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch (e) {
          return json(res, 400, { ok: false, error: "bad json" });
        }
        if (!Array.isArray(payload.tasks)) return json(res, 400, { ok: false, error: "tasks[] required" });
        const data = load();
        viewsOf(data);
        data.tasks = payload.tasks.slice(0, 400).map(syncTask).filter((t) => t.Title);
        if (Array.isArray(payload.sections)) {
          data.sections = payload.sections.slice(0, 20).map((sec, i) => ({
            id: String((sec && sec.id) || "s" + i).replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 24),
            name: String((sec && sec.name) || "").slice(0, 60),
            order: Number((sec && sec.order) != null ? sec.order : i) || i,
          })).filter((sec) => sec.name);
        }
        if (payload.mode) data.mode = String(payload.mode).slice(0, 12);
        save(data);
        return json(res, 200, {
          ok: true,
          tasks: data.tasks.length,
          sections: (data.sections || []).length,
          blocks: viewsOf(data).length,
          updated: data.updated,
        });
      });
    }

    if (url === "/api/b/code" && req.method === "GET") {
      const data = load();
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
      return body().then((raw) => {
        let payload = {};
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch (e) {
          return json(res, 400, { ok: false, error: "bad json" });
        }
        const data = load();
        const id = String(payload.id || "");
        const b = findView(data, id);
        if (!b || !b.body || typeof b.body.content !== "string") {
          return json(res, 404, { ok: false, error: "code block not found" });
        }
        const current = Number(b.body.version) || 1;
        const currentSha = crypto.createHash("sha256").update(b.body.content).digest("hex").slice(0, 16);
        const ifVersion = payload.ifVersion != null ? Number(payload.ifVersion) : null;
        if (ifVersion != null && ifVersion !== current) {
          return json(res, 409, {
            ok: false,
            error: "conflict",
            version: current,
            sha: currentSha,
            content: b.body.content,
          });
        }
        const content = String(payload.content == null ? "" : payload.content).slice(0, 200000);
        if (!content.trim()) return json(res, 400, { ok: false, error: "content required" });
        const sha = crypto.createHash("sha256").update(content).digest("hex").slice(0, 16);
        const version = current + 1;
        b.body.history = (Array.isArray(b.body.history) ? b.body.history : [])
          .concat([{ version: current, sha: currentSha, ts: new Date().toISOString() }])
          .slice(-5);
        b.body.content = content;
        b.body.version = version;
        b.body.sha = sha;
        if (payload.title) b.title = String(payload.title).slice(0, 80);
        b.updated = new Date().toISOString();
        save(data);
        return json(res, 200, { ok: true, version, sha, updated: b.updated });
      });
    }

    if ((url === "/api/b/add" || url === "/api/b/update" || url === "/api/b/clear" ||
         url === "/api/b/section/add" || url === "/api/b/section/del") && req.method === "POST") {
      return body().then((raw) => {
        let payload = {};
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch (e) {
          return json(res, 400, { ok: false, error: "bad json" });
        }
        const data = load();
        data.tasks = Array.isArray(data.tasks) ? data.tasks : [];
        data.sections = Array.isArray(data.sections) ? data.sections : [];

        if (url === "/api/b/section/add") {
          if (data.sections.length >= 20) return json(res, 400, { ok: false, error: "too many sections" });
          const name = String(payload.name || "").trim().slice(0, 60);
          if (!name) return json(res, 400, { ok: false, error: "name required" });
          const sid = String(payload.id || "").trim().replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 24) ||
            "s" + crypto.randomBytes(3).toString("hex");
          if (data.sections.some((s) => s.id === sid)) return json(res, 409, { ok: false, error: "section exists" });
          const sec = { id: sid, name, order: data.sections.length };
          data.sections.push(sec);
          save(data);
          return json(res, 200, { ok: true, section: sec, sections: data.sections });
        }

        if (url === "/api/b/section/del") {
          const sid = String(payload.id || "").trim();
          const before = data.sections.length;
          data.sections = data.sections.filter((s) => s.id !== sid);
          if (data.sections.length === before) return json(res, 404, { ok: false, error: "section not found" });
          data.tasks.forEach((x) => {
            if (x.Section === sid) x.Section = "";
          });
          save(data);
          return json(res, 200, { ok: true, sections: data.sections });
        }

        if (url === "/api/b/clear") {
          data.tasks = [];
          save(data);
          return json(res, 200, { ok: true, tasks: 0 });
        }

        if (url === "/api/b/add") {
          if (data.tasks.length >= 200) return json(res, 400, { ok: false, error: "board full" });
          const title = String(payload.title || "").trim().slice(0, 300);
          if (!title) return json(res, 400, { ok: false, error: "title required" });
          const task = {
            Id: "b" + (data.tasks.length + 1) + "-" + crypto.randomBytes(3).toString("hex"),
            Title: title,
            Note: String(payload.note || "").slice(0, 2000),
            Mode: ["auto", "task", "night"].includes(String(payload.mode)) ? String(payload.mode) : "task",
            Status: ["todo", "doing", "blocked", "done"].includes(String(payload.status)) ? String(payload.status) : "todo",
            Progress: Math.max(0, Math.min(100, Number(payload.progress) || 0)),
            Section: String(payload.section || payload.Section || "").slice(0, 24),
            Created: new Date().toISOString(),
            Updated: new Date().toISOString(),
          };
          data.tasks.push(task);
          save(data);
          return json(res, 200, { ok: true, task });
        }

        const id = String(payload.id || payload.Id || "");
        const task = data.tasks.find((x) => x.Id === id);
        if (!task) return json(res, 404, { ok: false, error: "task not found" });
        if (payload.title != null) task.Title = String(payload.title).slice(0, 300);
        if (payload.note != null) task.Note = String(payload.note).slice(0, 2000);
        if (payload.status != null && ["todo", "doing", "blocked", "done"].includes(String(payload.status))) {
          task.Status = String(payload.status);
        }
        if (payload.progress != null) task.Progress = Math.max(0, Math.min(100, Number(payload.progress) || 0));
        if (payload.section != null || payload.Section != null) {
          task.Section = String(payload.section != null ? payload.section : payload.Section).slice(0, 24);
        }
        task.Updated = new Date().toISOString();
        save(data);
        return json(res, 200, { ok: true, task });
      });
    }

    return json(res, 404, { ok: false, error: "unknown board endpoint" });
  }

  if (url.startsWith("/api/c/")) {
    const CHAT = path.join(__dirname, "chat");
    const queryId = (parsed.searchParams.get("board") || "").toString();
    const file = (id) => path.join(CHAT, id + ".json");
    const loadChat = (id) => {
      try {
        const d = JSON.parse(fs.readFileSync(file(id), "utf8"));
        d.messages = Array.isArray(d.messages) ? d.messages : [];
        return d;
      } catch (e) {
        return { board: id, messages: [] };
      }
    };
    const saveChat = (data) => {
      fs.mkdirSync(CHAT, { recursive: true });
      data.updated = new Date().toISOString();
      const target = file(data.board);
      const tmp = target + ".tmp";
      fs.writeFileSync(tmp, JSON.stringify(data), "utf8");
      fs.renameSync(tmp, target);
    };
    const pushMessage = (id, from, text) => {
      const data = loadChat(id);
      const msg = {
        id: crypto.randomBytes(8).toString("hex"),
        from: from === "ai" ? "ai" : "owner",
        text,
        ts: Date.now(),
      };
      data.messages.push(msg);
      if (data.messages.length > 500) data.messages = data.messages.slice(-500);
      saveChat(data);
      return msg;
    };

    if (url === "/api/c/send" && req.method === "POST") {
      return readBody(req).then((raw) => {
        let payload = {};
        try {
          payload = raw ? JSON.parse(raw) : {};
        } catch (e) {
          return json(res, 400, { ok: false, error: "bad json" });
        }
        const id = String(payload.board || queryId || "").trim();
        if (!/^[a-f0-9]{16}$/.test(id)) return json(res, 400, { ok: false, error: "bad board id" });
        const text = String(payload.text == null ? "" : payload.text).trim().slice(0, 4000);
        if (!text) return json(res, 400, { ok: false, error: "text required" });
        const msg = pushMessage(id, payload.from, text);
        return json(res, 200, { ok: true, id: msg.id, ts: msg.ts });
      });
    }

    if (url === "/api/c/list" && req.method === "GET") {
      if (!/^[a-f0-9]{16}$/.test(queryId)) return json(res, 400, { ok: false, error: "bad board id" });
      const data = loadChat(queryId);
      const since = Number(parsed.searchParams.get("since") || 0) || 0;
      const limit = Math.max(1, Math.min(500, Number(parsed.searchParams.get("limit") || 200) || 200));
      let list = since > 0 ? data.messages.filter((m) => m.ts > since) : data.messages;
      if (list.length > limit) list = list.slice(-limit);
      const last = data.messages.length ? data.messages[data.messages.length - 1].ts : 0;
      return json(res, 200, { ok: true, messages: list, last, count: data.messages.length });
    }

    return json(res, 404, { ok: false, error: "unknown chat endpoint" });
  }

  if (url === "/api/app/latest") {
    const file = path.join(__dirname, "app", "latest.json");
    let body = { ok: false };
    try {
      body = JSON.parse(fs.readFileSync(file, "utf8"));
      body.ok = true;
    } catch (e) {
    }
    return json(res, 200, body);
  }

  if (url === "/api/app/pc") {
    const file = path.join(__dirname, "app", "pc", "latest.json");
    let body = { ok: false };
    try {
      body = JSON.parse(fs.readFileSync(file, "utf8"));
      body.ok = true;
    } catch (e) {
    }
    return json(res, 200, body);
  }

  if (url.startsWith("/app/")) {
    const name = decodeURIComponent(url.slice("/app/".length));
    if (!/^(pc\/)?[A-Za-z0-9._-]+\.(apk|exe|zip)$/.test(name)) {
      res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
      return res.end("not found");
    }
    const apk = path.join(__dirname, "app", name);
    let stat = null;
    try {
      stat = fs.statSync(apk);
    } catch (e) {
    }
    if (!stat) {
      res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" });
      return res.end("not found");
    }
    res.writeHead(200, {
      "Content-Type": "application/vnd.android.package-archive",
      "Content-Length": stat.size,
      "Content-Disposition": `attachment; filename="${name}"`,
      "Cache-Control": "no-store",
    });
    return fs.createReadStream(apk).pipe(res);
  }

  if (trainHost && url === "/") {
    try {
      const buf = fs.readFileSync(path.join(__dirname, "train.html"));
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
      return res.end(buf);
    } catch (e) {
      return json(res, 500, { ok: false, error: String(e.message || e) });
    }
  }
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
  if (url === "/train" || url === "/train.html") {
    try {
      const buf = fs.readFileSync(path.join(__dirname, "train.html"));
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
      return res.end(buf);
    } catch (e) {
      return json(res, 500, { ok: false, error: String(e.message || e) });
    }
  }
  if (url === "/api/health") return json(res, 200, { ok: true, port: PORT });
  if (url === "/" || url === "/index.html") {
    try {
      const buf = fs.readFileSync(INDEX);
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
      return res.end(buf);
    } catch (e) {
      return json(res, 500, { ok: false, error: String(e.message || e) });
    }
  }
  json(res, 404, { ok: false, error: "not found" });
});

server.listen(PORT, HOST, () => {
  console.log(`Nexus board site: http://${HOST}:${PORT}  (данные: ${TASKS})`);
  if (process.argv.includes("--open")) {
    execFile("cmd", ["/c", "start", "", `http://${HOST}:${PORT}`], { windowsHide: true }, () => {});
  }
});

server.on("error", (e) => {
  console.error("порт занят или недоступен:", e.message);
  process.exit(1);
});

const PROXY_PORT = Number(process.env.NEXUS_PROXY_PORT || 8789);
const PROXY_TARGET = process.env.NEXUS_PROXY_TARGET || "http://127.0.0.1:8790";

const proxy = http.createServer((req, res) => {
  const parsed = new URL(req.url, `http://${req.headers.host || HOST}`);
  if (!allowed(req, parsed.searchParams)) {
    res.writeHead(401, { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" });
    return res.end(gatePage());
  }
  if (TOKEN && parsed.searchParams.get("k") === TOKEN) {
    res.writeHead(302, {
      "Set-Cookie": `nxt=${TOKEN}; Path=/; Max-Age=31536000; SameSite=Lax`,
      Location: parsed.pathname,
    });
    return res.end();
  }
  const up = new URL(PROXY_TARGET);
  const opts = {
    hostname: up.hostname,
    port: up.port || 80,
    path: req.url,
    method: req.method,
    headers: Object.assign({}, req.headers, { host: up.host }),
  };
  const preq = http.request(opts, (pres) => {
    res.writeHead(pres.statusCode || 502, pres.headers);
    pres.pipe(res);
  });
  preq.on("error", (e) => {
    res.writeHead(502, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("прогресс-хаб не отвечает: " + e.message);
  });
  req.pipe(preq);
});

proxy.listen(PROXY_PORT, HOST, () => {
  console.log(`прогресс обучения через ключ: http://${HOST}:${PROXY_PORT} → ${PROXY_TARGET}`);
});
