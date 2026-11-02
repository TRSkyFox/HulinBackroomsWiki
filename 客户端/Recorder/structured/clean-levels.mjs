// clean-levels.mjs — 刪掉「只有樓層標題、沒有任何內容」的樓層檔。
// 來源：換層瞬間 OCR 把 Level 0 讀成 Level 1，會在 levels/ 留下一個空檔。
// 用法：node clean-levels.mjs <sessionDir>
import fs from 'node:fs';
import path from 'node:path';

const session = process.argv[2];
if (!session) { console.error('usage: node clean-levels.mjs <sessionDir>'); process.exit(2); }
const dir = path.join(session, 'levels');
if (!fs.existsSync(dir)) process.exit(0);

const removed = [];
for (const name of fs.readdirSync(dir)) {
  if (!name.endsWith('.log')) continue;
  const p = path.join(dir, name);
  const text = fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, '');
  const lines = text.split(/\r?\n/);
  const content = lines.filter(l => {
    if (!/^\[\d{2}:\d{2}:\d{2}\]/.test(l)) return false;
    if (/樓層\/場景|\/場景/.test(l)) return false;
    return true;
  });
  const header = (lines.find(l => /^# 樓層紀錄：/.test(l)) || '');
  const own = (header.match(/^# 樓層紀錄：Level\s+([^\s「]+)/) || [])[1] || '';
  const ownH = content.some(l => l.includes('## Level ' + own) || l.includes('## Level ' + own + ' '));
  const resume = /冒險繼續|冒险继续|上次說到|上次说到|重新連線|重新连线/.test(text);
  if (content.length === 0 || (own && resume && !ownH)) {
    fs.rmSync(p, { force: true });
    removed.push(name);
  }
}
console.log(JSON.stringify({ ok: true, removed }, null, 2));
