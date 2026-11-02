// i18n 回歸測試：確保簡→繁不會再製造「四麵八方」這類錯字。
// 直接跑：node logger/structured/tests/i18n.test.mjs
import { explain } from '../i18n.mjs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const CASES = [
  ['四面八方', '四面八方'],
  ['四麵八方', '四麵八方'],
  ['里面', '裡面'],
  ['这里', '這裡'],
  ['皇后', '皇后'],
  ['最后', '最後'],
  ['身后', '身後'],
  ['头发', '頭髮'],
  ['发明', '發明'],
  ['干净', '乾淨'],
  ['干扰', '干擾'],
  ['一只', '一隻'],
  ['面条', '麵條'],
  ['面对', '面對'],
  ['放松', '放鬆'],
  ['松树', '松樹'],
  ['战斗', '戰鬥'],
  ['斗篷', '斗篷'],
  ['游戏', '遊戲'],
  ['游泳', '游泳'],
  ['云朵', '雲朵'],
  ['人云亦云', '人云亦云'],
  ['复杂', '複雜'],
  ['重复', '重複'],
  ['日历', '日曆'],
  ['历史', '歷史'],
  ['获得', '獲得'],
  ['收获', '收穫'],
  ['时钟', '時鐘'],
  ['分钟', '分鐘'],
  ['说时过还进这门车马', '說時過還進這門車馬'],
  ['一条条狭窄的维修隧道往四面八方延伸，墙上、天花板上爬满了锈蚀的管',
   '一條條狹窄的維修隧道往四面八方延伸，牆上、天花板上爬滿了鏽蝕的管']
];

export function run() {
  const checks = [];
  for (const [src, want] of CASES) {
    const got = explain(src).text;
    checks.push({ name: 'i18n:' + src.slice(0, 12), ok: got === want, detail: got === want ? '' : got + ' != ' + want });
  }
  /* 二次轉換不應該再變（避免同一份 staging 被重跑時越改越怪） */
  const once = explain('里面有一面条，战斗后放松。').text;
  const twice = explain(once).text;
  checks.push({ name: 'i18n:idempotent', ok: once === twice, detail: once + ' / ' + twice });
  return { ok: checks.every(c => c.ok), checks };
}

const IS_MAIN = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));
if (IS_MAIN) {
  const r = run();
  console.log(JSON.stringify(r, null, 2));
  process.exit(r.ok ? 0 : 1);
}
