import fs from 'node:fs';
import path from 'node:path';
import { text, paragraphs, floorName, safePath, looksGarbled, cleanResults } from './text.mjs';

/*
 * records.jsonl -> Wiki staging（不直接改 formal content）
 * 用法：node import-wiki.mjs <records.jsonl> [--out <dir>]
 *
 * 2026-10-28：簡→繁改由 i18n.mjs 處理（字級反推會把「四面」變「四麵」）。
 * 下面只留「已知的 OCR 形近錯字」，不是簡繁轉換。
 */

function esc(s) {
  return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
/* 檔名／目錄名一律走 safePath：它會清掉 ] 「 」 這些會破壞 id|name 的字元。 */
function safe(s) {
  return safePath(s);
}
function readJsonl(file) {
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).map(s => JSON.parse(s));
}

/* 文字清洗只剩一個入口（text.mjs）；這裡保留舊名字讓下面的程式碼不必大改。 */
const hant = s => text(s);

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node import-wiki.mjs <records.jsonl> [--out <dir>]'); process.exit(2); }
const src = path.resolve(args[0]);
const oi = args.indexOf('--out');
const out = oi >= 0 ? path.resolve(args[oi + 1]) : path.join(path.dirname(src), 'wiki-staging');
fs.mkdirSync(path.join(out, 'floors'), { recursive: true });
fs.mkdirSync(path.join(out, 'entities'), { recursive: true });
fs.mkdirSync(path.join(out, 'events'), { recursive: true });
fs.mkdirSync(path.join(out, 'evidence', 'entities'), { recursive: true });
fs.mkdirSync(path.join(out, 'evidence', 'floors'), { recursive: true });

const records = readJsonl(src);
/* <session>/structured/records.jsonl → <session>；fixture 也適用。 */
const sessionDir = path.dirname(path.dirname(src));
function normalizePalette(p){
  if(!p) return null;
  const bg=p.bg||p.background, card=p.card||p.panel, acc=p.acc||p.accent, tx=p.tx||p.text;
  return {...p, bg, card, acc, tx, background:bg||p.background, panel:card||p.panel, accent:acc||p.accent, text:tx||p.text};
}
function existingPalette(id){
  /* 舊行為：從正式庫用同一個 id 抓配色。
     結果 Level 1（停車區）拿到 Level 0（大廳）的棕色，兩層底色一模一樣。
     跨層配色一律不採用；沒有本場次證據就只留空。 */
  return null;
}

/* 樓層名的比對一律「先繁化再比」：配色表/floor-colors.log 是 OCR 原文（简体），
   正式樓層名是繁體，直接比字串永遠對不上。 */
const nmOf = s => floorName(text(s));

/* 樓層底色：截圖取樣被刪掉之後，floor-colors.log 是唯一還留著的現場證據。
   日誌一行長這樣（2026-10-30 的格式）：
     2026-10-30 12:10:47  #4B4424  rgb(75,68,36)  Level 0 「大厅」  牆色=#332D18  幀#37
   `#4B4424` 是 main 面板內側的顏色（卡片色），`牆色=` 才是面板外面的牆。
   以前這裡的正則要的是「樓層：Level … 配色=#…」，跟實際格式對不上 → 永遠抓不到，
   所以配色一路走截圖那條，才會出現近黑/白色/張冠李戴的底色。 */
function parseColorLog(sessionDir) {
  try {
    const log = path.join(sessionDir, 'floor-colors.log');
    if (!fs.existsSync(log)) return [];
    const out = [];
    for (const line of fs.readFileSync(log, 'utf8').split(/\r?\n/)) {
      const c = line.match(/(#[0-9A-Fa-f]{6})\s+rgb\(/);
      if (!c) continue;
      const nm = line.match(/Level\s*[\d.]+\s*「([^」]*)」/);
      const wall = line.match(/牆色=(#[0-9A-Fa-f]{6})/);
      out.push({ color: c[1].toUpperCase(), wall: wall ? wall[1].toUpperCase() : '', name: nm ? nm[1] : '' });
    }
    return out;
  } catch { return []; }
}

function modeOf(list) {
  const counts = new Map();
  for (const v of list) if (v) counts.set(v, (counts.get(v) || 0) + 1);
  const best = [...counts.entries()].sort((a, b) => b[1] - a[1])[0];
  return best ? best[0] : '';
}

function wallBg(sessionDir, name) {
  const want = nmOf(name);
  if (!want) return '';
  return modeOf(parseColorLog(sessionDir).filter(r => r.wall && nmOf(r.name) === want).map(r => r.wall));
}

function observedBg(sessionDir, name) {
  const want = nmOf(name);
  if (!want) return '';
  return modeOf(parseColorLog(sessionDir).filter(r => nmOf(r.name) === want).map(r => r.color));
}

/* 亮度：太黑（轉場/黑幕）或太白（載入畫面）都不能當底色 */
function luminance(hex) {
  const m = /^#?([0-9A-Fa-f]{6})$/.exec(String(hex || ''));
  if (!m) return -1;
  const n = parseInt(m[1], 16);
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
  return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
}
function saneBg(hex) {
  const l = luminance(hex);
  return l >= 0.04 && l <= 0.94;
}
function rgbDist(a, b) {
  const pa = parseInt(String(a).replace('#', ''), 16), pb = parseInt(String(b).replace('#', ''), 16);
  if (!Number.isFinite(pa) || !Number.isFinite(pb)) return 999;
  return Math.hypot(((pa >> 16) & 255) - ((pb >> 16) & 255), ((pa >> 8) & 255) - ((pb >> 8) & 255), (pa & 255) - (pb & 255));
}

/* 這一層到底能用哪份配色：
   1. 這一場有量到「牆色」（面板外側）→ 用牆色當 bg；
      截圖那份配色若跟牆色一致（距離 ≤ 40），卡片/強調色一起沿用，否則只留牆色。
   2. 否則用截圖配色，但底色必須合理（不能近黑/近白）。
   3. 都沒有 → null，讓站點用預設主題，不要假裝有配色 */
function paletteFor(r, sessionDir, hName) {
  const p = r.palette && (r.palette.bg || r.palette.background) ? r.palette : null;
  const palName = nmOf(r.paletteName || '');
  const want = nmOf(hName || r.name || '');
  const shot = (p && (!palName || palName === want)) ? normalizePalette(p) : null;

  const wall = wallBg(sessionDir, want || r.rawName || '');
  if (wall && saneBg(wall)) {
    if (shot && shot.bg && saneBg(shot.bg) && rgbDist(shot.bg, wall) <= 40) {
      return { ...shot, bg: wall, background: wall, _derivedFrom: 'wall-sample' };
    }
    return normalizePalette({ bg: wall, _derivedFrom: 'wall-sample' });
  }

  if (shot && shot.bg && saneBg(shot.bg)) return shot;

  const bg = observedBg(sessionDir, want || r.rawName || '');
  if (bg && saneBg(bg)) return normalizePalette({ bg, _derivedFrom: 'floor-colors.log' });
  return null;
}

function bigrams(s){ const n=String(s||'').replace(/\s+/g,''); const m=new Map(); for(let i=0;i<n.length-1;i++){const g=n.slice(i,i+2);m.set(g,(m.get(g)||0)+1);} return m; }
function simName(a,b){ const A=bigrams(a),B=bigrams(b); if(!A.size||!B.size) return 0; let x=0; for(const k of A.keys()) if(B.has(k)) x++; return x/Math.max(A.size,B.size); }
function loadEntityIndex(){
  const root='D:/BACKROOMS/wiki/content/entities'; const list=[];
  if(!fs.existsSync(root)) return list;
  for(const d of fs.readdirSync(root,{withFileTypes:true})){
    if(!d.isDirectory()) continue;
    const f=path.join(root,d.name,'entity.json');
    if(!fs.existsSync(f)) continue;
    try{ const o=JSON.parse(fs.readFileSync(f,'utf8')); if(o.n) list.push(o); }catch{}
  }
  return list;
}
const ENTITIES = loadEntityIndex();
const ENEMY_PREFIXES = ['時間錯亂','时间错乱','越打越兇','越打越凶','不可名狀','不可名状','惡夢般','恶梦般','強壯','强壮','恐怖','敏捷','堅硬','坚硬','殘影','残影','守護','守护','呼喚','呼唤','獵殺','猎杀','吞噬','群體','群体','脆弱','統率','统率','吞光','適應','适应','不滅','不灭','貪婪','贪婪','帶刺','带刺','粘人','巨大','不死','嗜血','飢餓','饥饿'].sort((a,b)=>b.length-a.length);
function stripEnemyPrefix(s){
  let out=String(s||'').trim();
  for(let changed=true;changed;){
    changed=false;
    for(const p of ENEMY_PREFIXES){
      const re=new RegExp('^'+p+'的?\\s*');
      if(re.test(out)){ out=out.replace(re,'').trim(); changed=true; break; }
    }
  }
  return out;
}

function stripUnitSuffix(s){ return String(s||'').replace(/\s*[A-Za-z]$/,'').trim(); }
function canonicalEntity(name){
  let raw=String(name||'').trim();
  if(!raw) return raw;
  {
    const parts=raw.split(/[、,，]/).map(x=>x.trim()).filter(Boolean);
    const bases=parts.map(x=>stripEnemyPrefix(stripUnitSuffix(x))).filter(Boolean);
    if (bases.length && bases.length===parts.length && bases.every(x=>x===bases[0]) && bases[0].length>=2) raw=bases[0];
  }
  raw=stripEnemyPrefix(raw);
  const compact=raw.replace(/\s+/g,'');
  const alias=[['嗡鳴體','嗡鳴體'],['嘯鳴體','嗡鳴體'],['無面者','無面者'],['微笑夢','微笑夢魘'],['獵犬','獵犬'],['流浪者','流浪者的幻影'],['潦浪者','流浪者的幻影'],['回聲人影','回聲人影']];
  for(const [pre,canon] of alias) if(compact.includes(pre)) return canon;
  const parts=raw.split(/[、,，]/).map(x=>x.trim()).filter(Boolean);
  if(parts.length>1){
    const base=parts.map(x=>x.replace(/\s*[ABＡＢ]$/,'').trim());
    if(!base.every(x=>x===base[0])) return hant(raw); // 不同敵人，先不合併
  }
  const cand=[raw, raw.replace(/\s*[ABＡＢ]$/,'').trim()];
  if(parts.length>1){ const b=parts[0].replace(/\s*[ABＡＢ]$/,'').trim(); if(b && !cand.includes(b)) cand.push(b); }
  for(const c of cand){ const hit=ENTITIES.find(e=>e.n===c); if(hit) return hit.n; }
  let best=null,bestScore=0;
  for(const e of ENTITIES){ for(const c of cand){ const s=simName(c,e.n); if(s>bestScore){bestScore=s;best=e;} } }
  if(best && bestScore>=0.62) return best.n;
  return hant(raw);
}


function bump(map, key) { if (!key) return; map.set(key, (map.get(key) || 0) + 1); }
function byCount(map) { return [...map.entries()].sort((a,b) => b[1]-a[1] || b[0].length-a[0].length); }
function pickText(map) { const c = byCount(map); return c.length ? c[0][0] : ''; }

const report = { source: src, floors: [], entities: [], choices: [], warnings: [] };

for (const r of records.filter(x => x.type === 'floor')) {
  const id = String(r.floorId || 'unknown');
  const rawName = String(r.rawName || r.name || '');
  const intro = String(r.intro || '').trim();
  if (!rawName) { report.warnings.push('floor 缺名稱：' + r.id); continue; }
  /* 名字是 key 的一部分：先過 text()（繁化／玩家名／標點），再過 floorName()（去括號）。
     `停車區]` 這種名字會讓站點的 id|name 對不上，樓層明明打過卻顯示「鎖住」。 */
  const hName = floorName(text(rawName));
  if (!hName) { report.warnings.push('floor 名稱清完是空的：' + rawName); continue; }
  const paras = paragraphs(intro);
  const pal = paletteFor(r, sessionDir, hName);
  const dir = path.join(out, 'floors', safe(id + '-' + hName));
  fs.mkdirSync(dir, { recursive: true });
  const floor = {
    key: id + '|' + hName,
    id, name: hName,
    info: { id, name: hName },
    /* 一段一個 <br>，不是一行一個 —— OCR 的視覺換行不能變成斷詞。 */
    doc: paras.length ? '<h4>環境</h4>\n<blockquote>' + paras.map(esc).join('<br>') + '</blockquote>' : '',
    palette: pal,
    _staging: {
      source: r.source, confidence: r.confidence, needsReview: (r.confidence || 0) < 0.8,
      rawName, rawIntro: intro,
      paletteSource: pal ? (pal._derivedFrom || 'floor-shot') : 'none'
    }
  };
  fs.writeFileSync(path.join(dir, 'floor.json'), JSON.stringify(floor, null, 2) + '\n', 'utf8');
  fs.appendFileSync(path.join(out, 'evidence', 'floors', safe(id) + '.jsonl'), JSON.stringify({ type: 'floor', rawName, rawIntro: intro, confidence: r.confidence, source: r.source }) + '\n', 'utf8');
  report.floors.push({ id, name: hName, confidence: r.confidence, needsReview: floor._staging.needsReview, palette: floor._staging.paletteSource });
}

/* 實體名必須像「名字」。OCR 常把一整句動作讀成敵人：
   「你在四周翻了翻」「他倒在地上」「浪者的」。這些進了正式庫就是垃圾，
   以前只是 validation 的一行 warning，照樣往下走。 */
function badEntityName(s) {
  const t = String(s || '').trim();
  if (!t) return true;
  if (/^[你我他她它牠]/.test(t)) return true;
  if (/[。！？；，]/.test(t)) return true;
  if (/的$/.test(t)) return true;
  if (t.length > 14) return true;
  return false;
}

const entityGroups = new Map();
for (const r of records.filter(x => x.type === 'combat')) {
  const e = r.enemy || {};
  if (!e.name) continue;
  if (badEntityName(e.name)) { report.warnings.push('跳過非實體名：' + e.name); continue; }
  let hName = canonicalEntity(e.name);
  if (/嗡鳴體|嘯鳴體|嗡鸣体/.test(hName)) hName = '嗡鳴體';
  if (/微笑夢|微笑梦/.test(hName)) hName = '微笑夢魘';
  const g = entityGroups.get(hName) || { n: hName, skills: [], desc: '', drops: [], notes: [], confidence: 0, source: r.source, rawName: e.name, records: 0, skillCount: new Map(), dropCount: new Map(), noteCount: new Map(), descCount: new Map() };
  for (const n of e.skills || []) { const h = hant(n); if (h && !g.skills.includes(h)) g.skills.push(h); if (h) bump(g.skillCount, h); }
  const intro = hant(e.intro || '');
  if (intro) { bump(g.descCount, intro); if (intro.length > g.desc.length) g.desc = intro; }
  for (const d of e.drops || []) { const h = hant(d).trim(); if (h && !g.drops.includes(h)) g.drops.push(h); if (h) bump(g.dropCount, h); }
  for (const n of e.resistances || []) { const h = hant(n).trim(); if (h && !g.notes.includes(h)) g.notes.push(h); if (h) bump(g.noteCount, h); }
  fs.appendFileSync(path.join(out, 'evidence', 'entities', safe(hName) + '.jsonl'), JSON.stringify({ type: 'entity', rawName: e.name, intro: e.intro || '', skills: e.skills || [], drops: e.drops || [], resistances: e.resistances || [], confidence: r.confidence, source: r.source }) + '\n', 'utf8');
  g.confidence = Math.max(g.confidence, r.confidence || 0);
  g.records++;
  entityGroups.set(hName, g);
}
for (const g of entityGroups.values()) {
  const dir = path.join(out, 'entities', safe(g.n));
  fs.mkdirSync(dir, { recursive: true });
  const curated = ENTITIES.find(e => e.n === g.n);
  const skills = [...new Set([...(curated && curated.skills ? curated.skills.map(s => typeof s === 'string' ? s : s.n).filter(Boolean) : []), ...byCount(g.skillCount).map(x => x[0])])];
  const drops = [...new Set([...(curated && curated.drops ? curated.drops : []), ...byCount(g.dropCount).map(x => x[0])])];
  const notes = [...new Set([...(curated && curated.resist && curated.resist.notes ? curated.resist.notes : []), ...byCount(g.noteCount).map(x => x[0])])];
  const desc = (curated && curated.desc) || pickText(g.descCount) || g.desc;
  const entity = {
    n: g.n,
    skills: skills.map(n => ({ n, eff: '' })),
    desc,
    drops,
    resist: { notes },
    _staging: { source: g.source, confidence: g.confidence, needsReview: g.confidence < 0.8, rawName: g.rawName, records: g.records,
      evidence: { skills: Object.fromEntries(byCount(g.skillCount)), drops: Object.fromEntries(byCount(g.dropCount)), resist: Object.fromEntries(byCount(g.noteCount)), descVariants: byCount(g.descCount).length } }
  };
  fs.writeFileSync(path.join(dir, 'entity.json'), JSON.stringify(entity, null, 2) + '\n', 'utf8');
  report.entities.push({ name: g.n, confidence: g.confidence, needsReview: g.confidence < 0.8 });
}

const choiceFiles = new Map();
for (const r of records.filter(x => x.type === 'choice')) {
  const floor = String(r.floor || 'unknown');
  const file = path.join(out, 'events', safe('floor-' + floor + '-choices.jsonl'));
  const h = { ...r, prompt: hant(r.prompt), options: r.options.map(o => ({ ...o, text: hant(o.text) })), selected: r.selected ? { ...r.selected, text: hant(r.selected.text) } : null, result: cleanResults(r.result) };
  if (!choiceFiles.has(file)) choiceFiles.set(file, []);
  choiceFiles.get(file).push(h);
  report.choices.push({ floor, prompt: h.prompt, options: h.options.length, selected: h.selected, confidence: r.confidence });
}
for (const [file, arr] of choiceFiles) fs.writeFileSync(file, arr.map(x => JSON.stringify(x)).join('\n') + '\n', 'utf8');

fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ out, floors: report.floors.length, entities: report.entities.length, choices: report.choices.length, warnings: report.warnings.length }, null, 2));
