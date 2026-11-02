// sync.mjs — 把 pipeline 的唯一來源（logger/structured）同步到 app／可攜版。
//
// 為什麼要有這個檔：2026-10-28 之前 parse.mjs 同時存在三份、內容各不相同，
// 「修好的那份」和「實際在跑的那份」不是同一個檔案。從現在起：
//   logger/structured         = 唯一來源（只改這裡）
//   player-logger/app         = 執行副本（由這支程式同步）
//   player-logger/dist        = 可攜版副本（由這支程式同步）
//
// 用法：
//   node logger/structured/sync.mjs           同步（只複製有差異的檔）
//   node logger/structured/sync.mjs --check   只檢查，有漂移就 exit 1
//   node logger/structured/sync.mjs --dest app
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
if (!/(logger|记录器|Recorder)[\\\\/]structured$/.test(HERE)) {
  console.error('sync.mjs 只能從 logger/structured 執行（目前：' + HERE + '）');
  process.exit(2);
}
const CLIENT = path.resolve(HERE, '..', '..');
const ROOT = path.resolve(CLIENT, '..');
const args = process.argv.slice(2);
const checkOnly = args.includes('--check');
const destArg = args.indexOf('--dest');
const destFilter = destArg >= 0 ? args[destArg + 1] : 'all';
if (!['all', 'app', 'dist'].includes(destFilter)) {
  console.error('--dest 只接受 all / app / dist');
  process.exit(2);
}

const DEST = {
  app: CLIENT,
  dist: path.join(CLIENT, 'dist/Expedition')
};
const dests = Object.entries(DEST).filter(([k]) => destFilter === 'all' || destFilter === k);

/* structured 底下要同步的檔：程式、字型、比對器、說明。tests/ 不進發行包。 */
const STRUCTURED_FILES = fs.readdirSync(HERE, { withFileTypes: true })
  .filter(e => e.isFile() && /\.(mjs|exe|otf|md|json|txt)$/i.test(e.name))
  .map(e => e.name)
  .sort();

const PAIRS = [];
for (const name of STRUCTURED_FILES) {
  PAIRS.push({ rel: 'structured/' + name, src: path.join(HERE, name) });
}
const rapidExe = path.join(HERE, 'bin', 'rapid-ocr.exe');
if (fs.existsSync(rapidExe)) PAIRS.push({ rel: 'structured/bin/rapid-ocr.exe', src: rapidExe });
/* 記錄器本體與後處理：來源各自只有一份，同步到 app／dist 避免再次分叉。 */
PAIRS.push(
  { rel: 'backroom-logger.ps1', src: path.join(CLIENT, 'Recorder/backroom-logger.ps1') },
  { rel: 'palette-from-shots.ps1', src: path.join(CLIENT, 'Recorder/palette-from-shots.ps1') },
  { rel: 'volume-watch.ps1', src: path.join(CLIENT, 'volume-watch.ps1') },
  { rel: 'ExpeditionClient.exe.config', src: path.join(CLIENT, 'ExpeditionClient.exe.config') }
);

function sha(file) {
  const h = crypto.createHash('sha256');
  h.update(fs.readFileSync(file));
  return h.digest('hex');
}
function same(a, b) {
  try {
    const sa = fs.statSync(a), sb = fs.statSync(b);
    if (!sa.isFile() || !sb.isFile()) return false;
    if (sa.size !== sb.size) return false;
    return sha(a) === sha(b);
  } catch { return false; }
}

const drift = [];
const copied = [];
for (const [key, base] of dests) {
  for (const p of PAIRS) {
    if (!fs.existsSync(p.src)) { drift.push({ dest: key, rel: p.rel, why: 'missing-source' }); continue; }
    const dst = path.join(base, p.rel);
    if (same(p.src, dst)) continue;
    drift.push({ dest: key, rel: p.rel, why: fs.existsSync(dst) ? 'different' : 'missing-dest' });
    if (!checkOnly) {
      fs.mkdirSync(path.dirname(dst), { recursive: true });
      fs.copyFileSync(p.src, dst);
      copied.push({ dest: key, rel: p.rel });
    }
  }
}

/* 舊的第三份副本若還在，提醒（不自動刪，避免動到使用者檔案）。 */
const legacy = path.join(CLIENT, 'structured');
if (fs.existsSync(legacy)) {
  console.warn('⚠ 仍存在舊副本：player-logger/structured（請確認後移入 _archive）');
}

if (checkOnly) {
  if (drift.length) {
    console.log(JSON.stringify({ ok: false, drift }, null, 2));
    process.exit(1);
  }
  console.log(JSON.stringify({ ok: true, checked: PAIRS.length * dests.length }, null, 2));
} else {
  console.log(JSON.stringify({ ok: true, copied: copied.length, upToDate: PAIRS.length * dests.length - copied.length, details: copied }, null, 2));
}
