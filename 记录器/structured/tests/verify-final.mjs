import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { run as runI18n } from './i18n.test.mjs';
import { run as runFont } from './font.test.mjs';
import { run as runData } from './data.test.mjs';
import { run as runRecorder } from './recorder.test.mjs';
import { run as runContent } from '../validate-content.mjs';
import { floorName } from '../text.mjs';

/*
 * 最終驗收：verify-final.mjs
 *
 * 2026-10-29 改版：不再寫死樓層號。
 * 舊版把「399-霓虹天堂」「0.22 全面翻修」「>=60 層」寫在程式裡，
 * 結果它不是回歸測試，而是某一天資料狀態的快照 —— 清空 content 之後
 * 直接 ENOENT 崩掉，什麼都測不到。
 *
 * 現在只看「內容本身合不合法」，有幾層就驗幾層：
 *   - floor.json 的 key 必須是 id|name，且 name 不含括號
 *   - events.json 的 selected 必須真的在 actions 裡
 *   - 正文不能出現玩家帳號名、聊天 UI、狀態流水
 *   - 站點的 FLOOR_DISCOVERED 每一層都要真的有 floor.json
 *   - app／dist 與 logger/structured 不能漂移
 */

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const wiki = path.join(root, 'wiki');
const content = path.join(wiki, 'content');
const floorsRoot = path.join(content, 'floors');
const logger = path.join(root, '记录器/structured');
const app = path.join(root, '客户端/structured');
const dist = path.join(root, '客户端/dist/Expedition/structured');

const out = { ok: true, checks: [] };
function check(name, ok, detail) { out.checks.push({ name, ok: !!ok, detail: detail === undefined ? '' : String(detail) }); if (!ok) out.ok = false; }
const norm = s => String(s || '').replace(/\s+/g, '').replace(/[「」『』“”]/g, '').trim();
const readJSON = p => JSON.parse(fs.readFileSync(p, 'utf8'));

/* ── 場次內容基本檢查 ── */
const coverageGaps = [];   // {key, observed, declared} → 稍後要求站點標成 partial
if (!fs.existsSync(floorsRoot)) {
  check('floors_dir', false, 'content/floors 不存在');
} else {
  const dirs = fs.readdirSync(floorsRoot, { withFileTypes: true }).filter(d => d.isDirectory() && !d.name.startsWith('_'));
  check('floors_present', dirs.length > 0, dirs.length + ' 層');
  const keys = new Set();
  for (const d of dirs) {
    const fp = path.join(floorsRoot, d.name, 'floor.json');
    if (!fs.existsSync(fp)) { check('floor_json_' + d.name, false, '缺 floor.json'); continue; }
    let f;
    try { f = readJSON(fp); } catch (e) { check('floor_json_' + d.name, false, 'JSON 壞'); continue; }
    check('floor_key_' + d.name, String(f.key) === String(f.id) + '|' + String(f.name), f.key);
    check('floor_name_clean_' + d.name, floorName(f.name) === String(f.name || ''), JSON.stringify(f.name));
    check('floor_dir_matches_' + d.name, floorName(d.name) === floorName(String(f.id) + '-' + String(f.name)), d.name);
    check('floor_info_' + d.name, f.info && f.info.name === f.name && String(f.info.id) === String(f.id), JSON.stringify(f.info));
    /* 2026-10-31：使用者要求把「舊資料裡只有名字」的樓層也收進來
       （遊戲內顯示亂碼、無法進入）。這種條目 _status = 'name-only'，
       沒有正文是預期狀態，不算失敗。 */
    if (f._status === 'name-only' || f._status === 'id-only' || f._introMissing) {
      check('floor_name_only_' + d.name, !String(f.doc || '').trim(), 'name-only，正文待補');
    } else {
      check('floor_doc_not_empty_or_partial_' + d.name,
        (String(f.doc || '').replace(/<[^>]*>/g, '').trim().length >= 10) || (f._staging && f._staging.needsReview),
        'doc=' + String(f.doc || '').length);
    }
    check('floor_no_player_name_' + d.name, !/狐天|狐夭/.test(String(f.doc || '')), '');
    if (f.palette) check('floor_palette_shape_' + d.name, /^#[0-9A-Fa-f]{6}$/.test(String(f.palette.bg || f.palette.background || '')), JSON.stringify(f.palette.bg));

    /* 事件 */
    const ep = path.join(floorsRoot, d.name, 'events.json');
    if (fs.existsSync(ep)) {
      let ev;
      try { ev = readJSON(ep); } catch (e) { check('events_json_' + d.name, false, 'JSON 壞'); ev = null; }
      if (ev) {
        check('events_floor_id_' + d.name, String(ev.floorId) === String(f.id), ev.floorId + ' != ' + f.id);
        check('events_floor_name_' + d.name, floorName(ev.floorName) === String(ev.floorName || ''), JSON.stringify(ev.floorName));
        const events = Array.isArray(ev.events) ? ev.events : [];
        const bad = events.filter(e => e.selected && !(e.actions || []).some(a => norm(a.text) === norm(e.selected)));
        check('events_selected_in_actions_' + d.name, bad.length === 0, bad.map(e => e.name + ':' + e.selected).join(' '));
        const dirty = events.filter(e => /狐天|狐夭|按\s*Enter|人看完了|理智\s*[-+]?\d/.test(JSON.stringify([e.intro, e.results, e.actions])));
        check('events_no_ui_noise_' + d.name, dirty.length === 0, dirty.map(e => e.name).join(' '));
        const noIntro = events.filter(e => (e.kind || 'event') !== 'ending' && String(e.intro || '').length < 12);
        check('events_intro_length_' + d.name, noIntro.length === 0, noIntro.map(e => e.name).join(' '));
        /* 覆蓋率：資料自己說有幾個事件。收不齊是允許的，但站點必須標「待核對」。 */
        const declared = events.reduce((m, e) => Math.max(m, (e.progress && Number(e.progress.total)) || 0), 0);
        if (declared > events.length) coverageGaps.push({ key: String(f.key), observed: events.length, declared });

        /* 路口：以前解析得到卻沒人寫進 events.json，這一條就是防它再退回去。 */
        const juncs = Array.isArray(ev.junctions) ? ev.junctions : [];
        /* 有些路口只有一個「往前走」的選項（單向提示），1 條是合法的；
           超過 4 條或沒有文字才是解析錯誤。 */
        const badJ = juncs.filter(j => !j || !j.text || !Array.isArray(j.paths) || j.paths.length < 1 || j.paths.length > 4);
        check('junctions_shape_' + d.name, badJ.length === 0, badJ.map(j => (j && j.text) || '?').join(' '));
        const badPath = juncs.filter(j => (j.paths || []).some(p => !p || !p.text || String(p.text).length < 2));
        check('junctions_paths_' + d.name, badPath.length === 0, badPath.length + ' junction(s)');
      }
    }

    /* 結局 */
    const xp = path.join(floorsRoot, d.name, 'endings.json');
    if (fs.existsSync(xp)) {
      const arr = readJSON(xp);
      const list = Array.isArray(arr) ? arr : [arr];
      const badId = list.filter(e => e && e.floorId != null && String(e.floorId) !== String(f.id));
      check('endings_floor_id_' + d.name, badId.length === 0, badId.map(e => e.name).join(' '));
      const fake = list.filter(e => (e.lines || []).length === 1 && (e.lines || [])[0] === e.name);
      check('endings_no_title_only_body_' + d.name, fake.length === 0, fake.map(e => e.name).join(' '));
    }
    keys.add(String(f.key));
  }
}

/* ── 站點與內容一致 ── */
const sitePath = path.join(wiki, 'site/index.html');
if (!fs.existsSync(sitePath)) check('site_exists', false, 'site/index.html 不存在');
else {
  const site = fs.readFileSync(sitePath, 'utf8');
  function siteObj(name) {
    const m = site.match(new RegExp('var ' + name + ' = (\\{.*?\\});', 's'));
    if (!m) return null;
    try { return JSON.parse(m[1]); } catch { return null; }
  }
  const disc = siteObj('FLOOR_DISCOVERED') || {};
  const partial = siteObj('FLOOR_PARTIAL') || {};
  const pal = siteObj('FLOOR_PAL') || {};
  const palName = siteObj('FLOOR_PAL_NAME') || {};
  check('site_discovered_json', !!siteObj('FLOOR_DISCOVERED'), Object.keys(disc).length + ' keys');
  const contentKeys = new Set();
  if (fs.existsSync(floorsRoot)) {
    for (const d of fs.readdirSync(floorsRoot, { withFileTypes: true })) {
      if (!d.isDirectory() || d.name.startsWith('_')) continue;
      const fp = path.join(floorsRoot, d.name, 'floor.json');
      if (fs.existsSync(fp)) { try { contentKeys.add(String(readJSON(fp).key)); } catch { } }
    }
  }
  const orphans = Object.keys(disc).filter(k => !contentKeys.has(k));
  check('site_discovered_backed_by_content', orphans.length === 0, orphans.join(' '));
  const palKeys = new Set([...Object.keys(pal), ...Object.keys(palName)]);
  check('site_palette_keys_sane', [...palKeys].every(k => /^[\d.]+(\|.+)?$/.test(k)), [...palKeys].slice(0, 5).join(','));
  check('site_endings_arrays', /var END_DATA = \{"bad":\[/.test(site), 'END_DATA');
  check('site_partial_is_object', typeof partial === 'object', String(partial));
  check('site_no_own_singular', !/"own":/.test(site), 'own singular');
  check('site_discovered_keys_have_id', Object.keys(disc).every(k => k.includes('|')), Object.keys(disc).slice(0, 3).join(' '));
  /* 2026-10-30：這條不再比「事件數 vs 出口進度」。
     `出口進度 2/15` 的 15 是出口節點數，跟事件條數無關；事件也不可能收齊
     （沒走到的分支不會出現在畫面上）。現在只驗一件真的事：
     站點標成 partial 的樓層，必須是正文短到 40 字以下的那種。 */
  const shortDocs = [];
  const docByKey = {};
  try {
    const dir = path.join(root, 'wiki', 'content', 'floors');
    for (const d of fs.readdirSync(dir)) {
      const fp = path.join(dir, d, 'floor.json');
      if (!fs.existsSync(fp)) continue;
      const f = JSON.parse(fs.readFileSync(fp, 'utf8'));
      const txt = String(f.doc || '').replace(/<[^>]*>/g, '').replace(/\s+/g, '');
      if (f.key) docByKey[f.key] = txt.length;
      if (f.key && f._status === 'name-only') { out.nameOnlyKeys = out.nameOnlyKeys || []; out.nameOnlyKeys.push(f.key); }
      if (txt.length > 0 && txt.length < 40) shortDocs.push(f.key);
    }
  } catch { }
  /* name-only（只有名字、沒有正文）不是「不完整」，站點不標 partial，驗收也不該要求。 */
  const nameOnly = new Set(out.nameOnlyKeys || []);
  const wrongMark = Object.keys(docByKey).filter(k => nameOnly.has(k) ? !!partial[k] : ((docByKey[k] > 0 && docByKey[k] < 40) !== !!partial[k]));
  check('site_marks_incomplete_floors', wrongMark.length === 0,
    '不一致：' + wrongMark.map(k => k + '(' + docByKey[k] + '字,partial=' + !!partial[k] + ')').join(' '));
  out.partialAudit = { shortDocs, marked: Object.keys(partial), docByKey };
  out.coverage = coverageGaps;
}

/* ── 管線副本：app／dist 不可以跟 logger/structured 不同 ── */
/* ── PowerShell 檔案必須是 UTF-8 with BOM ──
   2026-10-29 真實事故：編輯器存檔把 backroom-logger.ps1 的 BOM 吃掉，
   PS 5.1 就會把整份中文當 ANSI 讀 —— 錄製器的中文全變亂碼。
   這種錯在語法檢查裡看不出來，只能靠檢查檔頭。 */
const ps1 = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name.startsWith('_archive')) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { walk(p); continue; }
    if (/\.ps1$/i.test(e.name)) ps1.push(p);
  }
})(root);
const noBom = ps1.filter(p => {
  const b = fs.readFileSync(p);
  if (b.length > 3 && b[0] === 0xEF && b[1] === 0xBB && b[2] === 0xBF) return false;
  /* 純 ASCII 的腳本沒有 BOM 也不會亂碼，只檢查真的有中文的。 */
  let nonAscii = false;
  for (let i = 0; i < b.length; i++) if (b[i] > 0x7f) { nonAscii = true; break; }
  return nonAscii;
});
check('ps1_utf8_bom', noBom.length === 0, noBom.map(p => path.relative(root, p)).join(' '));

function sha(p) { return crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex'); }
const pipeFiles = fs.readdirSync(logger, { withFileTypes: true })
  .filter(e => e.isFile() && /\.(mjs|exe|otf|json|md|txt)$/i.test(e.name))
  .map(e => e.name);
let drift = [];
for (const base of [app, dist]) {
  for (const f of pipeFiles) {
    const a = path.join(logger, f), b = path.join(base, f);
    if (!fs.existsSync(b) || sha(a) !== sha(b)) drift.push(path.basename(base) + '/' + f);
  }
}
check('pipeline_in_sync', drift.length === 0, drift.slice(0, 6).join(', '));

/* ── 必要檔案 ── */
for (const p of [
  '记录器/structured/text.mjs',
  '记录器/structured/event-clean.mjs',
  '记录器/structured/sync.mjs',
  '记录器/structured/i18n.mjs',
  '记录器/structured/char-freq.json',
  'wiki/content/build.mjs',
  'wiki/content/template.html',
  '客户端/volume-watch.ps1'
]) check('file_' + p, fs.existsSync(path.join(root, p)), p);

/* ── 四組回歸測試＋內容引用完整性 ── */
const contentResult = runContent();
for (const r of [runI18n(), runFont(), runData(), runRecorder(), contentResult]) {
  for (const c of r.checks) check(c.name, c.ok, c.detail);
}
out.warnings = contentResult.warnings || [];

out.summary = { total: out.checks.length, failed: out.checks.filter(x => !x.ok).length, warnings: out.warnings.length };
console.log(JSON.stringify(out, null, 2));
if (!out.ok) process.exit(1);
