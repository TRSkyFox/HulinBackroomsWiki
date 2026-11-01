import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { CONTENT, INCOMING } from './paths.mjs';
import { report } from './progress.mjs';
import { cleanEvent, cleanActions } from './event-clean.mjs';
import { text, cleanResults, floorName, paragraphs } from './text.mjs';
import { addSample, addOutcomeSample, observedLines } from './variants.mjs';
import { eventToSteps, hasMultipleSteps } from './steps.mjs';

/*
 * event-import.mjs — 把解析器輸出轉成「可審查的事件 staging」。
 * 預設只寫 session/structured/events.staging.json，不改正式 Wiki。
 * 加 --apply 才會把 staging 併入「該樓層資料夾」的 events.json；同名事件不覆蓋。
 * 樓層事件一律寫在 wiki/content/floors/<樓層>/events.json，不再寫全域 events.json。
 */

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node event-import.mjs <sessionDir> [--apply]'); process.exit(2); }
const session = path.resolve(args[0]);
const apply = args.includes('--apply');
const HERE = path.dirname(fileURLToPath(import.meta.url));
const wikiRoot = CONTENT;
const floorsRoot = path.join(wikiRoot, 'floors');
const jsonl = f => fs.existsSync(f) ? fs.readFileSync(f, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).filter(Boolean).map(s => { try { return JSON.parse(s); } catch { return null; } }).filter(Boolean) : [];
const norm = s => String(s || '').replace(/\s+/g, '').replace(/[“”「」『』]/g, '').replace(/[，。！？、；：]/g, '').trim();

/* 以專案現成的繁→簡字表做「同名」比對；沒有字表時仍可運作。
   注意：這裡只用來「比對 key」，不再拿來做繁化 —— 繁化統一走 text.mjs，
   不然事件欄位又會退回那套手刻字表（結果就是 results 還是簡體）。 */
const hantToHans = new Map();
try {
  const mapPath = path.join(HERE, 'i18n-table.txt');
  if (fs.existsSync(mapPath)) {
    for (const line of fs.readFileSync(mapPath, 'utf8').split(/\r?\n/)) {
      if (!line || line.trim().startsWith('#')) continue;
      const chars = line.trim().split(/\s+/).filter(x => /^[\u3400-\u9fff]$/.test(x));
      for (let i = 0; i + 1 < chars.length; i += 2) hantToHans.set(chars[i], chars[i + 1]);
    }
  }
} catch (e) {}
const toHans = s => [...String(s || '')].map(c => hantToHans.get(c) || c).join('');
const keyName = s => toHans(norm(s));
const badName = s => !s || s.length < 2 || /^(?:力量|敏捷|體質|体质|感知|智力|魅力|體力|体力|理智)\s*(?:難度|难度)|^【|^Level\s*\?|你迷失|这一局|這一局|稱號|称号|继续|繼續|要怎麼做|要怎么做|选一个去做|選一個去做|^\d+$/.test(s);

const floorNames = new Map();
const floorDirs = new Map();
const floorIntros = new Map();
if (fs.existsSync(floorsRoot)) {
  for (const d of fs.readdirSync(floorsRoot, { withFileTypes: true })) {
    if (!d.isDirectory()) continue;
    const p = path.join(floorsRoot, d.name, 'floor.json');
    if (!fs.existsSync(p)) continue;
    const f = JSON.parse(fs.readFileSync(p, 'utf8'));
    const fid = String(f.id);
    /* 正式庫的名字也可能帶括號；對外一律用清乾淨的版本。 */
    floorNames.set(fid, floorName(f.name || ''));
    floorDirs.set(fid, path.join(floorsRoot, d.name));
    floorIntros.set(fid, String(f.doc || '').replace(/<[^>]*>/g, '').replace(/\s+/g, ''));
  }
}

const floorConf = new Map();
try {
  const sd = path.join(INCOMING, path.basename(session), 'floors');
  if (fs.existsSync(sd)) {
    for (const d of fs.readdirSync(sd, { withFileTypes: true })) {
      if (!d.isDirectory()) continue;
      const p = path.join(sd, d.name, 'floor.json');
      if (!fs.existsSync(p)) continue;
      const f = JSON.parse(fs.readFileSync(p, 'utf8'));
      floorConf.set(String(f.id), Number((f._staging && f._staging.confidence) || 0));
    }
  }
} catch (e) {}

const records = jsonl(path.join(session, 'structured', 'events.bound.jsonl'));
const ACTION_FIX = new Map([
  ['电话', '投幣，撥自己家裡的號碼'],
  ['電話', '投幣，撥自己家裡的號碼'],
  ['找销电话线', '拔掉電話線'],
  ['找銷電話線', '拔掉電話線'],
  ['不理它们:', '不理它們'],
  ['不理它們:', '不理它們'],
  ['把倒掉的', '把倒掉的椅子扶好'],
]);
const byFloor = new Map();
const seen = new Set();
/* 這些「事件」其實是 UI 或戰鬥流水，不是事件：
   商店／接委託／「某某狠刺防禦10」都是戰鬥與商店畫面被事件解析器吃進來。 */
const NOT_AN_EVENT = /^(?:商店|商店街|接下委託|接受委託|戰鬥|戰鬥結束)$|防禦\s*\d+\s*$|^(?:獲得|得到)道具?$/;
let importDone = 0;
let junkSkipped = 0;
for (const r of records) {
  report(session, '事件審核', ++importDone, records.length, r.name ? ('事件：' + r.name) : ('第 ' + importDone + ' 筆'));
  if (r.type !== 'event' || !r.name || badName(r.name)) continue;
  if (NOT_AN_EVENT.test(String(r.name).trim())) { junkSkipped++; continue; }
  const fid = String(r.floor || '');
  if (!fid || !floorNames.has(fid)) continue; // 不把事件塞進未知/未建立樓層
  const actions = (r.actions || []).filter(a => a && a.text && !badName(a.text)).map(a => ({
    text: ACTION_FIX.get(a.text) || a.text,
    check: a.check || null,
    perception: !!a.perception,
    hint: a.hint || '',
    selected: !!a.selected
  }));
  if (!r.intro && !actions.length) continue;
  if (!actions.length && (r.kind || 'event') !== 'ending') continue;
  const key = fid + '|' + keyName(r.name);
  if (seen.has(key)) continue;
  seen.add(key);
  if (!byFloor.has(fid)) byFloor.set(fid, []);
  const evIntro = text(r.intro || '');
  const floorIntro = floorIntros.get(fid) || '';
  const ev = {
    name: text(r.name),
    kind: r.kind || 'event',
    intro: evIntro,
    actions: actions.map(a => ({ ...a, text: text(a.text), hint: text(a.hint || '') })),
    selected: r.selected ? text(r.selected.text) : null,
    /* 結果不再是原封不動搬進來：截斷的殘句（「地上有一支手电筒，开关是」）直接丟；
       和樓層介紹／自身 intro 高度重疊的句子也丟（事件與結果錯配）。 */
    results: cleanResults(r.results).filter(line => !isFloorIntroLine(line, floorIntro, evIntro)),
    check: r.check || null,
    effects: r.effects || [],
    progress: r.progress || null,
    combat: r.combat ? text(r.combat) : null,
    source: path.basename(session),
    /* 出處：之後任何一條內容都能回答「哪一場、哪一帧、什麼信心」。 */
    _src: {
      session: path.basename(session),
      frame: (r.source && r.source.frame) || r.startFrame || null,
      ts: (r.source && r.source.ts) || null,
      confidence: r.inputBound ? 0.9 : 0.6,
      rule: 'event-clean@2026-10-29'
    },
    confidence: r.inputBound ? 0.9 : 0.6,
    bound: !!r.inputBound
  };
  const cleaned = cleanEvent(ev);
  if (!cleaned) continue;
  byFloor.get(fid).push(cleaned);
}

/* 2026-11-01：事件與結果對不上 —— 實測結果裡常混進「樓層介紹」的句子
   （例：空心的牆 → 結果卻是 Level 0 的開場描述）或這一則自己的 intro。
   座標過濾行不通（不同 OCR 區塊的 y 不能互比），改用文字比對：
   結果句若和樓層介紹／自身 intro 高度重疊，就丟掉。 */
function bigrams(x) {
  const g = new Set();
  for (let i = 0; i < x.length - 1; i++) g.add(x.slice(i, i + 2));
  return g;
}
function overlap(a, b) {
  if (!a || !b || a.length < 8 || b.length < 8) return 0;
  const A = bigrams(a), B = bigrams(b);
  let n = 0;
  for (const x of A) if (B.has(x)) n++;
  return n / Math.min(A.size, B.size);
}
function isFloorIntroLine(line, introText, selfIntro) {
  const n = String(line || '').replace(/\s+/g, '');
  if (n.length < 10) return false;
  if (overlap(n, introText) >= 0.7) return true;
  if (overlap(n, selfIntro) >= 0.7) return true;
  return false;
}

const staging = { generatedAt: new Date().toISOString(), session: path.basename(session), floors: {} };
for (const [fid, events] of byFloor) staging.floors[fid] = {
  floorId: fid,
  floorName: floorNames.get(fid) || fid,
  /* 按「這局第幾個事件」排，而不是按名字筆畫。
     wiki 是給人照著玩的路線，名字排序會把同一個岔路的事件打散。 */
  events: events.sort((a, b) => {
    const pa = a.progress && Number.isFinite(a.progress.current) ? a.progress.current : 1e6;
    const pb = b.progress && Number.isFinite(b.progress.current) ? b.progress.current : 1e6;
    if (pa !== pb) return pa - pb;
    return String(a.name).localeCompare(String(b.name), 'zh-Hant');
  })
};

/* ── 路口（junction） ──
   以前 events.bound.jsonl 裡明明解析出路口，卻沒有任何一支程式寫進 events.json，
   所以站點的事件圖永遠只有事件、沒有路口。這裡把它補上。
   路口文字常混著 UI（出口進度、背包內容），所以只留像敘述的段落；
   選項沿用 cleanActions 同一套清洗與去重。 */
const junctionsByFloor = new Map();
function junctionScene(raw, prompt) {
  /* 先清掉黏在段落裡的 UI 片段，再判斷像不像敘述；
     以前是「整段含出口進度就丟」，結果把整段正常的場景描述一起丟了。 */
  const strip = s => s
    .replace(/出口進度\s*\d+\s*\/\s*\d+/g, '')
    .replace(/^\s*\[[^\]]{0,12}\]\s*/, '')
    .replace(/^\s*【[^】]{0,12}】\s*/, '')
    .trim();
  const paras = paragraphs(raw)
    .map(strip)
    .filter(l => l.length >= 6 && !/^(背包|道具|耐久)/.test(l));
  /* 同一段被連續幾帧各讀一次 → 去重（保留比較完整的那一份）。 */
  const flat = s => s.replace(/[\s，。！？、；：·・…]/g, '');
  const uniq = [];
  for (const p of paras) {
    const fp = flat(p);
    const i = uniq.findIndex(q => { const fq = flat(q); return fq.includes(fp) || fp.includes(fq); });
    if (i >= 0) { if (p.length > uniq[i].length) uniq[i] = p; continue; }
    uniq.push(p);
  }
  let t = uniq.join(' ').trim();
  /* 路口文字必須像敘述：不能是對話殘片（「里的東西。」「，我不會想喝」）。 */
  const dialogue = (t.match(/[「」]/g) || []).length >= 2;
  const narrative = t.length >= 10 && /[。！？]/.test(t) && !/^[「『，。]/.test(t) && !dialogue;
  if (!narrative) {
    const q = text(String(prompt || '').replace(/[（(][^）)]*[）)]/g, '')).replace(/[？?]$/, '').trim();
    t = q || t;
  }
  return t.slice(0, 160);
}
for (const r of records) {
  if (r.type !== 'junction') continue;
  const fid = String(r.floor || '');
  if (!fid || !floorNames.has(fid)) continue;
  const ca = cleanActions((r.paths || []).map(p => ({ text: p.text, perception: !!p.perception })), r.selected ? r.selected.text : '');
  if (!ca) continue;
  const scene = junctionScene(r.text, r.prompt);
  if (!scene) continue;
  const sig = fid + '#' + ca.actions.map(a => norm(a.text)).sort().join('|');
  if (!junctionsByFloor.has(fid)) junctionsByFloor.set(fid, { seen: new Set(), list: [] });
  const bucket = junctionsByFloor.get(fid);
  if (bucket.seen.has(sig)) continue;
  bucket.seen.add(sig);
  bucket.list.push({
    text: scene,
    paths: ca.actions.map(a => ({ text: a.text, perception: !!a.perception, selected: a.text === ca.selected })),
    selected: ca.selected || null,
    _src: { session: path.basename(session), frames: (r.frames || []).slice(0, 1), rule: 'junction@2026-10-29' }
  });
}

const outPath = path.join(session, 'structured', 'events.staging.json');
/* 只有路口、沒有事件的樓層也要出現在 staging，不然路口永遠寫不進去。 */
for (const [fid, bucket] of junctionsByFloor) {
  if (!staging.floors[fid]) {
    staging.floors[fid] = { floorId: fid, floorName: floorNames.get(fid) || fid, events: [] };
  }
  staging.floors[fid].junctions = bucket.list;
}
for (const f of Object.values(staging.floors)) if (!f.junctions) f.junctions = [];
fs.writeFileSync(outPath, JSON.stringify(staging, null, 2) + '\n', 'utf8');

if (!apply) {
  console.log(JSON.stringify({
    staging: outPath,
    floors: Object.keys(staging.floors),
    events: Object.values(staging.floors).reduce((n, f) => n + f.events.length, 0),
    junctions: Object.values(staging.floors).reduce((n, f) => n + (f.junctions || []).length, 0),
    junkSkipped, applied: false
  }, null, 2));
  process.exit(0);
}

const dirty = /難度|难度|擲出|掷出|你決定|你决定|要怎麼做|要怎么做|选一个去做|選一個去做|按右鍵|按右键|出口進度|出口进度|狀態變化|状态变化|戰鬥結束|战斗结束|繼續|继续/;
const autoMode = process.argv.includes('--auto');
function eligible(e) {
  if ((e.kind || 'event') === 'ending') {
    return !!e.intro && e.intro.length >= 8;
  }
  const acts = e.actions || [];
  if (acts.length < 2 || acts.length > 4) return false;
  if (!acts.every(a => a.text && !dirty.test(a.text))) return false;
  /* 一般事件沒有 selected 就不自動寫入：寧可留 staging，也不要猜錯選項。 */
  if (!e.selected) return false;
  if (e.selected && !acts.some(a => norm(a.text) === norm(e.selected))) return false;
  if (!e.intro || e.intro.length < (autoMode ? 16 : 8) || dirty.test(e.intro)) return false;
  if (!autoMode && (!e.bound || e.confidence < 0.9)) return false;
  return true;
}
let added = 0, skipped = 0, rejected = 0, endingsWritten = 0, updated = 0, junctionsAdded = 0;
for (const [fid, f] of Object.entries(staging.floors)) {
  const dir = floorDirs.get(String(fid));
  if (!dir) { rejected += (f.events || []).length; continue; }
  if (!autoMode && floorConf.has(String(fid)) && floorConf.get(String(fid)) < 0.8) { rejected += (f.events || []).length; continue; }
  const eventPath = path.join(dir, 'events.json');
  const endingsPath = path.join(dir, 'endings.json');
  const existed = fs.existsSync(eventPath);
  const target = existed
    ? JSON.parse(fs.readFileSync(eventPath, 'utf8'))
    : { floorId: fid, floorName: f.floorName || floorNames.get(String(fid)) || fid, junctions: [], events: [] };
  target.floorId = fid;
  target.floorName = target.floorName || f.floorName || floorNames.get(String(fid)) || fid;
  target.events ||= [];
  /* 路口只補不覆蓋：同一個路徑組合視為同一個路口。 */
  target.junctions = Array.isArray(target.junctions) ? target.junctions : [];
  const sigOf = j => (j.paths || []).map(p => norm(p.text)).sort().join('|');
  const haveJ = new Set(target.junctions.map(sigOf));
  let junctionsHere = 0;
  for (const j of (f.junctions || [])) {
    const s = sigOf(j);
    if (!s || haveJ.has(s)) continue;
    haveJ.add(s); target.junctions.push(j); junctionsAdded++; junctionsHere++;
  }
  let addedHere = 0;
  let updatedHere = 0;
  let endings = fs.existsSync(endingsPath) ? JSON.parse(fs.readFileSync(endingsPath, 'utf8')) : [];
  let endingsAdded = 0;
  for (const e of f.events) {
    if (!eligible(e)) { rejected++; continue; }
    if ((e.kind || 'event') === 'ending') {
      const nm = String(e.name || '迷失');
      /* 結局正文常被螢幕標題蓋掉（「狐霖的後室 RPG / 全員迷失 / Level 1」）。
         與其拿一行標題冒充正文，不如留空並標「待補」——至少不是假的。 */
      const lines = String(e.intro || '').split(/\r?\n+/).map(s => text(s).trim()).filter(Boolean)
        .map(s => s.replace(/後室\s*RPG|后室\s*RPG/gi, '')
          .replace(/Level\s*[\d.]+/gi, '')
          .split(nm).join('')
          .replace(/^[\s，。、；：]+|[\s，。、；：]+$/g, '')
          .replace(/^的+|的+$/g, '')
          .trim())
        .filter(s => s.length >= 6);
      const category = /迷失|死亡/.test(nm) ? 'bad' : /逃生|逃脫|逃脱|出口/.test(nm) ? 'good' : 'extra';
      const entry = {
        category,
        label: category === 'bad' ? '壞結局' : category === 'good' ? '好結局' : '其他',
        name: nm,
        status: lines.length ? '部分' : '待補',
        floors: 'Level ' + fid,
        lv: String(fid),
        trigger: '',
        lines,
        after: '',
        owns: [],
        floorId: String(fid),
        _src: { session: path.basename(session), frame: (e._src && e._src.frame) || null, rule: 'event-clean@2026-10-29' }
      };
      const idx = endings.findIndex(x => keyName(x.name) === keyName(nm));
      if (idx >= 0) endings[idx] = { ...endings[idx], ...entry };
      else endings.push(entry);
      endingsAdded++;
      continue;
    }
    const old = target.events.find(x => keyName(x.name) === keyName(e.name));
    if (old) {
      /* 2026-10-31 使用者：樣本數與機率記在【每個選項】上，不是記在事件上。
         同一個選項在不同場次抽到不同結果 → 那個選項的 variants 加一個樣本。 */
      const obs = observedLines(e);
      let sampled = false;
      {
        const selE = (e.actions || []).find(a => a.selected) || null;
        const selOld = selE ? (old.actions || []).find(a => String(a.text).replace(/\s/g, '') === String(selE.text).replace(/\s/g, '')) : null;
        if (selOld && obs) {
          /* 舊資料還沒有 variants：先把舊的結果當第一個樣本 */
          if (!selOld.variants || !selOld.variants.length) {
            const prev = observedLines(old);
            if (prev) addSample(selOld, prev.lines, prev.effects);
          }
          sampled = addSample(selOld, obs.lines, obs.effects);
          if (obs.branch) addOutcomeSample(selOld, obs.branch);
        }
      }
      /* 同名事件安全更新：只有新資料「有 selected 且 actions 合法」才覆蓋；
         沒有 selected 的新資料不動舊內容，避免把已整理的事件洗掉。 */
      if (e.selected && e.actions && e.actions.length >= 2) {
        const keepVariants = old.variants;
        const keepVariantTotal = old.variantTotal;
        const keepOutcomeStats = old.outcomeStats;
        const keepOutcomes = old.outcomes;
        old.name = e.name || old.name;
        old.actions = e.actions;
        old.selected = e.selected;
        old.intro = e.intro;
        /* 分支內容也要合併，不能因為覆蓋而丟掉另一支 */
        if (e.outcomes) {
          const merged = { ...(keepOutcomes || {}) };
          for (const k of ['success', 'failure']) {
            const a = merged[k], b = e.outcomes[k];
            if (b && b.lines && b.lines.length) merged[k] = { ...(a || {}), ...b };
            else if (a) merged[k] = a;
          }
          old.outcomes = merged;
        }
        old.variants = keepVariants;
        old.variantTotal = keepVariantTotal;
        old.outcomeStats = keepOutcomeStats;
        old.updatedAt = new Date().toISOString();
        updated++; updatedHere++;
      } else if (sampled) {
        old.updatedAt = new Date().toISOString();
        updated++; updatedHere++;
      } else {
        skipped++;
      }
      continue;
    }
    const { source, confidence, bound, ...event } = e;
    /* 2026-11-01：連環事件（一個事件裡多次「你決定」）拆成 steps */
    if (hasMultipleSteps(event)) event.steps = eventToSteps(event);
    /* 第一次看到這一則：把這次的結果存成第一個樣本（之後每再遇到一次就 +1） */
    {
      const obs = observedLines(event);
      const sel = (event.actions || []).find(a => a.selected) || null;
      if (obs && sel) { addSample(sel, obs.lines, obs.effects); if (obs.branch) addOutcomeSample(sel, obs.branch); }
    }
    target.events.push(event);
    added++; addedHere++;
  }
  if (endingsAdded) { fs.writeFileSync(endingsPath, JSON.stringify(endings, null, 2) + '\n', 'utf8'); endingsWritten += endingsAdded; }
  /* 沒有新東西就不要寫檔（以前每次收工都留一個 0 事件的 events.json 空殼）。 */
  if (!existed && addedHere === 0 && junctionsHere === 0) continue;
  if (existed && addedHere === 0 && updatedHere === 0 && junctionsHere === 0) continue;
  fs.writeFileSync(eventPath, JSON.stringify(target, null, 2) + '\n', 'utf8');
}
const result = { staging: outPath, applied: true, added, updated, skipped, rejected, endings: endingsWritten, junctions: junctionsAdded, junkSkipped, floors: Object.keys(staging.floors) };
/* 同時寫檔：run.mjs 以前靠 stdout 管道讀結果，沙箱／CI 會因為 EPERM 拿不到，
   整段自動匯入就被誤判成失敗。寫檔比管道可靠。 */
try { fs.writeFileSync(path.join(session, 'structured', 'event-import.json'), JSON.stringify(result, null, 2) + '\n', 'utf8'); } catch (e) {}
console.log(JSON.stringify(result, null, 2));
