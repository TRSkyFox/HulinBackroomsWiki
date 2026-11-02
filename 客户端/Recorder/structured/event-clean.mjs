// event-clean.mjs — 把 OCR 事件清洗成可自動匯入的結構。
// 不靠「質量分」：用固定 UI 語意截斷（難度／選項／結果／進度），再去重。
//
// 2026-10-29 改版：文字清洗統一走 text.mjs。
// 以前 intro 走 cleanIntro、actions 走 cleanActions、results 完全不洗，
// 於是同一則事件三種純度（正文繁中、選項半繁半簡、結果五行殘句）。
import { text, isUiLine, isTerminator, looksGarbled, cleanResults } from './text.mjs';

const norm = s => String(s || '').replace(/\s+/g, '').replace(/[「」『』“”]/g, '').trim();
function bigrams(s) { const n = norm(s); const out = new Set(); for (let i = 0; i < n.length - 1; i++) out.add(n.slice(i, i + 2)); return out; }
export function sim(a, b) { const A = bigrams(a), B = bigrams(b); if (!A.size || !B.size) return 0; let n = 0; for (const x of A) if (B.has(x)) n++; return n / Math.max(A.size, B.size); }

const STOP = [
  /難度\s*[0-9lI]/i,
  /* 「感知難度 12」被 OCR 讀成「知難度巧」時，後面沒有數字也要斷。 */
  /難度/,
  /^(得到|失去|體力|体力|理智|飢餓|饥饿|經驗|经验)\s*[:：]/,
  /出口進度|出口进度|已經走過|已经走过|記憶碎片|记忆碎片|走出了這一層|走出了这一层/,
  /^(走|往|推|拉|選|选|爬|轉|转|拿|檢查|检查|憋|閉|闭|倒退|換|换|找|站|坐|等|離|离|回|進|进|出).{1,40}$/,
  /^(跟隨多數|跟随多数|強制倒數|强制倒数)/,
];

/* 事件正文：把視覺換行接成句子，丟掉 UI／狀態／社交流水。
   段落邊界用句末標點判斷，不沿用 OCR 的換行。 */
export function cleanIntro(raw, actions = []) {
  const lines = String(raw || '').split(/\r?\n/).map(s => s.trim()).filter(Boolean);
  const acts = actions.map(a => norm(a.text || a)).filter(Boolean);
  const out = [];
  for (const line0 of lines) {
    const line = text(line0);
    if (!line) continue;
    /* 界線先判：狀態列／看完計數出現 = 這則事件結束，後面的字是下一則的。 */
    if (isTerminator(line)) break;
    if (isUiLine(line) || looksGarbled(line)) continue;
    if (STOP.some(rx => rx.test(line))) break;
    if (acts.some(a => a && sim(line, a) > 0.68)) break;
    const prev = out.length ? out[out.length - 1] : '';
    /* 同一句被連續兩帧各讀一次（標點略有出入），用去標點的包含關係判斷。 */
    const flat = s => String(s).replace(/[\s，。！？、；：·・,.;:!?]/g, '');
    if (prev && flat(prev).includes(flat(line))) continue;
    if (prev && sim(prev, line) > 0.82) continue;
    if (prev && !/[。！？…]$/.test(prev)) out[out.length - 1] = prev + line;
    else out.push(line);
  }
  return text(out.join(''))
    .replace(/[，,]{2,}/g, '，')
    .replace(/[。．]{2,}/g, '。')
    .trim();
}

/* 選項：必須像「玩家可以按的按鈕」。
   OCR 常把同一顆按鈕讀成兩三種版本（「閉上眼睛 數到乾」/「上眼睛，數到十」），
   所以先去掉殘片，再把相似的合併、留下字比較完整的那一個。 */
export function cleanActions(actions = [], selected = '') {
  /* 原始候選可以到 8 條：OCR 會把同一顆按鈕讀成 3~4 種變體，
     先讓它們進來、去重之後才要求 2–4 條。以前在這裡一刀砍掉 >4，
     於是「家具留下的痕跡」（6 條變體 = 3 個真選項）整則事件被丟掉。 */
  if (!Array.isArray(actions) || actions.length > 8) return null;
  const sel = String(selected || '').trim();
  const junk = /[冂綫迄寰蟋堡刂丶丿丨乀灬]/;
  const kept = [];
  for (const a of actions) {
    let t = text(String(a && a.text != null ? a.text : a || '').trim());
    t = t.replace(/^上假人/, '換上假人')
      .replace(/^[★☆◆◇●○·・.…\s]+/, '')
      /* 選項前面偶爾會黏一個「女」；那是 UI 圖示被 OCR 讀出來的，不是字。 */
      .replace(/^女(?=[用力拿站走跑推拉選选伸把壓压])/, '')
      /* 頭尾的方括號是 OCR 吃掉引號留下的（「走向那扇黑色的金屬門]」）。 */
      .replace(/^[\[\]［］【】〔〕]+|[\[\]［］【】〔〕]+$/g, '')
      .trim();
    if (!t) continue;
    if (/會用掉|会用掉/.test(t)) continue;
    if (isUiLine(t) || looksGarbled(t)) continue;
    if (/難度|檢定|掷|擲|骰/.test(t)) continue;
    /* 「★「工地安全帽」」這種是道具標籤，不是選項。 */
    if (/^[「『][^」』]{1,10}[」』]$/.test(t)) continue;
    if (/^(力量|敏捷|體質|感知|智力|魅力|力|敏|體|感|智|魅)\s*\d+\s*\+?$/.test(t)) continue;
    if (t.length < 2 || t.length > 120) continue;   /* 只擋 OCR 串成一整段的殘片，不再用 34 字砍選項 */
    if (junk.test(t)) continue;
    if (/^(繼續|继续|跟隨多數|跟随多数|送出|選一個|选一个)/.test(t)) continue;
    /* 同一顆按鈕被讀成長短兩版時，短的那版通常是 OCR 漏字
       （「仔閱讀」是「仔細閱讀」掉了一個「細」）。用「子序列」判斷，不要只看頭尾。 */
    const isSubseq = (s, l) => { let i = 0; for (const ch of l) if (ch === s[i]) i++; return s.length >= 2 && i === s.length; };
    const short = kept.find(k => {
      const a = norm(k.text), b = norm(t);
      if (a === b) return false;
      return a.length < b.length ? isSubseq(a, b) : isSubseq(b, a);
    });
    if (short) {
      if (norm(t).length > norm(short.text).length) short.text = t;
      continue;
    }
    const dup = kept.find(k => sim(k.text, t) > 0.62);
    if (dup) {
      /* 留下比較完整的那一個：先比長度，長度接近時選「不是殘片」的。 */
      if (t.length > dup.text.length) dup.text = t;
      continue;
    }
    kept.push({ ...a, text: t });
  }
  if (kept.length < 1 || kept.length > 8) return null;   /* 不再要求 2~4：只有一個選項也可能是真的 */
  if (!sel) {
    kept.forEach(k => k.selected = false);
    return { actions: kept, selected: null };
  }
  let selIdx = kept.findIndex(k => norm(k.text) === norm(text(sel)));
  if (selIdx < 0) {
    let best = 0, idx = -1;
    kept.forEach((k, i) => { const s = sim(k.text, sel); if (s > best) { best = s; idx = i; } });
    /* 0.7 以下就當「這個 selected 不是這組選項」——寧可整則事件不寫。 */
    if (best < 0.7) return null;
    selIdx = idx;
  }
  kept.forEach((k, i) => k.selected = i === selIdx);
  return { actions: kept, selected: kept[selIdx].text };
}

export function cleanEvent(e) {
  const kind = e.kind || 'event';
  if (kind === 'ending') {
    const intro = cleanIntro(e.intro || '');
    if (!intro) return null;   /* 不設上限：結局描述再長也是內容 */
    return { ...e, kind, intro, actions: [], selected: null };
  }
  const ca = cleanActions(e.actions || [], e.selected || '');
  if (!ca) return null;
  const intro = cleanIntro(e.intro || '', ca.actions);
  if (!intro) return null;   /* 不設字數上限：長描述照收（由段落切分處理可讀性） */
  /* 結果也要洗：截斷的殘句不如不寫。 */
  const results = cleanResults(e.results);
  return { ...e, intro, results, actions: ca.actions, selected: ca.selected };
}
