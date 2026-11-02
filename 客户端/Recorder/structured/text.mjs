// text.mjs — 對外文字的「唯一出口」。
//
// 為什麼要集中：以前 doc 走 i18n + fixOcrText + 狐天過濾，事件只走一張字表，
// 結果是同一段文字在 doc 是繁中、在 results 還是簡中、在 intro 又漏了玩家名。
// 從現在起，任何會進 wiki 的字都必須經過 text()／paragraphs()／cleanResults()。
//
// 三條原則：
//   1. 只做「確定」的修正（簡→繁、已知形近字、玩家名、重複標點），不猜內容。
//   2. OCR 的視覺換行要合併成句子，不能變成 <br> 把詞劈開。
//   3. 看不懂的行就丟掉；寧可少一行，也不要留垃圾。

import { toHant } from './i18n.mjs';
import { fixOcrText } from './ocr-fix.mjs';

/* 玩家帳號名：只出現在面板／發言位置，正文一律換成「你」。
   `天狐` 是遊戲內角色，絕對不能跟著換。 */
const PLAYER_NAME = /狐夭|狐天/g;

/* 樓層名／檔名會帶進來的雜訊：OCR 常把「」讀成 [] 或 》 */
const BRACKET_CHARS = /[\[\]［］【】〔〕<>《》«»「」『』“”‘’]/g;

export function stripBrackets(s) {
  return String(s == null ? '' : s).replace(BRACKET_CHARS, '').trim();
}

/* 樓層名：所有 floor 目錄名 / key / 顯示名的唯一入口。
   `1-停車區]` 這種名字會讓 id|name 對不上，站點直接判定「鎖住」，所以在這裡就清掉。 */
export function floorName(s) {
  return stripBrackets(String(s == null ? '' : s).replace(/\s+/g, ' '))
    .replace(/^[\s·・\-—_,，、。:：;；]+|[\s·・\-—_,，、。:：;；]+$/g, '')
    .trim();
}

/* 檔案系統安全：中文不動，只清掉 Windows 不允許、以及會撞到 id|name 的字元。 */
export function safePath(s) {
  const cleaned = floorName(String(s == null ? '' : s).replace(/[\\/:*?"<>|]/g, '＿'))
    .replace(/[. ]+$/g, '')
    .slice(0, 80);
  return cleaned || 'unknown';
}

/* 重複／錯位標點：OCR 很愛把「，」讀成「·」，把句號讀兩次。 */
/* 2026-10-31：字表是「逐字」判斷，有些字（云／荡）為了避免過度轉換被放進
   KEEP_AS_IS，結果遊戲裡的「空荡荡」「云慢慢」就永遠不會轉。這裡用「詞」補回來。 */
/* 2026-11-01 使用者：樓層介紹裡玩家角色會說一句話（行首帶名字、彩色字）。
   名字要換成「你」。只在行首且後面接冒號／引號時替換，
   避免把正文中途的提及（例如 NPC 天狐）也換掉。 */
const PLAYER_LINE_START = /^(?:天狐|狐天|狐夭|冴汰)(?=\s*[：:「『])/gm;

const WORD_FIX = [
  [/空荡荡/g, '空蕩蕩'], [/荡荡/g, '蕩蕩'],
  [/云(?=慢慢|霧|雾|層|层|朵|彩|海|端)/g, '雲'],
  [/脑袋/g, '腦袋'],
];

/* 選項文字裡的重複字（耳耳耳耳朵）幾乎都是 OCR 疊字，收斂成一個；
   但「嗡嗡嗡」「咚咚咚」這種擬聲詞要保留 → 白名單。 */
const ONOMATOPOEIA = new Set([...'嗡轰轟咚滴哗嘩哈啊嘿砰沙哗啪嗒咔滋嘶唰咕噜嚕']);
export function collapseRepeats(s) {
  return String(s || '').replace(/([\u4e00-\u9fa5])\1{2,}/g, (m, ch) => (ONOMATOPOEIA.has(ch) ? m : ch));
}

const PUNCT_FIX = [
  [/[·•‧]/g, '，'],
  [/\u3000/g, ' '],
  /* OCR 把破折號「——」讀成「一一」；兩邊都是中文時幾乎不會是「一一列舉」。 */
  [/([\u4e00-\u9fff])一一(?=[\u4e00-\u9fff])(?![列說说举舉指確确檢检介細细道])/g, '$1——'],
  [/[，,]{2,}/g, '，'],
  [/[。．｡]{2,}/g, '。'],
  [/[！!]{2,}/g, '！'],
  [/[？?]{2,}/g, '？'],
  [/[、]{2,}/g, '、'],
  [/，([。！？；：])/g, '$1'],
  [/([。！？])，/g, '$1'],
];

/* 一般文字：簡→繁 → 已知形近字 → 玩家名 → 標點。 */
export function text(s) {
  let t = String(s == null ? '' : s);
  if (!t) return '';
  t = toHant(t);
  t = fixOcrText(t);
  t = t.replace(PLAYER_LINE_START, '你');
  t = t.replace(PLAYER_NAME, '你');
  for (const [re, to] of WORD_FIX) t = t.replace(re, to);
  for (const [re, to] of PUNCT_FIX) t = t.replace(re, to);
  return t.trim();
}

/* UI／狀態／社交流水：這些不是事件正文，任何位置出現都該丟。 */
const UI_LINE = [
  /^[（(]\s*\d+\s*\/\s*\d+\s*人?\s*(?:看完了?|已讀|已读)\s*[）)]?$/,
  /^[（(]?\s*\d+\s*\/\s*\d+\s*[）)]?$/,
  /按\s*Enter/i,
  /要怎麼做|要怎么做|按右鍵|按右键/,
  /^(?:你|她|他|牠|它)?\s*[:：]\s*(?:理智|體力|飢餓|生命|精神|經驗|經驗值|金幣|硬幣)\s*[-+]?\s*\d+/,
  /^.{0,4}[:：]\s*(?:理智|體力|飢餓|生命|精神|經驗|經驗值|金幣|硬幣)\s*[-+]?\s*\d+/,
  /^(?:理智|體力|飢餓|生命|精神|經驗|金幣)\s*[-+]\s*\d+$/,
  /^【[^】]{1,12}】$/,
  /* 隊伍頻道／聊天快捷：常被 OCR 混進正文 */
  /^(?:我走|不要|小心|我沒事|我没事|救命|哈哈|快跑|救我|等等|好啊|好耶|真的假的|太可怕|可惡|可恶|嗯+|喔+|哦+)[！!…。～~]*$/,
  /^(?:跟[我隨随]走|跟[隨随]多數|跟随多数|送[出送]|送出訊息)\s*[！!]?$/,
  /^[說说][點点].{0,6}$/,
  /^[說说][，,].{0,6}[麼么麽]/,
  /^说.{0,4}(?:什么|什).{0,2}[。！!]?$/,
  /^(?:繼續|继续|下一個|下一个|跳過|跳过|返回|離開|离开)$/,
  /* 道具欄的分類標籤，常被 OCR 黏在場景文字前面 */
  /^(背包|道具|物品|材料|裝備|装备|任務|任务|委託|委托|粉筆|粉笔|鑰匙|钥匙|彈藥|弹药)$/,
  /^[\s\d\p{P}\p{S}_]+$/u,
];

/* 「這一段事件到這裡結束」的界線。
   狀態列（天：理智-1）、看完計數、提問列一旦出現，後面的字就不屬於這則事件，
   以前是 continue 繼續收，於是把「下一個事件」的正文也吃進來。 */
const TERMINATOR = [
  /^.{0,4}[:：]\s*(?:理智|體力|飢餓|生命|精神|經驗|經驗值|金幣|硬幣)\s*[-+]?\s*\d+/,
  /^[（(]\s*\d+\s*\/\s*\d+\s*人?\s*(?:看完了?|已讀|已读)\s*[）)]?$/,
  /要怎麼做|要怎么做|按右鍵|按右键/,
];

export function isTerminator(line) {
  const s = String(line == null ? '' : line).trim();
  if (!s) return false;
  return TERMINATOR.some(rx => rx.test(s));
}

export function isUiLine(line) {
  const s = String(line == null ? '' : line).trim();
  if (!s) return true;
  return UI_LINE.some(rx => rx.test(s));
}

/* 看起來像 OCR 壞掉的字串：孤立拉丁字母、沒有中文也沒有數字、超長單行。 */
export function looksGarbled(s) {
  const t = String(s == null ? '' : s).trim();
  if (!t) return true;
  const cjk = (t.match(/[\u3400-\u9fff]/g) || []).length;
  const latin = (t.match(/[A-Za-z]/g) || []).length;
  const digit = (t.match(/[0-9]/g) || []).length;
  if (cjk === 0 && digit === 0) return true;
  /* 單一拉丁字母被空白夾住 → 「女 r 粉筆」這種殘片 */
  if (cjk > 0 && /(?:^|\s)[A-Za-z](?:\s|$)/.test(t)) return true;
  if (latin > 0 && cjk === 0 && !/^[A-Za-z0-9\s.\-_/]+$/.test(t)) return true;
  return false;
}

/* OCR 的視覺換行 → 真正的段落。
   規則：上一行沒有句末標點就接起來（中文換行不需要空格）；
   有句末標點才開新段落。這樣「比一般的日 / 灯吵得多」會合成一句，
   不會變成「比一般的日<br>燈吵得多」。 */
export function paragraphs(raw) {
  const lines = String(raw == null ? '' : raw).split(/\r?\n/).map(s => s.trim()).filter(Boolean);
  const out = [];
  for (const line of lines) {
    if (isUiLine(line) || looksGarbled(line)) continue;
    const prev = out.length ? out[out.length - 1] : '';
    /* 同一行被兩帧各讀一次（標點略有出入）：開頭 12 字一樣就當重複。
       優先留「有句末標點」的那一版 —— 句尾是 (車區) 這種殘尾的通常是壞讀。 */
    if (prev && line.slice(0, 12) === prev.slice(0, 12)) {
      const endPunct = s => /[。！？…]$/.test(s);
      if (endPunct(line) && !endPunct(prev)) out[out.length - 1] = line;
      else if (!endPunct(prev) && line.length > prev.length) out[out.length - 1] = line;
      continue;
    }
    if (prev && !/[。！？!?…]$/.test(prev)) out[out.length - 1] = prev + line;
    else out.push(line);
  }
  return out.map(p => text(p).replace(/\s+/g, ' ').trim()).filter(Boolean);
}

/* 事件結果：截斷的行不要。
   現在 floor 1 的 results 是五條殘句（「地上有一支手电筒，开关是」），
   留著只會讓事件頁看起來像壞掉；寧可空。 */
export function cleanResults(list) {
  const out = [];
  for (const raw of (Array.isArray(list) ? list : [])) {
    const t = text(raw).replace(/\s+/g, ' ').trim();
    if (!t || t.length < 6) continue;
    if (isUiLine(t) || looksGarbled(t)) continue;
    if (!/[。！？…」』）)]$/.test(t)) continue;
    if (out.includes(t)) continue;
    out.push(t);
  }
  return out.slice(0, 6);
}
