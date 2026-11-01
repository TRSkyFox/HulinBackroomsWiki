import fs from 'node:fs';
import path from 'node:path';

/*
 * 檢查 staging 是否可以合併進正式 Wiki。
 * 不呼叫 AI；只用既有 content 的 id/name 與欄位完整度。
 *
 * node validate.mjs <stagingDir>
 */

const staging = process.argv[2];
if (!staging) { console.error('usage: node validate.mjs <stagingDir>'); process.exit(2); }
const root = path.resolve(staging);
const content = 'D:/BACKROOMS/wiki/content';

function readJson(p) { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return null; } }
function walk(dir, name, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, name, out);
    else if (e.name === name) out.push(p);
  }
  return out;
}

const existingFloors = walk(path.join(content, 'floors'), 'floor.json').map(readJson).filter(Boolean);
const existingEntities = walk(path.join(content, 'entities'), 'entity.json').map(readJson).filter(Boolean);
const existingFloorIds = new Map(existingFloors.map(f => [String(f.id), f]));
const existingEntityNames = new Set(existingEntities.map(e => String(e.n || '')));

const result = { staging: root, checkedAt: new Date().toISOString(), errors: [], warnings: [], ok: true, floors: [], entities: [] };

for (const p of walk(path.join(root, 'floors'), 'floor.json')) {
  const f = readJson(p);
  if (!f) { result.errors.push('無法讀取 ' + p); continue; }
  const id = String(f.id || '');
  const item = { id, name: f.name, file: p, errors: [], warnings: [] };
  if (!id) item.errors.push('缺 id');
  if (!f.name) item.errors.push('缺 name');
  const ex = existingFloorIds.get(id);
  if (ex) {
    if (String(ex.name) !== String(f.name)) item.warnings.push('與既有樓層名稱不同：' + ex.name + ' / ' + f.name);
    item.existing = true;
  }
  const pal = f.palette || {};
  const need = ['bg', 'card', 'acc', 'tx'];
  for (const k of need) if (!(pal[k] || ({bg:pal.background,card:pal.panel,acc:pal.accent,tx:pal.text})[k])) item.warnings.push('palette 缺 ' + k);
  if (!f.doc || f.doc.length < 30) item.warnings.push('介紹太短或空');
  const conf = f._staging && f._staging.confidence;
  if (conf != null && conf < 0.8) item.warnings.push('confidence ' + conf);
  if (item.errors.length) { result.ok = false; result.errors.push(...item.errors.map(x => f.name + ': ' + x)); }
  result.warnings.push(...item.warnings.map(x => f.name + ': ' + x));
  result.floors.push(item);
}

for (const p of walk(path.join(root, 'entities'), 'entity.json')) {
  const e = readJson(p);
  if (!e) { result.errors.push('無法讀取 ' + p); continue; }
  const item = { name: e.n, file: p, errors: [], warnings: [] };
  if (!e.n) item.errors.push('缺 n');
  if (existingEntityNames.has(String(e.n))) item.warnings.push('實體已存在，合併前要確認');
  if (!e.desc) item.warnings.push('缺介紹');
  if (!Array.isArray(e.skills) || !e.skills.length) item.warnings.push('缺技能');
  if (!Array.isArray(e.drops) || !e.drops.length) item.warnings.push('缺掉落');
  if (!e.resist || !Array.isArray(e.resist.notes) || !e.resist.notes.length) item.warnings.push('缺抗性');
  if (item.errors.length) { result.ok = false; result.errors.push(...item.errors.map(x => e.n + ': ' + x)); }
  result.warnings.push(...item.warnings.map(x => e.n + ': ' + x));
  result.entities.push(item);
}

fs.writeFileSync(path.join(root, 'validation.json'), JSON.stringify(result, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ ok: result.ok, errors: result.errors.length, warnings: result.warnings.length, floors: result.floors.length, entities: result.entities.length }, null, 2));
if (!result.ok) process.exit(1);
