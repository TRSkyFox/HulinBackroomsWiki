import fs from 'node:fs';
import path from 'node:path';
import { CONTENT } from './paths.mjs';
import { floorName, safePath } from './text.mjs';

/*
 * 最終合併：把 staging 的觀測資料併入正式 content。
 * 原則是「只增不減」：
 *  - 既有 desc / skills / resist.res / resist.weak 不改
 *  - 只追加 drops、skills（同名保留正式版）、_observations
 *  - 低信心或新樓層不自動建立
 *
 * node merge-final.mjs <stagingDir> --confirm
 */

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node merge-final.mjs <stagingDir> --confirm'); process.exit(2); }
const staging = path.resolve(args[0]);
const confirm = args.includes('--confirm');
/* 預設不把事件塞進樓層 doc：事件的唯一來源是 floors/<層>/events.json。
   舊的 AUTO-EVENTS 機制會產生第二份事件（而且 verify-final 明確禁止），
   只有真的要除錯時才加 --with-doc-events。 */
const writeDocEvents = args.includes('--with-doc-events');
const content = CONTENT;
const valPath = path.join(staging, 'validation.json');
if (!fs.existsSync(valPath)) { console.error('缺 validation.json'); process.exit(2); }
const val = JSON.parse(fs.readFileSync(valPath, 'utf8'));
if (!val.ok) { console.error('validation 不通過，拒絕合併'); process.exit(1); }

function read(p) { return JSON.parse(fs.readFileSync(p, 'utf8')); }
function write(p, o) { fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, JSON.stringify(o, null, 2) + '\n', 'utf8'); }
function uniq(a) { return [...new Set((a || []).filter(Boolean))]; }

/* 配色必須「能用」才寫進正式庫。
   2026-10-29 21:22 那場：自動截圖取到的是角色建立畫面（白底），
   大廳的配色被寫成 bg=#FCFCFC、tx=#FCFCFC —— 白底白字，頁面直接看不見。
   判準只看兩件事：前後景對比夠不夠、是不是白底白字。 */
function lumOf(hex) {
  const m = String(hex || '').match(/^#?([0-9a-fA-F]{6})$/);
  if (!m) return NaN;
  const n = parseInt(m[1], 16);
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255;
}
function usablePalette(p) {
  if (!p) return false;
  const bg = lumOf(p.bg || p.background), tx = lumOf(p.tx || p.text);
  if (!Number.isFinite(bg) || !Number.isFinite(tx)) return false;
  if (bg > 0.92 && tx > 0.92) return false;      // 白底白字
  if (Math.abs(bg - tx) < 0.12) return false;    // 對比不足，一定是取樣錯了
  return true;
}

const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const backup = path.join(root, '_backup', 'final-' + stamp);
const report = { staging, stamp, entities: [], floors: [], skipped: [], backup };

// index formal
const entityDirs = fs.readdirSync(path.join(content, 'entities'), { withFileTypes: true }).filter(d => d.isDirectory());
const entityIndex = new Map();
for (const d of entityDirs) {
  const p = path.join(content, 'entities', d.name, 'entity.json');
  if (!fs.existsSync(p)) continue;
  const o = read(p);
  if (o.n) entityIndex.set(o.n, p);
}
const floorDirs = fs.readdirSync(path.join(content, 'floors'), { withFileTypes: true }).filter(d => d.isDirectory());
const floorIndex = new Map();
for (const d of floorDirs) {
  const p = path.join(content, 'floors', d.name, 'floor.json');
  if (!fs.existsSync(p)) continue;
  const o = read(p);
  if (o.id != null) floorIndex.set(String(o.id), p);
}

function backupFile(p) {
  const rel = path.relative(content, p).replace(/[\\/]/g, '__');
  fs.mkdirSync(backup, { recursive: true });
  fs.copyFileSync(p, path.join(backup, rel));
}

// entities
for (const d of fs.readdirSync(path.join(staging, 'entities'), { withFileTypes: true }).filter(x => x.isDirectory())) {
  const sp = path.join(staging, 'entities', d.name, 'entity.json');
  if (!fs.existsSync(sp)) continue;
  const s = read(sp);
  if (!s.n) continue;
  const conf = (s._staging && s._staging.confidence) || 0;
  if (conf < 0.7) { report.skipped.push({ type: 'entity', name: s.n, reason: 'confidence ' + conf }); continue; }
  const ep = entityIndex.get(s.n);
  if (!ep) { report.skipped.push({ type: 'entity', name: s.n, reason: 'no existing formal page' }); continue; }
  const before = read(ep);
  const after = JSON.parse(JSON.stringify(before));
  after.drops = uniq([...(before.drops || []), ...(s.drops || [])]);
  const haveSkill = new Set((before.skills || []).map(x => typeof x === 'string' ? x : x.n));
  after.skills = [...(before.skills || [])];
  for (const sk of s.skills || []) {
    const n = typeof sk === 'string' ? sk : sk.n;
    if (n && !haveSkill.has(n)) { after.skills.push(typeof sk === 'string' ? { n, eff: '' } : sk); haveSkill.add(n); }
  }
  after._observations = {
    source: 'structured-staging',
    staging: staging,
    rawName: s._staging && s._staging.rawName,
    evidence: s._staging && s._staging.evidence,
    resistNotes: (s.resist && s.resist.notes) || []
  };
  if (!confirm) { report.entities.push({ name: s.n, dryRun: true, dropsAdded: after.drops.length - (before.drops || []).length }); continue; }
  backupFile(ep);
  write(ep, after);
  report.entities.push({ name: s.n, dropsAdded: after.drops.length - (before.drops || []).length, skillsAdded: after.skills.length - (before.skills || []).length });
}

// floors：既有樓層只補資料；沒有正式頁的新樓層也建立（低信心會在 _staging 標 needsReview）
function dataFloorMeta(id) {
  const nf = path.join(content, 'floors', '_names.json');
  if (!fs.existsSync(nf)) return {};
  const names = read(nf);
  return { name: names[String(id)] || '' };
}
const seenFloorIds = new Set();
for (const d of fs.readdirSync(path.join(staging, 'floors'), { withFileTypes: true }).filter(x => x.isDirectory())) {
  const sp = path.join(staging, 'floors', d.name, 'floor.json');
  if (!fs.existsSync(sp)) continue;
  const s = read(sp);
  if (!s.id) continue;
  const sid = String(s.id);
  if (seenFloorIds.has(sid)) { report.skipped.push({ type: 'floor', name: s.name, reason: 'duplicate id ' + sid }); continue; }
  seenFloorIds.add(sid);
  const conf = (s._staging && s._staging.confidence) || 0;
  const fp = floorIndex.get(String(s.id));
  if (!fp) {
    /* 新樓層：只有信心 >= 0.8 且真的有介紹才建立。
       以前的版本連 0.55 的單幀 OCR 都會直接建成正式頁（Level 0.22 就是這樣來的）。 */
    if (conf < 0.8) { report.skipped.push({ type: 'floor', name: s.name || s.id, reason: 'new floor confidence ' + conf + ' < 0.8' }); continue; }
    if (!s.doc || String(s.doc).length < 40) { report.skipped.push({ type: 'floor', name: s.name || s.id, reason: 'new floor has no intro' }); continue; }
    const meta = dataFloorMeta(s.id);
    /* 名字只走 floorName()：括號、引號、尾標點都不該進 key 跟目錄名。
       `1-停車區]` 就是這樣來的——站點的 id|name 對不上，樓層明明打過卻顯示鎖住。 */
    const name = floorName(meta.name || s.name || ('Level ' + s.id)) || ('Level ' + s.id);
    const dir = path.join(content, 'floors', safePath(String(s.id) + '-' + name));
    const np = path.join(dir, 'floor.json');
    if (fs.existsSync(np)) { floorIndex.set(String(s.id), np); continue; }
    const pal = usablePalette(s.palette) ? s.palette : (meta.color ? { bg: meta.color } : null);
    const created = {
      key: String(s.id) + '|' + name,
      id: String(s.id),
      name,
      info: { id: String(s.id), name },
      doc: s.doc || '',
      palette: pal,
      _staging: { source: 'structured-staging', staging, confidence: conf, needsReview: conf < 0.8, created: true, rawName: s._staging && s._staging.rawName },
      _observations: { source: 'structured-staging', staging, confidence: conf, intro: s._staging && s._staging.rawIntro }
    };
    if (!confirm) { report.floors.push({ id: s.id, name, created: true, dryRun: true, confidence: conf }); continue; }
    fs.mkdirSync(dir, { recursive: true });
    write(np, created);
    floorIndex.set(String(s.id), np);
    report.floors.push({ id: s.id, name, created: true, confidence: conf });
    continue;
  }
  if (conf < 0.8) { report.skipped.push({ type: 'floor', name: s.name || s.id, reason: 'existing floor confidence ' + conf + ' < 0.8' }); continue; }
  const before = read(fp);
  const after = JSON.parse(JSON.stringify(before));
  const oldDoc = String(after.doc || '');
  const newDoc = String(s.doc || '');
  if (newDoc) {
    if (!oldDoc || oldDoc.length < 40) {
      after.doc = newDoc;
    } else if (newDoc !== oldDoc) {
      /* 既有介紹不自動覆蓋（連錄音室自己建的樓層也一樣）。
         2026-10-28 之前這裡是 after.doc = s.doc，一次收工就能把好好的介紹換成截斷的 OCR。 */
      after._docCandidate = {
        staging,
        confidence: conf,
        length: newDoc.length,
        note: '新 OCR 介紹未自動覆蓋；要採用請人工比對 staging/floors/ 下的 floor.json'
      };
    }
  }
  const palOk = s.palette && ['bg', 'card', 'line', 'acc', 'tx'].every(k => s.palette[k]) && usablePalette(s.palette);
  if (palOk && conf >= 0.8 && (!after.palette || after._staging)) after.palette = s.palette;
  after._observations = { source: 'structured-staging', staging, confidence: conf, intro: s._staging && s._staging.rawIntro };
  if (!confirm) { report.floors.push({ id: s.id, name: s.name, dryRun: true }); continue; }
  backupFile(fp);
  write(fp, after);
  report.floors.push({ id: s.id, name: s.name });
}

// 事件：把 choices JSONL 變成樓層 doc 的 <h4>事件</h4> 表格，
// 讓既有事件瀏覽器自動把這一層的事件節點建起來。
function escHtml(s) {
  return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
const AUTO_BEGIN = '<!-- AUTO-EVENTS:BEGIN -->';
const AUTO_END = '<!-- AUTO-EVENTS:END -->';
const eventDir = path.join(staging, 'events');
const eventReview = [];
if (fs.existsSync(eventDir)) {
  report.events = report.events || [];
  for (const fn of fs.readdirSync(eventDir)) {
    const m = fn.match(/^floor-(.+)-choices\.jsonl$/);
    if (!m) continue;
    const fid = m[1];
    const fp = floorIndex.get(String(fid));
    if (!fp) { report.skipped.push({ type: 'events', floor: fid, reason: 'no existing floor' }); continue; }
    const rawRows = fs.readFileSync(path.join(eventDir, fn), 'utf8').split(/\r?\n/).filter(Boolean).map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean);
    // 只把「有信心、欄位可讀」的事件放進正式 Wiki。
    // 未選／低信心／選項太短／背包雜訊 → 留在 staging 的原始 JSONL，不進事件瀏覽器。
    const bad = /冂|囗|廾|刂|乂|㐅|忄|女读|女讀|女后|女後|的的|辑|輯|间号码|間號碼|读这个|讀這個|光滑机的的|光滑機的的|背包|裝備|装备|硬幣|硬币|經驗|经验|升級|升级|體力|理智|出口進度|出口进度|商店|攤|摊|檢定|检定|難度|难度|玩家/;
    const rows = rawRows.map(e => {
      const conf = Number(e.confidence) || 0;
      const opts = (e.options || []).map(o => String(o.text || '').trim()).filter(t => t.length >= 3 && !bad.test(t));
      if (!e.prompt || String(e.prompt).trim().length < 3 || opts.length < 2) return null;
      if (conf < 0.8) return null;
      const result = (e.result || []).map(r => String(r).trim())
        .filter(r => r.length >= 6 && !bad.test(r))
        .slice(0, 5);
      return { ...e, options: (e.options || []).filter(o => opts.includes(String(o.text || '').trim())), result, confidence: conf };
    }).filter(Boolean);
    const optKey = e => (e.options || []).map(o => String(o.text || '').trim()).join('\u0001');
    for (const raw of rawRows) {
      const accepted = rows.some(r => String(r.prompt || '') === String(raw.prompt || '') && optKey(r) === optKey(raw));
      eventReview.push({
        floor: fid,
        accepted,
        confidence: Number(raw.confidence) || 0,
        prompt: raw.prompt || '',
        options: (raw.options || []).map(o => o.text),
        selected: raw.selected ? raw.selected.text : null,
        result: (raw.result || []).slice(0, 5),
        reason: accepted ? 'accepted' : (Number(raw.confidence) < 0.8 ? 'low_confidence' : 'unreadable_or_noise')
      });
    }
    if (!writeDocEvents) {
      report.events.push({ floor: fid, count: rows.length, raw: rawRows.length, docEvents: false });
      continue;
    }
    const before = read(fp);
    const old = String(before.doc || '');
    let base = old;
    const a = old.indexOf(AUTO_BEGIN), b = old.indexOf(AUTO_END);
    if (a >= 0 && b > a) base = (old.slice(0, a) + old.slice(b + AUTO_END.length)).trim();
    // 每次匯入都是「觀察」：同一個事件可以出現很多次，不能去重。
    // _events 保留全部觀察；_eventStats 才按事件聚合，讓 Wiki 顯示各選項/結果的次數與機率。
    const normKey = x => String(x || '').replace(/\s+/g, '');
    const eventKey = e => JSON.stringify({
      p: normKey(e.prompt),
      o: (e.options || []).map(o => normKey(o.text)).join('\u0001')
    });
    const oldEvents = Array.isArray(before._events) ? before._events : [];
    const mergedEvents = [...oldEvents, ...rows];
    const statMap = new Map();
    for (const e of mergedEvents) {
      const k = eventKey(e);
      let g = statMap.get(k);
      if (!g) {
        g = {
          prompt: String(e.prompt || ''),
          options: (e.options || []).map(o => String(o.text || '')),
          observations: 0,
          selected: new Map(),
          results: new Map()
        };
        statMap.set(k, g);
      }
      g.observations++;
      const sel = e.selected ? String(e.selected.text || '').trim() : '';
      if (sel) g.selected.set(sel, (g.selected.get(sel) || 0) + 1);
      for (const r of (e.result || [])) {
        const t = String(r || '').trim();
        if (t) g.results.set(t, (g.results.get(t) || 0) + 1);
      }
    }
    const eventStats = [...statMap.values()].map(g => ({
      prompt: g.prompt,
      options: g.options,
      observations: g.observations,
      selected: [...g.selected.entries()]
        .map(([text, count]) => ({ text, count, probability: g.observations ? +(count / g.observations).toFixed(4) : 0 }))
        .sort((a, b) => b.count - a.count),
      results: [...g.results.entries()]
        .map(([text, count]) => ({ text, count, probability: g.observations ? +(count / g.observations).toFixed(4) : 0 }))
        .sort((a, b) => b.count - a.count)
    })).sort((a, b) => b.observations - a.observations);

    const tr = eventStats.map((g, i) => {
      const opts = g.options.map(o => '- ' + escHtml(o)).join('<br>');
      const sel = g.selected.length
        ? '<br><b>已選：</b>' + g.selected.map(x => escHtml(x.text) + '（' + x.count + ' 次，' + Math.round(x.probability * 100) + '%）').join('；')
        : '';
      const res = g.results.length
        ? g.results.slice(0, 8).map(x => '<br>' + escHtml(x.text) + '（' + x.count + ' 次）').join('')
        : '';
      return '<tr><td>' + escHtml(g.prompt || ('事件 ' + (i + 1))) + '</td><td>' + opts + sel + res + '<br><span style="color:#888">出現 ' + g.observations + ' 次</span></td></tr>';
    }).join('');
    const auto = eventStats.length ? (AUTO_BEGIN + '\n<h4>事件</h4>\n<table><tbody>' + tr + '</tbody></table>\n' + AUTO_END) : '';
    const after = JSON.parse(JSON.stringify(before));
    after.doc = base + (base && auto ? '\n' : '') + auto;
    after._events = mergedEvents;
    after._eventStats = eventStats;
    if (!confirm) { report.events.push({ floor: fid, dryRun: true, count: rows.length, raw: rawRows.length }); continue; }
    backupFile(fp);
    write(fp, after);
    report.events.push({ floor: fid, count: rows.length, raw: rawRows.length });
  }
}

fs.mkdirSync(staging, { recursive: true });
if (eventReview.length) {
  fs.writeFileSync(path.join(staging, 'events-review.jsonl'), eventReview.map(x => JSON.stringify(x)).join('\n') + '\n', 'utf8');
  report.eventsReview = { total: eventReview.length, accepted: eventReview.filter(x => x.accepted).length, rejected: eventReview.filter(x => !x.accepted).length };
}
fs.writeFileSync(path.join(staging, 'merge-final-report.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ confirm, entities: report.entities.length, floors: report.floors.length, events: (report.events || []).length, skipped: report.skipped.length, backup }, null, 2));
if (!confirm) { console.log('dry-run；加 --confirm 才寫入'); process.exit(0); }
