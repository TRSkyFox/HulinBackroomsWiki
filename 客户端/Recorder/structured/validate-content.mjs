// validate-content.mjs — 內容引用完整性與欄位檢查。
//
// 目的：build 之前先抓「條目存在、但引用對不上」與「必要欄位缺失」。
// 錯誤（ok=false）會讓 CI／verify 失敗；warning 只報告，不擋建置。
//
// 直接跑：node 记录器/structured/validate-content.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..', '..');
const CONTENT = path.join(ROOT, 'wiki', 'content');
const CATEGORIES = new Set(['weapon', 'armor', 'accessory', 'tool', 'consumable', 'ammo']);
const ENDING_CATEGORIES = new Set(['bad', 'core', 'good', 'lost', 'extra']);

const read = p => JSON.parse(fs.readFileSync(p, 'utf8'));
const dirs = p => fs.existsSync(p)
  ? fs.readdirSync(p, { withFileTypes: true }).filter(e => e.isDirectory() && !e.name.startsWith('_')).map(e => e.name)
  : [];
const names = v => {
  if (typeof v === 'string') return [v];
  if (v && typeof v === 'object' && typeof v.n === 'string') return [v.n];
  if (Array.isArray(v)) return v.flatMap(names);
  return [];
};

export function run() {
  const checks = [];
  const warnings = [];
  const add = (name, ok, detail = '') => checks.push({ name, ok: !!ok, detail: String(detail) });

  /* ── 物品 ── */
  const items = [];
  for (const cat of dirs(path.join(CONTENT, 'items'))) {
    for (const d of dirs(path.join(CONTENT, 'items', cat))) {
      const p = path.join(CONTENT, 'items', cat, d, 'item.json');
      if (!fs.existsSync(p)) { add('item:missing-json:' + cat + '/' + d, false, p); continue; }
      try { items.push({ cat, dir: d, j: read(p) }); }
      catch (e) { add('item:bad-json:' + cat + '/' + d, false, e.message); }
    }
  }
  const itemNames = new Set();
  const itemIds = new Set();
  const dupNames = [], dupIds = [], badCat = [], missingItemField = [];
  for (const { cat, dir, j } of items) {
    if (!j.n) missingItemField.push(cat + '/' + dir + ':n');
    if (!j.c) missingItemField.push(cat + '/' + dir + ':c');
    /* eff 缺失／空陣列與 desc 空字串都是內容債（warning），不是結構錯誤。 */
    if (j.c && !CATEGORIES.has(j.c)) badCat.push(j.n || dir + ':' + j.c);
    if (itemNames.has(j.n)) dupNames.push(j.n); else itemNames.add(j.n);
    if (j.i) { if (itemIds.has(j.i)) dupIds.push(j.i); else itemIds.add(j.i); }
  }
  add('items:unique-name', dupNames.length === 0, dupNames.slice(0, 10).join(', '));
  add('items:unique-id', dupIds.length === 0, dupIds.slice(0, 10).join(', '));
  add('items:category', badCat.length === 0, badCat.slice(0, 10).join(', '));
  add('items:required-fields', missingItemField.length === 0, missingItemField.slice(0, 10).join(', '));
  const emptyDesc = items.filter(x => !String(x.j.desc || '').trim()).length;
  const emptyEff = items.filter(x => !Array.isArray(x.j.eff) || x.j.eff.length === 0).length;
  const noEffField = items.filter(x => !Array.isArray(x.j.eff)).length;
  if (emptyDesc) warnings.push({ kind: 'item-empty-desc', count: emptyDesc, examples: items.filter(x => !String(x.j.desc || '').trim()).slice(0, 8).map(x => x.j.n) });
  if (emptyEff) warnings.push({ kind: 'item-empty-eff', count: emptyEff, examples: items.filter(x => !Array.isArray(x.j.eff) || x.j.eff.length === 0).slice(0, 8).map(x => x.j.n) });
  if (noEffField) warnings.push({ kind: 'item-no-eff-field', count: noEffField, examples: items.filter(x => !Array.isArray(x.j.eff)).slice(0, 8).map(x => x.j.n) });

  /* ── 角色 ── */
  const chars = [];
  for (const d of dirs(path.join(CONTENT, 'characters'))) {
    const p = path.join(CONTENT, 'characters', d, 'character.json');
    if (!fs.existsSync(p)) continue;
    try { chars.push({ dir: d, j: read(p) }); }
    catch (e) { add('character:bad-json:' + d, false, e.message); }
  }
  const charNames = new Set();
  const dupChars = [];
  const badChars = [];
  for (const { dir, j } of chars) {
    if (charNames.has(j.n)) dupChars.push(j.n); else charNames.add(j.n);
    if (!j.n || !j.title || !j.bio || !j.attrs || !Array.isArray(j.skills)) badChars.push(j.n || dir);
  }
  add('characters:unique-name', dupChars.length === 0, dupChars.join(', '));
  add('characters:required-fields', badChars.length === 0, badChars.slice(0, 10).join(', '));

  /* ── 實體 ── */
  const ents = [];
  for (const d of dirs(path.join(CONTENT, 'entities'))) {
    const p = path.join(CONTENT, 'entities', d, 'entity.json');
    if (!fs.existsSync(p)) continue;
    try { ents.push({ dir: d, j: read(p) }); }
    catch (e) { add('entity:bad-json:' + d, false, e.message); }
  }
  const entNames = new Set();
  const dupEnts = [];
  const badEnts = [];
  for (const { dir, j } of ents) {
    if (entNames.has(j.n)) dupEnts.push(j.n); else entNames.add(j.n);
    if (!j.n) badEnts.push(dir);
  }
  add('entities:unique-name', dupEnts.length === 0, dupEnts.join(', '));
  add('entities:required-fields', badEnts.length === 0, badEnts.slice(0, 10).join(', '));

  /* ── 樓層 ── */
  const floors = [];
  for (const d of dirs(path.join(CONTENT, 'floors'))) {
    const p = path.join(CONTENT, 'floors', d, 'floor.json');
    if (!fs.existsSync(p)) { add('floor:missing-json:' + d, false, p); continue; }
    try { floors.push({ dir: d, j: read(p) }); }
    catch (e) { add('floor:bad-json:' + d, false, e.message); }
  }
  const floorKeys = new Set();
  const dupFloorKeys = [];
  const badFloors = [];
  const stalePlaceholder = [];
  for (const { dir, j } of floors) {
    if (floorKeys.has(j.key)) dupFloorKeys.push(j.key); else floorKeys.add(j.key);
    if (!j.key || !j.id || !j.name) badFloors.push(dir);
    if (j.key !== String(j.id) + '|' + String(j.name)) badFloors.push(dir + ':key');
    if (j._status && !['name-only', 'id-only'].includes(j._status)) badFloors.push(dir + ':status');
    const doc = String(j.doc || '').replace(/<[^>]*>/g, '').trim();
    if (j._status && doc) stalePlaceholder.push(dir + ':' + j._status + '但有正文');
  }
  add('floors:unique-key', dupFloorKeys.length === 0, dupFloorKeys.slice(0, 10).join(', '));
  add('floors:required-fields', badFloors.length === 0, badFloors.slice(0, 10).join(', '));
  add('floors:placeholder-status', stalePlaceholder.length === 0, stalePlaceholder.slice(0, 10).join(', '));

  /* ── 事件與結局 ── */
  const eventIssues = [];
  const endingIssues = [];
  for (const { dir, j: f } of floors) {
    const ep = path.join(CONTENT, 'floors', dir, 'events.json');
    if (fs.existsSync(ep)) {
      try {
        const ev = read(ep);
        if (String(ev.floorId) !== String(f.id)) eventIssues.push(dir + ':floorId');
        for (const e of (ev.events || [])) {
          if (e.selected && !(e.actions || []).some(a => String(a.text || '').trim() === String(e.selected).trim())) {
            eventIssues.push(dir + ':' + (e.name || '?') + ':selected');
          }
        }
        for (const j of (ev.junctions || [])) {
          const n = Array.isArray(j.paths) ? j.paths.length : 0;
          if (n < 1 || n > 4) eventIssues.push(dir + ':' + String(j.text || '?').slice(0, 20) + ':paths=' + n);
        }
      } catch (e) { eventIssues.push(dir + ':bad-json'); }
    }
    const xp = path.join(CONTENT, 'floors', dir, 'endings.json');
    if (fs.existsSync(xp)) {
      try {
        const arr = read(xp);
        for (const e of (Array.isArray(arr) ? arr : [arr])) {
          if (!e || !e.name) endingIssues.push(dir + ':missing-name');
          if (e && !ENDING_CATEGORIES.has(e.category)) endingIssues.push(dir + ':' + (e.name || '?') + ':category');
          if (e && e.floorId != null && String(e.floorId) !== String(f.id)) endingIssues.push(dir + ':' + (e.name || '?') + ':floorId');
        }
      } catch (e) { endingIssues.push(dir + ':bad-json'); }
    }
  }
  add('events:shape', eventIssues.length === 0, eventIssues.slice(0, 10).join(', '));
  add('endings:shape', endingIssues.length === 0, endingIssues.slice(0, 10).join(', '));

  /* ── 引用 ── */
  const dangling = [];
  const checkRefs = (where, list) => {
    for (const n of list) if (n && !itemNames.has(n) && !/^[0-9.\s]+$/.test(n)) dangling.push(where + ' → ' + n);
  };
  for (const { j } of chars) checkRefs('角色 ' + j.n, names(j.items || []));
  for (const { j } of ents) checkRefs('實體 ' + j.n, names(j.drops || []));
  for (const d of dirs(path.join(CONTENT, 'shops'))) {
    const p = path.join(CONTENT, 'shops', d, 'shop.json');
    if (!fs.existsSync(p)) continue;
    try {
      const s = read(p);
      for (const row of (s.it || [])) if (Array.isArray(row)) checkRefs('商店 ' + d, [row[0]]);
    } catch { /* 由 build 處理 */ }
  }
  for (const { j } of floors) checkRefs('樓層 ' + j.name, names((j.info || {}).items || []));
  try {
    for (const r of read(path.join(CONTENT, 'recipes', 'recipes.json'))) {
      checkRefs('配方 ' + (Array.isArray(r.o) ? r.o.join('/') : r.o), names(r.m || []));
      checkRefs('配方 ' + (Array.isArray(r.o) ? r.o.join('/') : r.o), names(r.o || []));
    }
  } catch { /* optional */ }
  try {
    for (const e of read(path.join(CONTENT, 'enhancements', 'enhancements.json'))) checkRefs('強化 ' + e.m, [e.m]);
  } catch { /* optional */ }
  if (dangling.length) warnings.push({ kind: 'dangling-item-refs', count: dangling.length, examples: dangling.slice(0, 12) });

  /* ── 絕對路徑 ── */
  const abs = [];
  (function walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) { walk(p); continue; }
      if (!e.name.endsWith('.json')) continue;
      const txt = fs.readFileSync(p, 'utf8');
      if (/[A-Za-z]:\\\\(?:BACKROOMS|Users)/i.test(txt)) abs.push(path.relative(CONTENT, p));
    }
  })(CONTENT);
  if (abs.length) warnings.push({ kind: 'absolute-paths', count: abs.length, examples: abs.slice(0, 8) });

  return { ok: checks.every(c => c.ok), checks, warnings };
}

const IS_MAIN = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));
if (IS_MAIN) {
  const r = run();
  console.log(JSON.stringify({
    ok: r.ok,
    summary: { checks: r.checks.length, failed: r.checks.filter(c => !c.ok).length, warnings: r.warnings.length },
    checks: r.checks,
    warnings: r.warnings
  }, null, 2));
  process.exit(r.ok ? 0 : 1);
}
