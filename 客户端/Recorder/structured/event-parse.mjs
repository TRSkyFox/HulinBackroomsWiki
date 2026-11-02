import fs from 'node:fs';
import path from 'node:path';
import { FLOORS } from './paths.mjs';
import { report } from './progress.mjs';
import { collapseRepeats } from './text.mjs';
import { classifyFrame, headerScore } from './ink.mjs';
import { extractEvents } from './event-extract.mjs';

/*
 * event-parse.mjs — 由 structured.jsonl 重建路口／事件／戰鬥事件
 * 依《事件结构规范.md》：
 *  - 樓層介紹、玩家台詞、路口、事件、戰鬥、出口分開
 *  - 判定只記屬性與成功／失敗，不記難度數字與骰值
 *  - 感知是 path 上的附加提示，不是獨立 path 類型
 *  - event 完成才進入下一個 junction
 * 輸出：structured/events.jsonl + structured/events-report.json
 */

const W = 1606;
const H = 929;
const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node event-parse.mjs <sessionDir>'); process.exit(2); }
const session = path.resolve(args[0]);
const outDir = path.join(session, 'structured');
fs.mkdirSync(outDir, { recursive: true });

const frames = fs.readFileSync(path.join(session, 'structured.jsonl'), 'utf8')
  .split(/\r?\n/).filter(Boolean).map(s => { try { return JSON.parse(s); } catch { return null; } }).filter(Boolean);

const FLOOR_LIST = (() => {
  const root = FLOORS;
  const out = [];
  try {
    for (const d of fs.readdirSync(root, { withFileTypes: true })) {
      if (!d.isDirectory()) continue;
      const p = path.join(root, d.name, 'floor.json');
      if (!fs.existsSync(p)) continue;
      const o = JSON.parse(fs.readFileSync(p, 'utf8'));
      out.push({ id: String(o.id), name: String(o.name || ''), key: String(o.key || '') });
    }
  } catch (e) {}
  return out;
})();
function canonFloor(id, name) {
  const n = norm(name);
  if (n) {
    let hit = FLOOR_LIST.find(f => norm(f.name) === n && (!id || f.id === id))
      || FLOOR_LIST.find(f => norm(f.name) === n);
    if (!hit) {
      let best = null, bestScore = 0;
      for (const f of FLOOR_LIST) {
        const s = nameSim(n, norm(f.name));
        if (s > bestScore) { bestScore = s; best = f; }
      }
      if (best && bestScore >= 0.45) hit = best;
    }
    if (hit) return { id: hit.id, name: hit.name };
  }
  if (id) {
    const hit = FLOOR_LIST.find(f => f.id === String(id));
    if (hit && !n) return { id: hit.id, name: hit.name };
  }
  return { id: String(id || ''), name: name || '' };
}

const UI_EXACT = /^(等大家決定|等大家决定|背包|看背景|設定|设定|離開|离开|隊伍頻道|队伍频道|送出|說点什麼|说点什么|思考中|行動中|行动中|閱讀中|阅读中|準備好|准备好|說出來|说出来|已決定|已决定)$/;
const UI_ANY = /跟隨多數|跟随多数|強制倒數|强制倒数|催促|載入|载入|點一下|点一下|跟隨當前|跟随当前/;
const CHECK_RE = /[（(]\s*(力量|敏捷|體質|体质|感知|智力|魅力)\s*[^）)]*[）)]/;
const OUTCOME_RE = /大成功|大失敗|大失败|成功|失敗|失败/;
const EFFECT_RE = /(理智|體力|体力|經驗|经验|口渴|飢餓|饥饿|生命|HP|SAN)\s*([+-]?\s*\d+)/g;
const EVENT_RE = /【\s*([^】]{1,40})\s*】/;
/* 2026-10-31：【接下去委託】這種括號不是事件。
   例：`【接下委托】跑进管子里的猫★☆☆☆☆` 是任務標題，被當成事件之後，
   任務描述＋下一個場景全部併進它的 intro，變成一段四不像的正文。 */
const NOT_EVENT_RE = /^(接下|委[託托]|委托|商店|販賣機|贩卖机|自動販賣機|传送|傳送|傳送門|任务|任務|普通|稀有|史詩|史诗|傳說|传说|精良|貴重|贵重|特殊|高級|高级|中級|中级|低級|低级)/;
const COMBAT_RE = /【\s*(?:戰鬥|战斗)\s*】\s*([^【】]{0,30})/;

/* 2026-10-31：順便把「·」「…」「—」這類裝飾字也去掉 —— 同一段文字每一幀 OCR 出來，
   差一個「·」就算不同句，於是 intro 裡出現三次幾乎一樣的段落。 */
const norm = s => String(s || '')
  .replace(/\s+/g, '')
  .replace(/[“”「」『』]/g, '')
  .replace(/[，。！？、；：]/g, '')
  .replace(/[·…—–_・"'\-]/g, '')
  .trim();
function grams(s) {
  const n = norm(s), out = new Set();
  for (let i = 0; i < n.length - 1; i++) out.add(n.slice(i, i + 2));
  return out;
}
function nameSim(a, b) {
  const A = grams(a), B = grams(b);
  if (!A.size || !B.size) return 0;
  let n = 0; for (const x of A) if (B.has(x)) n++;
  return n / Math.max(A.size, B.size);
}
const clean = s => String(s || '').replace(/\s+/g, ' ').trim();
const isUI = s => !s || UI_EXACT.test(norm(s)) || UI_ANY.test(String(s)) || /^第\s*\d+\s*[層层]/.test(norm(s));
const isDialogue = s => /^[^：:]{1,18}[：:]\s*[「『]/.test(clean(s)) || /^[^：:]{1,12}：「/.test(clean(s));
/* 檢定行：有括號的（力量 檢定）也認，沒括號的「你魅力难度12」也認 */
const isCheck = s => parseCheck(s) !== null || /擲出|掷出|檢定|检定/.test(s);
const isAbility = s => /認出來了|认出来了|翻到這一頁|翻到这一页|讀過的資料|读过的资料|圖鑑|图鉴/.test(s);
/* 2026-10-30：OCR 常把「你」吃掉，只剩「決定：…」或「：！坐下采…」，一樣要擋 */
const ACTOR_DECISION = /^[→>\-–—\s]*[^\s：:]{1,12}[：:]\s*[「『]/;
const isDecision = s => ACTOR_DECISION.test(String(s || '')) || /你決定|你决定/.test(s) || /^[：:！!\s]*決[定定]\s*[：:]/.test(String(s || ''));
/* 檢定判定行：屬性＋難度＋擲骰＋成功／失敗 */
/* QTE（快速反應）：燈管掉下來、地板塌了這類「閃！」的即時反應。
   使用者：QTE 成功與失敗結果不同，要一起記。
   遊戲一定會先出現一行提示（閃！／快躲／快速反應），失敗常見「你慢了一步」。 */
const QTE_RE = /QTE|快速反應|快速反应|反應鍵|闪！|閃！|閃開|闪开|快躲|快闪|快閃/;
const QTE_FAIL_RE = /慢了一步|沒躲過|没躲过|失敗|失败|被打中|被砸|撲空|扑空/;
const QTE_OK_RE = /順利|顺利|閃過|闪过|躲開|躲开|成功了|反應過來|反应过来/;

const CHK_RESULT_RE = /(力量|敏捷|體質|体质|感知|智力|魅力)\s*(?:檢定|检定)?[^0-9]{0,6}?(?:難度|难度)\s*[:：]?\s*(\d{1,2})/;
const ROLL_RE = /(?:擲出|掷出)\s*([0-9]{1,2}\s*[+＋]\s*[0-9]{1,2}\s*=\s*[0-9]{1,2})/;
const OUTCOME_RE2 = /[→>]\s*(失敗|失败|成功|過了|过了)/;
function parseCheckResult(str) {
  const t = String(str || '');
  const m = t.match(CHK_RESULT_RE);
  if (!m) return null;
  const chk = { attribute: m[1].replace('体质', '體質'), difficulty: Number(m[2]), hasCheck: true };
  const r = t.match(ROLL_RE);
  if (r) chk.roll = r[1].replace(/\s+/g, '');
  const o = t.match(OUTCOME_RE2);
  if (o) chk.outcome = /失敗|失败/.test(o[1]) ? 'failure' : 'success';
  return chk;
}
function ensureOutcomes(ev) {
  if (!ev.outcomes) ev.outcomes = {};
  return ev.outcomes;
}
const isPromptPath = s => /走哪條路|走哪条路|要走哪|要往哪|往哪裡走|往哪里走/.test(s);
const isPromptEvent = s => /怎[麼么]做|选一个去做|選一個去做|妻怎么做|宴怎么做/.test(s);
const isComplete = s => /看完之後按|看完之后按|按「繼續」|按「继续」/.test(s);
const isProgress = s => /出口進度|出口进度/.test(s);
/* 介面上的固定雜訊。
   2026-10-30：整張 OCR 的帧會把「背包（B）」「設定（靜音）」「Level 37.1「深水區」」
   一起掃進來，以前只擋半角括號的「背包 (B)」，全角的一律漏掉，
   於是這些行被當成「事件結果」寫進 Wiki。 */
const UI_CHROME_RE = new RegExp([
  '^背包\\s*[（(]\\s*[BbＢ]\\s*[）)]$',
  '^設定\\s*[（(][^）)]*[）)]$',
  '^设置\\s*[（(][^）)]*[）)]$',
  '^Level\\s*[\\d.]+\\s*[「『][^」』]*[」』]$',
  '^(體力|体力|理智|經驗|经验|金幣|金币)\\s*[+-]\\s*\\d+$',
].join('|'));

const isNoise = s => UI_CHROME_RE.test(clean(s))
  || /[0-9]+\s*[+＋]\s*[0-9]+\s*=\s*[0-9]+/.test(s)      /* 10 +6=16 */
  || /★.*?(升到|升級|升级)/.test(s)                        /* ★天狐升到2级 */
  || /^(成功|失敗|失败)\s*[！!。]?$/.test(String(s).trim())
  || /跟隨多數|跟随多数|點一下|点一下|可以跳過|可以跳过|空白鍵|空白键|按空白|下一頁|下一页|擲出|掷出|先攻順序|先攻顺序|第\s*\d+\s*回合|輪到你|轮到你|選擇行動|选择行动|防禦\s*[+＋]|防御\s*[+＋]|傷害減半|伤害减半|切換武器|切换武器|使用物品|逃跑|敏捷檢定|敏捷检定|背包裡|背包里|耐久|耐久度|硬度|攻擊\s*[：:]|攻击\s*[：:]/.test(s) || isUI(s);
const floorIdOf = s => (String(s).match(/Level\s*(\d+(?:\.\d+)?)/i) || [])[1] || '';
const floorNameOf = s => (String(s).match(/[「『]([^」』]{1,30})[」』]/) || [])[1] || '';

/* ── 版面節奏：行距與「大空隙」──────────────���─────────────
   2026-10-31：使用者提出「事件之間有明顯空隙，用空隙判斷」。
   實測一張 1606x929 的畫面：同一段內行距 7~8px，段落之間 35px，區塊之間 84px。
   所以空隙是可用的結構訊號 —— 但「段落之間」也有 35px，光靠它會把長描述切成好幾段，
   因此只在「這一則已經被決定過」（出現 →你決定／出口進度）之後，才用大空隙切斷，
   避免把下一個場景併進上一則的 intro。 */
function linePitch(lines) {
  const ys = (lines || []).map(l => Number(l.y) || 0).filter(v => v > 0).sort((a, b) => a - b);
  const ds = [];
  for (let i = 1; i < ys.length; i++) { const d = ys[i] - ys[i - 1]; if (d > 0 && d < 200) ds.push(d); }
  if (!ds.length) return 0;
  ds.sort((a, b) => a - b);
  return ds[Math.floor(ds.length / 2)];
}
function isBigGap(prevBottom, y, pitch) {
  if (!pitch) return false;
  const gap = Number(y) - Number(prevBottom);
  /* 行距的 3 倍、或至少 30px —— 都隨解析度縮放，不是固定字數。 */
  return gap > Math.max(pitch * 3, 30);
}

/* 事件邊界用的「空行」：比一般段落更大的空隙。
   實測 1606x929：行距約 28px、段落約 35px、區塊約 84px。
   2.5 倍行距（≈70px）只會命中真正的區塊空隙，不會切斷普通段落。 */
function isEventGap(prevBottom, y, pitch) {
  if (!pitch) return false;
  return (Number(y) - Number(prevBottom)) > Math.max(pitch * 2.5, 50);
}

/* ── 版面切塊 ────────────────────────────────────────────────
   2026-10-31：使用者提出「事件之間有明顯空隙」。實測（1606x929）：
     同一段內行距 7~8px ／ 段落之間 35px ／ 區塊之間 84px
   所以把一幀的主文面板依空隙切成「塊」，每塊就是一個自然段。
   事件的 intro 只取「標題那塊 → 到出現 →你決定／出口進度／下一個【 之前」的塊，
   這樣既不會把下一個場景吞進來，也不會因為段落多而被切斷（段落本來就是同一個事件）。
   全部用版面幾何判斷，沒有任何字數門檻。 */
function splitBlocks(lines, pitch) {
  const arr = (lines || []).slice().sort((a, b) => (a.y - b.y) || (a.x - b.x));
  const blocks = [];
  let cur = null, prevBottom = -1;
  for (const l of arr) {
    const big = cur && pitch && (Number(l.y) - prevBottom) > Math.max(pitch * 3, 30);
    if (!cur || big) { cur = { lines: [l], y0: l.y, y1: l.y + (l.h || 0) }; blocks.push(cur); }
    else { cur.lines.push(l); cur.y1 = l.y + (l.h || 0); }
    prevBottom = l.y + (l.h || 0);
  }
  return blocks;
}
/* 這一塊是不是「新的區段」開頭（不是本事件正文） */
function isSectionBlock(block) {
  const t = clean(block.lines[0] && block.lines[0].t);
  return /^(【|——)/.test(t)
    || /出口進度|出口进度|走哪條路|走哪条路|要走哪|跟隨多數|跟随多数/.test(t)
    || /[0-9]+\s*[+＋]\s*[0-9]+\s*=\s*[0-9]+/.test(t);
}

const mainLines = f => (f.lines || []).filter(l => l.x < W * 0.62 && l.y >= H * 0.075 && l.y < H * 0.90 && !isUI(l.t));
const optionLines = f => (f.lines || []).filter(l => l.x < W * 0.10 && l.y >= H * 0.62 && l.y < H * 0.94 && !isUI(l.t));
const firstLine = (f, re) => (f.lines || []).find(l => re.test(l.t));

function uniquePush(list, text) {
  const t = clean(text);
  if (!t || t.length < 2 || isNoise(t)) return false;
  const n = norm(t);
  if (!n) return false;
  /* 2026-10-31：不只要「丟掉重複」，還要「用長的取代短的」。
     面板底部常把一行切成兩半，先收到的是殘尾（`了。下面很熱，全都是`），
     後收到的是整行；以前只檢查「新的是否被舊的包含」，
     於是殘尾留下、整行被當重複丟掉，intro 就變成一段殘句。 */
  const grams2 = x => { const g = new Set(); for (let i = 0; i < x.length - 1; i++) g.add(x.slice(i, i + 2)); return g; };
  for (let i = 0; i < list.length; i++) {
    const y = norm(typeof list[i] === 'string' ? list[i] : list[i].text);
    if (y === n) return false;
    let dup = false;
    if (y.length > 8 && n.length > 8 && (y.includes(n) || n.includes(y))) dup = true;
    /* 只差一兩個符號（OCR 每幀略有不同）→ 也算同一句，留長的 */
    if (!dup && y.length > 10 && n.length > 10) {
      const A = grams2(y), B = grams2(n);
      let inter = 0; for (const x of A) if (B.has(x)) inter++;
      const jac = inter / (A.size + B.size - inter || 1);
      if (jac >= 0.8) dup = true;
    }
    if (dup) {
      if (n.length > y.length) list[i] = t;   /* 新的更完整 → 取代舊的 */
      return false;
    }
  }
  list.push(t);
  return true;
}
function addText(obj, key, text) {
  if (!obj[key]) obj[key] = [];
  uniquePush(obj[key], text);
}
function textOf(a) { return (a || []).join('\n'); }
function addEffect(obj, s) {
  const out = [];
  let m;
  EFFECT_RE.lastIndex = 0;
  while ((m = EFFECT_RE.exec(s))) {
    const amount = parseInt(String(m[2]).replace(/\s+/g, ''), 10);
    if (!Number.isFinite(amount)) continue;
    const kind = m[1].replace('体力', '體力').replace('经验', '經驗');
    out.push({ kind, amount });
  }
  if (!out.length) return;
  if (!obj.effects) obj.effects = [];
  for (const e of out) {
    if (!obj.effects.some(x => x.kind === e.kind && x.amount === e.amount)) obj.effects.push(e);
  }
}
/* 檢定字串的兩種寫法：
   （力量 檢定）        ← 有括號
   你魅力难度12          ← 遊戲實際輸出，沒有括號、也沒有「檢定」二字
   以前只認第一種，所以每一個 action 的 check 都是 null、Wiki 上看不到
   「要用哪個屬性、難度多少、成功還是失敗」。 */
const ATTR_SRC = '力量|敏捷|體質|体质|感知|智力|魅力';
const CHECK_PAREN = new RegExp('[（(]\\s*(' + ATTR_SRC + ')\\s*[^）)]*[）)]');
const CHECK_BARE = new RegExp('(' + ATTR_SRC + ')\\s*(?:檢定|检定)?\\s*(?:難度|难度)');

function parseCheck(s) {
  const str = String(s || '');
  if (!str) return null;
  const m = str.match(CHECK_PAREN) || str.match(CHECK_BARE);
  if (!m) return null;
  const out = { attribute: m[1].replace('体质', '體質'), hasCheck: true, roll: null };
  const dc = str.match(/(?:難度|难度)\s*[:：]?\s*(\d{1,2})/);
  if (dc) out.difficulty = Number(dc[1]);
  const o = str.match(OUTCOME_RE);
  if (o) out.outcome = /失敗|失败/.test(o[0]) ? 'failure' : 'success';
  return out;
}

function addCheck(obj, s) {
  const c = parseCheck(s);
  if (!c) return;
  obj.check = Object.assign({}, obj.check || {}, c);
}

const records = [];
let floor = null;
let event = null;
let junction = null;
let lastProgress = null;
let lastFloorId = '';
let lastFloorName = '';

function floorFor(f) {
  const title = firstLine(f, /Level\s*\d+(?:\.\d+)?/i);
  const status = firstLine(f, /第\s*\d+\s*[层層]\s*[|｜Il]/);
  const hdr = mainLines(f).find(l => /——\s*Level\s*\d+(?:\.\d+)?/i.test(l.t));
  const id = floorIdOf((title && title.t) || (hdr && hdr.t) || '');
  const name = floorNameOf((title && title.t) || (hdr && hdr.t) || '');
  if (id) {
    const cf = canonFloor(id, name || lastFloorName);
    lastFloorId = cf.id || id;
    if (cf.name) lastFloorName = cf.name;
    return { id: lastFloorId, name: lastFloorName, title: title ? title.t : '', status: status ? status.t : '' };
  }
  return null;
}

function ensureFloor(f) {
  const nf = floorFor(f);
  if (!nf) return;
  if (!floor || floor.floorId !== nf.id) {
    floor = { type: 'floor', floorId: nf.id, name: nf.name, title: nf.title, status: nf.status, firstFrame: f.frame, events: [] };
    records.push(floor);
    event = null; junction = null;
  } else if (nf.name) floor.name = nf.name;
}

function sim(a, b) {
  const A = norm(a), B = norm(b);
  if (!A || !B) return 0;
  let same = 0;
  const n = Math.min(A.length, B.length);
  for (let i = 0; i < n; i++) if (A[i] === B[i]) same++;
  return same / Math.max(A.length, B.length);
}

function ensureEvent(f, name, kind = 'event') {
  ensureFloor(f);
  name = clean(name).replace(/^【|】$/g, '');
  if (/你迷失|这一局|這一局|稱號|称号|冒險結束|冒险结束/.test(name)) return null;
  if (event && !event.completed && event.kind === kind && sim(event.name, name) >= 0.45) {
    if (name.length > event.name.length) event.name = name;
    event.frames.push(f.frame);
    return event;
  }
  event = { type: 'event', kind, floor: floor ? floor.floorId : lastFloorId, name, intro: [], actions: [], results: [], effects: [], frames: [f.frame], startFrame: f.frame };
  if (junction && junction.selected) { event.fromJunction = junction.id; event.fromPath = junction.selected.text; }
  records.push(event);
  return event;
}
function ensureJunction(f) {
  ensureFloor(f);
  if (junction && !junction.closed) return junction;
  junction = { type: 'junction', id: 'j-' + records.length, floor: floor ? floor.floorId : lastFloorId, text: [], prompt: '要走哪條路？', paths: [], results: [], frames: [f.frame] };
  records.push(junction);
  return junction;
}
function closeJunction() { if (junction) { junction.closed = true; junction = null; } }
function closeEvent() { if (event) { event.completed = true; event = null; } }

/* 戰鬥不是事件流程，只是某個選擇的結果。
   只記錄「這裡會進入戰鬥：敵人」，不保存 HP／先攻／回合／逃跑過程。 */
function markCombatTrigger(name, frame) {
  const floorRec = records.slice().reverse().find(r => r.type === 'floor');
  const fid = floorRec ? floorRec.floorId : (floor ? floor.floorId : lastFloorId);
  const last = records.slice().reverse().find(r =>
    r.floor === fid &&
    ((r.type === 'junction' && r.selected) || (r.type === 'event' && r.selected)));
  if (last && (!frame || !last.frames || Math.max(...last.frames) >= frame - 4)) {
    last.combat = name;
    last.combats = last.combats || [];
    if (!last.combats.includes(name)) last.combats.push(name);
    if (last.type === 'junction' && last.selected) last.selected.combat = name;
  } else if (floorRec) {
    floorRec.combats = floorRec.combats || [];
    if (!floorRec.combats.includes(name)) floorRec.combats.push(name);
  } else if (floor) {
    floor.combats = floor.combats || [];
    if (!floor.combats.includes(name)) floor.combats.push(name);
  }
  closeEvent();
  closeJunction();
}

function parseOptions(f, promptY, forEvent) {
  const opts = [];
  for (const l of optionLines(f)) {
    if (l.y <= promptY + 4) continue;
    let t = collapseRepeats(clean(l.t)).replace(/^[-–—•·]\s*/, '');
    if (!t || isNoise(t)) continue;
    if (/^(說出來|说出来|跟隨多數|跟随多数)$/.test(t)) continue;
    /* 2026-10-31：選項區常混進裝備列與 UI（★「工地安全帽」、★力量4+、S W S、随多数：）
       —— 這些不是選項。 */
    if (/^[★☆]/.test(t)) continue;
    if (/^S\s*W\s*S$/i.test(t)) continue;
    if (/^[随隨跟]?多數[:：]?$/.test(t)) continue;
    if (/^随多数/.test(t) || /^跟隨多數/.test(t)) continue;
    if (/難度|难度|檢定|检定/.test(t)) {
      if (opts.length) {
        const chk = parseCheck(t);
        if (chk) opts[opts.length - 1].check = chk;
        if (/只有你察覺到|只有你察觉到/.test(t)) opts[opts.length - 1].perception = true;
      }
      continue;
    }
    const hint = t.match(/[（(]\s*只有你察覺到[）)]\s*(.*)$/);
    if (hint && opts.length) {
      opts[opts.length - 1].perception = true;
      opts[opts.length - 1].hint = clean(hint[1]);
      continue;
    }
    if (/^[（(]/.test(t)) continue;
    if (opts.some(o => norm(o.text) === norm(t))) continue;
    opts.push({ text: t, y: l.y, perception: false });
  }
  /* 2026-11-01 使用者：選項要按照畫面順序（由上到下）排。 */
  opts.sort((a, b) => (a.y || 0) - (b.y || 0));
  return opts;
}

let eventParseDone = 0;
for (const f of frames) {
  report(session, '重建事件', ++eventParseDone, frames.length, '第 ' + (f && f.frame) + ' 幀');
  if (!f) continue;
  /* 2026-10-31：layout 不是 normal 就不是故事畫面（背包、收藏櫃、結局全螢幕…）。
     以前在這些幀上照樣找【】，於是【普通】【稀有】這種稀有度標籤變成一則則「事件」，
     整份物品清單被吃進 intro。 */
  if (f.layout && f.layout !== 'normal') continue;
  ensureFloor(f);
  const lines = f.layout === 'ending' ? (f.lines || []) : mainLines(f);
  const _framePitch = linePitch(lines);
  const _eventGapStarts = new Set();
  for (let _gi = 1; _gi < lines.length; _gi++) {
    const _prev = lines[_gi - 1], _cur = lines[_gi];
    if (isEventGap((_prev.y || 0) + (_prev.h || 0), _cur.y || 0, _framePitch)) _eventGapStarts.add(_cur);
  }
  const story = lines.filter(l => !isCheck(l.t) && !isDialogue(l.t) && !isAbility(l.t) && !isDecision(l.t) && !isPromptPath(l.t) && !isPromptEvent(l.t) && !isComplete(l.t) && !isProgress(l.t) && !isNoise(l.t));

  // progress / 完成
  const prog = lines.find(l => isProgress(l.t));
  if (prog) {
    const m = prog.t.match(/(\d+)\s*\/\s*(\d+)/);
    if (m) lastProgress = { current: +m[1], total: +m[2] };
    if (event) event.progress = lastProgress;
  }
  if (lines.some(l => isComplete(l.t)) && event) event.completed = true;

  // event / combat marker
  const combatLine = lines.find(l => COMBAT_RE.test(l.t));
  /* 2026-10-31：事件標題必須「只有【名稱】」——不能有尾隨文字。
     遊戲裡事件標題是單獨一行 `【哭着的小孩】`；
     任務卡是 `【接下委托】跑进管子里的猫★☆☆☆☆`（同一行後面還有內容），
     以前整行都當事件，於是任務描述＋下一個場景全被吃進 intro。
     這條是結構判據（看這一行還有沒有別的內容），不是字數限制。 */
  /* 事件標題：文字條件（【】單獨一行、不是任務卡）＋ 顏色投票。
     2026-10-31：使用者指出「很多字體顏色都有差異」，實測事件標題是飽和度高的金色，
     正文是低飽和的橄欖色。有 ink 資料時，顏色能把「長得像標題但其實是正文」的誤判否掉；
     沒有 ink（舊場次）就完全照舊，行為不變。 */
  const _inkInfo = classifyFrame(lines);
  const _cand = [];
  for (const l of lines) {
    if (!EVENT_RE.test(l.t) || /戰鬥|战斗/.test(l.t)) continue;
    const nm = clean(l.t.match(EVENT_RE)[1]);
    if (NOT_EVENT_RE.test(nm)) continue;
    /* 標題後面接著★☆☆☆☆＝任務卡，不是事件 */
    const trailing = clean(l.t).replace(/^【[^】]*】/, '');
    if (/[★☆]/.test(trailing)) continue;
    _cand.push({ line: l, score: headerScore(l, _inkInfo.base) });
  }
  const eventLine = _cand.length ? _cand.sort((a, b) => b.score - a.score)[0].line : null;
  if (combatLine) {
    const m = combatLine.t.match(COMBAT_RE);
    const name = clean((m && m[1]) || '戰鬥');
    markCombatTrigger(name, f.frame);
    continue;
  } else if (eventLine) {
    const name = clean(eventLine.t.match(EVENT_RE)[1]);
    const ev = ensureEvent(f, name, 'event');
    if (!ev) continue;
    ev.header = clean(eventLine.t);
    ev.headerY = eventLine.y;   /* 標題在畫面中的 y（當幀座標） */
    /* QTE：這一幀有「閃！」這類提示 → 這一則是即時反應事件 */
    if (lines.some(l => QTE_RE.test(String(l.t)))) ev.qte = true;
    /* 以「版面塊」為單位收 intro（2026-10-31）。
       塊 = 主文面板裡被「大空隙」隔開的一段（段落／區段）。
       規則：
         ‧ 從「標題所在那一塊」開始，標題之前的行一律不算
         ‧ 逐塊往下；遇到「區段開頭塊」就停：
             - 塊首是【…】／{出口進度}／走哪條路／要怎麼做／擲骰算式
             - 塊首是 →你決定（那是結果，交給 results）
         ‧ 塊內多行照收（同一段被折行）
       全部是版面幾何＋固定字串，沒有字數門檻。 */
    const _pitch = linePitch(lines);
    const _blocks = splitBlocks(lines, _pitch);
    let _from = _blocks.findIndex(b => b.lines.some(l => l === eventLine));
    if (_from < 0) _from = _blocks.findIndex(b => b.lines.some(l => Math.abs((l.y || 0) - eventLine.y) <= 8));
    if (_from < 0) _from = 0;
    let _prevBlock = null;
    for (let bi = _from; bi < _blocks.length; bi++) {
      const b = _blocks[bi];
      if (bi > _from && _prevBlock && isEventGap(_prevBlock.y1, b.y0, _pitch)) break;
      const first = clean(b.lines[0] && b.lines[0].t);
      if (bi > _from && isSectionBlock(b)) break;
      if (bi > _from && isDecision(first)) break;
      if (bi > _from && (isPromptEvent(first) || isPromptPath(first) || isProgress(first) || isComplete(first))) break;
      for (const l of b.lines) {
        if (l === eventLine) continue;
        if (l.y < eventLine.y) continue;              /* 標題上方的行不屬於這一則 */
        if (isPromptEvent(l.t) || isPromptPath(l.t) || isProgress(l.t) || isComplete(l.t)) { bi = _blocks.length; break; }
        /* 任務卡（【接下委托】…★☆）也是邊界，不算本事件正文 */
        { const _m = String(l.t).match(/^【([^】]{1,40})】/); if (_m) { const _nm = clean(_m[1]); const _tr = clean(l.t).replace(/^【[^】]*】/, ''); if (NOT_EVENT_RE.test(_nm) || /[★☆]/.test(_tr)) { bi = _blocks.length; break; } } }
        if (isCheck(l.t)) { addCheck(ev, l.t); continue; }
        if (isDecision(l.t)) continue;
        if (isDialogue(l.t) || isAbility(l.t)) continue;
        addText(ev, 'intro', l.t);
      }
      _prevBlock = b;
    }

  }

  // 結局：可能發生在既有事件之後，不能只在 `!event` 時才偵測。
  {
    const _nf = floorFor(f);
    const endFloor = _nf ? { ..._nf, floorId: _nf.id } : floor;
    const endLine = lines.find(l => /冒险结束|冒險結束|这一局结束|這一局結束|你死了|你已死亡/.test(l.t))
      || (f.layout === 'ending' && lines.find(l => /全員?迷失|全员?迷失|全員?逃生|全员?逃生|你迷失|你已迷失/.test(l.t)));
    if (endFloor && endLine) {
      const em = endLine.t.match(/全員?迷失|全员?迷失|全員?逃生|全员?逃生|你迷失|你已迷失/);
      const ename = em ? em[0] : (/你死了|你已死亡/.test(endLine.t) ? '死亡' : '冒險結束');
      const ev = ensureEvent(endFloor, ename, 'ending');
      if (ev) {
        const endingLines = f.layout === 'ending'
          ? lines.filter(l => !/点一下|點一下|跳过|跳過|^\s*1\s*\/\s*1\s*$/.test(l.t))
          : [endLine];
        for (const l of endingLines) addText(ev, 'intro', l.t);
      }
    }
  }

  // 沒有【戰鬥】標記時的戰鬥／出口 fallback（只看明確畫面文字）
  if (!event) {
    const curFloor = floorFor(f) || floor;
    const combatHeader = lines.find(l => /第\s*\d+\s*层\s*[|丨]\s*战斗|第\s*\d+\s*層\s*[|丨]\s*戰鬥|先攻顺序|先攻順序/.test(l.t));
    if (curFloor && combatHeader) {
      const near = lines.find(l => /猎犬|獵犬|光附|夢魘|梦魇|实体|實體|戰鬥|战斗/.test(l.t) && !/第\s*\d+/.test(l.t) && !/先攻/.test(l.t));
      const nm = near ? clean(near.t)
        .replace(/^[【〖〔\[]*\s*(?:战斗|戰鬥)\s*[】〗〕\]]*\s*/, '')
        .replace(/^[【〖〔\[]+/, '')
        .replace(/[】〗〕\]]+$/, '')
        .replace(/[（(].*$/, '')
        .replace(/灼烧.*$/, '')
        .slice(0, 20) : '戰鬥';
      markCombatTrigger(nm, f.frame);
      continue;
    }
    const exitLine = lines.find(l => /这一层再也找不到别的路了|這一層再也找不到別的路了|要從哪裡離開|要从哪里离开/.test(l.t));
    if (curFloor && exitLine) {
      const ev = ensureEvent(curFloor, '出口', 'exit');
      if (ev) addText(ev, 'intro', exitLine.t);
    }
  }

  // path junction
  const pathPrompt = lines.find(l => isPromptPath(l.t));
  if (pathPrompt && !(event && !event.completed)) {
    const j = junction && !junction.closed && !junction.selected ? junction : ensureJunction(f);
    if (!j.frames) j.frames = [f.frame];
    else if (!j.frames.includes(f.frame)) j.frames.push(f.frame);
    if (j.closed || j.selected) { closeJunction(); ensureJunction(f); }
    j.prompt = clean(pathPrompt.t);
    const before = lines.filter(l => l.y < pathPrompt.y && l.y > pathPrompt.y - 180 && !isDecision(l.t) && !isCheck(l.t) && !isDialogue(l.t) && !isAbility(l.t) && !/^【/.test(clean(l.t))).slice(-2);
    for (const l of before) addText(j, 'text', l.t);
    const opts = parseOptions(f, pathPrompt.y, false);
    for (const o of opts) if (!j.paths.some(x => norm(x.text) === norm(o.text))) j.paths.push(o);
  }

  // event action prompt
  const actionPrompt = lines.find(l => isPromptEvent(l.t));
  if (actionPrompt && event) {
    event.prompt = clean(actionPrompt.t);
    const opts = parseOptions(f, actionPrompt.y, true).filter(o => !(junction && junction.paths.some(p => norm(p.text) === norm(o.text))));
    for (const o of opts) {
      if (!event.actions.some(x => norm(x.text) === norm(o.text))) event.actions.push(o);
    }
  }

  // decision: path or action
  const dec = lines.find(l => isDecision(l.t));
  if (dec) {
    const txt = clean(dec.t).replace(/^.*?[：:]\s*[「『]?/, '').replace(/[」』]?$/, '').trim();
    /* 兩名玩家各自選一次時，畫面會連續出現「→ 角色：「選項」」。
       這些不是同一顆選項，而是同一個事件的多個 selected；先保留成 actions，
       後面的 results 再由 steps.mjs 切成多個階段。 */
    if (event && ACTOR_DECISION.test(dec.t)) {
      if (!event.actions.some(a => norm(a.text) === norm(txt))) {
        event.actions.push({ text: txt, selected: false });
      }
    }
    if (event && event.actions.length && !event.selected) {
      const hit = event.actions.find(a => norm(a.text) === norm(txt)) || event.actions.find(a => norm(a.text).includes(norm(txt)) || norm(txt).includes(norm(a.text)));
      if (hit) {
        event.selected = { text: txt, matched: true };
        hit.selected = true;
        event.selectedFrame = f.frame;
      } else {
        /* 選項文字對不上（OCR 把「坐下来」讀成「坐下采」之類）。
           以前這裡直接放棄 → event.selected 永遠是空的 → 後面的結果／效果全部收不到。
           現在照樣標成「已決定」，matched:false，結果文字還是能收。 */
        event.selected = { text: txt, matched: false };
        event.selectedFrame = f.frame;
      }
    } else if (event && event.selected && ACTOR_DECISION.test(dec.t)) {
      const hit2 = event.actions.find(a => norm(a.text) === norm(txt));
      if (hit2) hit2.selected = true;
    } else if (junction && !junction.selected) {
      const hit = junction.paths.find(p => norm(p.text) === norm(txt)) || junction.paths.find(p => norm(p.text).includes(norm(txt)) || norm(txt).includes(norm(p.text)));
      junction.selected = { text: txt, matched: !!hit };
      if (hit) hit.selected = true;
      junction.selectedFrame = f.frame;
      junction.closed = true;
    }
  }

  // checks / effects / results
  for (const l of lines) {
    /* 空行／大空隙 → 這裡已是下一段或下一個事件，不再收進本事件結果。 */
    if (_eventGapStarts.has(l)) break;
    /* 碰到下一個事件的標題就收手：同一個畫面上面是「上一個事件的結果」，
       下面已經是「下一個事件的標題＋介紹」，不收手會把整段新事件塞進舊事件的結果裡。 */
    if (/^【/.test(clean(l.t))) break;
    if (isCheck(l.t) && event) {
      if (event.selected || event.actions.length) {
        addCheck(event, l.t);
        addEffect(event, l.t);
      }
    }
    /* 檢定判定行：掛在「被選中的選項」上，並記下這一支是成功還是失敗 */
    if (event) {
      const _cr = parseCheckResult(l.t);
      /* QTE 判定：出現「慢了一步／閃過」這類字就歸到對應分支 */
      if (event.qte && (QTE_FAIL_RE.test(l.t) || QTE_OK_RE.test(l.t))) {
        const kind = QTE_FAIL_RE.test(l.t) ? 'failure' : 'success';
        event.branch = kind;
        ensureOutcomes(event)[kind] = ensureOutcomes(event)[kind] || { lines: [] };
        const _qa = (event.actions || []).find(a => a.selected) || null;
        if (_qa) { _qa.qte = true; _qa.outcome = kind; }
      }
      if (_cr) {
        event.check = Object.assign({}, event.check || {}, _cr);
        const _act = (event.actions || []).find(a => a.selected) || null;
        if (_act) { _act.check = Object.assign({}, _act.check || {}, _cr); _act.outcome = _cr.outcome || _act.outcome; }
        if (_cr.outcome) { event.branch = _cr.outcome; ensureOutcomes(event)[_cr.outcome] = ensureOutcomes(event)[_cr.outcome] || { lines: [] }; }
      }
    }
    /* 分支結束：出口進度／提示語／下一個【／背包商店 → 這一支的結果收完了 */
    if (event && event.branch) {
      const _bt = clean(l.t);
      if (isProgress(l.t) || isPromptEvent(l.t) || isPromptPath(l.t) || isComplete(l.t)
          || /^【/.test(_bt) || /背包|商店|收藏柜|队伍频道|設定|设定|离开|離開/.test(_bt)) {
        event.branch = null;
      }
    }
    if (event && event.selected && f.frame >= (event.selectedFrame || 0)) {
      /* 數值效果（體力 -1、經驗 +3）先收，這一行本身是 UI 雜訊還是不收進正文。 */
      addEffect(event, l.t);
        /* 2026-11-01 連環事件：第一次「你決定」就是本事件的選擇；
           第二次以後要留成階段標記，否則後段全丟。
           只有 results 已經有內容才留，避免一步事件被自己污染。 */
        if (isDecision(l.t)) {
          if ((event.results || []).length) addText(event, 'results', clean(l.t));
          continue;
        }
      if (!isDialogue(l.t) && !isCheck(l.t) && !isPromptEvent(l.t) && !isComplete(l.t) && !isProgress(l.t) && !isNoise(l.t)) {
        const n = norm(l.t);
        const already = [...(event.intro || []), ...(event.actions || []).map(a => a.text)].some(x => norm(x) === n || (norm(x).length > 8 && (norm(x).includes(n) || n.includes(norm(x)))));
        if (!already) {
          /* 有檢定分支時，結果文字歸到那支（成功／失敗）；沒有才放進一般 results */
          if (event.branch) {
            const box = ensureOutcomes(event)[event.branch] = ensureOutcomes(event)[event.branch] || { lines: [] };
            box.lines = box.lines || [];
            uniquePush(box.lines, l.t);
            addEffect(box, l.t);
          } else {
            /* 2026-10-31：這幾類是 UI，不是結果正文（實測污染過 results） */
            if (!/^(跟[隨随]多數|跟随多数|設定|设置|靜音|静音|★)/.test(clean(l.t))) addText(event, 'results', l.t);
          }
        }
      }
    }
  }
  if (event && event.completed) event = null;
}

// finalize
if (junction) junction.closed = true;
if (event) event.completed = !!event.progress || event.completed;

function uniqList(a, b) {
  const out = [];
  const keyOf = x => typeof x === 'string' ? norm(x) : (x && x.kind ? x.kind + '|' + x.amount : norm(x && x.text));
  for (const x of [...(a || []), ...(b || [])]) {
    const t = typeof x === 'string' ? clean(x) : x;
    if (typeof t === 'string' && (!t || isNoise(t))) continue;
    const n = keyOf(t);
    if (!out.some(y => keyOf(y) === n)) {
      if (t && typeof t === 'object' && t.text) {
        const near = out.find(y => y && typeof y === 'object' && y.text && sim(y.text, t.text) >= 0.62);
        if (near) {
          if (String(t.text).length > String(near.text).length) near.text = t.text;
          if (t.selected) near.selected = true;
          if (t.perception) near.perception = true;
          if (t.hint && !near.hint) near.hint = t.hint;
          if (t.check && !near.check) near.check = t.check;
          continue;
        }
      }
      out.push(t);
    }
  }
  return out;
}
function eventKey(e) {
  const n = norm(e.name).replace(/[a-z0-9]/g, '');
  return String(e.floor) + '|' + (n.slice(0, 6) || n);
}
function junctionKey(j) {
  const opts = (j.paths || []).map(p => norm(p.text)).sort().join('/');
  return String(j.floor) + '|' + opts + '|' + norm(j.text).slice(0, 10);
}

const mergedJ = new Map();
for (const r of records.filter(x => x.type === 'junction')) {
  const k = junctionKey(r);
  const old = mergedJ.get(k);
  if (!old) { mergedJ.set(k, JSON.parse(JSON.stringify(r))); continue; }
  old.paths = uniqList(old.paths, r.paths);
  old.results = uniqList(old.results, r.results);
  old.selected = old.selected || r.selected;
  old.prompt = old.prompt || r.prompt;
  old.closed = old.closed || r.closed;
  old.frames = [...new Set([...(old.frames || []), ...(r.frames || [])])];
}

const mergedE = new Map();
for (const r of records.filter(x => x.type === 'event')) {
  const k = eventKey(r);
  const old = mergedE.get(k);
  if (!old) { mergedE.set(k, JSON.parse(JSON.stringify(r))); continue; }
  old.intro = uniqList(old.intro, r.intro);
  old.actions = uniqList(old.actions, r.actions);
  old.results = uniqList(old.results, r.results);
  old.effects = uniqList(old.effects, r.effects);
  old.selected = old.selected || r.selected;
  old.check = old.check || r.check;
  old.progress = old.progress || r.progress;
  old.completed = old.completed || r.completed;
  if ((r.name || '').length > (old.name || '').length) old.name = r.name;
  old.frames = [...new Set([...(old.frames || []), ...(r.frames || [])])];
}

// 第二階段：同一層、名稱或 intro 高度相似的事件合併（OCR 變體）
const eventsByFloor = new Map();
for (const e of mergedE.values()) {
  if (!eventsByFloor.has(e.floor)) eventsByFloor.set(e.floor, []);
  eventsByFloor.get(e.floor).push(e);
}
const mergedE2 = [];
for (const list of eventsByFloor.values()) {
  for (const e of list) {
    const near = mergedE2.find(x => x.floor === e.floor && (
      sim(x.name, e.name) >= 0.55 ||
      (sim(textOf(x.intro), textOf(e.intro)) >= 0.78 && x.intro && e.intro)
    ));
    if (!near) { mergedE2.push(e); continue; }
    if (String(e.name).length > String(near.name).length) near.name = e.name;
    near.intro = uniqList(near.intro, e.intro);
    near.actions = uniqList(near.actions, e.actions);
    near.results = uniqList(near.results, e.results);
    near.effects = uniqList(near.effects, e.effects);
    near.selected = near.selected || e.selected;
    near.check = near.check || e.check;
    near.progress = near.progress || e.progress;
    near.completed = near.completed || e.completed;
  }
}


const floorMap = new Map();
/* 決策行 fallback：玩家按鍵不一定會被 input-bind 綁到（尤其滑鼠點選）。
   畫面上的「你決定：「…」」是權威來源；用它回填沒有 selected 的路口／事件。 */
for (const rec of [...mergedE2, ...mergedJ.values()]) {
  if (rec.selected) continue;
  const list = rec.type === 'junction' ? (rec.paths || []) : (rec.actions || []);
  if (!list.length || !rec.frames || !rec.frames.length) continue;
  const from = Math.min(...rec.frames), to = Math.max(...rec.frames);
  let found = null;
  for (const f of frames) {
    if (!f || f.frame < from || f.frame > to) continue;
    for (const l of (f.lines || [])) {
      if (!isDecision(l.t) && !/^[→>\s]*[^\s：:]{1,12}[：:]\s*[「『]/.test(l.t)) continue;
      const txt = clean(l.t).replace(/^.*?[：:]\s*[「『]?/, '').replace(/[」』]?$/, '').trim();
      const hit = list.find(a => norm(a.text) === norm(txt)) || list.find(a => norm(a.text).includes(norm(txt)) || norm(txt).includes(norm(a.text)));
      if (hit && (!found || f.frame >= found.frame)) found = { text: txt, matched: true, frame: f.frame, hit };
    }
  }
  if (found) {
    rec.selected = { text: found.text, matched: true };
    rec.selectedFrame = found.frame;
    found.hit.selected = true;
  }
}
for (const r of records.filter(x => x.type === 'floor')) {
  const k = String(r.floorId) + '|' + norm(r.name);
  if (!floorMap.has(k)) floorMap.set(k, r);
}
const cleanEvents = extractEvents(frames);
const out = [
  ...floorMap.values(),
  ...mergedJ.values(),
  ...cleanEvents
].map(r => {
  if (r.type === 'floor') return { ...r, intro: [], events: undefined };
  if (r.type === 'junction') {
    if (r.selected) r.selectedRaw = r.selected.text;
    if (r.selected && !r.paths.some(p => norm(p.text) === norm(r.selected.text) || norm(p.text).includes(norm(r.selected.text)) || norm(r.selected.text).includes(norm(p.text)))) r.selected = null;
    return { ...r, text: textOf(r.text), results: r.results };
  }
  if (r.type === 'event') {
    r.actions = (r.actions || []).filter(a => a && a.text && !/^(?:选一个去做|選一個去做|要怎么做|要怎麼做|继续|繼續|看完之后按.*)$/.test(norm(a.text)));
    if (r.selected && r.actions.length && !r.actions.some(a => norm(a.text) === norm(r.selected.text) || norm(a.text).includes(norm(r.selected.text)) || norm(r.selected.text).includes(norm(a.text)))) r.selected = null;
    const act = new Set((r.actions || []).map(a => norm(a.text)));
    const res = new Set((r.results || []).map(a => norm(a)));
    const intro = (typeof r.intro === 'string' ? [r.intro] : (r.intro || [])).filter(x => {
      const n = norm(x);
      return n && !act.has(n) && !res.has(n) && !/妻怎么做|怎么做|按右键|可以取消|选一个去做|跟隨多數|跟随多数/.test(n);
    });
    /* 最後再過一次同樣的去重規則（合併多幀時，殘尾與整行會同時存在） */
    const _introDedup = [];
    intro.forEach(x => uniquePush(_introDedup, x));
    return { ...r, intro: textOf(_introDedup), results: r.results };
  }
  return r;
}).filter(r => r.type === 'floor' || (r.type === 'junction' && r.paths.length) || (r.type === 'event' && r.name && r.name.length >= 2 && !/你迷失|这一局|這一局|稱號|称号|继续|繼續|Level\s*\?|^(?:力量|敏捷|體質|体质|感知|智力|魅力|體力|体力|理智)\s*(?:難度|难度)|^\d+$/.test(r.name)));

fs.writeFileSync(path.join(outDir, 'events.jsonl'), out.map(x => JSON.stringify(x)).join('\n') + (out.length ? '\n' : ''), 'utf8');
fs.writeFileSync(path.join(outDir, 'events-report.json'), JSON.stringify({
  session: path.basename(session), generatedAt: new Date().toISOString(),
  counts: { floors: out.filter(x => x.type === 'floor').length, junctions: out.filter(x => x.type === 'junction').length, events: out.filter(x => x.type === 'event').length, combats: out.filter(x => x.type === 'event' && x.kind === 'combat').length },
  records: out
}, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ out: path.join(outDir, 'events.jsonl'), counts: { junctions: out.filter(x => x.type === 'junction').length, events: out.filter(x => x.type === 'event').length } }, null, 2));

