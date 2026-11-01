/* 行字色 → 角色分類（2026-10-31）
   ------------------------------------------------------------------
   使用者：「取色不是早就該做了，很多字體顏色都有差異，可以更精準地讀取、多重驗證。」

   實測樣本（三張遊戲截圖，Windows OCR 逐行取色）：
     一般正文      #B0A57C ~ #BCB085   亮度中
     事件標題【】  #CDB952             亮度高、金
     樓層標題      #DAC557             亮度高、金
     → 你決定      #CEC495 / #D2C798   亮度高
     數值／效果    #C5B66E / #C5B87C   亮度中高、金
     出口進度      #89805B / #8B825D   亮度低
     選項          #E7DBA7 / #E5D9A6   亮度最高
     提示語        #E4CE5B             亮度高、金

   注意：每層配色不同（樓層主題會改色），所以**不能比絕對色值**，
   要用「本幀正文色的相對差異」來分類。
*/

function hexToRgb(hex) {
  const m = String(hex || '').match(/^#?([0-9a-f]{6})$/i);
  if (!m) return null;
  const v = parseInt(m[1], 16);
  return { r: (v >> 16) & 255, g: (v >> 8) & 255, b: v & 255 };
}
function lum({ r, g, b }) { return (0.299 * r + 0.587 * g + 0.114 * b) / 255; }
function sat({ r, g, b }) {
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
  return mx === 0 ? 0 : (mx - mn) / mx;
}
function hue({ r, g, b }) {
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  if (!d) return 0;
  let h;
  if (mx === r) h = ((g - b) / d) % 6;
  else if (mx === g) h = (b - r) / d + 2;
  else h = (r - g) / d + 4;
  h *= 60;
  return h < 0 ? h + 360 : h;
}

/** 本幀的「正文基準色」：出現次數最多的那一群（按亮度分箱） */
export function bodyBaseline(lines) {
  const bins = new Map();
  for (const l of lines || []) {
    const rgb = hexToRgb(l.ink);
    if (!rgb) continue;
    const L = lum(rgb);
    const k = Math.round(L * 20);        /* 0.05 一箱 */
    if (!bins.has(k)) bins.set(k, { n: 0, L: 0, s: 0, h: 0 });
    const o = bins.get(k); o.n++; o.L += L; o.s += sat(rgb); o.h += hue(rgb);
  }
  if (!bins.size) return null;
  /* 行數太少（<8 行有 ink）時基準不可靠 → 不做顏色判斷，退回純文字邏輯 */
  const _total = [...bins.values()].reduce((n2,o)=>n2+o.n,0);
  if (_total < 8) return null;
  const best = [...bins.values()].sort((a, b) => b.n - a.n)[0];
  return { L: best.L / best.n, s: best.s / best.n, h: best.h / best.n, n: best.n };
}

/**
 * 單行角色。
 * @returns {'title'|'bright'|'dim'|'gold'|'body'|'unknown'}
 */
export function roleOf(line, base) {
  const rgb = hexToRgb(line && line.ink);
  if (!rgb || !base) return 'unknown';
  const L = lum(rgb), s = sat(rgb), h = hue(rgb);
  const dL = L - base.L;
  const gold = s > 0.35 && h >= 30 && h <= 65;
  /* 標題 vs 數值：亮度幾乎一樣，差在飽和度（實測標題 s≈0.60、效果 s≈0.37~0.44） */
  if (gold && s > 0.52 && dL > 0.015) return 'title';
  if (gold && dL > 0.015) return 'gold';
  if (dL > 0.16) return 'bright';            /* 選項：很亮、不是金 */
  if (dL < -0.10) return 'dim';              /* 出口進度／暗字 */
  return 'body';
}

/** 整幀分類：回傳 Map(index → role) 與基準色 */
export function classifyFrame(lines) {
  const base = bodyBaseline(lines);
  const roles = new Map();
  (lines || []).forEach((l, i) => roles.set(i, roleOf(l, base)));
  return { base, roles };
}

/** 事件標題驗證：顏色像標題才給高分（給 event-parse 當第二票） */
export function headerScore(line, base) {
  const r = roleOf(line, base);
  if (r === 'title') return 3;
  if (r === 'bright') return 2;
  if (r === 'gold') return 1;
  if (r === 'body') return 0;
  return 0;
}
