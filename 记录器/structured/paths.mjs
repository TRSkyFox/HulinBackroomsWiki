// paths.mjs — 管線腳本的路徑唯一出口。
// 從目前檔案位置往上找含 `wiki/content` 的工作區根目錄；
// 這樣 source、`客户端/app/structured`、`dist/FoyerRecorder/structured` 都能用。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const HERE = path.dirname(fileURLToPath(import.meta.url));

function findRoot(start) {
  let dir = start;
  for (let i = 0; i < 10; i++) {
    if (fs.existsSync(path.join(dir, 'wiki', 'content'))) return dir;
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  /* 可攜版沒有 wiki/：退回 structured 的上一層，呼叫端再用 --staging／--portable 覆蓋。 */
  return path.resolve(start, '..', '..');
}

export const ROOT = findRoot(HERE);
export const WIKI = path.join(ROOT, 'wiki');
export const CONTENT = path.join(WIKI, 'content');
export const FLOORS = path.join(CONTENT, 'floors');
export const INCOMING = path.join(WIKI, '_incoming');
export const LOGS = path.join(ROOT, '记录器', 'logs');
export const STRUCTURED = HERE;
