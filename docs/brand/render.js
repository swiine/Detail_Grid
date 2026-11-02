// Renders the palette sheet, the logo sheet, and every logo as 16/24/32 px PNGs plus a .ico (for cowork-icon.ico).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const { palettes, logos } = require('./brand.js');
const out = process.argv[2];
fs.mkdirSync(path.join(out, 'icons'), { recursive: true });

const svg = (logo, p, size, crisp) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 16 16"${crisp ? ' shape-rendering="geometricPrecision"' : ''}>${logo.svg(p.accent, p.accentText)}</svg>`;

const page = (title, body) => `<!doctype html><html><head><meta charset="utf-8"><style>
body{margin:0;background:#0F1214;color:#E8ECEE;font:14px "Segoe UI",system-ui,sans-serif}
.sheet{width:1180px;padding:28px 32px}h1{margin:0 0 4px;font-size:26px}p.sub{margin:0 0 20px;color:#9AA4AA}
.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:18px}
.card{border-radius:12px;overflow:hidden;border:1px solid #2A3035}
.sw{display:flex;height:54px}.sw div{flex:1;display:flex;align-items:flex-end;padding:4px 6px;font:10px Consolas,monospace}
.mock{display:flex;height:170px}.rail{width:40px;display:flex;flex-direction:column;align-items:center;gap:6px;padding-top:8px}
.rail i{width:28px;height:28px;border-radius:6px;display:block}.fly{flex:1;padding:10px}
.btn{display:inline-block;padding:4px 12px;border-radius:4px;font-weight:600;font-size:12px}
.row{border-radius:6px;padding:7px 9px;margin-bottom:6px;font-size:12px}
.label{padding:10px 12px;display:flex;justify-content:space-between;align-items:center}
.lg{display:grid;grid-template-columns:repeat(3,1fr);gap:18px}
.lcard{background:#171B1E;border:1px solid #2A3035;border-radius:12px;padding:16px}
.lcard .big{display:flex;gap:18px;align-items:flex-end;margin-bottom:10px}
.px{image-rendering:pixelated;width:96px;height:96px;border:1px solid #2A3035}
.bar{display:flex;align-items:center;gap:10px;background:#343C49;padding:4px 10px;border-radius:4px;color:#bbc;font-size:11px;margin-top:10px;width:max-content}
.matrix{display:grid;grid-template-columns:150px repeat(6,1fr);gap:8px;align-items:center;margin-top:24px}
.matrix .cell{display:flex;justify-content:center;padding:8px;border-radius:8px}
</style></head><body><div class="sheet"><h1>${title}</h1>${body}</div></body></html>`;

(async () => {
  const b = await chromium.launch();
  const pg = await b.newPage({ viewport: { width: 1244, height: 900 }, deviceScaleFactor: 1.5 });

  // 1. Palettes
  let body = '<p class="sub">Six colour schemes for the CoWork strip. Each shows its colours and a small preview of the panel.</p><div class="grid">';
  for (const [i, p] of palettes.entries()) {
    body += `<div class="card" style="background:${p.back}"><div class="sw">` +
      ['back', 'card', 'rail', 'accent', 'text', 'dim'].map(k => `<div style="background:${p[k]};color:${k === 'accent' ? p.accentText : k === 'text' ? p.back : p.text}">${p[k]}</div>`).join('') +
      `</div><div class="mock"><div class="fly" style="color:${p.text}">
        <div style="display:flex;gap:7px;align-items:center;margin-bottom:9px;font-weight:600">${svg(logos[0], p, 14)} CoWork</div>
        <div class="row" style="background:${p.card}"><b style="color:#F5A35C">Sam</b> moved the kerb line</div>
        <div class="row" style="background:${p.mine};border-left:3px solid ${p.accent}"><b style="color:#5CC9F5">Alex</b> nice, updating sections</div>
        <span class="btn" style="background:${p.accent};color:${p.accentText}">Join CoWork</span>
        <span class="btn" style="border:1px solid ${p.accent};color:${p.accent};margin-left:6px">Message</span>
      </div><div class="rail" style="background:${p.rail}"><i style="background:${p.accent}"></i><i style="background:${p.card}"></i><i style="background:${p.card}"></i><i style="background:${p.card}"></i></div></div>
      <div class="label" style="background:${p.rail};color:${p.text}"><b>${i + 1}. ${p.name}</b><span style="color:${p.dim};font:11px Consolas">${p.id}</span></div></div>`;
  }
  body += '</div>';
  await pg.setContent(page('CoWork colour palettes', body));
  await pg.locator('.sheet').screenshot({ path: path.join(out, 'palettes.png') });

  // 2. Logos (in palette 1), with real-size 16 px and how it looks in the status bar; then every logo in every palette
  const p0 = palettes[0];
  body = '<p class="sub">Six logo ideas, each drawn on a 16 x 16 grid so it stays sharp as the status-bar icon. Shown big, as the actual 16 px pixels (zoomed), at real size in the status bar, then in every palette.</p><div class="lg">';
  for (const [i, l] of logos.entries()) {
    body += `<div class="lcard"><div class="big">${svg(l, p0, 96, true)}<img class="px" id="px-${l.id}"></div>
      <b>${String.fromCharCode(65 + i)}. ${l.name}</b><div style="color:#9AA4AA;font-size:12.5px;margin-top:3px">${l.note}</div>
      <div class="bar">MODEL ⊞ 1:500 ${svg(l, p0, 16)}</div></div>`;
  }
  body += '</div><div class="matrix"><div></div>' + palettes.map((p, i) => `<div style="text-align:center;font-size:11.5px;color:#9AA4AA">${i + 1}. ${p.name}</div>`).join('');
  for (const [i, l] of logos.entries())
    body += `<div><b>${String.fromCharCode(65 + i)}. ${l.name}</b></div>` + palettes.map(p => `<div class="cell" style="background:${p.back}">${svg(l, p, 40, true)}</div>`).join('');
  body += '</div>';
  await pg.setContent(page('CoWork logo ideas', body));

  // Make the real 16 px bitmaps in the page (canvas), show them zoomed, and save every logo x palette at 16/24/32 + .ico
  const icons = await pg.evaluate(async ({ palettes, logos, svgs }) => {
    const res = {};
    for (const key of Object.keys(svgs)) {
      res[key] = {};
      for (const size of [16, 24, 32]) {
        const img = new Image();
        img.src = 'data:image/svg+xml;base64,' + btoa(svgs[key].replaceAll('SIZE', size));
        await img.decode();
        const c = document.createElement('canvas'); c.width = c.height = size;
        c.getContext('2d').drawImage(img, 0, 0, size, size);
        res[key][size] = c.toDataURL('image/png');
      }
      const [logo, pal] = key.split('--');
      if (pal === palettes[0].id) document.getElementById('px-' + logo).src = res[key][16];
    }
    return res;
  }, {
    palettes: palettes.map(p => ({ id: p.id })), logos: logos.map(l => ({ id: l.id })),
    svgs: Object.fromEntries(logos.flatMap(l => palettes.map(p => [`${l.id}--${p.id}`,
      `<svg xmlns="http://www.w3.org/2000/svg" width="SIZE" height="SIZE" viewBox="0 0 16 16">${l.svg(p.accent, p.accentText)}</svg>`]))),
  });
  await pg.waitForTimeout(100);
  await pg.locator('.sheet').screenshot({ path: path.join(out, 'logos.png') });

  for (const [key, sizes] of Object.entries(icons)) {
    const pngs = [16, 24, 32].map(s => Buffer.from(sizes[s].split(',')[1], 'base64'));
    fs.writeFileSync(path.join(out, 'icons', key + '.png'), pngs[0]);
    // .ico holding the three PNGs (Windows reads PNG-in-ICO)
    const head = Buffer.alloc(6); head.writeUInt16LE(0, 0); head.writeUInt16LE(1, 2); head.writeUInt16LE(3, 4);
    let offset = 6 + 16 * 3; const dir = [];
    [16, 24, 32].forEach((s, i) => {
      const e = Buffer.alloc(16); e.writeUInt8(s, 0); e.writeUInt8(s, 1); e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6);
      e.writeUInt32LE(pngs[i].length, 8); e.writeUInt32LE(offset, 12); offset += pngs[i].length; dir.push(e);
    });
    fs.writeFileSync(path.join(out, 'icons', key + '.ico'), Buffer.concat([head, ...dir, ...pngs]));
  }
  await b.close();
})();
