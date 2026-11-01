/* 隨機結果的累積與機率統計（2026-10-31）
   ------------------------------------------------------------------
   使用者：「隨機結果肯定需要累積，而且也要統計結果算概率。」

   同一個事件、同一個選項，在不同場次可能得到不同結果 → 這是「隨機」。
   我們只看到每次的一個樣本，所以要：
     1. 把每次觀察到的結果當成一個「樣本」存進 variants
     2. 內容相同的樣本合併、次數 +1
     3. 機率 = 該樣本次數 / 總樣本數（樣本 < 2 時不顯示百分比，避免誤導）

   資料形狀：
     "variants": [
       { "lines": ["…"], "effects": [...], "count": 3 },
       { "lines": ["…"], "effects": [...], "count": 1 }
     ],
     "variantTotal": 4,
     "outcomeStats": { "success": 2, "failure": 3 }    // 檢定分支的實際分布
*/

/* 簽名：去掉標點/空白後的字串。內容一致就算同一個樣本。 */
export function sig(lines) {
  const t = (Array.isArray(lines) ? lines : [lines])
    .map(s => String(s || ''))
    .join('')
    .replace(/\s+/g, '')
    .replace(/[，。、；：！？「」『』（）()《》〈〉·…—–_・"'\-]/g, '');
  return t;
}

function recount(ev) {
  ev.variantTotal = (ev.variants || []).reduce((n, v) => n + (v.count || 1), 0);
}

/** 把一次觀察到的結果加進 variants（同內容合併並累加次數） */
export function addSample(ev, lines, effects) {
  const arr = (lines || []).filter(x => String(x || '').trim());
  if (!arr.length) return false;
  const s = sig(arr);
  if (!s) return false;
  ev.variants = ev.variants || [];
  const hit = ev.variants.find(v => sig(v.lines) === s);
  if (hit) {
    hit.count = (hit.count || 1) + 1;
    if ((!hit.effects || !hit.effects.length) && effects && effects.length) hit.effects = effects;
    /* 保留較完整的那份文字（同一結果 OCR 長短不一） */
    if (arr.join('').length > (hit.lines || []).join('').length) hit.lines = arr.slice();
  } else {
    ev.variants.push({ lines: arr.slice(), effects: effects || [], count: 1 });
  }
  recount(ev);
  return true;
}

/** 檢定分支：記錄這次走的是成功還是失敗（用來算分布） */
export function addOutcomeSample(ev, branch) {
  if (branch !== 'success' && branch !== 'failure') return false;
  ev.outcomeStats = ev.outcomeStats || {};
  ev.outcomeStats[branch] = (ev.outcomeStats[branch] || 0) + 1;
  return true;
}

/** 這一則事件「其中一次觀察」的結果文字（優先用分支內容，其次 results） */
export function observedLines(ev) {
  if (ev.outcomes) {
    for (const k of ['failure', 'success']) {
      const b = ev.outcomes[k];
      if (b && b.lines && b.lines.length) return { lines: b.lines, effects: b.effects || [], branch: k };
    }
  }
  if (ev.results && ev.results.length) return { lines: ev.results, effects: ev.effects || [], branch: null };
  return null;
}
