import fs from 'node:fs';
import path from 'node:path';

/*
 * 將已驗證的 staging 合併進正式 content。
 * 預設 dry-run；必須加 --confirm 才會寫入。
 * 既有同名／同 id 的正式檔不覆蓋，除非加 --update。
 *
 * node apply-wiki.mjs <stagingDir> --confirm [--update]
 */

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node apply-wiki.mjs <stagingDir> --confirm [--update]'); process.exit(2); }
const staging = path.resolve(args[0]);
const confirm = args.includes('--confirm');
const update = args.includes('--update');
const content = 'D:/BACKROOMS/wiki/content';
const reportPath = path.join(staging, 'validation.json');
if (!fs.existsSync(reportPath)) { console.error('缺少 validation.json，先跑 validate.mjs'); process.exit(2); }
const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
if (!report.ok) { console.error('validation 有錯誤，拒絕合併'); process.exit(1); }
if (!confirm) {
  console.log(JSON.stringify({ dryRun: true, staging, floors: report.floors.length, entities: report.entities.length }, null, 2));
  process.exit(0);
}

const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const backup = path.join('D:/BACKROOMS/wiki/_backup', 'apply-' + stamp);
fs.mkdirSync(backup, { recursive: true });
const applied = { stamp, staging, floors: [], entities: [], skipped: [] };

function copyIfNew(src, dst, meta) {
  if (fs.existsSync(dst) && !update) { applied.skipped.push({ file: dst, reason: 'already exists' }); return false; }
  if (fs.existsSync(dst)) {
    const rel = path.relative('D:/BACKROOMS/wiki', dst).replace(/[\\/]/g, '__');
    fs.copyFileSync(dst, path.join(backup, rel));
  }
  fs.mkdirSync(path.dirname(dst), { recursive: true });
  fs.copyFileSync(src, dst);
  return true;
}

for (const f of report.floors || []) {
  const src = f.file;
  if (!src || !fs.existsSync(src)) continue;
  const obj = JSON.parse(fs.readFileSync(src, 'utf8'));
  const dir = path.join(content, 'floors', String(obj.id) + '-' + String(obj.name).replace(/[\\/:*?"<>|]/g, '_'));
  const dst = path.join(dir, 'floor.json');
  if (copyIfNew(src, dst, f)) applied.floors.push({ id: obj.id, name: obj.name, dst });
}

for (const e of report.entities || []) {
  const src = e.file;
  if (!src || !fs.existsSync(src)) continue;
  const obj = JSON.parse(fs.readFileSync(src, 'utf8'));
  const dir = path.join(content, 'entities', String(obj.n).replace(/[\\/:*?"<>|]/g, '_'));
  const dst = path.join(dir, 'entity.json');
  if (copyIfNew(src, dst, e)) applied.entities.push({ name: obj.n, dst });
}

fs.writeFileSync(path.join(staging, '_applied.json'), JSON.stringify(applied, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ applied: applied.floors.length + applied.entities.length, skipped: applied.skipped.length, backup }, null, 2));
