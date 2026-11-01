// new.mjs — 快速新增條目（建立資料夾＋範本檔）
// 用法：
//   node content/new.mjs entity 竊皮者
//   node content/new.mjs character 白璃 狐狸
//   node content/new.mjs item 武器 骨刃短刀 11
//   node content/new.mjs floor 197 打烊的量販店
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const typeAlias = { entity: 'entity', 实体: 'entity', 實體: 'entity', character: 'character', 角色: 'character', item: 'item', 物品: 'item', floor: 'floor', 楼层: 'floor', 樓層: 'floor' };
const type = typeAlias[args[0]];
if (!type) {
  console.log('用法：node content/new.mjs <entity|character|item|floor> ...');
  process.exit(1);
}
const slug = s => String(s || '').replace(/[\\/:*?"<>|]/g, '＿').replace(/\s+/g, ' ').trim() || '未命名';
const write = (p, obj) => fs.writeFileSync(p, JSON.stringify(obj, null, 2) + '\n', 'utf8');
const mk = (...p) => { const d = path.join(HERE, ...p); fs.mkdirSync(d, { recursive: true }); return d; };

if (type === 'entity') {
  const name = args[1]; if (!name) { console.log('請給實體名稱'); process.exit(1); }
  const d = mk('entities', slug(name));
  const p = path.join(d, 'entity.json');
  if (fs.existsSync(p)) { console.log('已存在：' + p); process.exit(1); }
  write(p, { n: name, boss: false, skills: [], drops: [], resist: { res: '', nor: '', weak: '' }, desc: '' });
  console.log('已建立：' + p + '\n編輯後執行：node content/build.mjs');
}
else if (type === 'character') {
  const name = args[1], sp = args[2] || '自訂';
  if (!name) { console.log('請給角色名稱'); process.exit(1); }
  const d = mk('characters', slug(name));
  const p = path.join(d, 'character.json');
  if (fs.existsSync(p)) { console.log('已存在：' + p); process.exit(1); }
  write(p, { n: name, title: '', bio: '', attrs: '', skills: [], items: [], sp });
  const metaPath = path.join(HERE, 'characters/_meta.json');
  const meta = JSON.parse(fs.readFileSync(metaPath, 'utf8'));
  if (!meta.order.includes(name)) { meta.order.push(name); write(metaPath, meta); }
  console.log('已建立：' + p + '\n編輯後執行：node content/build.mjs');
}
else if (type === 'item') {
  const catArg = args[1], name = args[2], price = args[3] || '';
  const cats = { 武器: 'weapon', 裝備: 'armor', 饰品: 'accessory', 飾品: 'accessory', 道具: 'tool', 消耗品: 'consumable', 弹药: 'ammo', 彈藥: 'ammo' };
  const c = cats[catArg];
  if (!c || !name) { console.log('用法：node content/new.mjs item <武器|裝備|飾品|道具|消耗品|彈藥> 名稱 [價格]'); process.exit(1); }
  const d = mk('items', catArg, slug(name));
  const p = path.join(d, 'item.json');
  if (fs.existsSync(p)) { console.log('已存在：' + p); process.exit(1); }
  write(p, { i: 'item-' + name, n: name, e: '', c, p: String(price), sl: '', x: 'false', eff: [], desc: '' });
  const op = path.join(HERE, 'items/_order.json');
  const order = fs.existsSync(op) ? JSON.parse(fs.readFileSync(op, 'utf8')) : {};
  (order[catArg] = order[catArg] || []).push(name);
  write(op, order);
  console.log('已建立：' + p + '\n編輯後執行：node content/build.mjs');
}
else if (type === 'floor') {
  const id = args[1], name = args[2];
  if (!id || !name) { console.log('用法：node content/new.mjs floor <樓層ID> <名稱>'); process.exit(1); }
  const folder = slug(id + '-' + name);
  const d = mk('floors', folder);
  const p = path.join(d, 'floor.json');
  if (fs.existsSync(p)) { console.log('已存在：' + p); process.exit(1); }
  write(p, {
    key: id + '|' + name, id, name,
    info: { id, name, roman: '', items: [], desc: '' },
    doc: '', palette: null, paletteKey: null
  });
  fs.writeFileSync(path.join(d, 'design.css'), '/* ' + id + '｜' + name + ' 專屬頁面設計（只作用於 html[data-pal="' + id + '"]） */\n', 'utf8');
  console.log('已建立：' + p + '\n編輯後執行：node content/build.mjs');
}
