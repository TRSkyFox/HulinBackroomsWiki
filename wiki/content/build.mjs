// build.mjs — 由 content/ 產生 site/index.html（content 是唯一資料來源）
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const SITE = path.join(ROOT, 'site/index.html');
const readJSON = p => JSON.parse(fs.readFileSync(p, 'utf8'));
const dirs = p => fs.readdirSync(p, { withFileTypes: true }).filter(d => d.isDirectory() && !d.name.startsWith('_')).map(d => d.name);
const has = p => fs.existsSync(p);

/* 角色 */
const meta = readJSON(path.join(HERE, 'characters/_meta.json'));
const charMap = new Map();
for (const d of dirs(path.join(HERE, 'characters'))) {
  const p = path.join(HERE, 'characters', d, 'character.json');
  if (has(p)) { const c = readJSON(p); charMap.set(c.n, c); }
}
const chars = [];
for (const n of meta.order || []) if (charMap.has(n)) { chars.push(charMap.get(n)); charMap.delete(n); }
for (const c of charMap.values()) chars.push(c);

/* 實體 */
const ents = [];
for (const d of dirs(path.join(HERE, 'entities'))) {
  const p = path.join(HERE, 'entities', d, 'entity.json');
  if (has(p)) ents.push(readJSON(p));
}

/* 物品（依 _order.json 保留原順序） */
const order = has(path.join(HERE, 'items/_order.json')) ? readJSON(path.join(HERE, 'items/_order.json')) : {};
const items = [];
for (const cat of dirs(path.join(HERE, 'items'))) {
  const byName = new Map();
  for (const d of dirs(path.join(HERE, 'items', cat))) {
    const p = path.join(HERE, 'items', cat, d, 'item.json');
    if (has(p)) { const it = readJSON(p); byName.set(it.n, it); }
  }
  for (const n of order[cat] || []) if (byName.has(n)) { items.push(byName.get(n)); byName.delete(n); }
  for (const it of byName.values()) items.push(it);
}

/* 樓層
   「已收錄」的判定改成：這一層有真的正文（不是靠 _observations 這個手寫旗標）。
   2026-10-28 之前 53 層有正文卻因為沒有 _observations 而顯示「未探索」。 */
const FLOOR_INFO = {}, FLOOR_DOC = {}, FLOOR_PAL = {}, FLOOR_PAL_NAME = {}, FLOOR_DISCOVERED = {}, FLOOR_PARTIAL = {};
const FLOOR_MODE = {}, FLOOR_FONT = {};
const FLOOR_PAL_ROWS = [];
const FLOOR_SRC = {};
const plain = h => String(h || '').replace(/<[^>]*>/g, '').replace(/&nbsp;/g, ' ').replace(/\s+/g, ' ').trim();
const floorCss = [];
for (const d of dirs(path.join(HERE, 'floors'))) {
  const fp = path.join(HERE, 'floors', d, 'floor.json');
  if (!has(fp)) continue;
  const f = readJSON(fp);
  if (FLOOR_INFO[f.key]) throw new Error('重複樓層 key: ' + f.key);
  FLOOR_INFO[f.key] = f.info || {};
  FLOOR_DOC[f.key] = f.doc || '';
  const sid = String(f.id);
  if (f.theme && f.theme.mode) FLOOR_MODE[sid] = String(f.theme.mode);
  if (f.theme && f.theme.font) FLOOR_FONT[sid] = String(f.theme.font);
  (FLOOR_SRC[sid] = FLOOR_SRC[sid] || []).push({ dir: d, name: f.name, key: f.key });
  const text = plain(f.doc);
  if (text.length >= 10) FLOOR_DISCOVERED[f.key] = 1;
  /* 2026-10-30：門檻從 80 字放寬到 40。事件本來就收不齊（沒走到的分支不會出現在畫面上），
     用「字數不夠」當作「還沒收齊」只會讓每一層都掛一條橫幅，變成雜訊。 */
  /* 2026-10-31：_status=name-only 的樓層「只有名字」（遊戲內亂碼／無法進入），
   沒有正文是預期狀態，不標「只記錄到一小角」。 */
if (!f._introMissing && f._status !== 'name-only' && f._status !== 'id-only' && ((f._staging && f._staging.needsReview) || text.length < 40)) FLOOR_PARTIAL[f.key] = 1;
  if (f.palette) FLOOR_PAL_ROWS.push({ id: sid, key: f.key || (sid + '|' + f.name), paletteKey: f.paletteKey, palette: f.palette });
  const cp = path.join(HERE, 'floors', d, 'design.css');
  if (has(cp)) floorCss.push('/* ── ' + d + ' ── */\n' + fs.readFileSync(cp, 'utf8').trim() + '\n');
}
const DUP_IDS = Object.entries(FLOOR_SRC).filter(([, v]) => v.length > 1).map(([k]) => k);
/* 重複 id 的兩層不能共用同一個 palette key（1 的兩層會互相蓋色）。
   有重複 id 的樓層改存 id|name；沒有重複的仍用 id，保持既有 FLOOR_T／深連結不變。 */
for (const p of FLOOR_PAL_ROWS) {
  if (DUP_IDS.includes(String(p.id))) FLOOR_PAL_NAME[p.key] = p.palette;
  else FLOOR_PAL[p.paletteKey || p.id] = p.palette;
}

/* 商店／結局 */
const shops = [];
for (const d of dirs(path.join(HERE, 'shops'))) {
  const p = path.join(HERE, 'shops', d, 'shop.json');
  if (has(p)) shops.push(readJSON(p));
}
/* 結局：只讀各樓層資料夾的 endings.json。
   每一類都是「陣列」——以前 endings[cat] = e 是單槽，第二個壞結局會靜默蓋掉第一個。 */
const endings = { bad: [], core: [], good: [] };
const extraEndings = [];
const lost = { label: '迷失', name: '迷失', status: '部分', variants: [] };
for (const d of dirs(path.join(HERE, 'floors'))) {
  const ep = path.join(HERE, 'floors', d, 'endings.json');
  if (!has(ep)) continue;
  const floorPath = path.join(HERE, 'floors', d, 'floor.json');
  const floor = has(floorPath) ? readJSON(floorPath) : {};
  const arr = readJSON(ep);
  for (const raw of (Array.isArray(arr) ? arr : [arr])) {
    if (!raw || typeof raw !== 'object') continue;
    const e = { ...raw };
    if (floor.id != null && e.floorId != null && String(e.floorId) !== String(floor.id))
      throw new Error('結局檔樓層 ID 不符：' + ep + ' (' + e.floorId + ' != ' + floor.id + ')');
    const eid = String(e.floorId != null ? e.floorId : (floor.id != null ? floor.id : ''));
    if (DUP_IDS.includes(eid)) throw new Error('樓層 id 重複且有 endings.json，會混層：' + ep);
    /* 舊資料的單數 own（字串）轉成 owns 陣列，否則模板不會顯示。 */
    if (typeof e.own === 'string' && e.own.trim()) {
      e.owns = Array.isArray(e.owns) ? e.owns : [];
      e.owns.push({ who: '', t: e.own });
      delete e.own;
    }
    const cat = e.category || 'extra';
    delete e.category;
    if (cat === 'lost') {
      if (e.label) lost.label = e.label;
      if (e.name) lost.name = e.name;
      if (e.status) lost.status = e.status;
      lost.variants.push(e);
    } else if (cat === 'extra') {
      extraEndings.push(e);
    } else if (endings[cat]) {
      endings[cat].push(e);
    } else {
      extraEndings.push(e);   /* 未知分類不要遺失 */
    }
  }
}
if (lost.variants.length) endings.lost = lost;
if (extraEndings.length) endings.extra = extraEndings;
const FLOOR_NAME = readJSON(path.join(HERE,'floors/_names.json'));
for (const f of Object.values(FLOOR_INFO)) { if (f && f.id && !FLOOR_NAME[f.id]) FLOOR_NAME[f.id] = f.name || ''; }
const CP = { chars, perms: meta.perms || [], stats: meta.stats || [] };
const CP_SKILLS = readJSON(path.join(HERE, 'characters/_skills.json'));
const CHAR_EXTRA = readJSON(path.join(HERE, 'characters/_extra.json'));
const PAGES = readJSON(path.join(HERE, 'pages/pages.json'));
const RECIPES = readJSON(path.join(HERE, 'recipes/recipes.json'));
const ENCHANTS = readJSON(path.join(HERE, 'enhancements/enhancements.json'));

/* 事件圖：只讀每個樓層資料夾自己的 events.json。
   不再使用 content/events.json 這個全域檔。 */
const EVENT_GRAPH = { floors: {} };
for (const d of dirs(path.join(HERE, 'floors'))) {
  const evPath = path.join(HERE, 'floors', d, 'events.json');
  if (!has(evPath)) continue;
  const ev = readJSON(evPath);
  const floorPath = path.join(HERE, 'floors', d, 'floor.json');
  const floor = has(floorPath) ? readJSON(floorPath) : {};
  const fid = String(ev.floorId != null ? ev.floorId : (floor.id != null ? floor.id : ''));
  if (!fid) continue;
  if (DUP_IDS.includes(String(fid))) throw new Error('樓層 id 重複且有 events.json，事件會混層：' + evPath);
  if (floor.id != null && ev.floorId != null && String(ev.floorId) !== String(floor.id))
    throw new Error('事件檔樓層 ID 不符：' + evPath + ' (' + ev.floorId + ' != ' + floor.id + ')');
  const prev = EVENT_GRAPH.floors[fid];
  const next = {
    floorId: fid,
    floorName: ev.floorName || floor.name || (prev && prev.floorName) || ('Level ' + fid),
    junctions: ev.junctions || [],
    events: ev.events || []
  };
  if (prev) {
    next.junctions = [...(prev.junctions || []), ...next.junctions];
    next.events = [...(prev.events || []), ...next.events];
  }
  EVENT_GRAPH.floors[fid] = next;
}

/* 覆蓋率：2026-10-30 移除「用 progress.total 比事件數」那套。
   `出口進度 2/15` 的 15 是「這一層的出口節點總數」，跟事件條數無關；
   拿它跟 events.length 相減只會讓每一層都被標成「還沒收齊」——
   而事件本來就不可能收齊（沒走到的分支不會出現在畫面上）。
   現在只在一種情況標「收得很少」：整層正文短到 40 字以下（等於只掃到一角）。 */
const COVERAGE = {};
for (const [fid, g] of Object.entries(EVENT_GRAPH.floors)) {
  const docText = String(FLOOR_DOC[fid] || '').replace(/<[^>]*>/g, '');
  if (docText.length > 0 && docText.length < 40) {
    COVERAGE[fid] = { docChars: docText.length, reason: 'doc-too-short' };
    for (const s of (FLOOR_SRC[fid] || [])) FLOOR_PARTIAL[s.key] = 1;
  }
}

/* 讀模板 */
let html = fs.readFileSync(path.join(HERE, 'template.html'), 'utf8');
const data = { CP, CP_ENT: ents, ITEMS: items, FLOOR_INFO, FLOOR_DOC, FLOOR_PAL, FLOOR_PAL_NAME, FLOOR_DISCOVERED, FLOOR_PARTIAL, FLOOR_NAME, FLOOR_MODE, FLOOR_FONT, SHOPS: shops, END_DATA: endings, PAGES, RECIPES, ENCHANTS, CP_SKILLS, CHAR_EXTRA, EVENT_GRAPH };
for (const [name, value] of Object.entries(data)) {
  const token = '__WIKI_' + name + '__';
  if (!html.includes(token)) throw new Error('模板缺少 token: ' + token);
  const json = JSON.stringify(value).replace(/<\/script/gi, '<\\/script');
  html = html.replace(token, () => json);
}

/* 樓層設計：以 content/floors/<folder>/design.css 重建整段 */
const begin = '/* FLOOR-DESIGN:BEGIN（由 _data/floors/*/design.css 產生，不要手改） */';
const end = '/* FLOOR-DESIGN:END */';
const i = html.indexOf(begin), j = html.indexOf(end);
if (i < 0 || j < 0) throw new Error('找不到 FLOOR-DESIGN 區塊');
const block = begin + '\n\n' + floorCss.join('\n') + '\n' + end;
html = html.slice(0, i) + block + html.slice(j + end.length);

if (!has(path.join(ROOT, 'site/index.backup-before-content.html'))) {
  fs.copyFileSync(SITE, path.join(ROOT, 'site/index.backup-before-content.html'));
}
fs.writeFileSync(SITE, html, 'utf8');
console.log(JSON.stringify({
  characters: chars.length,
  entities: ents.length,
  items: items.length,
  floors: Object.keys(FLOOR_INFO).length,
  partial: Object.keys(FLOOR_PARTIAL).length,
  coverage: COVERAGE,
  shops: shops.length,
  endings: Object.keys(endings).length,
  floorCss: floorCss.length,
  bytes: Buffer.byteLength(html)
}, null, 2));
