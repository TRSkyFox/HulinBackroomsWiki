// build-char-freq.mjs — 從現有 Wiki 內容統計「哪些字真的以繁體在用」。
// 產出 structured/char-freq.json，讓簡→繁轉換在可攜版也能判斷方向。
// 只讀 content，不改任何東西。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..');
const READ = ['wiki/content'];
const OUT = path.join(HERE, 'char-freq.json');

const freq = new Map();
function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p);
    else if (/\.(json|md)$/i.test(e.name)) {
      const text = fs.readFileSync(p, 'utf8');
      for (const ch of text) {
        const cp = ch.codePointAt(0);
        if (cp >= 0x3400 && cp <= 0x9fff) freq.set(ch, (freq.get(ch) || 0) + 1);
      }
    }
  }
}
for (const rel of READ) {
  const dir = path.join(ROOT, rel);
  if (fs.existsSync(dir)) walk(dir);
}
const chars = {};
/* 在可攜版沒有 wiki/ 可讀；不要把 char-freq.json 清空。 */
if (freq.size < 100) { console.error('找不到足夠的 Wiki 內容，保留現有 char-freq.json'); process.exit(1); }
for (const [ch, n] of [...freq.entries()].sort((a, b) => b[1] - a[1])) chars[ch] = n;
fs.writeFileSync(OUT, JSON.stringify({ _meta: { source: READ.join(' + '), generated: new Date().toISOString(), chars: Object.keys(chars).length }, chars }, null, 0) + '\n', 'utf8');
console.log(JSON.stringify({ out: OUT, chars: Object.keys(chars).length, bytes: fs.statSync(OUT).size }, null, 2));
