import fs from 'node:fs';
import path from 'node:path';

const root = 'D:/BACKROOMS/wiki/content';
const events = JSON.parse(fs.readFileSync(path.join(root, 'events.json'), 'utf8'));
const floors = new Map();
for (const d of fs.readdirSync(path.join(root, 'floors'), { withFileTypes: true })) {
  if (!d.isDirectory()) continue;
  const p = path.join(root, 'floors', d.name, 'floor.json');
  if (!fs.existsSync(p)) continue;
  const f = JSON.parse(fs.readFileSync(p, 'utf8'));
  floors.set(String(f.id), f.name || '');
}
const errors = [];
const warnings = [];
const seen = new Set();
for (const [fid, f] of Object.entries(events.floors || {})) {
  if (!floors.has(String(fid)) && fid !== 'Void') errors.push(`unknown floor ${fid}`);
  for (const j of f.junctions || []) {
    if (!j.text) warnings.push(`${fid}: junction without text`);
    for (const p of j.paths || []) {
      if (!p.text) errors.push(`${fid}: empty path`);
      if (p.selected && j.selected && p.text !== j.selected) errors.push(`${fid}: selected path mismatch ${p.text}`);
    }
  }
  for (const e of f.events || []) {
    if (!e.name || !String(e.name).trim()) errors.push(`${fid}: event without name`);
    if (!e.intro && !(e.actions || []).length) warnings.push(`${fid}/${e.name}: no intro/actions`);
    if (seen.has(`${fid}/${e.name}`)) errors.push(`${fid}: duplicate event ${e.name}`);
    seen.add(`${fid}/${e.name}`);
    const acts = (e.actions || []).map(a => a.text);
    if (new Set(acts).size !== acts.length) errors.push(`${fid}/${e.name}: duplicate action`);
    if (e.selected && !acts.includes(e.selected)) errors.push(`${fid}/${e.name}: selected not in actions (${e.selected})`);
    if (e.kind === 'ending' && acts.length) errors.push(`${fid}/${e.name}: ending has actions`);
    if (e.kind === 'combat' && !acts.length) warnings.push(`${fid}/${e.name}: combat without actions`);
    const blob = JSON.stringify(e);
    if (/难度\s*\d+|難度\s*\d+|掷出\s*\d+|擲出\s*\d+/.test(blob)) errors.push(`${fid}/${e.name}: exact difficulty/roll leaked`);
  }
}
const summary = { floors: Object.keys(events.floors || {}).length, events: [...seen].length, errors: errors.length, warnings: warnings.length };
console.log(JSON.stringify({ summary, errors, warnings }, null, 2));
if (errors.length) process.exit(1);
