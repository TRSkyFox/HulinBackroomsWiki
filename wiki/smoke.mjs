#!/usr/bin/env node
/* 前端冒烟：用 headless Edge 開站，依序點側欄 7 個入口，檢查頁面有沒有渲染、Console 有沒有錯。
   2026-10-30：這一天我兩次把前端改壞（一次是刪掉 #evbPath 的監聽目標、一次是刪掉示範頁的掛載點），
   兩次都是「整站初始化中斷 → 頁面空白」，但 339 項驗收全過、完全抓不到。
   這支測試就是那個缺口：改完前端跑一次，五秒知道有沒有炸。

   用法：
     node wiki/smoke.mjs              # 跑，失敗 exit 1
     node wiki/smoke.mjs --keep       # 保留 _smoke.html 以便除錯
*/
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';

const ROOT = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..');
const SITE = path.join(ROOT, 'wiki', 'site');
const IDX = path.join(SITE, 'index.html');
const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'wiki-smoke-'));
const PROBE = path.join(tmpDir, 'probe.html');

const EDGE_CANDIDATES = [
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
];
const browser = EDGE_CANDIDATES.find(p => fs.existsSync(p));
if (!browser) { console.error('找不到 Edge/Chrome，跳過冒煙測試'); process.exit(0); }
if (!fs.existsSync(IDX)) { console.error('找不到 site/index.html，先跑 build'); process.exit(1); }

/* ── 注入探針 ── */
const NAVS = ['home', 'floors', 'items', 'chars', 'ent', 'mixed', 'endings', 'about'];
const probe = `
<script>
window.__SMOKE = { errors: [], steps: [] };
window.addEventListener('error', function(e){ window.__SMOKE.errors.push(String(e.message || e.error)); });
(function(){
  var NAVS = ${JSON.stringify(NAVS)};
  var i = 0;
  function snap(nav){
    var pv = document.getElementById('pageView');
    var pm = document.getElementById('pageMain');
    var len = pv ? pv.innerHTML.length : -1;
    /* 2026-10-31：只看 hidden 屬性不夠 —— #pageView 曾被套進 #pageMain 裡，
       屬性上是顯示的，實際祖先 display:none，整站等於空白。 */
    var r = pv ? pv.getBoundingClientRect() : null;
    var visible = pv ? (!pv.hidden && pv.offsetParent !== null && r && r.height > 50 && r.width > 100) : false;
    var text = pv ? (pv.textContent || '').replace(/\\s+/g, ' ').trim().slice(0, 40) : '';
    window.__SMOKE.steps.push({ nav: nav, len: len, visible: visible, text: text,
      pmHidden: pm ? !!pm.hidden : null });
  }
  function next(){
    if (i >= NAVS.length) {
      /* 最後再開一個樓層頁：openFloorView 會套樓層配色、組出事件瀏覽器，
         這是今天最容易被我改壞的一段（曾因 #t4 / #evbPath 不見而整站中斷）。 */
      try {
        if (typeof openFloorView === 'function') {
          openFloorView('0', '大廳');
          var pv2 = document.getElementById('pageView');
          var r2 = pv2 ? pv2.getBoundingClientRect() : null;
          window.__SMOKE.steps.push({ nav:'floor(0|大廳)', len: pv2 ? pv2.innerHTML.length : -1, visible: pv2 ? (!pv2.hidden && pv2.offsetParent !== null && r2 && r2.height > 50) : false, text: pv2 ? (pv2.textContent||'').replace(/\\s+/g,' ').trim().slice(0,40) : '' });
          /* 再切到「事件」分頁：這一格以前是 0 高度（DOM 在、畫面空白） */
          var _btns = Array.prototype.slice.call(document.querySelectorAll('#pageView button'));
          var _evTab = _btns.filter(function(b){ return (b.textContent||'').trim() === '事件'; })[0];
          if (_evTab) _evTab.click();
          var _pane = document.querySelector('#viewBody [data-fpane="ev"]');
          var _host = _pane ? _pane.querySelector('.fl-evhost') : null;
          var _evb = _pane ? _pane.querySelector('.evb') : null;
          window.__SMOKE.steps.push({ nav:'floor 事件分頁', len: _evb ? _evb.innerHTML.length : -1, visible: !!(_host && _host.offsetHeight > 80), text: _host ? ('事件容器高度 ' + _host.offsetHeight + 'px') : '找不到事件容器' });
        } else { window.__SMOKE.errors.push('openFloorView 不存在'); }
      } catch (e) { window.__SMOKE.errors.push('openFloorView: ' + e.message); }
      document.body.setAttribute('data-smoke', JSON.stringify(window.__SMOKE));
      return;
    }
    var nav = NAVS[i++];
    var el = document.querySelector('[data-nav="' + nav + '"]');
    if (!el) { window.__SMOKE.steps.push({ nav: nav, len: -1, visible: false, text: '', missing: true }); setTimeout(next, 120); return; }
    try { el.click(); } catch (e) { window.__SMOKE.errors.push('click ' + nav + ': ' + e.message); }
    setTimeout(function(){ snap(nav); setTimeout(next, 120); }, 420);
  }
  setTimeout(next, 900);
})();
</script>
</body>`;

const html = fs.readFileSync(IDX, 'utf8');
if (!html.includes('</body>')) { console.error('index.html 沒有 </body>，無法注入'); process.exit(1); }
fs.writeFileSync(PROBE, html.replace('</body>', probe), 'utf8');

/* ── 跑 headless，輸出用檔案（沙箱下不能開 pipe）── */
const outFile = path.join(tmpDir, 'dom.txt');
const errFile = path.join(tmpDir, 'err.txt');
const oFd = fs.openSync(outFile, 'w');
const eFd = fs.openSync(errFile, 'w');
const profile = path.join(tmpDir, 'profile');
const r = spawnSync(browser, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--user-data-dir=' + profile,
  '--enable-logging=stderr', '--v=0',
  '--virtual-time-budget=20000', '--dump-dom',
  'file:///' + PROBE.replace(/\\/g, '/'),
], { stdio: ['ignore', oFd, eFd], timeout: 90000 });
fs.closeSync(oFd); fs.closeSync(eFd);

const dom = fs.existsSync(outFile) ? fs.readFileSync(outFile, 'utf8') : '';
const errTxt = fs.existsSync(errFile) ? fs.readFileSync(errFile, 'utf8') : '';

/* ── 解析結果 ── */
const m = dom.match(/data-smoke="([^"]*)"/);
let result = null;
if (m) {
  try { result = JSON.parse(m[1].replace(/&quot;/g, '"').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')); } catch { }
}
const consoleErrors = errTxt.split(/\r?\n/).filter(l => l.includes('CONSOLE') && /(Uncaught|TypeError|ReferenceError|SyntaxError)/.test(l))
  .map(l => (l.match(/"([^"]*)"/) || [])[1] || l.slice(0, 160));

const problems = [];
if (!result) problems.push('探針沒有回傳結果（頁面可能整個沒初始化）');
else {
  for (const s of result.steps || []) {
    if (s.missing) problems.push(`${s.nav}: 側欄找不到這個入口`);
    else if (!s.visible) problems.push(`${s.nav}: pageView 是隱藏的`);
    else if (s.len < 400) problems.push(`${s.nav}: 內容只有 ${s.len} 字元（應該是空白頁）`);
  }
  for (const e of result.errors || []) problems.push('JS 錯誤: ' + e);
}
for (const e of consoleErrors) problems.push('Console: ' + e);

console.log('=== 前端冒煙 ===');
console.log('瀏覽器:', path.basename(browser));
if (result) {
  console.log('入口        內容長度  可見  前 40 字');
  for (const s of result.steps || []) {
    console.log(String(s.nav).padEnd(11), String(s.len).padStart(7), s.visible ? '  ✓ ' : '  ✗ ', (s.text || '').slice(0, 40));
  }
}
if (problems.length) {
  console.log('\n✗ 發現 ' + problems.length + ' 個問題：');
  for (const p of problems.slice(0, 12)) console.log('  - ' + p);
} else {
  console.log('\n✓ ' + NAVS.length + ' 個入口全部有內容、Console 無錯誤');
}

if (!process.argv.includes('--keep')) {
  try { fs.rmSync(tmpDir, { recursive: true, force: true }); } catch { }
} else {
  console.log('\n（--keep：保留 ' + PROBE + ' 與 ' + tmpDir + '）');
}
process.exit(problems.length ? 1 : 0);

