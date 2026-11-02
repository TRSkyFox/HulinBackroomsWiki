// font-correct.mjs — 用 RapidOCR 的辨識結果替換 Windows OCR 的行文字。
//
// 2026-10-29 改版：改成「邊錄邊認」。
//   - 錄製時 ocr-worker.mjs 會在背景把新出現的裁圖丟給 RapidOCR，結果寫進 rapid.jsonl
//   - 收工時這裡只負責「套用」，通常 1~2 秒結束，不用再等 87 秒
//   - 如果 worker 沒跑（舊場次、可攜版沒帶 worker），退回原本的批次路徑
//
// 裁圖檔名 = 內容的 SHA1 前 16 碼（記錄器負責去重），所以同名必定同圖。
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fixOcrText } from './ocr-fix.mjs';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));

// 舊 font-match 的安全替換規則；保留給回歸測試。
const SAFE = new Map([['夭', '天'], ['潦', '流'], ['麵', '面'], ['証', '證']]);
export function safeCorrection(a, b) {
  const x = [...String(a)], y = [...String(b)];
  if (x.length !== y.length) return null;
  for (let i = 0; i < x.length; i++) {
    if (x[i] === y[i]) continue;
    if (SAFE.get(x[i]) !== y[i]) return null;
  }
  return String(b);
}

function findRapid() {
  const cands = [
    path.join(HERE, 'bin', 'rapid-ocr.exe'),
    path.resolve(HERE, '..', '..', '客户端', 'dist', 'Expedition', 'structured', 'bin', 'rapid-ocr.exe'),
    path.join(process.env.LOCALAPPDATA || '', 'Expedition', 'app', 'structured', 'bin', 'rapid-ocr.exe'),
    path.join(process.env.LOCALAPPDATA || '', 'Expedition', 'app', 'structured', 'bin', 'rapid-ocr.exe'),
  ];
  return cands.find(p => p && fs.existsSync(p)) || null;
}

function runRapid(exe, cropDir, session) {
  const out = path.resolve(session, '.rapid-out.json');
  const tmp = path.resolve(session, '.rapid-tmp');
  fs.mkdirSync(tmp, { recursive: true });
  const r = spawnSync(exe, [path.resolve(cropDir), out], {
    stdio: ['ignore', 'pipe', 'pipe'],
    timeout: 30 * 60 * 1000,
    windowsHide: true,
    env: { ...process.env, TMP: tmp, TEMP: tmp },
  });
  if (r.error || r.status !== 0 || !fs.existsSync(out)) {
    return { error: r.error ? String(r.error) : `exit ${r.status}`, stderr: String(r.stderr || '').slice(0, 500) };
  }
  try { return { map: JSON.parse(fs.readFileSync(out, 'utf8')) }; }
  catch (e) { return { error: 'bad json ' + e.message }; }
  finally {
    try { fs.rmSync(out, { force: true }); } catch { }
    try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { }
  }
}

/* 讀背景 worker 已經認好的結果（rapid.jsonl，一行一筆）。 */
function loadWorkerResults(session) {
  const p = path.join(session, 'rapid.jsonl');
  const m = new Map();
  if (!fs.existsSync(p)) return m;
  for (const ln of fs.readFileSync(p, 'utf8').split(/\r?\n/)) {
    if (!ln) continue;
    try {
      const o = JSON.parse(ln);
      if (o && o.name) m.set(o.name, { text: o.text || '', score: Number(o.score) || 0 });
    } catch { }
  }
  return m;
}

export function correctSession(session) {
  const sf = path.join(session, 'structured.jsonl');
  const cropDir = path.join(session, 'linecrops');
  if (!fs.existsSync(sf)) return { skipped: 'no structured', checked: 0, changed: 0, failed: 0 };

  const raw = fs.readFileSync(sf, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).filter(Boolean);
  const refs = new Set();
  const cropText = new Map();
  const textKey = s => String(s || '').replace(/\s+/g, '').trim();
  for (const line of raw) {
    try {
      const f = JSON.parse(line);
      for (const l of f.lines || []) {
        if (l.c && l.t) {
          refs.add(l.c);
          if (!cropText.has(l.c)) cropText.set(l.c, l.t);
        }
      }
    } catch { }
  }
  if (!refs.size) return { skipped: 'no crop refs', checked: 0, changed: 0, failed: 0 };

  /* 1) 先拿背景 worker 的成果（正常情況這裡就 100% 有了） */
  const byName = loadWorkerResults(session);
  const fromWorker = byName.size;

  /* 2) worker 沒處理到的（沒裝、中途掛掉、舊場次）→ 退回批次路徑 */
  let legacyRan = false, legacyError = '';
  const pending = [...refs].filter(n => !byName.has(n) && fs.existsSync(path.join(cropDir, n)));
  if (pending.length) {
    const exe = findRapid();
    if (!exe) return { skipped: 'missing rapid-ocr.exe', checked: 0, changed: 0, failed: 0 };
    /* 文字級去重：同一段文字只送一張代表圖。 */
    const sent = new Map();   // textKey → 代表檔名
    for (const n of pending) {
      const k = textKey(cropText.get(n));
      if (k && !sent.has(k)) sent.set(k, n);
    }
    const tmpCrop = path.join(session, '.rapid-crops');
    fs.rmSync(tmpCrop, { recursive: true, force: true });
    fs.mkdirSync(tmpCrop, { recursive: true });
    for (const n of sent.values()) {
      try { fs.copyFileSync(path.join(cropDir, n), path.join(tmpCrop, n)); } catch { }
    }
    const rr = runRapid(exe, tmpCrop, session);
    fs.rmSync(tmpCrop, { recursive: true, force: true });
    if (rr.error) { legacyError = rr.error; }
    else {
      legacyRan = true;
      const map = rr.map || {};
      for (const n of pending) {
        const hit = map[n];
        if (hit) byName.set(n, { text: hit.text || '', score: Number(hit.score) || 0 });
      }
    }
  }

  /* 3) 套用 */
  const out = [];
  const textCache = new Map();
  let changed = 0, checked = 0, failed = 0, keptCrops = 0;
  for (const line of raw) {
    let f;
    try { f = JSON.parse(line); } catch { out.push(line); continue; }
    for (const l of f.lines || []) {
      if (!l.c || !l.t || !/[\u3400-\u9fff]/.test(l.t)) continue;
      keptCrops++;
      const key = textKey(l.t);
      let hit = textCache.get(key);
      if (hit === undefined) {
        hit = byName.get(l.c) || null;
        if (hit && !(hit.score >= 0.70)) hit = null;
        textCache.set(key, hit);
      }
      if (!hit) { failed++; continue; }
      checked++;
      let next = fixOcrText(String(hit.text || '').trim());
      if (!next || next === l.t) continue;
      // 太短/太長的識別結果不替換，避免把整行裁圖識別成半行
      if (next.length < Math.max(2, l.t.length * 0.45) || next.length > l.t.length * 1.8) { failed++; continue; }
      l.t0 = l.t;
      l.t = next;
      l.rapidScore = Math.round(hit.score * 1000) / 1000;
      changed++;
    }
    out.push(JSON.stringify(f));
  }
  fs.copyFileSync(sf, path.join(session, 'structured.ocr.jsonl'));
  fs.writeFileSync(sf, out.join('\n') + '\n', 'utf8');
  /* 收工：裁圖全部清掉（worker 正常時這裡本來就幾乎是空的）。 */
  if (keptCrops > 0 && process.env.KEEP_LINECROPS !== '1') {
    try { fs.rmSync(cropDir, { recursive: true, force: true }); } catch { }
  }
  return {
    checked, changed, failed, crops: keptCrops,
    workerResults: fromWorker, legacy: legacyRan, legacyError: legacyError || undefined,
    rapid: fromWorker > 0, ocrBackup: path.join(session, 'structured.ocr.jsonl')
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const session = process.argv[2];
  if (!session) { console.error('usage: node font-correct.mjs <sessionDir>'); process.exit(2); }
  console.log(JSON.stringify(correctSession(session), null, 2));
}
