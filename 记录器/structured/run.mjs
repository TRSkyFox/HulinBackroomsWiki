import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { WIKI, INCOMING } from './paths.mjs';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
if (!args[0]) { console.error('usage: node run.mjs <sessionDir> [--staging <dir>] [--apply-events] [--merge]'); process.exit(2); }
const session = path.resolve(args[0]);
// 先清掉換層瞬間產生的空白樓層檔（只有標題、沒有事件）
spawnSync(process.execPath, [path.join(here, 'clean-levels.mjs'), session], { stdio: 'inherit' });
const si = args.indexOf('--staging');
const wikiBuild = path.join(WIKI, 'content', 'build.mjs');
const portable = args.includes('--portable') || fs.existsSync(path.join(here, '..', 'portable.flag')) || !fs.existsSync(wikiBuild);
const staging = si >= 0 ? path.resolve(args[si + 1]) : (portable ? path.join(session, 'staging') : path.join(INCOMING, path.basename(session)));

function writeProgress(stage, percent, detail) {
  try {
    const tmp = path.join(session, 'progress.json.tmp');
    fs.writeFileSync(tmp, JSON.stringify({ stage, percent, detail, ts: Date.now() }) + '\n', 'utf8');
    fs.renameSync(tmp, path.join(session, 'progress.json'));
  } catch (e) {}
}
writeProgress('palette', 5, '樓層截圖配色取樣');

const palScript = path.join(here, '..', 'palette-from-shots.ps1');
if (fs.existsSync(path.join(session, 'floor-shots')) && fs.existsSync(palScript)) {
  const pal = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', palScript, '-SessionDir', session], { stdio: 'inherit' });
  if (pal.status !== 0) console.error('樓層截圖 palette 取樣失敗');
}
writeProgress('font', 15, '字體校正');
const fc = spawnSync(process.execPath, [path.join(here, 'font-correct.mjs'), session], { stdio: 'inherit' });
if (fc.status !== 0) console.error('字體校正失敗（保留原始 OCR）');
writeProgress('parse', 25, '解析 OCR 紀錄');
const parse = spawnSync(process.execPath, [path.join(here, 'parse.mjs'), session], { stdio: 'inherit' });
if (parse.status !== 0) process.exit(parse.status || 1);
writeProgress('event-parse', 30, '重建路口與事件');
const ep = spawnSync(process.execPath, [path.join(here, 'event-parse.mjs'), session], { stdio: 'inherit' });
if (ep.status !== 0) console.error('事件解析失敗（保留 raw）');
writeProgress('input-bind', 34, '綁定玩家輸入');
const ib = spawnSync(process.execPath, [path.join(here, 'input-bind.mjs'), session], { stdio: 'inherit' });
if (ib.status !== 0) console.error('輸入綁定失敗（保留 raw）');
/* 2026-10-28：預設不再寫入正式 content。
   以前這裡是「自動 --apply + 自動 --confirm」，等於每次關遊戲都直接改 Wiki，
   低信心 OCR 也會變成正式頁面（Level 0.22 就是這樣來的）。
   現在只產生 staging，要寫入必須明確加 --apply-events／--merge。 */
const applyEvents = args.includes('--apply-events');
const applyMerge = args.includes('--merge');
writeProgress('event-import', 36, (portable || !applyEvents) ? '事件候選（不寫入）' : '事件審查／匯入');
const ei = spawnSync(process.execPath, [path.join(here, 'event-import.mjs'), session].concat(portable || !applyEvents ? [] : ['--apply']), { stdio: 'inherit' });
if (ei.status !== 0) console.error('事件匯入失敗（staging 保留）');
writeProgress('transcript', 38, '逐字稿');
const tr = spawnSync(process.execPath, [path.join(here, 'transcript.mjs'), session], { stdio: 'inherit' });
if (tr.status !== 0) console.error('逐字稿產生失敗');
writeProgress('consensus', 48, '共識逐字稿');
const cs = spawnSync(process.execPath, [path.join(here, 'consensus.mjs'), session], { stdio: 'inherit' });
if (cs.status !== 0) console.error('共識逐字稿產生失敗');
writeProgress('import', 58, '寫入 staging');
const imp = spawnSync(process.execPath, [path.join(here, 'import-wiki.mjs'), path.join(session, 'structured', 'records.jsonl'), '--out', staging], { stdio: 'inherit' });
if (imp.status !== 0) process.exit(imp.status || 1);
writeProgress('validate', 68, '驗證 staging');
const val = spawnSync(process.execPath, [path.join(here, 'validate.mjs'), staging], { stdio: 'inherit' });
if (val.status !== 0) console.error('staging 驗證有問題（見 validation.json）');

if (portable) {
  // 給別人的版本：不碰 Wiki；只打包成可傳回的 .hulinlog.zip。
  const out = path.join(process.env.LOCALAPPDATA || path.dirname(session), 'HulinExpedition', 'exports');
  writeProgress('export', 78, '打包紀錄');
  const exp = spawnSync(process.execPath, [path.join(here, 'export-package.mjs'), session, staging, '--out', out], { stdio: 'inherit' });
  if (exp.status !== 0) console.error('紀錄包匯出失敗');
  writeProgress('done', 100, '完成');
  console.log(JSON.stringify({ session, staging, portable: true, exports: out }, null, 2));
} else {
  /* Wiki 主人本機：先 dry-run 產生審查報告，再決定要不要寫入。 */
  writeProgress('review', 76, '產生審查報告（不寫入）');
  const dry = spawnSync(process.execPath, [path.join(here, 'merge-final.mjs'), staging], { stdio: 'inherit' });
  if (dry.status !== 0) console.error('審查報告產生失敗（見 staging/merge-final-report.json）');

  let merged = false;
  /* --auto：關遊戲後無人值守的自動匯入。
     自動合併高信心樓層／配色，並套用「固定區域切乾淨」的事件；
     事件仍受 event-clean 的難度/選項/結果截斷與去重保護。
     驗證有 error 時整場不寫正式 Wiki，只留 staging。 */
  const autoImport = args.includes('--auto');
  if (autoImport && !applyMerge && !portable) {
    let eventAdded = 0;
    const report = { enabled: true, imported: false, reason: '', events: 'staging-only', at: new Date().toISOString() };
    try {
      const v = JSON.parse(fs.readFileSync(path.join(staging, 'validation.json'), 'utf8'));
      if ((v.errors || []).length) report.reason = 'validation errors: ' + (v.errors || []).length;
      else {
        writeProgress('auto-merge', 78, '自動合併高信心樓層');
        const am = spawnSync(process.execPath, [path.join(here, 'merge-final.mjs'), staging, '--confirm'], { stdio: 'inherit' });
        let floorMerged = false;
        if (am.status !== 0) report.reason = 'merge failed';
        else {
          try {
            const mr = JSON.parse(fs.readFileSync(path.join(staging, 'merge-final-report.json'), 'utf8'));
            floorMerged = (mr.floors || []).length > 0;
          } catch { floorMerged = true; }
        }
        // 先建新樓層資料夾，事件才寫得進去；既有樓層也可以直接補事件。
        writeProgress('auto-events', 80, '自動匯入乾淨事件');
        const ea = spawnSync(process.execPath, [path.join(here, 'event-import.mjs'), session, '--apply', '--auto'], { stdio: 'inherit' });
        if (ea.status !== 0) report.events = 'event import failed';
        else {
          /* 結果從檔案讀，不從 stdout 讀：管道在沙箱／CI 會 EPERM。 */
          try {
            const er = JSON.parse(fs.readFileSync(path.join(session, 'structured', 'event-import.json'), 'utf8'));
            eventAdded = er.added || 0;
            report.events = { applied: true, added: eventAdded, skipped: er.skipped || 0, rejected: er.rejected || 0, endings: er.endings || 0 };
          } catch { report.events = 'applied'; }
        }
        report.imported = floorMerged || eventAdded > 0;
        if (!report.imported) report.reason = 'nothing high-confidence';
        if (eventAdded > 0) {
          writeProgress('auto-build', 84, '重建站點（含新事件）');
          const bd = spawnSync(process.execPath, [wikiBuild], { stdio: 'inherit' });
          if (bd.status !== 0) report.reason = 'event added but build failed';
        }
      }
    } catch (e) { report.reason = e.message; }
    fs.writeFileSync(path.join(staging, 'auto-import.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');
    merged = !!report.imported;
  }
  if (applyMerge) {
    writeProgress('merge', 82, '合併正式 Wiki');
    const merge = spawnSync(process.execPath, [path.join(here, 'merge-final.mjs'), staging, '--confirm'], { stdio: 'inherit' });
    if (merge.status !== 0) console.error('正式合併失敗（見 merge-final-report.json）');
    else merged = true;
  }

  writeProgress('build', 90, '重建 Wiki');
  const build = spawnSync(process.execPath, [wikiBuild], { stdio: 'inherit' });
  if (build.status !== 0) console.error('Wiki build 失敗');

  /* 給人看的摘要：不要只丟一份 JSON 叫使用者自己找。 */
  let review = null;
  try {
    const v = JSON.parse(fs.readFileSync(path.join(staging, 'validation.json'), 'utf8'));
    const er = path.join(staging, 'events-review.jsonl');
    const ev = fs.existsSync(er) ? fs.readFileSync(er, 'utf8').split(/\r?\n/).filter(Boolean).map(JSON.parse) : [];
    review = {
      validation: { errors: (v.errors || []).length, warnings: (v.warnings || []).length },
      floors: (v.floors || []).length,
      entities: (v.entities || []).length,
      events: { total: ev.length, accepted: ev.filter(x => x.accepted).length, rejected: ev.filter(x => !x.accepted).length }
    };
  } catch { }

  writeProgress('done', 100, applyMerge ? '完成（已合併）' : '完成（僅 staging）');
  console.log(JSON.stringify({ session, staging, portable: false, merged, review }, null, 2));
  if (!merged) {
    console.log([
      '',
      '── 尚未寫入正式 Wiki（這是預設行為）──',
      '1) 先看：' + path.join(staging, 'validation.json'),
      '         ' + path.join(staging, 'events-review.jsonl'),
      '         ' + path.join(staging, 'merge-final-report.json'),
      '2) 要把事件寫進樓層 events.json：',
      '   node "' + path.join(here, 'event-import.mjs') + '" "' + session + '" --apply',
      '3) 要合併樓層／實體：',
      '   node "' + path.join(here, 'merge-final.mjs') + '" "' + staging + '" --confirm',
      '4) 重建網站：',
      '   node "' + wikiBuild + '"',
      '（或用一次跑完：node run.mjs "' + session + '" --apply-events --merge）'
    ].join('\n'));
  }
}
