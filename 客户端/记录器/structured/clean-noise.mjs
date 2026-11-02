#!/usr/bin/env node
/* 清掉事件正文裡的「介面雜訊」。
   2026-10-30：整張 OCR 把「背包 (B)」「設定〈靜音）」「Level 37.1 「深水區」」
   一起掃進事件結果，Wiki 上就出現一坨不是故事的句子。
   這裡只做減法：抽掉 UI 行、把「體力 -1」這種數值搬去 effects、去掉重複句。
   用法：
     node logger/structured/clean-noise.mjs            # 只報告
     node logger/structured/clean-noise.mjs --apply    # 寫回（先備份）
*/
import fs from 'node:fs';
import path from 'node:path';

const HERE = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const ROOT = path.resolve(HERE, '..', '..', '..');
const FLOORS = path.join(ROOT, 'wiki', 'content', 'floors');
const APPLY = process.argv.includes('--apply');

const CHROME = [
  /^Level\s*[\d.]+\s*[「『"].*$/,
  /^背包\s*[（(〈<].*[）)〉>]\s*$/,
  /^背包\s*[（(]?\s*[BbＢ]\s*[）)]?\s*$/,
  /^設定\s*[（(〈<].*$/,
  /^设置\s*[（(〈<].*$/,
  /^跟隨多數\s*[：:]?$/,
  /^跟随多数\s*[：:]?$/,
  /^【[^】]*】$/,
];
const STAT = /^\s*(體力|体力|理智|經驗|经验|金幣|金币|碎片)\s*([+\-−－])\s*(\d+)\s*$/;

function isChrome(t) {
  const s = String(t || '').trim();
  if (!s) return true;
  return CHROME.some(re => re.test(s));
}
function statOf(t) {
  const m = String(t || '').match(STAT);
  if (!m) return null;
  const kind = m[1].replace('体力', '體力').replace('经验', '經驗').replace('金币', '金幣');
  const neg = /[-−－]/.test(m[2]);
  return { kind, amount: neg ? -Number(m[3]) : Number(m[3]) };
}

const report = { floors: 0, changed: 0, removed: [], moved: 0 };
const backupDir = path.join(ROOT, 'wiki', '_backup', 'noise-' + new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19));

for (const dir of fs.readdirSync(FLOORS)) {
  const file = path.join(FLOORS, dir, 'events.json');
  if (!fs.existsSync(file)) continue;
  report.floors++;
  const doc = JSON.parse(fs.readFileSync(file, 'utf8'));
  let touched = false;
  for (const ev of doc.events || []) {
    const keep = [];
    for (const r of ev.results || []) {
      const st = statOf(r);
      if (st) {
        ev.effects = ev.effects || [];
        if (!ev.effects.some(e => e.kind === st.kind && e.amount === st.amount)) ev.effects.push(st);
        report.moved++;
        touched = true;
        continue;
      }
      if (isChrome(r)) { report.removed.push(dir + ' / ' + ev.name + ' / ' + String(r).slice(0, 40)); touched = true; continue; }
      const n = String(r).replace(/\s+/g, '');
      if (keep.some(k => String(k).replace(/\s+/g, '') === n)) { touched = true; continue; }
      keep.push(r);
    }
    if ((ev.results || []).length !== keep.length) ev.results = keep;
  }
  if (touched) {
    report.changed++;
    if (APPLY) {
      fs.mkdirSync(backupDir, { recursive: true });
      fs.copyFileSync(file, path.join(backupDir, dir.replace(/[\\/:*?"<>|]/g, '_') + '-events.json'));
      fs.writeFileSync(file, JSON.stringify(doc, null, 2) + '\n', 'utf8');
    }
  }
}

console.log(JSON.stringify({ apply: APPLY, ...report, backupDir: APPLY ? backupDir : null }, null, 1));
if (report.removed.length) {
  console.log('\n被抽掉的行（前 20）：');
  for (const r of report.removed.slice(0, 20)) console.log('  ' + r);
}
