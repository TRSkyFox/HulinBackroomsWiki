import fs from 'node:fs';
import path from 'node:path';
import { report } from './progress.mjs';

/*
 * 由 structured.jsonl 產生「螢幕文字變化」逐字稿。
 * 只跳過畫面完全沒變的幀；不把重複的 OCR 行重寫一次。
 *
 * node transcript.mjs <sessionDir>
 */

const session = process.argv[2];
if (!session) { console.error('usage: node transcript.mjs <sessionDir>'); process.exit(2); }
const src = path.join(session, 'structured.jsonl');
if (!fs.existsSync(src)) { console.error('找不到 structured.jsonl'); process.exit(2); }
const frames = fs.readFileSync(src, 'utf8').split(/\r?\n/).filter(Boolean).map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean);
const outJsonl = path.join(session, 'transcript.jsonl');
const outMd = path.join(session, 'transcript.md');
fs.writeFileSync(outJsonl, '', 'utf8');
fs.writeFileSync(outMd, '# 螢幕文字逐字稿\n\n', 'utf8');

let last = '';
let count = 0;
let frameDone = 0;
for (const f of frames) {
  report(session, '產生逐字稿', ++frameDone, frames.length, '第 ' + f.frame + ' 幀');
  const lines = (f.lines || []).slice().sort((a, b) => (a.y - b.y) || (a.x - b.x));
  const text = lines.map(l => String(l.t || '').trim()).filter(Boolean).join('\n');
  if (!text || text === last) continue;
  last = text;
  count++;
  const rec = { ts: f.ts, frame: f.frame, w: f.w, h: f.h, text, lines };
  fs.appendFileSync(outJsonl, JSON.stringify(rec) + '\n', 'utf8');
  fs.appendFileSync(outMd, '## ' + (f.ts || '') + ' · 幀 ' + f.frame + '\n\n```text\n' + text + '\n```\n\n', 'utf8');
}
console.log(JSON.stringify({ frames: frames.length, changes: count, transcript: outMd }, null, 2));
