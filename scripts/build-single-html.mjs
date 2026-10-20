// Packs the `expo export --platform web` output (dist/) into one self-contained HTML file:
// every script is inlined and every asset (fonts, images) becomes a data: URI.
// Usage: npm run build:html  →  web-preview/solos-ascent.html
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, extname, join } from 'node:path';

const DIST = 'dist';
const OUT = process.argv[2] ?? 'web-preview/solos-ascent.html';

const MIME = {
  '.ttf': 'font/ttf',
  '.otf': 'font/otf',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
};

const dataUri = (path) => {
  const mime = MIME[extname(path)] ?? 'application/octet-stream';
  return `data:${mime};base64,${readFileSync(join(DIST, path)).toString('base64')}`;
};

// The router changes the URL when it redirects. Opened from file:// or a sub-path, the
// browser may refuse that; fall back to keeping the current URL instead of crashing.
const BOOT = `<script>
(function () {
  ['pushState', 'replaceState'].forEach(function (k) {
    var orig = history[k].bind(history);
    history[k] = function (s, t, u) {
      try { return orig(s, t, u); } catch (e) { try { return orig(s, t); } catch (_) {} }
    };
  });
})();
</script>`;

let html = readFileSync(join(DIST, 'index.html'), 'utf8');
let inlinedAssets = 0;

html = html.replace(/<script src="([^"]+)"[^>]*><\/script>/g, (_, src) => {
  let js = readFileSync(join(DIST, src), 'utf8');
  js = js.replace(/"(\/assets\/[^"]+)"/g, (m, p) => {
    inlinedAssets++;
    return JSON.stringify(dataUri(p));
  });
  // Keep the bundle from closing the <script> tag early.
  return `<script>${js.replace(/<\/script/gi, '<\\/script')}</script>`;
});

html = html.replace(/<link rel="icon" href="([^"]+)"\s*\/?>/, (_, href) => `<link rel="icon" href="${dataUri(href)}">`);
html = html.replace('<body>', `<body style="background:#08080F">\n${BOOT}`);

mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, html);
console.log(`Wrote ${OUT} (${(html.length / 1024 / 1024).toFixed(2)} MB, ${inlinedAssets} assets inlined)`);
