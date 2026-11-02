import fs from 'node:fs';
import path from 'node:path';
import { report as reportProgress } from './progress.mjs';

/*
 * input-bind.mjs — 把 input.jsonl 的鍵盤輸入綁到路口／事件選項
 * 只做可驗證的綁定：按鍵 1/2/3 + 最近的畫面選項。
 * 不確定時保留 raw，不猜。
 */

const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node input-bind.mjs <sessionDir>'); process.exit(2); }
const session = path.resolve(args[0]);
const jsonl = f => fs.existsSync(f) ? fs.readFileSync(f, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).filter(Boolean).map(s => { try { return JSON.parse(s); } catch { return null; } }).filter(Boolean) : [];
const frames = jsonl(path.join(session, 'structured.jsonl'));
const inputs = jsonl(path.join(session, 'input.jsonl'));
const records = jsonl(path.join(session, 'structured', 'events.jsonl'));

const frameTime = new Map();
for (const f of frames) {
  const t = Date.parse(String(f.ts).replace(' ', 'T') + '+08:00');
  if (!Number.isNaN(t)) frameTime.set(f.frame, t);
}
function nearestRecord(ts) {
  let best = null, bestDelta = Infinity;
  for (const r of records) {
    if (!['junction', 'event'].includes(r.type)) continue;
    let min = Infinity, max = -Infinity;
    for (const fr of r.frames || []) {
      const ft = frameTime.get(fr);
      if (ft == null) continue;
      if (ft < min) min = ft;
      if (ft > max) max = ft;
    }
    if (!Number.isFinite(min)) continue;
    // 按鍵必須落在這個畫面存在的時間範圍內；不能拿「未來才出現」的選項回填。
    if (ts < min - 800 || ts > max + 800) continue;
    const d = Math.min(Math.abs(ts - min), Math.abs(ts - max));
    if (d < bestDelta) { bestDelta = d; best = r; }
  }
  return best;
}
function nearestFrame(ts) {
  let best = null, bestDelta = Infinity;
  for (const [fr, ft] of frameTime) {
    const d = Math.abs(ft - ts);
    if (d < bestDelta) { bestDelta = d; best = fr; }
  }
  return { frame: best, delta: bestDelta };
}
function frameAt(ts) {
  const { frame, delta } = nearestFrame(ts);
  if (delta > 2500) return null;
  return frames.find(x => x.frame === frame) || null;
}
function hasActionPrompt(ts) {
  const f = frameAt(ts);
  if (!f) return false;
  return (f.lines || []).some(l => /要怎么做|要怎麼做|选一个去做|選一個去做|轮到你了|輪到你了|选择行动|選擇行動/.test(l.t));
}
function isContinueFrame(f) {
  return (f.lines || []).some(l => /看完之后按|看完之後按|按「继续」|按「繼續」|继续（|\/ ?人看完了/.test(l.t));
}
function clickOption(ts, y) {
  const { frame, delta } = nearestFrame(ts);
  if (delta > 2500) return null;
  const f = frames.find(x => x.frame === frame);
  if (!f) return null;
  if (isContinueFrame(f) && y > 580 && y < 760) return { continue: true, frame };
  const opts = (f.lines || []).filter(l => l.x < 160 && l.y > 600 && l.y < 900).sort((a, b) => a.y - b.y);
  let best = null, bestDy = Infinity;
  for (let i = 0; i < opts.length; i++) {
    const d = Math.abs(opts[i].y - y);
    if (d < bestDy) { bestDy = d; best = i; }
  }
  // 選項上方可能有標題，滑鼠點在選項列附近才接受
  if (best == null || bestDy > 80) return null;
  return { frame, index: best, text: opts[best].t };
}

const bindings = [];
const usedSingleChoice = new Set();
let inputDone = 0;
for (const x of inputs) {
  reportProgress(session, '綁定輸入', ++inputDone, inputs.length, '第 ' + inputDone + ' 筆輸入');
  const e = x.event || x;
  const ts = Number(e.t) || Date.parse(String(x.hostTs).replace(' ', 'T') + '+08:00');
  if (!Number.isFinite(ts)) continue;
  if (e.type === 'keydown' && /^[1-8]$/.test(String(e.key))) {
    const r = nearestRecord(ts);
    if (!r) { bindings.push({ ts, type: 'key', key: e.key, bound: false, reason: 'no_nearby_record' }); continue; }
    if (r.type === 'event' && !hasActionPrompt(ts) && !r.prompt) {
      bindings.push({ ts, type: 'key', key: e.key, record: r.id || r.name, bound: false, reason: 'no_action_prompt' });
      continue;
    }
    const singleKey = r.id || r.name;
    if (r.type !== 'event' || r.kind !== 'combat') {
      if (usedSingleChoice.has(singleKey)) {
        bindings.push({ ts, type: 'key', key: e.key, record: singleKey, bound: false, reason: 'already_selected' });
        continue;
      }
      usedSingleChoice.add(singleKey);
    }
    const idx = Number(e.key) - 1;
    const list = r.type === 'junction' ? (r.paths || []) : (r.actions || []);
    if (!list[idx]) {
      if (r.type === 'junction' && r.selectedRaw) {
        bindings.push({ ts, type: 'key', key: e.key, recordType: r.type, record: r.id || r.name, floor: r.floor, index: idx, text: r.selectedRaw, bound: true, rawFallback: true });
      } else {
        bindings.push({ ts, type: 'key', key: e.key, record: r.id || r.name, bound: false, reason: 'no_option_index' });
      }
      continue;
    }
    bindings.push({ ts, type: 'key', key: e.key, recordType: r.type, record: r.id || r.name, floor: r.floor, index: idx, text: list[idx].text, bound: true });
  } else if ((e.type === 'mousedown' || e.type === 'touchstart') && e.y > 580) {
    const hit = clickOption(ts, e.y);
    if (!hit) continue;
    if (hit.continue) { bindings.push({ ts, type: 'click', x: e.x, y: e.y, frame: hit.frame, bound: false, reason: 'continue_click' }); continue; }
    const r = nearestRecord(ts);
    if (!r) { bindings.push({ ts, type: 'click', x: e.x, y: e.y, frame: hit.frame, bound: false, reason: 'no_nearby_record' }); continue; }
    const list = r.type === 'junction' ? (r.paths || []) : (r.actions || []);
    const exact = list.find(o => o.text === hit.text);
    if (r.type === 'event' && !hasActionPrompt(ts) && !r.prompt && !exact) { bindings.push({ ts, type: 'click', x: e.x, y: e.y, frame: hit.frame, record: r.id || r.name, bound: false, reason: 'no_action_prompt' }); continue; }
    const singleKey = r.id || r.name;
    if (r.type !== 'event' || r.kind !== 'combat') {
      if (usedSingleChoice.has(singleKey)) { bindings.push({ ts, type: 'click', x: e.x, y: e.y, frame: hit.frame, record: singleKey, bound: false, reason: 'already_selected' }); continue; }
      usedSingleChoice.add(singleKey);
    }
    const target = exact || list[hit.index];
    if (!target) { bindings.push({ ts, type: 'click', x: e.x, y: e.y, frame: hit.frame, record: r.id || r.name, bound: false, reason: 'no_matching_option' }); continue; }
    bindings.push({ ts, type: 'click', x: e.x, y: e.y, frame: hit.frame, recordType: r.type, record: r.id || r.name, floor: r.floor, text: target.text, bound: true });
  }
}

const report = {
  session: path.basename(session),
  generatedAt: new Date().toISOString(),
  inputs: inputs.length,
  bound: bindings.filter(x => x.bound).length,
  unbound: bindings.filter(x => !x.bound).length,
  bindings
};
fs.writeFileSync(path.join(session, 'structured', 'input-bind.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');

// 把可驗證的綁定寫回事件副本；不覆蓋原始 events.jsonl
const bound = records.map(r => {
  if (!['junction', 'event'].includes(r.type)) return r;
  const hits = bindings.filter(b => b.bound && b.record === (r.id || r.name) && b.recordType === r.type);
  if (!hits.length) return r;
  const out = JSON.parse(JSON.stringify(r));
  /* parser 已經用畫面上的「你決定」回填 selected 時，以它為準；
     輸入綁定（尤其座標點擊）只補沒有 selected 的紀錄，避免覆蓋正確答案。 */
  if (out.selected) { out.inputBound = true; return out; }
  const last = hits[hits.length - 1];
  if (out.type === 'junction') {
    out.selected = { text: last.text, matched: true };
    for (const p of out.paths || []) if (p.text === last.text) p.selected = true;
  } else {
    out.selected = { text: last.text, matched: true };
    for (const a of out.actions || []) if (a.text === last.text) a.selected = true;
    // 把 OCR 混進 intro 的行動／結果行切掉；原始 events.jsonl 不動。
    const acts = (out.actions || []).map(a => String(a.text || '').replace(/\s+/g, ''));
    const intro = String(out.intro || '').split(/\r?\n/).filter(line => {
      const n = String(line).replace(/\s+/g, '');
      return n && !acts.includes(n) && !/妻怎么做|怎么做|按右键|可以取消|选一个去做/.test(n);
    });
    out.intro = intro.join('\n');
  }
  out.inputBound = true;
  return out;
});
fs.writeFileSync(path.join(session, 'structured', 'events.bound.jsonl'), bound.map(x => JSON.stringify(x)).join('\n') + (bound.length ? '\n' : ''), 'utf8');
console.log(JSON.stringify({ inputs: inputs.length, bound: report.bound, unbound: report.unbound }, null, 2));
