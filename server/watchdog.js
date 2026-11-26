const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const http = require("node:http");
const net = require("node:net");
const { execSync, execFileSync, spawn } = require("node:child_process");

const ROOT = path.resolve(__dirname, "..");
const LOG = path.join(__dirname, "watchdog.log");
const PORT = Number(process.env.NEXUS_SITE_PORT || 8788);

// внешние пути — через env с прежними значениями по умолчанию, чтобы работало и не на этой машине
const BOARD_EXE = process.env.NEXUS_BOARD_EXE || path.join(ROOT, "pc", "NexusBoard.exe");
const WATCH_JS = process.env.NEXUS_MAX_WATCH_JS || path.join(os.homedir(), "mc-demo", "system", "max-watch.js");
const WATCH_PID = process.env.NEXUS_MAX_WATCH_PID || path.join(os.homedir(), "mc-demo", "logs", "max-watch.pid");
const CF = process.env.NEXUS_CF_BIN || "C:\\Program Files (x86)\\cloudflared\\cloudflared.exe";
const CF_CONFIG = process.env.NEXUS_CF_CONFIG || path.join(os.homedir(), ".cloudflared", "board.yml");

function log(line) {
  const row = `[${new Date().toISOString()}] ${String(line).replace(/[\r\n]+/g, " ")}\n`;
  try {
    fs.appendFileSync(LOG, row);
  } catch {}
  process.stdout.write(row);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function portBusy(port, timeout = 800) {
  return new Promise((resolve) => {
    const sock = net.connect({ host: "127.0.0.1", port });
    let done = false;
    const finish = (v) => {
      if (done) return;
      done = true;
      try {
        sock.destroy();
      } catch {}
      resolve(v);
    };
    sock.setTimeout(timeout);
    sock.on("connect", () => finish(true));
    sock.on("timeout", () => finish(false));
    sock.on("error", () => finish(false));
  });
}

// ждём, пока порт освободится после Stop-Process, иначе новый сервер упадёт на EADDRINUSE
async function waitPortFree(port, ms = 10000) {
  const started = Date.now();
  while (Date.now() - started < ms) {
    if (!(await portBusy(port))) return true;
    await sleep(300);
  }
  return !(await portBusy(port));
}

function health() {
  return new Promise((resolve) => {
    const req = http.get({ host: "127.0.0.1", port: PORT, path: "/api/health", timeout: 3000 }, (res) => {
      res.resume();
      // здоровым считаем только явный 200: 401 означает, что отвечает не то, что нужно
      resolve(res.statusCode === 200);
    });
    req.on("error", () => resolve(false));
    req.on("timeout", () => {
      req.destroy();
      resolve(false);
    });
  });
}

function ps(cmd) {
  try {
    return execSync(`powershell -NoProfile -Command "${cmd}"`, {
      encoding: "utf8",
      timeout: 20000,
      windowsHide: true,
    }).trim();
  } catch {
    return "";
  }
}

function tunnelAlive() {
  return ps(
    "(Get-CimInstance Win32_Process -Filter \\\"Name='cloudflared.exe'\\\" | Where-Object {$_.CommandLine -like '*nexus-board*'} | Measure-Object).Count",
  );
}

function startSite() {
  const p = spawn(process.execPath, [path.join(__dirname, "server.js")], {
    cwd: ROOT,
    detached: true,
    stdio: "ignore",
    windowsHide: true,
  });
  p.unref();
}

function startTunnel() {
  const p = spawn(CF, ["--config", CF_CONFIG, "--no-autoupdate", "tunnel", "run", "nexus-board"], {
    detached: true,
    stdio: "ignore",
    windowsHide: true,
  });
  p.unref();
}

function watcherAlive() {
  try {
    const pid = Number(fs.readFileSync(WATCH_PID, "utf8").trim());
    if (!pid) return false;
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function readToken() {
  try {
    return fs.readFileSync(path.join(__dirname, "token.txt"), "utf8").trim();
  } catch {
    return "";
  }
}

function fetchTrain() {
  return new Promise((resolve) => {
    const req = http.get(
      { host: "127.0.0.1", port: PORT, path: "/api/train", timeout: 8000, headers: { Cookie: "nxt=" + readToken() } },
      (res) => {
        let body = "";
        res.on("data", (c) => (body += c));
        res.on("end", () => {
          try {
            resolve(JSON.parse(body));
          } catch {
            resolve(null);
          }
        });
      },
    );
    req.on("error", () => resolve(null));
    req.on("timeout", () => {
      req.destroy();
      resolve(null);
    });
  });
}

function tailFile(file, bytes) {
  let fd = null;
  try {
    const st = fs.statSync(file);
    if (!st.isFile()) return "";
    const len = Math.min(st.size, bytes);
    const buf = Buffer.alloc(len);
    fd = fs.openSync(file, "r");
    fs.readSync(fd, buf, 0, len, st.size - len);
    let start = 0;
    if (st.size > len) while (start < 4 && start < buf.length && (buf[start] & 0xc0) === 0x80) start++;
    return buf.slice(start).toString("utf8");
  } catch {
    return "";
  } finally {
    if (fd !== null) {
      try {
        fs.closeSync(fd);
      } catch {}
    }
  }
}

// обрезка истории без чтения всего файла: хвост 8 МБ, атомарная замена через tmp+rename
function trimTrainHistory(file, maxLines = 5000) {
  try {
    const text = tailFile(file, 8 * 1024 * 1024);
    if (!text) return;
    const lines = text.split("\n").filter(Boolean);
    if (lines.length <= maxLines) return;
    const keep = lines.slice(-maxLines).join("\n") + "\n";
    const tmp = file + "." + process.pid + ".tmp";
    fs.writeFileSync(tmp, keep, "utf8");
    fs.renameSync(tmp, file);
  } catch (e) {
    log("историю обучения не обрезать: " + String(e.message).slice(0, 100));
  }
}

async function syncBoard() {
  const d = await fetchTrain();
  if (!d || !d.run || d.pct == null) return;
  const marker = path.join(__dirname, "train-sync.txt");
  const row = `${d.run}|${d.pct}|${d.step}`;
  let prev = "";
  try {
    prev = fs.readFileSync(marker, "utf8").trim();
  } catch {}
  if (prev === row) return;
  const note = `${d.run}: ${d.step}/${d.total} (${d.pct}%), осталось ${d.remain}, стадия ${d.stage}`;
  const hm = String(d.elapsed || "0:00:00").split(":").map(Number);
  const elapsedSec = hm.length === 3 ? hm[0] * 3600 + hm[1] * 60 + hm[2] : hm.length === 2 ? hm[0] * 60 + hm[1] : 0;
  const budget = 8 * 3600;
  const runName = d.run || "";
  let pctInt;
  if (runName.includes("ada-v6") && d.stage === "training") {
    // цепочка: шаг 1 (до 40 %) + сборка (40–60 %) + обучение v6 по бюджету (60–99 %)
    pctInt = Math.max(60, Math.min(99, Math.round(60 + (39 * elapsedSec) / budget)));
  } else if (runName.includes("ada-v6")) {
    pctInt = 45;
  } else {
    pctInt = Math.max(1, Math.min(40, Math.round((40 * (Number(d.pct) || 0)) / 100)));
  }
  try {
    execFileSync(BOARD_EXE, ["--progress", "t1", "--to", String(pctInt), "--note", note], {
      cwd: ROOT,
      timeout: 20000,
      windowsHide: true,
      env: Object.assign({}, process.env, { NEXUSBOARD_NO_UI: "1" }),
    });
    fs.writeFileSync(marker, row, "utf8");
    try {
      const hist = path.join(ROOT, "data", "train-history.ndjson");
      const point = {
        ts: new Date().toISOString(),
        run: d.run,
        stage: d.stage,
        step: d.step,
        total: d.total,
        loss: d.loss,
        rate: d.rate,
      };
      fs.appendFileSync(hist, JSON.stringify(point) + "\n", "utf8");
      trimTrainHistory(hist);
    } catch {}
    log("доска: t1 → " + note);

    const epochPct = d.total ? (100 * d.step) / d.total : 0;
    const budgetPct = Math.max(0, Math.min(100, Math.round((100 * elapsedSec) / budget)));
    const hours = (s) => (s / 3600).toFixed(1).replace(".", ",");
    const note23 = `шаг ${d.step}/${d.total} (${epochPct.toFixed(1).replace(".", ",")}% эпохи), loss ${d.loss == null ? "—" : d.loss}, ${d.rate}, бюджет ${hours(budget)} ч: прошло ${hours(elapsedSec)} ч, осталось ${hours(Math.max(0, budget - elapsedSec))} ч. Прогресс: train.nexus-your-heper.ru`;
    const marker23 = path.join(__dirname, "train-sync-t23.txt");
    const prev23 = (() => {
      try {
        return fs.readFileSync(marker23, "utf8").trim();
      } catch {
        return "";
      }
    })();
    const row23 = `${d.run}|${d.step}`;
    if (prev23 !== row23) {
      try {
        if (!prev23) {
          execFileSync(BOARD_EXE, ["--start", "t23", "--note", note23], {
            cwd: ROOT,
            timeout: 20000,
            windowsHide: true,
            env: Object.assign({}, process.env, { NEXUSBOARD_NO_UI: "1" }),
          });
        } else {
          execFileSync(BOARD_EXE, ["--progress", "t23", "--to", String(budgetPct), "--note", note23], {
            cwd: ROOT,
            timeout: 20000,
            windowsHide: true,
            env: Object.assign({}, process.env, { NEXUSBOARD_NO_UI: "1" }),
          });
        }
        fs.writeFileSync(marker23, row23, "utf8");
        log("доска: t23 → " + note23);
      } catch (e) {
        log("t23 не обновилась: " + String(e.message).slice(0, 90));
      }
    }
  } catch (e) {
    log("доска не обновилась: " + String(e.message).slice(0, 100));
  }
}

async function main() {
  const report = [];

  if (!(await health())) {
    if (await portBusy(PORT)) {
      log(`сайт слушает ${PORT}, но /api/health не ответил за 3 с — перезапускаю`);
      ps(`Get-NetTCPConnection -LocalPort ${PORT} -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }`);
      if (!(await waitPortFree(PORT, 10000))) log(`порт ${PORT} не освободился за 10 с — пробую поднять всё равно`);
    } else {
      log("сайт не запущен — поднимаю");
    }
    startSite();
    report.push("сайт: поднят");
  }

  if (Number(tunnelAlive()) === 0) {
    log("туннель nexus-board не найден — поднимаю");
    startTunnel();
    report.push("туннель: поднят");
  }

  if (!watcherAlive()) {
    log("наблюдатель MAX не найден — запускаю");
    try {
      execSync(`node "${WATCH_JS}" start`, { cwd: path.dirname(path.dirname(WATCH_JS)), timeout: 60000, windowsHide: true });
      report.push("MAX: наблюдатель поднят");
    } catch (e) {
      log("наблюдатель не поднялся: " + String(e.message).slice(0, 120));
    }
  }

  if (!report.length) log("всё живо");

  await syncBoard();
}

main().catch((e) => log("ошибка сторожа: " + String(e.message).slice(0, 200)));
