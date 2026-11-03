// import-manual.mjs — 把手動記錄（Recorder/logs/manual/*.jsonl）合進 wiki。
// 用法：
//   node import-manual.mjs             # 只報告（dry-run）
//   node import-manual.mjs --confirm   # 真的寫入（先備份）
//   node import-manual.mjs --dir <manualDir>
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { toHant } from './i18n.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..', '..');
const CONTENT = path.join(ROOT, 'wiki', 'content');
const FLOORS = path.join(CONTENT, 'floors');
const ITEMS = path.join(CONTENT, 'items');
const BACKUP = path.join(ROOT, '备份');

const args = process.argv.slice(2);
const confirm = args.includes('--confirm');
const dirArg = args.indexOf('--dir');
const manualDir = dirArg >= 0 ? path.resolve(args[dirArg + 1]) : path.join(ROOT, '客户端', 'Recorder', 'logs', 'manual');

const readJson = (p) => JSON.parse(fs.readFileSync(p, 'utf8'));
const writeJson = (p, v) => fs.writeFileSync(p, JSON.stringify(v, null, 2) + '\n', 'utf8');
const norm = (s) => String(s || '').replace(/\s+/g, '').replace(/[「」『』【】]/g, '').trim();
const T = (s) => toHant(String(s == null ? '' : s));
/* OCR 常把 0 讀成 O、1 讀成 l/I */
const fid = (s) => {
  let v = String(s == null ? '' : s).trim();
  if (/^[Oo]$/.test(v)) v = '0';
  if (/^[Il]$/.test(v)) v = '1';
  v = v.replace(/^([Oo])(?=[.\d])/, '0').replace(/^([Il])(?=[.\d])/, '1');
  return v;
};

/* 樓層索引 */
const floorById = new Map();
for (const d of fs.readdirSync(FLOORS)) {
  const p = path.join(FLOORS, d, 'floor.json');
  if (!fs.existsSync(p)) continue;
  const f = readJson(p);
  floorById.set(String(f.id), { dir: d, data: f, path: p });
}

/* 角色目錄（角色名 → 資料夾） */
const charDir = new Map();
for (const d of fs.readdirSync(path.join(CONTENT, 'characters'))) {
  const p = path.join(CONTENT, 'characters', d, 'character.json');
  if (!fs.existsSync(p)) continue;
  try {
    const c = readJson(p);
    if (c && c.n) charDir.set(norm(c.n), d);
  } catch {}
}

/* 物品索引 */
const itemByKey = new Map(); // norm(name) -> {path, data, dir}
for (const cat of fs.readdirSync(ITEMS)) {
  const cp = path.join(ITEMS, cat);
  if (!fs.statSync(cp).isDirectory()) continue;
  for (const d of fs.readdirSync(cp)) {
    const p = path.join(cp, d, 'item.json');
    if (!fs.existsSync(p)) continue;
    const j = readJson(p);
    itemByKey.set(norm(j.n), { path: p, data: j, dir: cat });
  }
}

/* 讀 JSONL */
function readJsonl(file) {
  if (!fs.existsSync(file)) return [];
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).map((l) => {
    try { return JSON.parse(l); } catch { return null; }
  }).filter(Boolean);
}
function allJsonl(prefix) {
  if (!fs.existsSync(manualDir)) return [];
  const out = [];
  for (const f of fs.readdirSync(manualDir)) {
    if (f.startsWith(prefix) && f.endsWith('.jsonl')) out.push(...readJsonl(path.join(manualDir, f)));
  }
  return out;
}

const events = allJsonl('events-');
const items = allJsonl('items-');
const floors = allJsonl('floors-');
const exits = allJsonl('exits-');
const endings = allJsonl('endings-');
const pending = allJsonl('pending-');

const pendingFiles = new Map();
const report = { events: 0, merged: 0, items: 0, floors: 0, exits: 0, endings: 0, skipped: 0, pending: pending.length };
const writes = [];

/* ── 事件 ── */
for (const rec of events) {
  const fkey = fid(rec.floorId);
  const fl = floorById.get(fkey);
  if (!fl) { report.skipped++; continue; }
  const step1 = (rec.steps || [])[0] || rec;
  const step2 = (rec.steps || [])[1] || {};
  const name = T(step1.title).trim();
  if (!name) { report.skipped++; continue; }
  const p = path.join(FLOORS, fl.dir, 'events.json');
  const j = pendingFiles.get(p) || (fs.existsSync(p) ? readJson(p) : { floorId: fkey, floorName: fl.data.name, events: [], junctions: [] });
  if (!Array.isArray(j.events)) j.events = [];
  const existing = j.events.find((e) => norm(e.name) === norm(name));
  const branch = {
    selected: step2.choice ? T(step2.choice) : null,
    results: Array.isArray(step2.results) ? step2.results.map(T) : [],
    check: step2.check || null,
    combat: step2.combat || null
  };
  if (existing) {
    if (!Array.isArray(existing.branches)) existing.branches = [];
    const dup = existing.branches.some((b) => norm(b.selected) === norm(branch.selected) && norm((b.results || []).join('')) === norm(branch.results.join('')));
    if (!dup && branch.selected) existing.branches.push(branch);
    pendingFiles.set(p, j);
    writes.push({ path: p, data: j });
    report.merged++;
    continue;
  }
  const ev = {
    name,
    kind: (Array.isArray(rec.outcomes) ? rec.outcomes : [rec.outcome]).includes('shop') ? 'shop' : ((Array.isArray(rec.outcomes) ? rec.outcomes : [rec.outcome]).includes('rest') ? 'rest' : 'event'),
    intro: T(step1.intro),
    actions: (step1.options || []).map((o) => {
      const a = { text: T(o.text) };
      if (o.check) a.check = { attribute: T(o.check) };
      if (o.requirement) a.requirement = typeof o.requirement === 'string' ? T(o.requirement) : o.requirement;
      return a;
    }),
    selected: step2.choice ? T(step2.choice) : null,
    results: Array.isArray(step2.results) ? step2.results.map(T) : [],
    _src: 'manual'
  };
  if (step2.check) ev.check = step2.check;
  if (step2.combat) ev.combat = step2.combat;
  if (rec.scopes) ev.scopes = rec.scopes;
  if (rec.characters) ev.characters = rec.characters;
  if (rec.outcomes) ev.outcomes = rec.outcomes;
  j.events.push(ev);
  pendingFiles.set(p, j);
  writes.push({ path: p, data: j });
  report.events++;

  const scopes = Array.isArray(rec.scopes) ? rec.scopes : [];
  const chars = Array.isArray(rec.characters) ? rec.characters : [];
  if (scopes.includes('character')) {
    for (const ch of chars) {
      const dir = charDir.get(norm(ch));
      if (!dir) continue;
      const cp = path.join(CONTENT, 'characters', dir, 'events.json');
      const arr = pendingFiles.get(cp) || (fs.existsSync(cp) ? readJson(cp) : []);
      const list = Array.isArray(arr) ? arr : (arr.events || []);
      if (!list.some((e) => norm(e.name) === norm(name))) {
        list.push(Object.assign({}, ev, { character: ch }));
        pendingFiles.set(cp, list);
        writes.push({ path: cp, data: list });
        report.events++;
      }
    }
  }
  if (scopes.includes('common')) {
    const cp = path.join(CONTENT, 'events', 'common', 'events.json');
    const arr = pendingFiles.get(cp) || (fs.existsSync(cp) ? readJson(cp) : []);
    const list = Array.isArray(arr) ? arr : (arr.events || []);
    if (!list.some((e) => norm(e.name) === norm(name))) {
      list.push(Object.assign({}, ev, { scope: 'common' }));
      pendingFiles.set(cp, list);
      writes.push({ path: cp, data: list });
      report.events++;
    }
  }
}

/* ── 物品 ── */
const CAT = { '消耗品': ['消耗品', 'consumable'], '道具': ['道具', 'tool'], '身體': ['裝備', 'armor'], '身体': ['裝備', 'armor'], '頭部': ['裝備', 'armor'], '头部': ['裝備', 'armor'], '防具': ['裝備', 'armor'], '飾品': ['飾品', 'accessory'], '饰品': ['飾品', 'accessory'], '近戰武器': ['武器', 'weapon'], '近战武器': ['武器', 'weapon'], '遠程武器': ['武器', 'weapon'], '远程武器': ['武器', 'weapon'], '武器': ['武器', 'weapon'], '彈藥': ['彈藥', 'ammo'], '弹药': ['彈藥', 'ammo'] };
for (const rec of items) {
  const name = T(rec.name).trim();
  if (!name) { report.skipped++; continue; }
  const key = norm(name);
  const typeKey = Object.keys(CAT).find((k) => String(rec.type || '').startsWith(k));
  const [dirName, code] = typeKey ? CAT[typeKey] : ['道具', 'tool'];
  const existing = itemByKey.get(key);
  if (existing) {
    const j = existing.data;
    const eff = new Set([...(j.eff || []), ...(rec.eff || []).map(T)].filter(Boolean));
    const cures = new Set([...(j.cures || []), ...(rec.cures || []).map(T)].filter(Boolean));
    if (!j.desc && rec.desc) j.desc = T(rec.desc);
    j.eff = [...eff];
    if (cures.size) j.cures = [...cures];
    writes.push({ path: existing.path, data: j });
  } else {
    const dir = path.join(ITEMS, dirName, name);
    const p = path.join(dir, 'item.json');
    const j = {
      i: 'item-' + name, n: name, c: code, e: '', p: String(rec.price || ''), sl: '', x: 'false',
      eff: (rec.eff || []).map(T).filter(Boolean)
    };
    if (rec.desc) j.desc = T(rec.desc);
    if ((rec.cures || []).length) j.cures = (rec.cures || []).map(T);
    writes.push({ path: p, data: j, mkdir: dir });
    itemByKey.set(key, { path: p, data: j, dir: dirName });
  }
  report.items++;
}

/* ── 樓層介紹 ── */
for (const rec of floors) {
  const fkey = fid(rec.floorId);
  const fl = floorById.get(fkey);
  if (!fl) { report.skipped++; continue; }
  const f = fl.data;
  const intro = T(rec.intro).trim();
  if (!intro) { report.skipped++; continue; }
  if (!String(f.doc || '').replace(/<[^>]*>/g, '').trim()) f.doc = '<p>' + intro.replace(/\n/g, '<br>') + '</p>';
  if (rec.palette && typeof rec.palette === 'object') {
    f.palette = Object.assign({}, f.palette || {}, rec.palette, { mode: rec.theme || 'dark' });
  }
  writes.push({ path: fl.path, data: f });
  report.floors++;
}

/* ── 出口 ── */
for (const rec of exits) {
  const fkey = fid(rec.floorId);
  const fl = floorById.get(fkey);
  if (!fl) { report.skipped++; continue; }
  const p = path.join(FLOORS, fl.dir, 'exits.json');
  const arr = pendingFiles.get(p) || (fs.existsSync(p) ? readJson(p) : []);
  arr.push({ event: T(rec.event), intro: T(rec.intro), transition: T(rec.transition), target: String(rec.target || ''), pending: !!rec.pending, _src: 'manual' });
  pendingFiles.set(p, arr);
  writes.push({ path: p, data: arr });
  report.exits++;

  if (rec.target) {
    const tgt = floorById.get(fid(rec.target));
    if (tgt) {
      const ep = path.join(FLOORS, tgt.dir, 'entrances.json');
      const earr = pendingFiles.get(ep) || (fs.existsSync(ep) ? readJson(ep) : []);
      const elist = Array.isArray(earr) ? earr : [];
      const fromId = fid(rec.floorId);
      if (!elist.some((e) => String(e.from) === String(fromId) && norm(e.event) === norm(rec.event))) {
        elist.push({ from: fromId, fromName: rec.floorName || '', event: rec.event || '', transition: rec.transition || '', _src: 'manual' });
        pendingFiles.set(ep, elist);
        writes.push({ path: ep, data: elist });
        report.entrances = (report.entrances || 0) + 1;
      }
    }
  }
}

/* ── 結局 ── */
for (const rec of endings) {
  const fkey = fid(rec.floorId);
  const fl = floorById.get(fkey);
  if (!fl) { report.skipped++; continue; }
  const p = path.join(FLOORS, fl.dir, 'endings.json');
  const arr = pendingFiles.get(p) || (fs.existsSync(p) ? readJson(p) : []);
  arr.push({ name: T(rec.name), category: rec.category || 'settle', lines: [T(rec.intro)].filter(Boolean), epilogue: T(rec.epilogue), _src: 'manual' });
  pendingFiles.set(p, arr);
  writes.push({ path: p, data: arr });
  report.endings++;
}

/* ── 寫入 ── */
console.log('manual dir:', manualDir);
console.log('found:', { events: events.length, items: items.length, floors: floors.length, exits: exits.length, endings: endings.length, pending: pending.length });
if (!confirm) {
  console.log('dry-run:', report);
  console.log('加 --confirm 才會寫入（會先備份）');
  process.exit(0);
}

const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const backupDir = path.join(BACKUP, 'manual-' + stamp);
const done = new Set();
for (const w of writes) {
  const rel = path.relative(ROOT, w.path);
  if (done.has(rel)) continue;
  done.add(rel);
  if (fs.existsSync(w.path)) {
    const bp = path.join(backupDir, rel);
    fs.mkdirSync(path.dirname(bp), { recursive: true });
    fs.copyFileSync(w.path, bp);
  }
}
for (const w of writes) {
  if (w.mkdir) fs.mkdirSync(w.mkdir, { recursive: true });
  fs.mkdirSync(path.dirname(w.path), { recursive: true });
  writeJson(w.path, w.data);
}
console.log('written:', report);
console.log('backup:', backupDir);


