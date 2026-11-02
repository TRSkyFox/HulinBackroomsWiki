// event-extract.mjs — 以「畫面事件卡」為單位重建事件，避免跨事件污染。
//
// 規則：
//  - 只在正常遊玩畫面找 `【事件名】` 標題；標題以上的文字是上一個事件，全部忽略。
//  - 標題到第一個「投票／要怎麼做」或「→角色：選擇」之間才是 intro。
//  - 選項只從 prompt 下方的 opts 區取；箭頭是已選，不是新選項。
//  - 選擇後到下一個出口進度／跳過提示／下一個標題之間才是 results。
//  - 同一事件連續多幀只在同一個 current 上累積，不重新建立。
//  - 兩個玩家先後選：每支箭頭是一個 step，各自收自己的結果。

const W = 1606;
const H = 929;
const HEADER = /^【([^】]{1,40})】\s*$/;
const PROMPT = /全队投票|全隊投票|要怎么做|要怎麼做|要走哪条路|要走哪條路|要由谁|要由誰|选择行动|選擇行動|誰來|谁来|投票選一個人|投票选一个人/;
const DECISION = /^[→>]\s*[^：:\s]{1,12}[：:]\s*[「『]/;
const EXIT = /出口进度|出口進度|点一下文字|點一下文字|看完之后按|看完之後按|可以跳过|可以跳過|走出了這一層|走出了这一层/;
const RESULT_JUNK = /^→|^Level\s*[\d.]+|^第\s*\d+|只有.*能決定|先挑第一樣|大家休息的時候|下面有新的內容|卷到最下面|跟随多数|跟隨多數|点一下文字|點一下文字|按空白键|按空白鍵|^\s*[（(].*[）)]\s*$/;
const NOISE = /跟随多数|跟隨多數|按右键|按右鍵|取消|投票请|投票請|人看完|人準備|人准备|选择行动|選擇行動|难度|難度|检定|檢定|攻击|攻擊|技能|防御|防禦|闪躲|閃躲|逃跑|背包|装备|裝備|物品|商店|价格|價格|硬币|硬幣|经验|經驗|升级|升級|状态|狀態|理智\s*[+-]|体力\s*[+-]|體力\s*[+-]|口渴\s*[+-]|饥饿\s*[+-]|飢餓\s*[+-]/;

const norm = s => String(s || '').replace(/\s+/g, '').replace(/[「」『』“”]/g, '').trim();
const keyOf = s => norm(s).replace(/[，。！？、丶；：…·.,!?;:]/g, '');
const clean = s => String(s || '').replace(/\s+/g, ' ').trim();

function floorOf(frame, current) {
  const title = (frame.lines || []).find(l => l.r === 'title' && /Level\s*[\d.]+/i.test(l.t));
  if (!title) return current;
  const m = String(title.t).match(/Level\s*([\d.]+)\s*[「"']?([^」"']*)/i);
  if (!m) return current;
  return { id: m[1], name: clean(m[2]).replace(/[」"'】]/g, '').trim() };
}

function shouldKeep(t) {
  const s = clean(t);
  if (s.length < 2) return false;
  if (NOISE.test(s)) return false;
  if (/^[（(]?[0-9]+\s*\/\s*[0-9]+/.test(s)) return false;
  return true;
}

function optionOf(l) {
  let t = clean(l.t).replace(/^[-–—•·]\s*/, '').replace(/^[★☆]\s*/, '').trim();
  if (/你自己的选择|你自己的選擇|你选了就算数|你選了就算數/.test(t)) return '';
  if (!shouldKeep(t)) return '';
  if (/^(继续|繼續|选一个去做|選一個去做|要怎么做|要怎麼做|跟随多数|跟隨多數|天狐|狐天|狐夭|你|你們|你们)$/i.test(t)) return '';
  if (/难度|難度|检定|檢定|全员|全員/i.test(t)) return '';
  if (/^「.+」.*(?:会用掉|會用掉|用掉)/.test(t)) return '';
  return t;
}

function finalize(cur, out) {
  if (!cur) return;
  // intro 以最完整的一份為準
  cur.intro = clean(cur.intro).replace(/(?:天狐|狐天|狐夭|冴汰)/g, '你');
  for (const a of cur.actions || []) a.text = String(a.text || '').replace(/(?:天狐|狐天|狐夭|冴汰)/g, '你');
  if (cur.steps && cur.steps.length) {
    for (const st of cur.steps) {
      const seen = new Set();
      const filtered = st.results.map(clean).filter(Boolean).map(x => {
        // 檢定結果只留「成功／失敗」後面的敘述；骰值、難度、屬性都不是事件結果正文。
        const pos = Math.max(x.lastIndexOf('成功'), x.lastIndexOf('失败'), x.lastIndexOf('失敗'));
        if (pos >= 0) {
          let after = x.slice(pos + 2).trim();
          after = after.replace(/^(?:[^\s，。；]{1,8}[+\-－]\d+[、，]?)+/, '').trim();
          return after.length >= 2 ? after : '';
        }
        return x;
      }).filter(Boolean).filter(x => {
        const k = keyOf(x).replace(/[，。！？、丶；：…·.,!?;:]/g, '');
        if (seen.has(k)) return false;
        seen.add(k); return true;
      });
      const joined = [];
      for (const x of filtered) {
        if (!joined.length) joined.push(x);
        else if (!/[。！？…」』）)]$/.test(joined[joined.length - 1])) joined[joined.length - 1] += x;
        else joined.push(x);
      }
      st.results = joined.map(x => String(x)
        .replace(/(?:天狐|狐天|狐夭|冴汰)/g, '你')
        .replace(/[，、；]?[^，。；]{0,10}(?:理智|體力|体力|口渴|飢餓|饥饿|經驗|经验|HP|MP)\s*[-+]?\d+/g, '')
        .trim()).filter(Boolean).filter(x => {
        const t = clean(x);
        if (!t) return false;
        if (/^Level\s*[\d.]+|^第\s*\d+|^→|看完之后|看完之後|点一下文字|點一下文字|按空白键|按空白鍵/.test(t)) return false;
        if (/只有.*能(?:決定|决定)|先挑第一[樣样]|大家休息的(?:時候|时候)|下面有新的(?:內容|内容)|卷到最下面|跟随多数|跟隨多數|天狐把營火包|天狐把营火包/.test(t)) return false;
        if (/^(?:所有人|全隊|全队|天狐|狐天|你|你們|你们)\s*[：:]?\s*(?:理智|體力|体力|口渴|飢餓|饥饿|經驗|经验|HP|MP)\s*[-+]?\d+/.test(t)) return false;
        if (/^[（(][^）)]{0,12}[）)]$/.test(t)) return false;
        if (cur._introKeys && cur._introKeys.has(keyOf(t))) return false;
        return true;
      });
    }
    const first = cur.steps[0];
    cur.selected = first.selected ? { text: first.selected, matched: true } : null;
    cur.results = [...new Set(cur.steps.flatMap(s => s.results).map(clean).filter(Boolean))];
  }
  if (!cur.selected) { out.push(cur); return; }
  cur.confidence = (cur.actions.length >= 2 && cur.intro.length >= 8) ? 0.9 : 0.7;
  out.push(cur);
}

export function extractEvents(frames) {
  const out = [];
  let cur = null;
  let floor = { id: '', name: '' };
  let lastSignature = '';
  let lastFrame = -9999;
  const sigOf = e => norm(e.name) + '|' + norm(e.selected?.text);

  for (const f of frames) {
    if (!f || !Array.isArray(f.lines)) continue;
    if (f.layout && f.layout !== 'normal' && !(cur && cur.selected && f.layout === 'panel')) {
      // 面板／背包／結局不是事件開場；但事件已選、結果只出現在 panel 時仍要收。
      continue;
    }
    const nextFloor = floorOf(f, floor);
    if ((nextFloor.id !== floor.id || nextFloor.name !== floor.name) && cur) {
      finalize(cur, out); cur = null;
    }
    floor = nextFloor;
    const lines = f.lines.slice().sort((a, b) => (a.y - b.y) || (a.x - b.x));
    const headerLine = lines.find(l => l.r === 'main' && HEADER.test(clean(l.t)));
    const closeLine = lines.find(l => l.r === 'main' && /出口进度|出口進度/.test(l.t));

    if (headerLine) {
      const name = clean(headerLine.t.match(HEADER)[1]);
      if (!cur && lastSignature.startsWith(norm(name) + '|') && f.frame - lastFrame <= 60) {
        // 同一段已收束畫面的重複幀
        cur = null;
        continue;
      }
      if (cur && norm(cur.name) === norm(name)) {
        cur.frames.push(f.frame);
        cur.headerY = Math.min(cur.headerY, headerLine.y);
      } else {
        if (cur) { lastSignature = sigOf(cur); lastFrame = f.frame; }
        finalize(cur, out);
        const provisional = { name, selected: null, results: [] };
        const sig = sigOf(provisional);
        if (sig === lastSignature && f.frame - lastFrame <= 45) {
          // 同一段畫面在相鄰幀重複出現；不要再生一個事件。
          cur = null;
        } else {
          cur = {
            type: 'event', kind: 'event', floor: floor.id, name,
            intro: '', actions: [], selected: null, steps: [],
            results: [], effects: [], frames: [f.frame], startFrame: f.frame,
            header: clean(headerLine.t), headerY: headerLine.y,
            prompt: '', promptY: -1, _lastY: headerLine.y, _introKeys: new Set(),
            confidence: 0.7, source: { frame: f.frame, ts: f.ts }
          };
        }
      }
    }
    if (!cur) continue;

    // prompt 與選項：只取 prompt 下方的 opts，不把箭頭當選項。
    const closeY = closeLine ? closeLine.y : Number.MAX_VALUE;
    const promptLine = lines.find(l => l.r === 'main' && PROMPT.test(clean(l.t)) && l.y > cur.headerY && l.y < closeY && !cur.selected);
    if (promptLine) {
      cur.prompt = clean(promptLine.t);
      cur.promptY = promptLine.y;
      for (const l of lines) {
        if (l.y <= promptLine.y) continue;
        if (l.r !== 'opts' && !(l.r === 'main' && l.x < W * 0.16)) continue;
        const t = optionOf(l);
        if (t && !cur.actions.some(a => norm(a.text) === norm(t))) cur.actions.push({ text: t, selected: false });
      }
    }

    // 先收 intro：標題以下、prompt/decision 以上。
    const firstDecisionY = Math.min(
      ...lines.filter(l => l.r === 'main' && l.y > cur.headerY && DECISION.test(clean(l.t))).map(l => l.y),
      Number.MAX_VALUE
    );
    const introEnd = Math.min(cur.promptY > 0 ? cur.promptY : Number.MAX_VALUE, firstDecisionY, closeY);
    if (!cur.selected) {
      for (const l of lines) {
        if (l.r !== 'main' || l.y <= cur.headerY || l.y >= introEnd) continue;
        const t = clean(l.t);
        if (!shouldKeep(t) || HEADER.test(t) || PROMPT.test(t) || DECISION.test(t)) continue;
        const k = keyOf(t);
        if (cur._introKeys.has(k)) continue;
        cur._introKeys.add(k);
        if (!cur.intro) cur.intro = t;
        else if (/[。！？…」』]$/.test(cur.intro)) cur.intro += '\n' + t;
        else cur.intro += t;
      }
    }

    // 已選：箭頭到下一個箭頭（或出口）之間是各 step 的結果。
    const decisions = (f.layout === 'panel' && cur.selected) ? [] : lines.filter(l => l.r === 'main' && l.y > cur.headerY && DECISION.test(clean(l.t)));
    if (decisions.length) {
      for (let i = 0; i < decisions.length; i++) {
        const dl = decisions[i];
        const txt = clean(dl.t).replace(/^.*?[：:]\s*[「『]?/, '').replace(/[」』]?$/, '').trim();
        if (!txt) continue;
        let st = cur.steps[cur.steps.length - 1];
        if (!st || st.selected !== txt) {
          const hit = cur.actions.find(a => keyOf(a.text) === keyOf(txt)) ||
            cur.actions.find(a => keyOf(a.text).includes(keyOf(txt)) || keyOf(txt).includes(keyOf(a.text)));
          if (hit) { hit.selected = true; if (cur.selected && keyOf(cur.selected.text) === keyOf(txt)) cur.selected.text = hit.text; }
          else if (!cur.actions.length) cur.actions.push({ text: txt, selected: true });
          st = { intro: '', actions: cur.actions.map(a => a.text), selected: hit ? hit.text : txt, results: [] };
          cur.steps.push(st);
          if (!cur.selected) cur.selected = { text: hit ? hit.text : txt, matched: !!hit };
        }
        const nextY = i + 1 < decisions.length ? decisions[i + 1].y : Number.MAX_VALUE;
        for (const l of lines) {
          if (l.r !== 'main' || l.y <= dl.y || l.y >= Math.min(nextY, closeY)) continue;
          const t = clean(l.t);
          if (/走出了這一層|走出了这一层/.test(t)) break;
          if (!t || RESULT_JUNK.test(t) || PROMPT.test(t) || HEADER.test(t) || DECISION.test(t)) continue;
          if (/跟随多数|跟隨多數|点一下文字|點一下文字|按空白键|按空白鍵/.test(t)) continue;
          if (cur._introKeys && cur._introKeys.has(keyOf(t))) continue;
          st.results.push(t);
          if (/走出了這一層|走出了这一层/.test(t)) { cur._end = true; break; }
        }
      }
      cur._lastY = Math.max(cur._lastY, decisions[decisions.length - 1].y);
    } else if (cur.selected && cur.steps.length) {
      // 後續幀：標題以下、出口以上的文字都是目前最後一個 step 的結果。
      const st = cur.steps[cur.steps.length - 1];
      if (f.layout === 'panel') {
        // panel 版面的欄位順序不是空間順序：結果可能在已選行上方。
        for (const l of lines) {
          if (l.r !== 'main' || l.y <= cur.headerY || l.y >= closeY) continue;
          const t = clean(l.t);
          if (/走出了這一層|走出了这一层/.test(t)) break;
          if (!t || RESULT_JUNK.test(t) || HEADER.test(t) || PROMPT.test(t) || DECISION.test(t)) continue;
          if (/跟随多数|跟隨多數|点一下文字|點一下文字|按空白键|按空白鍵/.test(t)) continue;
          if (t === clean(cur.steps[cur.steps.length - 1].selected)) continue;
          if (cur._introKeys && cur._introKeys.has(keyOf(t))) continue;
          if (norm(cur.intro).includes(norm(t))) continue;
          if (!st.results.includes(t)) st.results.push(t);
        }
        if (st.results.length) {
          cur.closed = true; lastSignature = sigOf(cur); lastFrame = f.frame;
          finalize(cur, out); cur = null;
        }
        continue;
      }
      for (const l of lines) {
        if (l.r !== 'main' || l.y <= cur.headerY || l.y >= closeY) continue;
        const t = clean(l.t);
        if (/走出了這一層|走出了这一层/.test(t)) break;
        if (!t || RESULT_JUNK.test(t) || HEADER.test(t) || PROMPT.test(t) || DECISION.test(t)) continue;
        if (/跟随多数|跟隨多數|点一下文字|點一下文字|按空白键|按空白鍵/.test(t)) continue;
        st.results.push(t);
      }
    }

    if (closeLine) {
      cur.closed = true;
      lastSignature = sigOf(cur);
      lastFrame = f.frame;
      finalize(cur, out);
      cur = null;
    }
  }
  finalize(cur, out);
  // 同一事件在相鄰幀重複出現時只留一個；這是「畫面重繪」，不是新觀測。
  const dedup = [];
  for (const e of out.filter(x => x.name && x.selected && x.intro)) {
    const key = norm(e.name) + '|' + norm(e.selected.text);
    const prev = dedup.find(x => norm(x.name) + '|' + norm(x.selected.text) === key && Math.abs((x.startFrame || 0) - (e.startFrame || 0)) <= 80);
    if (prev) {
      if (!(prev.results || []).length && (e.results || []).length) prev.results = e.results;
      continue;
    }
    dedup.push(e);
  }
  return dedup;
}

// CLI：node event-extract.mjs <sessionDir>，只輸出摘要供除錯，不寫正式檔。
if (process.argv[1] && process.argv[1].endsWith('event-extract.mjs')) {
  const fs = await import('node:fs');
  const path = await import('node:path');
  const session = path.resolve(process.argv[2] || '');
  const frames = fs.readFileSync(path.join(session, 'structured.jsonl'), 'utf8')
    .split(/\r?\n/).filter(Boolean).map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean);
  const ev = extractEvents(frames);
  console.log(JSON.stringify({ events: ev.length, sample: ev.slice(0, 10).map(e => ({ name: e.name, floor: e.floor, actions: e.actions.map(a => a.text), selected: e.selected?.text, results: e.results.slice(0, 5), intro: e.intro.slice(0, 100) })) }, null, 2));
}
