// ocr-worker.mjs — 錄製期間在背景把 linecrops 用 RapidOCR 重認一次。
//
// 為什麼要有這支：rapid-ocr.exe 是 PyInstaller 單檔包（119MB），每次啟動要解包模型，
// 1~3 秒起跳。原本的做法是收工後一次把所有裁圖丟給它 —— 11 分鐘的局要等 87 秒。
// 現在改成邊錄邊認：裁圖一出現就被這裡吃掉，收工時 font-correct 只要套結果，不用再等。
//
// 用法：node ocr-worker.mjs <sessionDir>
//   <sessionDir>/linecrops/*.png        記錄器寫的裁圖（已按內容 hash 去重、檔名就是 hash）
//   <sessionDir>/rapid.jsonl            本程式輸出：{name,text,score,ms}
//   <sessionDir>/ocr-stop.flag          收工時由記錄器建立 → 清空佇列後寫 ocr-done.ack 結束
//   <sessionDir>/ocr-crash.log          出錯時記錄（不擋錄製）

import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const session = process.argv[2] ? path.resolve(process.argv[2]) : '';
if (!session) { console.error('usage: node ocr-worker.mjs <sessionDir>'); process.exit(2); }

const cropDir = path.join(session, 'linecrops');
const outPath = path.join(session, 'rapid.jsonl');
const stopFlag = path.join(session, 'ocr-stop.flag');
const ackFile = path.join(session, 'ocr-done.ack');
const crashLog = path.join(session, 'ocr-crash.log');
const batchDir = path.join(session, '.ocrbatch');
const tmpDir = path.join(session, '.ocrtmp');

const IDLE_MS = 1500;      // 佇列靜止這麼久才送出（避免一有圖就啟動程式）
const MAX_BATCH = 200;     // 一次最多幾張
const TICK_MS = 400;

function findRapid() {
  const cands = [
    path.join(HERE, 'bin', 'rapid-ocr.exe'),
    path.resolve(HERE, '..', '..', '客户端', 'dist', 'Expedition', 'structured', 'bin', 'rapid-ocr.exe'),
    path.join(process.env.LOCALAPPDATA || '', 'Expedition', 'app', 'structured', 'bin', 'rapid-ocr.exe'),
  ];
  return cands.find(p => p && fs.existsSync(p)) || null;
}
const exe = process.env.RAPID_OCR_EXE || findRapid();
if (!exe || !fs.existsSync(exe)) { console.error('no rapid-ocr.exe'); process.exit(3); }
/* 允許用 .cmd/.bat 墊片（測試用）：Windows 上必須走 shell 才執行得起來。 */
const useShell = /\.(cmd|bat)$/i.test(exe);

const done = new Set();
if (fs.existsSync(outPath)) {
  for (const ln of fs.readFileSync(outPath, 'utf8').split(/\r?\n/)) {
    if (!ln) continue;
    try { const o = JSON.parse(ln); if (o && o.name) done.add(o.name); } catch { }
  }
}

let lastSeen = 0, lastCount = -1, busy = false;
function logCrash(msg) {
  try { fs.appendFileSync(crashLog, '[' + new Date().toISOString() + '] ' + msg + '\n', 'utf8'); } catch { }
}
function listPending() {
  if (!fs.existsSync(cropDir)) return [];
  let names;
  try { names = fs.readdirSync(cropDir); } catch { return []; }
  return names.filter(n => n.toLowerCase().endsWith('.png') && !done.has(n));
}
function runBatch(names) {
  fs.rmSync(batchDir, { recursive: true, force: true });
  fs.mkdirSync(batchDir, { recursive: true });
  fs.mkdirSync(tmpDir, { recursive: true });
  for (const n of names) {
    try { fs.copyFileSync(path.join(cropDir, n), path.join(batchDir, n)); } catch { }
  }
  const out = path.join(session, '.ocrbatch.json');
  const r = spawnSync(exe, [batchDir, out], {
    stdio: 'ignore',
    shell: useShell,
    windowsHide: true,
    timeout: 10 * 60 * 1000,
    env: { ...process.env, TMP: tmpDir, TEMP: tmpDir },
  });
  if (r.error || !fs.existsSync(out)) { logCrash('batch failed: ' + (r.error ? String(r.error) : 'no output')); return 0; }
  let map;
  try { map = JSON.parse(fs.readFileSync(out, 'utf8')); } catch (e) { logCrash('bad json ' + e.message); return 0; }
  const rows = [];
  for (const n of names) {
    const hit = map[n];
    if (!hit) continue;
    rows.push(JSON.stringify({ name: n, text: hit.text || '', score: hit.score || 0, ms: hit.ms || 0 }));
    done.add(n);
    /* 認過的裁圖就刪掉 —— 這是「幾乎不佔空間」的關鍵。
       分數太低或空字串也刪：留著只會被下次再認一次，結果一樣。 */
    try { fs.rmSync(path.join(cropDir, n), { force: true }); } catch { }
  }
  if (rows.length) fs.appendFileSync(outPath, rows.join('\n') + '\n', 'utf8');
  try { fs.rmSync(out, { force: true }); } catch { }
  fs.rmSync(batchDir, { recursive: true, force: true });
  return rows.length;
}

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

(async function main() {
  fs.mkdirSync(cropDir, { recursive: true });
  for (;;) {
    const pending = listPending();
    if (pending.length !== lastCount) { lastCount = pending.length; lastSeen = Date.now(); }
    const idle = Date.now() - lastSeen;
    if (!busy && pending.length && (pending.length >= MAX_BATCH || idle >= IDLE_MS)) {
      busy = true;
      try { runBatch(pending.slice(0, MAX_BATCH)); } catch (e) { logCrash('runBatch ' + e.message); }
      busy = false;
      lastCount = -1;
      continue;
    }
    if (fs.existsSync(stopFlag) && !pending.length && !busy) {
      try { fs.writeFileSync(ackFile, String(Date.now()), 'utf8'); } catch { }
      return;
    }
    await sleep(TICK_MS);
  }
})();
