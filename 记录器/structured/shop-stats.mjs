// shop-stats.mjs — 從 session 的 shops.jsonl 統計每間商店的商品出現率。
// 用法：node shop-stats.mjs <sessionDir> [<sessionDir> ...]
// 同一 visit 的多頁快照會合併成一次到訪；同商品只算一次。
import fs from 'node:fs';
import path from 'node:path';

const dirs = process.argv.slice(2).filter(Boolean);
if (!dirs.length) {
  console.error('用法：node shop-stats.mjs <sessionDir> [<sessionDir> ...]');
  process.exit(2);
}

function collect(dir) {
  const file = path.join(dir, 'shops.jsonl');
  if (!fs.existsSync(file)) return null;
  const visits = new Map();
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    if (!line.trim()) continue;
    let r;
    try { r = JSON.parse(line); } catch { continue; }
    if (!r || !r.visit || !Array.isArray(r.items)) continue;
    const key = String(r.visit);
    const prev = visits.get(key);
    // 同一 visit 的後續頁/後續快照會補上更多商品；保留最新價格與庫存。
    const items = new Map(prev ? prev.items : []);
    for (const it of r.items) {
      if (!it || !it.n) continue;
      items.set(String(it.n), { n: String(it.n), price: it.price ?? null, stock: it.stock ?? null });
    }
    visits.set(key, {
      name: String(r.n || (prev && prev.name) || '未命名商店'),
      intro: String(r.q || (prev && prev.intro) || ''),
      sell: !!(r.sell || (prev && prev.sell)),
      items
    });
  }

  const shops = new Map();
  for (const v of visits.values()) {
    const key = v.name;
    if (!shops.has(key)) shops.set(key, { n: v.name, q: v.intro, sell: v.sell, visits: 0, items: new Map() });
    const shop = shops.get(key);
    shop.visits++;
    shop.sell = shop.sell || v.sell;
    if (!shop.q && v.intro) shop.q = v.intro;
    for (const it of v.items.values()) {
      if (!shop.items.has(it.n)) shop.items.set(it.n, { n: it.n, price: it.price, stock: it.stock, seen: 0 });
      const s = shop.items.get(it.n);
      s.seen++;
      if (it.price != null) s.price = it.price;
      if (it.stock != null) s.stock = it.stock;
    }
  }

  return {
    session: path.basename(dir),
    shops: [...shops.values()].map(s => ({
      n: s.n, q: s.q, sell: s.sell, visits: s.visits,
      items: [...s.items.values()]
        .map(it => ({ ...it, probability: s.visits ? +(it.seen / s.visits).toFixed(4) : 0 }))
        .sort((a, b) => b.probability - a.probability || a.n.localeCompare(b.n))
    }))
  };
}

const out = { generated: new Date().toISOString(), shops: [] };
const byName = new Map();
for (const dir of dirs) {
  const r = collect(path.resolve(dir));
  if (!r) continue;
  for (const s of r.shops) {
    if (!byName.has(s.n)) byName.set(s.n, { n: s.n, q: s.q, sell: s.sell, visits: 0, items: new Map() });
    const t = byName.get(s.n);
    t.visits += s.visits;
    t.sell = t.sell || s.sell;
    if (!t.q && s.q) t.q = s.q;
    // 每場的 probability 是用該場 visits 算的；跨場要重新用 seen 總數加權。
    for (const it of s.items) {
      const seen = Math.round(it.probability * s.visits);
      if (!t.items.has(it.n)) t.items.set(it.n, { n: it.n, price: it.price, stock: it.stock, seen: 0 });
      const x = t.items.get(it.n);
      x.seen += seen;
      if (it.price != null) x.price = it.price;
      if (it.stock != null) x.stock = it.stock;
    }
  }
}
out.shops = [...byName.values()].map(s => ({
  n: s.n, q: s.q, sell: s.sell, visits: s.visits,
  items: [...s.items.values()]
    .map(it => ({ ...it, probability: s.visits ? +(it.seen / s.visits).toFixed(4) : 0 }))
    .sort((a, b) => b.probability - a.probability || a.n.localeCompare(b.n))
}));

const dest = path.join(path.resolve(dirs[0]), 'shop-stats.json');
fs.writeFileSync(dest, JSON.stringify(out, null, 2) + '\n', 'utf8');
console.log(JSON.stringify({ dest, shops: out.shops.length, visits: out.shops.reduce((n, s) => n + s.visits, 0) }, null, 2));
