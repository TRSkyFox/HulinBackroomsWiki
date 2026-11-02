import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(here, '..');
const node = process.execPath;
const cases = [
  {
    name: 'combat drops + resist',
    dir: path.join(here, 'combat'),
    check(recs) {
      const c = recs.find(r => r.type === 'combat');
      if (!c) return '沒有 combat';
      if (!String(c.enemy.name).includes('流浪者的幻影')) return '敵名錯誤: ' + c.enemy.name;
      if (!c.enemy.drops.some(x => x.includes('打火'))) return '缺掉落: ' + JSON.stringify(c.enemy.drops);
      if (!c.enemy.resistances.some(x => x.includes('抵抗'))) return '缺抗性';
      return '';
    }
  },
  {
    name: 'enemy prefix + suffix',
    dir: path.join(here, 'edge-prefix'),
    check(recs) {
      const c = recs.find(r => r.type === 'combat');
      if (!c) return '沒有 combat';
      if (c.enemy.name !== '獵犬') return '前綴未剝除: ' + c.enemy.name;
      if (!c.enemy.drops.some(x => x.includes('獵犬的牙'))) return '缺掉落';
      if (!c.enemy.resistances.some(x => x.includes('火焰'))) return '缺弱點';
      return '';
    }
  },
  {
    name: 'choice filter + selected',
    dir: path.join(here, 'edge-choice'),
    check(recs) {
      const c = recs.find(r => r.type === 'choice');
      if (!c) return '沒有 choice';
      if (c.options.length !== 2) return '選項數量: ' + c.options.length;
      if (c.options.some(o => o.text.includes('街頭') || o.text.includes('街头'))) return '背包文字混入';
      if (!c.selected || !c.selected.text.includes('左边')) return 'selected 錯誤';
      return '';
    }
  }
];

/* 注意：全部用 stdio:'ignore'。
   沙箱／CI 會擋「程式讀另一個程式的 stdout 管道」（EPERM），
   以前用 encoding:'utf8' 抓輸出，在這裡就整組誤判失敗。
   這幾支程式都會把結果寫成檔案，直接讀檔就好。 */
let fail = 0;
for (const c of cases) {
  const staging = path.join(c.dir, '_test-staging');
  fs.rmSync(staging, { recursive: true, force: true });
  const parse = spawnSync(node, [path.join(root, 'parse.mjs'), c.dir], { stdio: 'ignore' });
  if (parse.error || parse.status !== 0) { console.log('FAIL', c.name, 'parse', parse.error && parse.error.code); fail++; continue; }
  const recsPath = path.join(c.dir, 'structured', 'records.jsonl');
  if (!fs.existsSync(recsPath)) { console.log('FAIL', c.name, 'no records'); fail++; continue; }
  const recs = fs.readFileSync(recsPath, 'utf8').split(/\r?\n/).filter(Boolean).map(JSON.parse);
  const err = c.check(recs);
  if (err) { console.log('FAIL', c.name, '-', err); fail++; continue; }
  const imp = spawnSync(node, [path.join(root, 'import-wiki.mjs'), recsPath, '--out', staging], { stdio: 'ignore' });
  const val = spawnSync(node, [path.join(root, 'validate.mjs'), staging], { stdio: 'ignore' });
  if (imp.error || val.error) { console.log('FAIL', c.name, '- spawn', (imp.error || val.error).code); fail++; continue; }
  if (imp.status !== 0 || val.status !== 0) {
    let why = '';
    try { why = JSON.stringify(JSON.parse(fs.readFileSync(path.join(staging, 'validation.json'), 'utf8')).errors || []).slice(0, 200); } catch {}
    console.log('FAIL', c.name, '- staging/validate', why); fail++; continue;
  }
  console.log('PASS', c.name);
}
if (fail) { console.log('FAILED', fail); process.exit(1); }
console.log('ALL TESTS PASSED');
