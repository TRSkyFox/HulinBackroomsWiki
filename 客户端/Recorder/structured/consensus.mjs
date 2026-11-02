import fs from 'node:fs';
import path from 'node:path';

/*
 * 多幀共識 OCR 逐字稿。
 * Windows OCR 對同一個畫面會在不同幀給出不同錯字；這裡用位置 + 出現次數投票，
 * 比單幀直接寫入穩定。字體/詞庫修正只套在明確的已知錯字上，不亂改玩家名。
 *
 * node consensus.mjs <sessionDir> [--window 5]
 */

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node consensus.mjs <sessionDir> [--window 5]'); process.exit(2); }
const session = args[0];
const wi = args.indexOf('--window');
const WSIZE = wi >= 0 ? Math.max(2, parseInt(args[wi + 1], 10) || 5) : 5;
const src = path.join(session, 'structured.jsonl');
if (!fs.existsSync(src)) { console.error('缺少 structured.jsonl'); process.exit(2); }
const frames = fs.readFileSync(src, 'utf8').split(/\r?\n/).filter(Boolean).map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean);

function norm(s) {
  return String(s || '').replace(/\s+/g, '').replace(/[「」『』“”"'`]/g, '').replace(/[，。,.;；：:、]/g, '');
}

// 只收「已確認」的 OCR 形近錯字；不確定的不動。
const FIX = [
  [/夭狐/g, '天狐'],
  [/潦浪者/g, '流浪者'],
  [/凹聲/g, '回聲'], [/凹声/g, '回声'],
  [/回聲人影/g, '回聲人影'],
  [/冂/g, '门'], [/囗/g, '口'],
  [/全麵/g, '全面'], [/麵闆/g, '面板'],
  [/齣/g, '出'], [/祇/g, '只'],
  [/祂/g, '他'], [/牠/g, '它'],
];
function fix(s) { let out = String(s || ''); for (const [re, to] of FIX) out = out.replace(re, to); return out; }

function lineKey(l) {
  return (l.r || '') + '|' + Math.round(Number(l.y || 0) / 10) + '|' + Math.round(Number(l.x || 0) / 50);
}
function lineText(l) { return fix(String(l.t || '').trim()); }

const outJsonl = path.join(session, 'consensus.jsonl');
const outMd = path.join(session, 'consensus.md');
fs.writeFileSync(outJsonl, '', 'utf8');
fs.writeFileSync(outMd, '# 多幀共識逐字稿\n\n', 'utf8');

let lastSig = '';
let emitted = 0;
for (let start = 0; start < frames.length; start += WSIZE) {
  const win = frames.slice(start, start + WSIZE);
  const buckets = new Map();
  for (const f of win) {
    for (const l of f.lines || []) {
      const t = lineText(l);
      if (!t) continue;
      const k = lineKey(l);
      const m = buckets.get(k) || new Map();
      m.set(t, (m.get(t) || 0) + 1);
      buckets.set(k, m);
    }
  }
  const lines = [];
  for (const [k, m] of buckets) {
    let best = '', bestN = 0;
    for (const [t, n] of m) {
      if (n > bestN || (n === bestN && t.length > best.length)) { best = t; bestN = n; }
    }
    if (!best) continue;
    const [r, yb, xb] = k.split('|');
    lines.push({ r, y: Number(yb) * 10, x: Number(xb) * 50, text: best, count: bestN, variants: m.size });
  }
  lines.sort((a, b) => (a.y - b.y) || (a.x - b.x));
  const text = lines.map(x => x.text).join('\n');
  const sig = norm(text);
  if (!sig || sig === lastSig) continue;
  lastSig = sig;
  emitted++;
  const f0 = win[0];
  const rec = { ts: f0.ts, frame: f0.frame, window: win.length, text, lines };
  fs.appendFileSync(outJsonl, JSON.stringify(rec) + '\n', 'utf8');
  fs.appendFileSync(outMd, '## ' + (f0.ts || '') + ' · 幀 ' + f0.frame + '\n\n```text\n' + text + '\n```\n\n', 'utf8');
}
console.log(JSON.stringify({ frames: frames.length, window: WSIZE, screens: emitted, md: outMd }, null, 2));
