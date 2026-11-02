// CoWork's colour schemes and the Interlock logo (kept in step with src/CoWork.Core/Brand.cs).
const palettes = [
  { id: "mint", name: "Slate & Mint", back: "#1E2A30", card: "#26363D", text: "#EAF2EF", dim: "#93A8A3", accent: "#3DDC97", accentText: "#0D2A1F", rail: "#172126", mine: "#21463C", edge: "#3A4F57", field: "#141D21", good: "#5CC98A", warn: "#F07F72", accent2: "#4A9CFF" },
  { id: "amber", name: "Midnight & Amber", back: "#1B2030", card: "#252B3D", text: "#F1EFE8", dim: "#9EA3B5", accent: "#F2A541", accentText: "#2A1C05", rail: "#141826", mine: "#3A3222", edge: "#3A4258", field: "#121622", good: "#5CC98A", warn: "#F07F72", accent2: "#5EC8FF" },
  { id: "coral", name: "Graphite & Coral", back: "#232427", card: "#2E3034", text: "#F2F2F0", dim: "#A3A5AA", accent: "#FF6B5B", accentText: "#FFFFFF", rail: "#1A1B1D", mine: "#4A2E2B", edge: "#45484E", field: "#18191B", good: "#5CC98A", warn: "#F5B946", accent2: "#36C2C2" },
  { id: "sand", name: "Deep Teal & Sand", back: "#10353A", card: "#164349", text: "#F4EEDF", dim: "#9DB9B6", accent: "#E9C46A", accentText: "#10353A", rail: "#0B2A2E", mine: "#1F5157", edge: "#2C6168", field: "#0A2629", good: "#7FD1A6", warn: "#F08C7E", accent2: "#E76F51" },
  { id: "violet", name: "Ink & Violet", back: "#1D1B2B", card: "#28253A", text: "#EEEAF7", dim: "#A49FBC", accent: "#9B8CFF", accentText: "#15122A", rail: "#161422", mine: "#352F55", edge: "#3E3A57", field: "#141220", good: "#5CC98A", warn: "#F07F72", accent2: "#FF7AC6" },
  { id: "survey", name: "Survey Orange", back: "#24282B", card: "#2F3438", text: "#F3F1EC", dim: "#A6ABAE", accent: "#FF8C1A", accentText: "#1E1300", rail: "#1B1E20", mine: "#4A3520", edge: "#474D52", field: "#1A1D1F", good: "#5CC98A", warn: "#F07F72", accent2: "#4A9CFF" },
  { id: "civil3d", name: "Civil 3D", back: "#3B4453", card: "#454F61", text: "#F2F4F7", dim: "#A7B1BF", accent: "#0696D7", accentText: "#FFFFFF", rail: "#2E3440", mine: "#2D4F6E", edge: "#5B6678", field: "#2B313C", good: "#5FBF7F", warn: "#E0675A", accent2: "#F2A541" },
];
// The 16 x 16 pixel-art Interlock: a = accent, A = accent shadow, c = accent2, C = accent2 shadow, b = tile, . = clear.
const interlockPixels = [
  "................",
  ".bbbbbbbbbbbbbb.",
  "bbbbbbbbbbbbbbbb",
  "bbbaaaaaabbbbbbb",
  "bbbaaaaaaAbbbbbb",
  "bbbaaAAAAAbbbbbb",
  "bbbaaAbbbbbbbbbb",
  "bbbaaAbbbbbccbbb",
  "bbbaaAbbbbbccCbb",
  "bbbbAAbbbbbccCbb",
  "bbbbbbbbbbbccCbb",
  "bbbbbbbccccccCbb",
  "bbbbbbbccccccCbb",
  "bbbbbbbbCCCCCCbb",
  ".bbbbbbbbbbbbbb.",
  "................",
];
const shade = (hex, f = 0.6) => { const n = parseInt(hex.slice(1), 16); const h = v => Math.floor(v * f).toString(16).padStart(2, "0").toUpperCase(); return "#" + h(n >> 16 & 255) + h(n >> 8 & 255) + h(n & 255); };
const pixelColour = (ch, p) => ({ a: p.accent, A: shade(p.accent), c: p.accent2, C: shade(p.accent2), b: p.rail })[ch] || null;
// The smooth 3D version (64 grid) for big app icons.
const interlockSvg = (p, id = p.id) => `<defs>
  <linearGradient id="t-${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${p.card}"/><stop offset="1" stop-color="${p.rail}"/></linearGradient>
  <linearGradient id="g-${id}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".16"/><stop offset=".5" stop-color="#fff" stop-opacity="0"/></linearGradient>
  <linearGradient id="a-${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${p.accent}"/><stop offset="1" stop-color="${shade(p.accent, .82)}"/></linearGradient>
  <linearGradient id="c-${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${p.accent2}"/><stop offset="1" stop-color="${shade(p.accent2, .82)}"/></linearGradient>
  <filter id="s-${id}" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="2" stdDeviation="1.6" flood-color="#000" flood-opacity=".45"/></filter></defs>
  <rect width="64" height="64" rx="14" fill="url(#t-${id})"/><rect width="64" height="64" rx="14" fill="url(#g-${id})"/>
  <g filter="url(#s-${id})">
  <polygon points="12,12 40,12 40,21.2 21.2,21.2 21.2,40 12,40" transform="translate(2.4 2.4)" fill="${shade(p.accent)}"/>
  <polygon points="12,12 40,12 40,21.2 21.2,21.2 21.2,40 12,40" fill="url(#a-${id})"/>
  <polygon points="52,52 24,52 24,42.8 42.8,42.8 42.8,24 52,24" transform="translate(2.4 2.4)" fill="${shade(p.accent2)}"/>
  <polygon points="52,52 24,52 24,42.8 42.8,42.8 42.8,24 52,24" fill="url(#c-${id})"/></g>`;
module.exports = { palettes, interlockPixels, shade, pixelColour, interlockSvg };
