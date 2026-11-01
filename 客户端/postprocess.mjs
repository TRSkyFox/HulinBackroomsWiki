// postprocess.mjs — 把一場 wiki.jsonl 拆成「局 → 樓層 → 事件」
// 用法：node postprocess.mjs <session-folder>
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const dir = process.argv[2];
if (!dir) { console.error('用法：node postprocess.mjs <session-folder>'); process.exit(1); }
const progressPath = path.join(dir, 'progress.json');
function writeProgress(stage, percent, detail) {
  try {
    const tmp = progressPath + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify({ stage, percent, detail, ts: Date.now() }) + '\n', 'utf8');
    fs.renameSync(tmp, progressPath);
  } catch (e) {}
}
writeProgress('postprocess', 5, '讀取 wiki.jsonl');
const events = fs.readFileSync(path.join(dir, 'wiki.jsonl'), 'utf8').trim().split(/\r?\n/).filter(Boolean).map(JSON.parse);

const isStart = e => /开始冒险|開始冒險/.test(e.v || '') && /1\s*\/\s*1/.test(e.v || '');
const starts = [];
for (let i = 0; i < events.length; i++) if (isStart(events[i])) starts.push(i);
// 保留真正進入遊戲的起點：兩個起點間至少 20 秒或有樓層事件
const chosen = [];
for (const i of starts) {
  if (!chosen.length) { chosen.push(i); continue; }
  const prev = events[chosen[chosen.length - 1]];
  if (i - chosen[chosen.length - 1] > 20) chosen.push(i);
}
if (!chosen.length) chosen.push(0);

const floorRe = /^\s*Level\s*([0-9]+(?:\.[0-9]+)?)\s*[「『]?\s*(.*)$/i;
const canonical = { '0': '大廳', '4': '廢棄辦公室', '10': '豐收麥田', '13': '玻璃之狂', '35': '第零天', '87': '脫水' };

function parseFloor(v) {
  const m = floorRe.exec(v || '');
  if (!m) return null;
  let name = (m[2] || '').replace(/[」』]/g, '').replace(/[「『].*$/, '').trim();
  const id = m[1];
  if (canonical[id]) name = canonical[id];
  if (!name) name = 'Level ' + id;
  return { id, name };
}

const outRoot = path.join(dir, 'clean');
fs.rmSync(outRoot, { recursive: true, force: true });
fs.mkdirSync(outRoot, { recursive: true });

const summary = { session: path.basename(dir), runs: [] };
for (let r = 0; r < chosen.length; r++) {
  const from = chosen[r];
  const to = (r + 1 < chosen.length) ? chosen[r + 1] : events.length;
  let run = events.slice(from, to);
  // 只從第一個樓層標題開始記錄，避免登入／主選單／建房畫面混進來
  const firstFloor = run.findIndex(e => e.k === 'H' && parseFloor(e.v));
  if (firstFloor > 0) run = run.slice(Math.max(0, firstFloor - 3));
  let cur = { id: '0', name: '大廳' };
  const floors = new Map();
  const order = [];
  let started = false;

  for (const e of run) {
    if (e.k === 'H') {
      const f = parseFloor(e.v);
      if (f) {
        // 只認 Level N 標題；「第 N 層」是場景/區域，不是樓層
        cur = f; started = true;
      }
    }
    if (!started && !['N', 'H'].includes(e.k)) continue;
    if (!floors.has(cur.id)) {
      floors.set(cur.id, { id: cur.id, name: cur.name, events: [] });
      order.push(cur.id);
    }
    floors.get(cur.id).events.push(e);
  }

  const runDir = path.join(outRoot, 'run-' + String(r + 1).padStart(2, '0'));
  fs.mkdirSync(runDir, { recursive: true });
  const files = [];
  for (const id of order) {
    const f = floors.get(id);
    const safe = id.replace(/[\\/:*?"<>|]/g, '_');
    const file = path.join(runDir, safe + '-' + f.name.replace(/[\\/:*?"<>|]/g, '_') + '.md');
    const lines = ['# 第 ' + (r + 1) + ' 局／Level ' + id + '「' + f.name + '」', ''];
    for (const e of f.events) lines.push('[' + e.t + '] ' + e.k + ' ' + e.v);
    fs.writeFileSync(file, lines.join('\n') + '\n', 'utf8');
    files.push({ id, name: f.name, events: f.events.length, file: path.relative(outRoot, file) });
  }
  summary.runs.push({ run: r + 1, start: run[0] && run[0].t, floors: files });
}

fs.writeFileSync(path.join(outRoot, 'session.json'), JSON.stringify(summary, null, 2) + '\n', 'utf8');
writeProgress('postprocess', 30, '樓層拆分完成');
console.log(JSON.stringify(summary, null, 2));

// 有 structured.jsonl 就自動跑結構化解析＋Wiki staging（不直接改正式 content）
try {
  const here = path.dirname(fileURLToPath(import.meta.url));
  /* 重要：Wiki 主人這台機器上，優先跑 repo 裡的管線。
     可攜版是「啟動時把 app/ 複製到 %LOCALAPPDATA%」，如果客戶端早就開著，
     它手上那份複製就是舊的 —— 2026-10-29 20:13 就是這樣：記錄器跑的是改動前的
     管線，把已經清乾淨的 content 又寫回帶玩家名、帶 UI 的版本。
     repo 在的話一律用 repo，複製過不過期都不影響。 */
  const repoRun = 'D:/BACKROOMS/记录器/structured/run.mjs';
  const localRun = path.join(here, 'structured', 'run.mjs');
  const structuredRun = fs.existsSync(repoRun) ? repoRun : localRun;
  const sf = path.join(dir, 'structured.jsonl');
  if (fs.existsSync(structuredRun) && fs.existsSync(sf)) {
    writeProgress('structured', 35, '結構化管線');
    console.log('pipeline: ' + structuredRun);
    const r = spawnSync(process.execPath, [structuredRun, dir, '--auto'], { stdio: 'inherit' });
    if (r.status !== 0) console.error('structured pipeline exit ' + r.status);
    writeProgress('structured', 100, '完成');
  }
  if (!fs.existsSync(sf)) writeProgress('postprocess', 100, '完成');
} catch (e) { console.error('structured pipeline failed: ' + e.message); }
