// 記錄器／配色腳本的靜態回歸：防止「改 A 壞 B」。
// 直接跑：node logger/structured/tests/recorder.test.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..', '..', '..');
const logger = fs.readFileSync(path.join(ROOT, '客户端/记录器/backroom-logger.ps1'), 'utf8');
const parsejs = fs.readFileSync(path.join(ROOT, '客户端/记录器/structured/parse.mjs'), 'utf8');
const palette = fs.readFileSync(path.join(ROOT, '客户端/记录器/palette-from-shots.ps1'), 'utf8');
const runjs = fs.readFileSync(path.join(ROOT, '客户端/记录器/structured/run.mjs'), 'utf8');
const mergejs = fs.readFileSync(path.join(ROOT, '客户端/记录器/structured/merge-final.mjs'), 'utf8');
const workerjs = fs.readFileSync(path.join(ROOT, '客户端/记录器/structured/ocr-worker.mjs'), 'utf8');
const eventjs = fs.readFileSync(path.join(ROOT, '客户端/记录器/structured/event-import.mjs'), 'utf8');
const fontjs = fs.readFileSync(path.join(ROOT, '记录器/structured/font-correct.mjs'), 'utf8');
const cleanjs = fs.readFileSync(path.join(ROOT, '记录器/structured/event-clean.mjs'), 'utf8');

export function run() {
  const checks = [];
  const add = (n, ok, d) => checks.push({ name: n, ok: !!ok, detail: d || '' });

  add('logger:floorhead-brackets', /RxFloorHead = .*《〈<\{（/.test(logger), 'RxFloorHead must accept OCR brackets');
  add('logger:levelhead-decimals', /\(\?:\\\.\[\\dIlLOo\]\{1,2\}\)\?/.test(logger), 'RxLevelHead must accept Level 0.2');
  add('logger:number-parser-brackets', /Get-LevelNumberFromText[\s\S]{0,500}《〈<\{（/.test(logger), 'title parser must accept 《 2');
  add('logger:shot-needs-header', /LevelFromHeader/.test(logger) && /LevelFromHeader -and/.test(logger), 'no menu screenshots');
  add('logger:H-not-scene', logger.includes("$v -match '^\\s*(?i:level|leve[l1i]*)'"), '第 N 层 must not be H');
  add('logger:prefix-name-guard', /\$feName/.test(logger) && /\$nameOk/.test(logger), '483 must not become Level 4');
  add('logger:no-3digit-prefix', /\$n -match '\^\\d\{3,\}'/.test(logger), '3-digit floors are trusted');
  add('logger:update-bare-leve', /\(\?i\)leve\[l1i\]\*/.test(logger), 'Leve 483 (no final l) must update floor');
  add('logger:linecrop-frame-no', /LineDir \$state\.LineDir -FrameNo \$state\.FrameNo/.test(logger), 'linecrops must not overwrite across frames');
  /* 2026-10-29：改成「邊錄邊認」＋ 裁圖按內容 hash 命名。
     同一張圖只會被 OCR 一次，靠的是「檔名就是內容 hash」＋ worker 的 done 集合。 */
  add('logger:linecrop-hash-names', /LineCropHash/.test(logger) && /ComputeHash\(\$bytes\)/.test(logger), 'crops must be deduped by content hash');
  add('logger:frame-dedupe', /LastImgHash/.test(logger) && /reuseFrame/.test(logger), 'identical frames must reuse previous OCR');
  add('font:worker-dedupe', /done\.has\(n\)/.test(workerjs) && /rapid\.jsonl/.test(workerjs), 'same crop image OCRs once (ocr-worker)');
  /* 2026-10-29：沒有 selected 的事件一律不寫（連 --auto 也一樣）。
     寧可留 staging，也不要寫一則「不知道玩家選了什麼」的事件。 */
  add('event:require-selected', /if \(!e\.selected\) return false/.test(eventjs), 'events without a chosen action stay in staging');
  add('event:clean-boundary', /\[0-9lI\]/.test(cleanjs), 'event intro stops at difficulty');
  add('parse:continuation-merge', /out\[out\.length - 1\] \+= t/.test(parsejs) && !/\|\^，\|/.test(parsejs), '，也是灰色的。 must merge, not drop');
  add('logger:stable-shot-fields', /FloorStableKey\s*=/.test(logger) && /FloorStableCount\s*=/.test(logger), 'state fields');
  add('logger:shot-needs-main-lines', /shotMainLines\s*=/.test(logger) && /shotMainLines -ge 4/.test(logger), 'avoid loading screen shot');
  add('logger:no-auto-apply', runjs.includes("args.includes('--apply-events')") && runjs.includes("args.includes('--merge')"), 'explicit flags');
  add('pipeline:auto-import', runjs.includes("args.includes('--auto')") && runjs.includes('auto-import.json'), 'auto import report');
  add('pipeline:merge-lowconf', mergejs.includes('existing floor confidence'), 'existing low-conf floor untouched');
  add('pipeline:event-floor-conf', eventjs.includes('floorConf'), 'events gated by floor confidence');

  add('palette:line-sampling', /LineMode/.test(palette) && /\$line = LineMode/.test(palette), 'line from panel border');
  add('palette:saturated-acc', /SaturatedOnly/.test(palette), 'acc must be saturated');
  add('palette:keep-existing', /kept = \$true/.test(palette), 'do not overwrite with []');
  add('palette:edge-bg', /EdgeMode/.test(palette), 'bg from corners');

  add('pipeline:clean-levels', fs.existsSync(path.join(ROOT, '记录器/structured/clean-levels.mjs')), 'clean-levels.mjs');
  return { ok: checks.every(c => c.ok), checks };
}

const IS_MAIN = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));
if (IS_MAIN) {
  const r = run();
  console.log(JSON.stringify(r, null, 2));
  process.exit(r.ok ? 0 : 1);
}
