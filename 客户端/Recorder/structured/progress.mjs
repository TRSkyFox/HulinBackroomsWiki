import fs from 'node:fs';
import path from 'node:path';

/* 把「目前整理到第幾條」寫進 progress.json，讓 GUI 直接顯示。
   原子寫入，避免 GUI 讀到半個 JSON。 */
export function report(session, stage, current, total, item) {
  try {
    const cur = Math.max(0, Number(current) || 0);
    const tot = Math.max(0, Number(total) || 0);
    const percent = tot > 0 ? Math.max(0, Math.min(100, Math.round((cur / tot) * 100))) : 0;
    const payload = {
      stage: String(stage || ''),
      percent,
      current: cur,
      total: tot,
      item: String(item || ''),
      detail: String(item || ''),
      ts: Date.now()
    };
    const tmp = path.join(session, 'progress.json.tmp');
    fs.writeFileSync(tmp, JSON.stringify(payload) + '\n', 'utf8');
    fs.renameSync(tmp, path.join(session, 'progress.json'));
  } catch { }
}
