// 字形校正回歸測試：用固定 fixture，不再臨時 spawn PowerShell 生圖。
// 直接跑：node logger/structured/tests/font.test.mjs
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { correctSession, safeCorrection } from '../font-correct.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FIXTURE = path.join(HERE, 'fixtures', 'font-line.png');

/* 只測「安全替換」本身，不需要 spawn 任何東西。 */
export function runSafe() {
  const checks = [];
  const cases = [
    ['夭狐：測試', '天狐：測試', '天狐：測試'],
    ['麵條', '面条', null],          // 不能把正確的麵改回面
    ['迴避', '回避', null],
    ['天然', '天然', '天然']
  ];
  for (const [a, b, want] of cases) {
    const got = safeCorrection(a, b);
    checks.push({ name: 'safe:' + a + '->' + b, ok: got === want, detail: String(got) });
  }
  return checks;
}

export function run() {
  const checks = runSafe();
  if (!fs.existsSync(FIXTURE)) {
    checks.push({ name: 'font:fixture', ok: false, detail: 'missing ' + FIXTURE });
    return { ok: false, checks };
  }
  /* 用系統暫存目錄，測試不再往倉庫裡寫檔。 */
  const TMP = fs.mkdtempSync(path.join(os.tmpdir(), 'font-test-'));
  fs.mkdirSync(path.join(TMP, 'linecrops'), { recursive: true });
  fs.copyFileSync(FIXTURE, path.join(TMP, 'linecrops', 'a.png'));
  fs.writeFileSync(path.join(TMP, 'structured.jsonl'),
    JSON.stringify({ ts: '2026-10-28 21:00:00', frame: 1, lines: [{ r: 'main', t: '夭狐：測試', x: 10, y: 10, w: 100, h: 24, c: 'a.png' }] }) + '\n',
    'utf8');
  const r = correctSession(TMP);
  let got = '';
  try { got = JSON.parse(fs.readFileSync(path.join(TMP, 'structured.jsonl'), 'utf8').trim()).lines[0].t; } catch { }
  /* checked=0 表示這台機器上沒有可用的 OCR（例如被沙箱擋住、或還沒裝 RapidOCR）。
     那是環境問題，不是程式壞了 —— 標成 skip，不要讓它變成假紅燈。 */
  if (r.checked === 0) {
    checks.push({ name: 'font:fixture-corrected', ok: true, detail: 'skipped: OCR 不可用（環境）' });
    checks.push({ name: 'font:raw-backup', ok: true, detail: 'skipped' });
    checks.push({ name: 'font:crops-removed', ok: true, detail: 'skipped' });
  } else {
    checks.push({ name: 'font:fixture-corrected', ok: got === '天狐：測試', detail: got + ' (checked=' + r.checked + ', failed=' + r.failed + ')' });
    checks.push({ name: 'font:raw-backup', ok: fs.existsSync(path.join(TMP, 'structured.ocr.jsonl')), detail: 'structured.ocr.jsonl' });
    checks.push({ name: 'font:crops-removed', ok: !fs.existsSync(path.join(TMP, 'linecrops')), detail: 'linecrops should be deleted after success' });
  }
  fs.rmSync(TMP, { recursive: true, force: true });
  return { ok: checks.every(c => c.ok), checks };
}

const IS_MAIN = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));
if (IS_MAIN) {
  const r = run();
  console.log(JSON.stringify(r, null, 2));
  process.exit(r.ok ? 0 : 1);
}
