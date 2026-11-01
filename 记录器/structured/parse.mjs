import fs from 'node:fs';
import path from 'node:path';
import { FLOORS } from './paths.mjs';
import { report as reportProgress } from './progress.mjs';
import { floorName, text } from './text.mjs';
/* 樓層名的比對一律先繁化再比：配色表是 OCR 原文（简体）、正式樓層是繁體，
   直接比字串永遠對不上，配色就會被誤判成「不是這一層」。 */
const nmOf = s => floorName(text(s));

/*
 * structured.jsonl -> 候選樓層／岔路／戰鬥記錄
 * 規則式，不使用 AI。輸出只供 staging 審核。
 *
 * node parse.mjs <sessionDir> [--out <dir>]
 */

const W = 1606;
const H = 929;
/* 配色表：以前只用 id 當 key，於是同 id 的兩層（1「停車區」/ 1「停電的樓上」）
   會拿到同一份色；更糟的是記錄器把幽靈樓層（霓虹车站）的取樣寫進 id=1，
   正式樓層就跟著變色。現在 key 是 id|name，名字對不上就不採用。 */
const FLOOR_PALETTES = (() => {
  const empty = { byKey: new Map(), byId: new Map() };
  try {
    const f = path.join(process.argv[2], 'floor-palettes.json');
    if (!fs.existsSync(f)) return empty;
    const a = JSON.parse(fs.readFileSync(f, 'utf8').replace(/^\uFEFF/, ''));
    const byKey = new Map(), byId = new Map();
    for (const x of (Array.isArray(a) ? a : [a])) {
      if (!x || !x.palette) continue;
      const id = String(x.id);
      const nm = nmOf(x.name || '');
      byKey.set(id + '|' + nm, x.palette);
      /* 同 id 出現第二次 → 這份 id 不可信，直接標成 null，之後不准用 id 直取。 */
      if (byId.has(id)) byId.set(id, null);
      else byId.set(id, { name: nm, palette: x.palette });
    }
    return { byKey, byId };
  } catch { return empty; }
})();
function paletteFor(floorId, name) {
  const id = String(floorId || '');
  const nm = nmOf(name || '');
  const exact = FLOOR_PALETTES.byKey.get(id + '|' + nm);
  if (exact) return exact;
  const one = FLOOR_PALETTES.byId.get(id);
  if (one && one.palette && one.name === nm) return one.palette;
  return null;
}
const UI = [
  '等大家決定', '跟隨多數', '跟隨當前', '強制倒數', '催促', '背包 (B)', '看背景',
  '設定', '離開', '隊伍頻道', '送出', '說点什麼', '說点什麼……', '思考中', '行動中',
  '閱讀中', '準備好', '開始冒險', '建立角色', '換頭貼', '外觀與徽章', '記憶商店',
  '排行榜', '授權與來源', 'Patreon', '感謝名單', 'Discord', '繁體中文', '简体中文',
  '公開房間', '一個人冒險', '開房間', '好友', '會員', '登入', '訪客', '載入'
];
const DIFF = /難度|难度|體質|体质|力量|敏捷|感知|智力|魅力|檢定|检定/;

function norm(t) {
  return String(t || '')
    .replace(/\s+/g, '')
    .replace(/[“”「」『』]/g, '')
    .replace(/[，。！？、；：]/g, '')
    .trim();
}

function bigrams(s){
  const n=norm(s); const out=new Set();
  for(let i=0;i<n.length-1;i++) out.add(n.slice(i,i+2));
  return out;
}
function sim(a,b){
  const A=bigrams(a), B=bigrams(b);
  if(!A.size || !B.size) return 0;
  let inter=0; for(const x of A) if(B.has(x)) inter++;
  return inter / Math.max(A.size,B.size);
}
function ui(t) {
  const n = norm(t);
  if (!n) return true;
  return UI.some(w => n.includes(norm(w)));
}
function jsonl(file) {
  return fs.readFileSync(file, 'utf8').split(/\r?\n/).filter(Boolean).map(s => {
    try { return JSON.parse(s); } catch { return null; }
  }).filter(Boolean);
}
function floors() {
  const root = FLOORS;
  const out = [];
  if (!fs.existsSync(root)) return out;
  for (const e of fs.readdirSync(root, { withFileTypes: true })) {
    if (!e.isDirectory()) continue;
    const p = path.join(root, e.name, 'floor.json');
    if (!fs.existsSync(p)) continue;
    try {
      const o = JSON.parse(fs.readFileSync(p, 'utf8'));
      out.push({ id: String(o.id ?? ''), name: String(o.name ?? ''), key: String(o.key ?? '') });
    } catch { }
  }
  return out;
}
function numFix(s) { return String(s || '').replace(/[Oo]/g, '0').replace(/[Il]/g, '1'); }
function matchFloor(list, title, rawName) {
  const n = norm(title + ' ' + rawName);
  const mm = n.match(/level([\dOoIl]+(?:\.[\dOoIl]+)?)/i);
  const id = mm ? numFix(mm[1]) : '';
  let best = null;
  for (const f of list) {
    const fn = norm(f.name);
    if (fn && n.includes(fn)) { best = f; break; }
  }
  if (!best && id) best = list.find(f => String(f.id) === id) || null;
  return best;
}
function linesOf(f) {
  return f.lines || [];
}
function storyOf(f) {
  return linesOf(f).filter(l => l.x < W * 0.62 && l.y >= H * 0.075 && l.y < H * 0.63 && !ui(l.t));
}
function titleOf(f) {
  return linesOf(f).find(l => /Level\s*[\dOoIl]+(?:\.[\dOoIl]+)?/i.test(l.t) && l.y < H * 0.08) || null;
}
function statusOf(f) {
  return linesOf(f).find(l => l.y < H * 0.09 && /第\s*\d+\s*层\s*[|｜Il]/.test(l.t)) || null;
}
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

const ENEMY_LABELS = new Set(['抗性','敏捷','力量','體力','理智','防禦','防御','弱點','弱点','抵抗','狀態','状态','傷害','伤害','回合','攻擊','攻击','命中','閃避','速度','逃跑','背包','裝備','装备','技能','道具','先攻','MISS','書','书','等級','等级']);
/* 只留「像敵人名字」的字串：2~8 個漢字，去掉 A/B 編號、前綴、UI 標籤。
   回傳空字串代表這幀讀到的不是可靠名字，不要拿它建立新實體。 */
function cleanEnemyName(raw){
  let s = String(raw || '').trim();
  s = s.split(/[、,，丨|]/)[0].trim();
  s = s.replace(/^[。、,.\-\s]+/, '').replace(/\s*[A-Za-z]\s*$/, '').trim();
  s = stripEnemyPrefix(s);
  if (!/^[\u4e00-\u9fff]{2,8}$/.test(s)) return '';
  if (ENEMY_LABELS.has(s)) return '';
  return s;
}
function enemyOf(f, status, story) {
  const marker = story.find(l => /【戰鬥】|【战斗】|〖战斗〕|〖戰鬥〗|〔战斗〗|〔戰鬥〕|\[戰鬥\]|\[战斗\]/.test(l.t));
  if (!marker && !(status && /戰鬥|战斗/.test(status.t))) return null;
  let name = '';
  if (marker) {
    const m = marker.t.match(/[】〗〕\]]\s*(.+)$/);
    name = m ? cleanEnemyName(m[1]) : '';
  }
  /* 敵人欄可能同時有 A/B 兩隻；逐行清理，不要把所有行 join 成一個名字。 */
  const panelNames = linesOf(f)
    .filter(x => x.x < W * 0.25 && x.y >= H * 0.07 && x.y < H * 0.17 && !ui(x.t) && !/第\s*\d+\s*层|防\s*\d|防\s*御|回合|攻击|命中|掷出|先攻|点一下/.test(x.t))
    .map(x => cleanEnemyName(x.t))
    .filter(Boolean);
  if (!name && panelNames.length) name = panelNames[0];
  if (!name && status && /戰鬥|战斗/.test(status.t)) {
    const al = linesOf(f).find(x => /攻击\s*[^\s：:，。〈（(]{1,8}\s*[：:〈（(]/.test(x.t));
    if (al) {
      const am = al.t.match(/攻击\s*([^\s：:，。〈（(]{1,8})\s*[：:〈（(]/);
      if (am && !/防御|防禦|检定|檢定/.test(am[1])) name = cleanEnemyName(am[1]);
    }
  }
  return name ? { name, marker, panelNames } : null;
}

/* 只留「弱點／抗性」這種短註記；戰鬥流水（命中、傷害、1d6、回合）一律不留。 */
function isResistNote(t) {
  const s = String(t || '').trim();
  if (!s || s.length > 40) return false;
  if (/命中|造成|傷害|伤害|1d\d|骰|擲|掷|防御\s*\d|防\s*\d|回合|體力上限|背包|技能|指出弱點|后室百科|戰鬥技能|使用|選擇/.test(s)) return false;
  return /^(弱點|弱点|抗性|抵抗|免疫|怕)|弱點[:：]|弱点[:：]|沒什麼用|没什么用|效果不好/.test(s);
}
function isDropLine(t) {
  return /战利品|戰利品|掉落|获得|獲得|找到\s*\d+\s*(硬币|硬幣)|得到「|沒有人要「|没有人要「|想要的人按「想要」/.test(t)
    && !/升到|升级|升級|开关|開關|经验|經驗|听得到|聽得到/.test(t);
}
function cleanDrop(t) {
  return String(t).replace(/^.*?(?:战利品[：:]|戰利品[：:]|获得|獲得|掉落|找到|得到|沒有人要|没有人要)\s*/, '').replace(/想要的人按.*$/, '').trim();
}

function choiceOf(f) {
  const L = linesOf(f).filter(l => l.x < W * 0.62 && !ui(l.t));
  const prompt = L.find(l => l.y >= H * 0.63 && l.y < H * 0.69 && /要走哪|要怎麼|要怎么做|看完之後|看完之后|怎麼做|怎么做/.test(l.t));
  if (!prompt) return null;
  if (/看完之后按|看完之後按|继续|繼續/.test(prompt.t)) return null;
  const opts = [];
  for (const l of L) {
    if (l.x >= W * 0.08) continue; // 只看左側選項欄，避免背包／角色面板文字混入
    if (l.y <= prompt.y + 8 || l.y > H * 0.88) continue;
    const t = String(l.t).trim();
    if (!t || t.length < 2 || ui(t) || DIFF.test(t)) continue;
    if (/^[0-9０-９，,。.\s]+$/.test(t)) continue;
    if (/^[★☆]|^[「『]|女\s*r/.test(t) && opts.length) { opts[opts.length - 1].sub = t; continue; }
    if (/^[★☆]?[「『]?(杏仁水|绷带|罐頭|罐头)/.test(t) && opts.some(o => /杏仁水|绷带|罐頭|罐头/.test(o.text))) continue;
    if (opts.some(o => norm(o.text) === norm(t))) continue;
    opts.push({ id: 'opt-' + (opts.length + 1), text: t, difficulty: '' });
    if (opts.length >= 8) break;
  }
  // 難度行：接在選項附近，但不當成選項
  for (const o of opts) {
    const base = L.find(l => norm(l.t) === norm(o.text));
    if (!base) continue;
    const d = L.find(l => Math.abs(l.y - base.y) < 34 && /難度|难度/.test(l.t));
    if (d) o.difficulty = d.t.trim();
  }
  if (opts.length < 2) return null;
  let ptxt = prompt.t.trim().replace(/^妻/, '要').replace(/（按右键可以取消）|\(按右键可以取消\)|（再点一次可以取消）|\(再点一次可以取消\)/g, '').trim();
  return { prompt: ptxt, options: opts, promptLine: prompt };
}
function textOf(lines) {
  const arr = lines.map(l => String(l.t).trim()).filter(t => t && !ui(t));
  /* OCR 會把同一句斷成「，也是灰色的。」這種續行；不要丟，接回上一行。 */
  const out = [];
  for (const t of arr) {
    if (/^[，。、；：]/.test(t) && out.length) out[out.length - 1] += t;
    else out.push(t);
  }
  return out.join('\n');
}
function closeChoice(ch, records) {
  if (!ch) return;
  if (ch.selected) ch.confidence = ch.selected.matched ? 0.85 : 0.6;
}
function parse(sessionDir) {
  const frames = jsonl(path.join(sessionDir, 'structured.jsonl'));
  const fl = floors();
  const records = [];
  let curFloor = null;
  let floorSeen = new Set();
  let choice = null;
  let combat = null;
  let enemyVotes = new Map();
  let afterDecision = false;
  let parseDone = 0;

  for (const f of frames) {
    reportProgress(sessionDir, '解析 OCR', ++parseDone, frames.length, '第 ' + f.frame + ' 幀');
    const title = titleOf(f);
    const status = statusOf(f);
    const story = storyOf(f);
    const dropStory = linesOf(f).filter(l => l.x < W * 0.62 && l.y >= H * 0.075 && l.y < H * 0.85 && !ui(l.t));
    const enemy = enemyOf(f, status, story);
    const ch = choiceOf(f);
    const qte = linesOf(f).some(l => /照顺序按|照順序按|快速反应|快速反應|按顺序按/.test(l.t));

    // QTE／小遊戲畫面：保留樓層，但不要當成選項或戰鬥
    if (qte) continue;

    // 樓層
    if (title) {
      if (process.env.DEBUG_PARSE) console.error('TITLE', f.frame, JSON.stringify(title.t));
      const m = title.t.match(/[「『]([^」』]+)[」』]/) || title.t.match(/[「『]([^」』]*)$/);
      let rawName = m ? m[1].trim() : '';
      if(!rawName){
        rawName = String(title.t)
          .replace(/^Level\s*[\dOoIl]+(?:\.[\dOoIl]+)?\s*/i, '')
          .replace(/^[「『]/, '').replace(/[」』]$/, '').trim();
      }
      const idm = title.t.match(/Level\s*([\dOoIl]+(?:\.[\dOoIl]+)?)/i);
      const id = idm ? numFix(idm[1]) : '';
      const hit = matchFloor(fl, title.t, rawName);
      const floorId = String(hit ? hit.id : id || '');
      const key = floorId || (rawName + '|' + title.t);
      if (process.env.DEBUG_PARSE) console.error('FLOOR?', JSON.stringify({rawName, id, hit: hit && hit.id, floorId, key}));
      if (!floorSeen.has(key)) {
        floorSeen.add(key);
        curFloor = {
          type: 'floor', id: 'floor-' + (hit ? hit.id : id || 'unknown'),
          floorId: hit ? hit.id : id, name: floorName(hit ? hit.name : rawName), rawName,
          title: title.t, palette: paletteFor(floorId, hit ? hit.name : rawName) || (f.bg ? { bg: f.bg } : null),
          intro: '', introFrame: 0, introScore: 0, confidence: (rawName && id) ? 0.85 : 0.55,
          source: { frame: f.frame, ts: f.ts }
        };
        const _prev = records.filter(x => x.type === 'floor').slice(-1)[0];
        curFloor._prevIntroLines = new Set(String((_prev && _prev.intro) || '').split(/\n+/).map(s => s.replace(/\s+/g, '').trim()).filter(Boolean));
        records.push(curFloor);
        choice = null;
        combat = null;
        enemyVotes.clear();
      } else if (hit && curFloor && String(curFloor.floorId) === String(hit.id)) {
        /* 正式庫裡的名字也可能帶括號；這裡再過一次，避免把髒名字帶回 staging。 */
        curFloor.name = floorName(hit.name);
      }
      const isResume = story.some(l => /冒险继续|冒險繼續|上次说到|上次說到|重新连线|重新連線/.test(l.t));
      if (curFloor && story.length && !isResume) {
        let lv = story.findIndex(l => /——\s*Level/i.test(l.t));
        if (lv < 0) lv = story.findIndex(l => /Level\s*[\dOoIl]+/i.test(l.t));
        const src = lv >= 0 ? story.slice(lv + 1) : story.slice();
        const noise = /背包|裝備|装备|硬幣|硬币|經驗|经验|升級|升级|升級點|升级点|出口進度|出口进度|工作台|配方|空的|右手|左手|耐久|物品|商店|攤|摊|價格|价格|設定|设定|取消|選單|菜单|戰鬥|战斗|攻擊|攻击|檢定|检定|體力|体力|理智|掉落|戰利品|战利品|已選|已选|想要|等趿|等趺|選|按|玩家|^Level|^【|^〖|^〔|^（|^\(|[\[\]［］【】]|\\|^第\s*\d+\s*[層层]|^出[口囗]|^EXIT|^\(EXIT\)|難度|难度|檢定|检定|失敗|失败|成功|杂讯|雜訊|^\s*[0-9]+\s*\/\s*[0-9]+|[0-9]|杲|剮|殳|蔔|浈|躉|罴|彳|亍|灬|刂|丶|丿|丨|乀|重新连线|重新連線|冒险继续|冒險繼續|上次说到|上次說到|已经走过|已經走過|廢卉|废卉|一地萬|一地万|煦心|大烈仕|长杲|長杲|骨頭在抗議|骨头在抗议|一放更可怕|一地方|甩線|甩线|了萬|了万|樣的遊樂場|样的游乐场/;
        const clean = src.filter(l => {
          const t = String(l.t).trim();
          return l.y >= H * 0.24 && l.y < H * 0.58 && t.length >= 2 && !noise.test(t) && !(curFloor._prevIntroLines && curFloor._prevIntroLines.has(t.replace(/\s+/g, '')))
            && !/(力|敏|體|体|感|智|魅)\s*\d/.test(t)
            && !/一个人冒险|背包里多|自订|自訂|【戰鬥】|【战斗】|获得|獲得|抵抗|抗性/.test(t)
            && !/^(潦浪者|流浪者|浪者)/.test(t)
            && !/^[^：:]{1,8}[：:]\s*「/.test(t) && !/^[^：:「『]{1,6}[「『]/.test(t);
        });
        /* 2026-11-01 使用者給的樓層介紹規則（Level 0 大廳截圖）：
             1. 起點：「—— Level N「名稱」 ——」橫幅（橫幅本身不算正文）
             2. 終點：緊接的下一個大空行（不是出口進度、不是固定 y）
             3. 橫幅上方的一切都不算（那裡是上一段殘影，例如「【大廳】…」）
             4. 最後一句角色台詞不記錄（「天狐：「…」」這種，彩色字開頭）
             5. 貼近面板底邊、被裁掉的最後一行不要 */
        const ordered = clean.slice().sort((a, b) => a.y - b.y);
        const bannerLine = story.find(l => /^[—\-–＝=]{2,}\s*Level/i.test(String(l.t).trim()))
          || story.find(l => /——\s*Level/i.test(String(l.t)));
        const bannerY = bannerLine ? Number(bannerLine.y) : -1;

        /* 行距 → 大空行的門檻 */
        const _ys = ordered.map(l => l.y);
        const _d = [];
        for (let i = 1; i < _ys.length; i++) { const g = _ys[i] - _ys[i - 1]; if (g > 0 && g < 300) _d.push(g); }
        _d.sort((a, b) => a - b);
        const _pitch = _d.length ? _d[Math.floor(_d.length / 2)] : 0;
        const gapLimit = _pitch ? Math.max(_pitch * 2.2, 24) : 24;

        const isCharLine = t => /^[^：:「『\s]{1,10}[：:]\s*[「『]/.test(String(t).trim())
          || /^[^：:「『\s]{1,6}[「『]/.test(String(t).trim());

        const block = [];
        for (let i = 0; i < ordered.length; i++) {
          const l = ordered[i];
          if (bannerY >= 0 && l.y <= bannerY) continue;                 /* 3. 橫幅以上不算 */
          if (block.length && (l.y - ordered[i - 1].y) > gapLimit) break; /* 2. 大空行 = 結束 */
          block.push(l);
        }
        /* 4. 角色台詞：只有「結尾」連續的那幾行（名字 + 一句引號台詞）不要。
              多人模式可能同時有 2~3 句，所以用 while 一次剝到底；
              出現在介紹中間的固定引用句不受影響（不碰中間的行）。 */
        while (block.length && isCharLine(block[block.length - 1].t)) block.pop();
        if (block.length && (block[block.length - 1].y + (block[block.length - 1].h || 0)) > H * 0.56) block.pop(); /* 5. 貼底被裁 */
        /* 2026-11-01 使用者：樓層介紹裡若出現玩家角色名，一律改成「你」。
           介紹會固定引用角色說的話（那些保留），只有最後一句是角色自己的台詞（已在上方丟掉）。 */
        const PLAYER_IN_INTRO = /(?:天狐|狐天|狐夭|冴汰)/g;
        const txt = textOf(block).replace(PLAYER_IN_INTRO, '你').replace(/[，,]{2,}/g, '，').replace(/[。．]{2,}/g, '。').replace(/^[，。、]+/, '');
        const near = !curFloor.introFrame || (f.frame - curFloor.introFrame) <= 2;
        const env = (txt.match(/牆|墙|房間|房间|走廊|地毯|燈|灯|空氣|空气|天花板|地板|門|门|霧|雾|水|森林|城市|商場|商场|大廳|大厅/g) || []).length;
        const score = txt.length + env * 30;
        if (txt.length >= 20 && near && txt.length >= String(curFloor.intro || '').length && score > (curFloor.introScore || 0)) { curFloor.intro = txt; curFloor.introScore = score; if (!curFloor.introFrame) curFloor.introFrame = f.frame; }
      }
    }

    // 岔路：同一段選項面板的 OCR 抖動要合併，不要每一幀都新增
    if (ch) {
      const osim = choice ? sim(choice.options.map(o=>o.text).join(' '), ch.options.map(o=>o.text).join(' ')) : 0;
      const same = choice && (f.frame - (choice.source.frame || 0)) < 70 && !choice.selected
        && Math.abs((choice.options.length || 0) - ch.options.length) <= 1
        && (sim(choice.prompt, ch.prompt) > 0.2 || osim > 0.35);
      if (same && osim > 0.55) {
        // 同一面板的 OCR 變體：用較完整的新文字，不新增記錄
        choice.options = ch.options;
        choice.prompt = ch.prompt;
      }
      if (!same) {
        if (choice) closeChoice(choice, records);
        if (!/看完之后按|看完之後按|继续|繼續/.test(ch.prompt)) combat = null;
        choice = {
          type: 'choice', id: 'choice-' + records.length,
          floor: curFloor ? curFloor.floorId : '',
          prompt: ch.prompt, options: ch.options, selected: null, result: [],
          confidence: 0.65, source: { frame: f.frame, ts: f.ts }
        };
        records.push(choice);
        afterDecision = false;
      }
    }

    // 戰鬥：敵人名要連續 2 幀一致才建立，避免 OCR 抖動產生一堆垃圾實體
    if (enemy) {
      const cand = cleanEnemyName(enemy.name) || (enemy.panelNames || []).map(cleanEnemyName).find(Boolean) || '';
      let votes = 0;
      if (cand) { votes = (enemyVotes.get(cand) || 0) + 1; enemyVotes.set(cand, votes); }
      const sameEnemy = combat && cand && sim(combat.enemy.name, cand) > 0.6;
      const nearSame = combat && cand && sim(combat.enemy.name, cand) >= 0.3;
      if (!combat && cand && votes >= 2) {
        combat = {
          type: 'combat', id: 'combat-' + records.length,
          floor: curFloor ? curFloor.floorId : '',
          enemy: { name: cand, intro: '', skills: [], drops: [], resistances: [] },
          confidence: 0.55, source: { frame: f.frame, ts: f.ts }
        };
        records.push(combat);
        choice = null;
      } else if (combat && cand && !sameEnemy && !nearSame && votes >= 2) {
        combat = {
          type: 'combat', id: 'combat-' + records.length,
          floor: curFloor ? curFloor.floorId : '',
          enemy: { name: cand, intro: '', skills: [], drops: [], resistances: [] },
          confidence: 0.55, source: { frame: f.frame, ts: f.ts }
        };
        records.push(combat);
        choice = null;
      }
      if (combat) {
        const marker = enemy.marker;
        if (!combat.enemy.intro) {
          const idx = marker ? story.findIndex(l => l === marker) : -1;
          const after = idx >= 0 ? story.slice(idx + 1) : story;
          const introLines = after.filter(l => !/先攻|輪到你|第\s*\d+\s*回合|第\s*\d+\s*层|选择行动|選擇行動|获得|獲得|抵抗|抗性|弱點|弱点|钝击|揮砍|挥砍|狠刺|〖战斗|【战斗|点一下|等大家|防御\s*\d|防\s*\d/.test(l.t));
          const intro = textOf(introLines);
          if (intro && intro.length >= 12) { combat.enemy.intro = intro; combat.confidence = 0.78; }
        }
        for (const l of dropStory) {
          const tt = String(l.t).trim();
          const acts = tt.match(/揮砍|挥砍|狠刺|射擊|射击|撞擊|撞击|咬|抓|攻擊|攻击/g) || [];
          for (const a of acts) { const sk = a === '攻擊' ? '攻擊' : a; if (sk && !combat.enemy.skills.includes(sk)) combat.enemy.skills.push(sk); }
          if (isResistNote(tt)) {
            if (!combat.enemy.resistances.includes(tt)) combat.enemy.resistances.push(tt);
          }
          if (isDropLine(tt)) {
            const drop = cleanDrop(tt);
            if (drop && !combat.enemy.drops.includes(drop)) combat.enemy.drops.push(drop);
          }
        }
      }
    }

    // 戰鬥結束後敵人欄會消失；勝利／掉落／抗性文字仍要掛回上一個 combat
    if (combat) {
      for (const l of dropStory) {
        const t = String(l.t).trim();
        if (isResistNote(t)) {
          if (!combat.enemy.resistances.includes(t)) combat.enemy.resistances.push(t);
        }
        if (isDropLine(t)) {
          const drop = cleanDrop(t);
          if (drop && !combat.enemy.drops.includes(drop)) combat.enemy.drops.push(drop);
        }
      }
    }

    // 選擇後：遊戲會寫「→ 你決定：…」；之後的同層故事才是結果
    const decision = story.find(l => /你決定|你决定/.test(l.t));
    if (choice && decision && !choice.selected && f.frame > (choice.source.frame || 0)) {
      const txt = String(decision.t).replace(/^.*?[：:]\s*[「『]?/, '').replace(/[」』]?$/, '').trim();
      const hit = choice.options.find(o => norm(o.text) === norm(txt))
        || choice.options.find(o => norm(o.text).includes(norm(txt)) || norm(txt).includes(norm(o.text)));
      if (hit) {
        choice.selected = { id: hit.id, text: txt, matched: true };
        choice.confidence = 0.85;
        afterDecision = true;
      }
    }
    if (choice && choice.selected) {
      for (const l of story) {
        const t = String(l.t).trim();
        if (!t || t.length < 5 || ui(t)) continue;
        if (choice.result.includes(t)) continue;
        if (choice.prompt && norm(t) === norm(choice.prompt)) continue;
        if (choice.options.some(o => norm(o.text) === norm(t))) continue;
        if (/你決定|你决定|點一下|点一下/.test(t)) continue;
        choice.result.push(t);
      }
    }

    if (choice && afterDecision && decision) {
      const after = story.filter(l => l.y >= decision.y + 4 && norm(l.t) !== norm(decision.t));
      for (const l of after) {
        const t = String(l.t).trim();
        if (t && t.length > 5 && !ui(t) && !choice.result.includes(t)) choice.result.push(t);
      }
    }
  }
  /* 最後補 selected：有些選項面板的「你決定」不在 story 區，或輸入綁定沒接上。
     逐個 choice 掃描它之後、下一個 choice/floor 之前的所有幀，取最後一個匹配的決策行。 */
  for (let i = 0; i < records.length; i++) {
    const rec = records[i];
    if (rec.type !== 'choice' || rec.selected) continue;
    const start = rec.source?.frame || 0;
    const next = records.slice(i + 1).find(r => r.type === 'choice' || r.type === 'floor');
    const end = next?.source?.frame || (start + 120);
    let hit = null;
    for (const f of frames) {
      if (!f || f.frame <= start || f.frame > end) continue;
      for (const l of (f.lines || [])) {
        if (!/你決定|你决定/.test(l.t) && !/^[→\s]*狐天[：:]\s*[「『]/.test(l.t)) continue;
        const txt = String(l.t).replace(/^.*?[：:]\s*[「『]?/, '').replace(/[」』]?$/, '').trim();
        const opt = rec.options.find(o => norm(o.text) === norm(txt))
          || rec.options.find(o => norm(o.text).includes(norm(txt)) || norm(txt).includes(norm(o.text)));
        if (opt && (!hit || f.frame >= hit.frame)) hit = { frame: f.frame, opt, txt };
      }
    }
    if (hit) {
      rec.selected = { id: hit.opt.id, text: hit.txt, matched: true };
      rec.confidence = 0.85;
    }
  }

  closeChoice(choice, records);
  return records.filter(r => {
    if (r.type === 'floor') return !!r.name;
    if (r.type === 'choice') return !!r.prompt && r.options.length >= 2;
    if (r.type === 'combat') return !!r.enemy.name;
    return true;
  });
}

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node parse.mjs <sessionDir> [--out <dir>]'); process.exit(2); }
const sessionDir = path.resolve(args[0]);
const oi = args.indexOf('--out');
const outDir = oi >= 0 ? path.resolve(args[oi + 1]) : path.join(sessionDir, 'structured');
fs.mkdirSync(outDir, { recursive: true });
const records = parse(sessionDir);
fs.writeFileSync(path.join(outDir, 'records.jsonl'), records.map(r => JSON.stringify(r)).join('\n') + (records.length ? '\n' : ''), 'utf8');
const report = {
  session: path.basename(sessionDir), generatedAt: new Date().toISOString(),
  counts: {
    floors: records.filter(r => r.type === 'floor').length,
    choices: records.filter(r => r.type === 'choice').length,
    combats: records.filter(r => r.type === 'combat').length
  },
  lowConfidence: records.filter(r => (r.confidence || 0) < 0.7).length,
  records
};
fs.writeFileSync(path.join(outDir, 'report.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ outDir, counts: report.counts, lowConfidence: report.lowConfidence }, null, 2));





