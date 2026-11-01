/* 連環事件（steps）—— 2026-11-01
   使用者：像「請勿按」那種，一個事件裡會連續出現好幾次「→ 你決定：「…」」，
   每決定一次就產生一段結果，然後再給下一組選項。
   目前模型只記一次選擇，第二段以後全丟。

   這裡先把「切步驟」這件事獨立成一個模組，任何吃到事件文字的流程都能用：
     splitSteps(lines)  →  [{ lines: [...] }, { lines: [...] }]
   規則很簡單：一行是「→ 你決定：…」就是新階段的起點。
*/

const DECIDE = /^[→>\-–—\s]*(?:你)?[決决]\s*[定定]\s*[：:]/;

export function splitSteps(textOrLines) {
  const lines = Array.isArray(textOrLines)
    ? textOrLines.map(String)
    : String(textOrLines || '').split(/\r?\n/);
  const out = [];
  let cur = null;
  for (const raw of lines) {
    const t = String(raw).trim();
    if (!t) continue;
    if (DECIDE.test(t)) {
      cur = { decide: t, lines: [] };
      out.push(cur);
      continue;
    }
    if (!cur) { cur = { decide: null, lines: [] }; out.push(cur); }
    cur.lines.push(t);
  }
  return out;
}

/** 把一個事件（舊格式：單一 selected + results）轉成 steps 陣列 */
export function eventToSteps(ev) {
  const steps = [];
  const intro = String(ev.intro || '').trim();
  const results = [].concat(ev.results || []);
  const selected = ev.selected || (ev.actions && ev.actions.find(a => a.selected)?.text) || null;

  /* 第二段以後的選擇會藏在 results 裡（「→ 你決定：…」） */
  const joined = results.join('\n');
  const parts = splitSteps(joined);

  steps.push({
    intro,
    actions: (ev.actions || []).map(a => a.text),
    selected,
    results: parts.length ? parts[0].lines : results
  });
  for (let i = 1; i < parts.length; i++) {
    steps.push({
      intro: '',
      actions: [],
      selected: String(parts[i].decide || '').replace(DECIDE, '').replace(/[「」]/g, ''),
      results: parts[i].lines
    });
  }
  return steps;
}

export function hasMultipleSteps(ev) {
  return /→\s*你?[決决]\s*[定定]/.test([].concat(ev.results || []).join('\n'));
}
