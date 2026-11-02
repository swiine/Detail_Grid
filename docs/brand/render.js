// Draws docs/brand: interlock.png (the logo in every scheme), palettes.png, and icons/interlock--<scheme>.png/.ico.
// Run: node docs/brand/render.js docs/brand
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const { palettes, interlockPixels, pixelColour, interlockSvg } = require('./brand.js');
const out = process.argv[2] || __dirname;
fs.mkdirSync(path.join(out, 'icons'), { recursive: true });

const big = (p, s) => `<svg xmlns="http://www.w3.org/2000/svg" width="${s}" height="${s}" viewBox="0 0 64 64">${interlockSvg(p, p.id + s)}</svg>`;
const pix = (p, s, style = '') => `<svg xmlns="http://www.w3.org/2000/svg" width="${s}" height="${s}" viewBox="0 0 16 16" shape-rendering="crispEdges" style="${style}">` +
  interlockPixels.map((r, y) => [...r].map((ch, x) => { const c = pixelColour(ch, p); return c ? `<rect x="${x}" y="${y}" width="1" height="1" fill="${c}"/>` : ''; }).join('')).join('') + '</svg>';
const page = body => `<!doctype html><html><head><meta charset="utf-8"><style>body{margin:0;background:#0C0E10;color:#E8ECEE;font:14px "Segoe UI",system-ui,sans-serif}
#s{padding:26px 30px;width:1120px}h1{margin:0 0 4px;font-size:24px}.sub{color:#9AA4AA;margin:0 0 18px}
.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:16px}.card{border-radius:14px;padding:16px;border:1px solid #262C31}
.bar{display:flex;align-items:center;gap:9px;background:#343C49;padding:4px 10px;border-radius:4px;color:#bbc;font-size:11px;margin-top:12px;width:max-content}
.sw{display:flex;height:46px}.sw div{flex:1;display:flex;align-items:flex-end;padding:3px 5px;font:10px Consolas,monospace}
.row{border-radius:6px;padding:7px 9px;margin-bottom:6px;font-size:12px}.btn{display:inline-block;padding:4px 12px;border-radius:4px;font-weight:600;font-size:12px}
</style></head><body><div id="s">${body}</div></body></html>`;

(async () => {
  const b = await chromium.launch();
  const pg = await b.newPage({ viewport: { width: 1200, height: 900 }, deviceScaleFactor: 2 });

  // The logo in every scheme
  let body = '<h1>CoWork logo: Interlock</h1><p class="sub">Two brackets slotting together into one square. Its colours follow the scheme picked in Settings. Shown big (3D), as the 16 px pixel art (zoomed), and at real size in the status bar.</p><div class="grid">';
  for (const p of palettes)
    body += `<div class="card" style="background:${p.back}"><div style="display:flex;gap:14px;align-items:flex-end">${big(p, 104)}${pix(p, 80, 'border:1px solid #ffffff22;border-radius:3px')}</div>
      <div class="bar">MODEL ⊞ 1:500 ${pix(p, 16)}</div><div style="margin-top:10px;font-weight:700;color:${p.text}">${p.name}</div></div>`;
  await pg.setContent(page(body + '</div>'));
  await pg.locator('#s').screenshot({ path: path.join(out, 'interlock.png') });

  // Palettes, with the logo in each preview
  body = '<h1>CoWork colour schemes</h1><p class="sub">Picked in Settings. The logo takes its two colours from the scheme.</p><div class="grid" style="grid-template-columns:repeat(3,1fr)">';
  for (const [i, p] of palettes.entries())
    body += `<div style="border-radius:12px;overflow:hidden;border:1px solid #262C31;background:${p.back}"><div class="sw">` +
      ['back', 'card', 'rail', 'accent', 'accent2', 'text'].map(k => `<div style="background:${p[k]};color:${k === 'text' ? p.back : k.startsWith('accent') ? '#0008' : p.text}">${p[k]}</div>`).join('') +
      `</div><div style="padding:12px;color:${p.text}"><div style="display:flex;gap:8px;align-items:center;font-weight:700;margin-bottom:10px">${big(p, 22)} CoWork</div>
      <div class="row" style="background:${p.card}"><b style="color:#F5A35C">Sam</b> moved the kerb line</div>
      <div class="row" style="background:${p.mine};border-left:3px solid ${p.accent}"><b style="color:#5CC9F5">Alex</b> nice, updating sections</div>
      <span class="btn" style="background:${p.accent};color:${p.accentText}">Join CoWork</span> <span class="btn" style="border:1px solid ${p.accent};color:${p.accent}">Message</span></div>
      <div style="padding:9px 12px;background:${p.rail};color:${p.text};display:flex;justify-content:space-between"><b>${i + 1}. ${p.name}</b><span style="color:${p.dim};font:11px Consolas">${p.id}</span></div></div>`;
  await pg.setContent(page(body + '</div>'));
  await pg.locator('#s').screenshot({ path: path.join(out, 'palettes.png') });
  await b.close();

  // Icon files, straight from the pixel grid (16 px, and 32 px with each pixel doubled), PNG + ICO.
  const zlib = require('zlib');
  const png = (p, scale) => {
    const n = 16 * scale, raw = Buffer.alloc((n * 4 + 1) * n);
    for (let y = 0; y < n; y++) {
      raw[y * (n * 4 + 1)] = 0;
      for (let x = 0; x < n; x++) {
        const c = pixelColour(interlockPixels[Math.floor(y / scale)][Math.floor(x / scale)], p), o = y * (n * 4 + 1) + 1 + x * 4;
        if (c) { const v = parseInt(c.slice(1), 16); raw[o] = v >> 16 & 255; raw[o + 1] = v >> 8 & 255; raw[o + 2] = v & 255; raw[o + 3] = 255; }
      }
    }
    const crc = buf => { let c, t = []; for (let k = 0; k < 256; k++) { c = k; for (let j = 0; j < 8; j++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; t[k] = c >>> 0; } c = ~0; for (const x of buf) c = t[(c ^ x) & 255] ^ (c >>> 8); return (~c) >>> 0; };
    const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const cr = Buffer.alloc(4); cr.writeUInt32BE(crc(td)); return Buffer.concat([len, td, cr]); };
    const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(n, 0); ihdr.writeUInt32BE(n, 4); ihdr[8] = 8; ihdr[9] = 6;
    return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
  };
  for (const p of palettes) {
    const p16 = png(p, 1), p32 = png(p, 2);
    fs.writeFileSync(path.join(out, 'icons', `interlock--${p.id}.png`), p16);
    const head = Buffer.from([0, 0, 1, 0, 2, 0]); let off = 6 + 32; const dir = [];
    for (const [s, data] of [[16, p16], [32, p32]]) { const e = Buffer.alloc(16); e[0] = s; e[1] = s; e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6); e.writeUInt32LE(data.length, 8); e.writeUInt32LE(off, 12); off += data.length; dir.push(e); }
    fs.writeFileSync(path.join(out, 'icons', `interlock--${p.id}.ico`), Buffer.concat([head, ...dir, p16, p32]));
  }
})();
