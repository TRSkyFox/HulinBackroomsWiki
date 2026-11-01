// 資料結構回歸測試：保證 merge 之後不會再出現重複實體、空 events、單數 own。
// 直接跑：node logger/structured/tests/data.test.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const CONTENT = path.join(ROOT, 'wiki', 'content');
const read = p => JSON.parse(fs.readFileSync(p, 'utf8'));
const dirs = p => fs.readdirSync(p, { withFileTypes: true }).filter(e => e.isDirectory()).map(e => e.name);

const CATS = new Set(['bad', 'core', 'good', 'lost', 'extra']);
const DUP_ENTITY_PAIRS = [['河后', '河後'], ['冰湖底下的蛇', '冰湖裡的蛇'], ['有失控的礦車', '失控的礦車']];

export function run() {
  const checks = [];
  const add = (name, ok, detail) => checks.push({ name, ok: !!ok, detail: detail || '' });

  /* 樓層 */
  const floors = [];
  for (const d of dirs(path.join(CONTENT, 'floors'))) {
    const p = path.join(CONTENT, 'floors', d, 'floor.json');
    if (!fs.existsSync(p)) continue;
    const f = read(p);
    floors.push({ dir: d, f });
  }
  add('floors:has-data', floors.length >= 1, floors.length + ' floors');
  add('floors:unique-key', new Set(floors.map(x => x.f.key)).size === floors.length, 'duplicate key');
  const byId = {};
  for (const x of floors) (byId[String(x.f.id)] = byId[String(x.f.id)] || []).push(x.dir);
  const dupIds = Object.entries(byId).filter(([, v]) => v.length > 1).map(([k]) => k);
  /* 2026-10-31：0.01「出口？」在舊資料裡是明信片上的樓層名，遊戲內無法進入。
   允許它以 name-only 存在（只有名字、沒有正文），但不允許有正文的假條目。 */
  add('floors:no-0.01', !floors.some(x => String(x.f.id) === '0.01' && x.f._status !== 'name-only'), '0.01 should be archived');
  /* 只有遊戲本身真的共用 id 的樓層（1／7／9）可以重複；其他重複代表建層邏輯壞了。 */
  add('floors:dup-ids-known', dupIds.every(id => ['1', '7', '9'].includes(id)), dupIds.slice().sort().join(','));
  /* 名字不可以帶括號：`停車區]` 會讓站點的 id|name 對不上（打過卻顯示鎖住）。 */
  add('floors:clean-names', floors.every(x => !/[\[\]［］【】〔〕「」『』]/.test(String(x.f.name || ''))), floors.filter(x => /[\[\]［］【】〔〕「」『』]/.test(String(x.f.name || ''))).map(x => x.f.name).join(' '));

  /* events：存在的必須有內容；同 id 樓層不可有 events（會混層） */
  const eventFiles = [];
  for (const d of dirs(path.join(CONTENT, 'floors'))) {
    const p = path.join(CONTENT, 'floors', d, 'events.json');
    if (!fs.existsSync(p)) continue;
    eventFiles.push({ d, e: read(p) });
  }
  add('events:non-empty', eventFiles.every(x => (x.e.junctions || []).length || (x.e.events || []).length),
    eventFiles.map(x => x.d + ':' + ((x.e.junctions || []).length + (x.e.events || []).length)).join(' '));
  add('events:no-dup-floor', eventFiles.every(x => !dupIds.includes(String(x.e.floorId))),
    eventFiles.map(x => x.e.floorId).join(','));

  /* 結局 */
  const endings = [];
  for (const d of dirs(path.join(CONTENT, 'floors'))) {
    const p = path.join(CONTENT, 'floors', d, 'endings.json');
    if (!fs.existsSync(p)) continue;
    const arr = read(p);
    for (const e of (Array.isArray(arr) ? arr : [arr])) endings.push({ d, e });
  }
  /* 筆數不寫死：走幾層就有幾筆，這裡只驗「有名字、分類合法」。 */
  add('endings:has', endings.length >= 0 && endings.every(x => x.e && x.e.name), endings.length + ' endings');
  add('endings:category', endings.every(x => CATS.has(x.e.category)), endings.map(x => x.e.category).join(','));
  add('endings:no-own-singular', endings.every(x => !('own' in x.e)), 'own singular found');
  add('endings:owns-shape', endings.every(x => x.e.owns == null || Array.isArray(x.e.owns)), 'owns not array');
  add('endings:floor-id', endings.every(x => x.e.floorId != null), 'missing floorId');

  /* 實體：名稱唯一、已知重複已合併 */
  const ents = dirs(path.join(CONTENT, 'entities')).map(d => {
    const p = path.join(CONTENT, 'entities', d, 'entity.json');
    return fs.existsSync(p) ? read(p) : null;
  }).filter(Boolean);
  add('entities:unique-name', new Set(ents.map(e => e.n)).size === ents.length, ents.length + ' entities');
  add('entities:no-known-dups', !DUP_ENTITY_PAIRS.some(([a, b]) => ents.some(e => e.n === a) && ents.some(e => e.n === b)), 'duplicate pair still present');
  add('entities:has-name', ents.every(e => e.n), 'entity without n');
  add('entities:no-skill-label', ents.every(e => !(e.skills || []).some(s => String(s && s.n || s) === '技能')), 'literal 技能 skill');
  const BAD_NOTE = /命中|造成|1d\d|傷害|伤害|回合|體力上限/;
  add('entities:no-combat-log', ents.every(e => {
    const notes = (e.resist && e.resist.notes) || [];
    const obs = (e._observations && e._observations.resistNotes) || [];
    return !notes.some(n => BAD_NOTE.test(String(n))) && !obs.some(n => BAD_NOTE.test(String(n)));
  }), 'combat log in resist notes');

  /* 角色 */
  const chars = dirs(path.join(CONTENT, 'characters')).map(d => {
    const p = path.join(CONTENT, 'characters', d, 'character.json');
    return fs.existsSync(p) ? read(p) : null;
  }).filter(Boolean);
  add('characters:present', chars.length >= 1, chars.length + '');
  add('characters:fields', chars.every(c => c.n && c.title && c.bio && c.attrs && Array.isArray(c.skills)), 'missing field');

  /* 玩家名不可以進正文（狐天 已確認是帳號名） */
  let playerLeak = [];
  function walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) { walk(p); continue; }
      if (!/\.(json|md)$/i.test(e.name)) continue;
      const txt = fs.readFileSync(p, 'utf8');
      if (txt.includes('狐天') || txt.includes('狐夭')) playerLeak.push(path.relative(CONTENT, p));
    }
  }
  walk(CONTENT);
  add('content:no-player-name', playerLeak.length === 0, playerLeak.join(', '));

  /* 建置產物：新的 token 與結局陣列有進去 */
  const site = path.join(ROOT, 'wiki', 'site', 'index.html');
  const html = fs.existsSync(site) ? fs.readFileSync(site, 'utf8') : '';
  add('site:built', html.length > 1000000, html.length + ' bytes');
  add('site:floor-partial', /var FLOOR_PARTIAL = \{/.test(html), 'FLOOR_PARTIAL missing');
  add('site:ending-arrays', /var END_DATA = \{"bad":\[/.test(html) && /"good":\[/.test(html), 'END_DATA not array');

  return { ok: checks.every(c => c.ok), checks };
}

const IS_MAIN = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));
if (IS_MAIN) {
  const r = run();
  console.log(JSON.stringify(r, null, 2));
  process.exit(r.ok ? 0 : 1);
}
