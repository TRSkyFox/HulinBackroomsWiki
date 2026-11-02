import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';

/*
 * 把一場 session 打包成可傳給 Wiki 主人的 .expedition.zip。
 * 不含手動截圖；Wiki content 不會被打包。
 *
 * node export-package.mjs <sessionDir> <stagingDir> [--out <dir>] [--include-raw]
 */

const args = process.argv.slice(2);
if (args.length < 2) { console.error('usage: node export-package.mjs <sessionDir> <stagingDir> [--out <dir>] [--include-raw]'); process.exit(2); }
const session = path.resolve(args[0]);
const staging = path.resolve(args[1]);
const oi = args.indexOf('--out');
const outDir = oi >= 0 ? path.resolve(args[oi + 1]) : path.join(process.env.LOCALAPPDATA || session, 'HulinExpedition', 'exports');
const includeRaw = args.includes('--include-raw');
if (!fs.existsSync(session) || !fs.existsSync(staging)) { console.error('session/staging 不存在'); process.exit(2); }

const name = path.basename(session);
const work = path.join(outDir, '_pack-' + name + '-' + process.pid);
fs.rmSync(work, { recursive: true, force: true });
fs.mkdirSync(path.join(work, 'session'), { recursive: true });
fs.mkdirSync(path.join(work, 'staging'), { recursive: true });

function copyFile(src, dst) {
  if (!fs.existsSync(src)) return;
  fs.mkdirSync(path.dirname(dst), { recursive: true });
  fs.copyFileSync(src, dst);
}
function copyDir(src, dst) {
  if (!fs.existsSync(src)) return;
  fs.cpSync(src, dst, { recursive: true, force: true });
}
for (const f of ['wiki.jsonl', 'wiki.md', 'structured.jsonl', 'structured.ocr.jsonl', 'transcript.md', 'transcript.jsonl', 'consensus.md', 'consensus.jsonl', 'volume.jsonl', 'floor-colors.log', '_transitions.log']) copyFile(path.join(session, f), path.join(work, 'session', f));
if (includeRaw) copyFile(path.join(session, 'ocr-raw.log'), path.join(work, 'session', 'ocr-raw.log'));
copyDir(path.join(session, 'clean'), path.join(work, 'session', 'clean'));
copyDir(path.join(session, 'levels'), path.join(work, 'session', 'levels'));

for (const f of ['report.json', 'validation.json', 'merge-final-report.json', 'events-review.jsonl']) copyFile(path.join(staging, f), path.join(work, 'staging', f));
for (const d of ['floors', 'entities', 'events', 'evidence']) copyDir(path.join(staging, d), path.join(work, 'staging', d));

const files = [];
function walk(dir) { for (const e of fs.readdirSync(dir, { withFileTypes: true })) { const p = path.join(dir, e.name); if (e.isDirectory()) walk(p); else files.push(p); } }
walk(work);
const fileInfo = files.map(p => ({
  path: path.relative(work, p).replace(/\\/g, '/'),
  bytes: fs.statSync(p).size,
  sha256: crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')
}));

let report = {};
try { report = JSON.parse(fs.readFileSync(path.join(staging, 'report.json'), 'utf8')); } catch { }
const manifest = {
  format: 'expedition-staging',
  version: 1,
  session: name,
  created: new Date().toISOString(),
  source: session,
  counts: { floors: (report.floors || []).length, entities: (report.entities || []).length, choices: (report.choices || []).length },
  files: fileInfo
};
fs.writeFileSync(path.join(work, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');
fs.writeFileSync(path.join(work, 'README.txt'), [
  '狐霖的後室 RPG — 紀錄包',
  '',
  '這個壓縮檔只包含：',
  '- session/：原始 logs、structured.jsonl',
  '- staging/：已解析的樓層、實體、事件、evidence',
  '- manifest.json：檔名、大小、sha256',
  '',
  'Wiki 主人可用「匯入紀錄.bat」把 staging 併入 Wiki。',
  '請不要把 Wiki content 放進這個壓縮檔。'
].join('\r\n'), 'utf8');

fs.mkdirSync(outDir, { recursive: true });
const zip = path.join(outDir, 'Expedition-' + name + '.expedition.zip');
try { if (fs.existsSync(zip)) fs.rmSync(zip, { force: true }); } catch { }
const ps = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
  'Compress-Archive -Path "' + work.replace(/"/g, '""') + '\\*" -DestinationPath "' + zip.replace(/"/g, '""') + '" -Force'], { encoding: 'utf8' });
if (ps.status !== 0 || !fs.existsSync(zip)) {
  console.error('壓縮失敗，保留資料夾：' + work);
  console.error(ps.stderr || ps.stdout || '');
  process.exit(1);
}
fs.rmSync(work, { recursive: true, force: true });
fs.writeFileSync(path.join(outDir, 'last-export.txt'), zip + '\r\n', 'utf8');
console.log(JSON.stringify({ zip, files: fileInfo.length, floors: manifest.counts.floors, entities: manifest.counts.entities, choices: manifest.counts.choices }, null, 2));
