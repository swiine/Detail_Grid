// CoWork palettes and logos. Each logo is drawn on a 16 x 16 grid so it stays sharp as a status-bar icon.
const palettes = [
  { id: "mint",   name: "Slate & Mint",     back: "#1E2A30", card: "#26363D", text: "#EAF2EF", dim: "#93A8A3", accent: "#3DDC97", accentText: "#0D2A1F", rail: "#172126", mine: "#21463C", edge: "#3A4F57" },
  { id: "amber",  name: "Midnight & Amber", back: "#1B2030", card: "#252B3D", text: "#F1EFE8", dim: "#9EA3B5", accent: "#F2A541", accentText: "#2A1C05", rail: "#141826", mine: "#3A3222", edge: "#3A4258" },
  { id: "coral",  name: "Graphite & Coral", back: "#232427", card: "#2E3034", text: "#F2F2F0", dim: "#A3A5AA", accent: "#FF6B5B", accentText: "#FFFFFF", rail: "#1A1B1D", mine: "#4A2E2B", edge: "#45484E" },
  { id: "sand",   name: "Deep Teal & Sand", back: "#10353A", card: "#164349", text: "#F4EEDF", dim: "#9DB9B6", accent: "#E9C46A", accentText: "#10353A", rail: "#0B2A2E", mine: "#1F5157", edge: "#2C6168" },
  { id: "violet", name: "Ink & Violet",     back: "#1D1B2B", card: "#28253A", text: "#EEEAF7", dim: "#A49FBC", accent: "#9B8CFF", accentText: "#15122A", rail: "#161422", mine: "#352F55", edge: "#3E3A57" },
  { id: "survey", name: "Survey Orange",    back: "#24282B", card: "#2F3438", text: "#F3F1EC", dim: "#A6ABAE", accent: "#FF8C1A", accentText: "#1E1300", rail: "#1B1E20", mine: "#4A3520", edge: "#474D52" },
];
// Each logo: (accent, ink) => SVG body in a 16 x 16 box. "ink" is the colour drawn on top of the accent.
const logos = [
  { id: "overlap", name: "Overlap", note: "Two circles sharing the middle: two people, one drawing.",
    svg: (a, k) => `<circle cx="6" cy="8" r="5" fill="${a}"/><circle cx="10" cy="8" r="5" fill="${a}" fill-opacity=".55"/><path d="M8 3.68 A5 5 0 0 1 8 12.32 A5 5 0 0 1 8 3.68Z" fill="${k}" fill-opacity=".85"/>` },
  { id: "crosshairs", name: "Two cursors", note: "Two crosshairs on a tile: CoWork's live sessions.",
    svg: (a, k) => `<rect x="0" y="0" width="16" height="16" rx="3.5" fill="${a}"/><g fill="${k}"><rect x="4" y="2" width="1.6" height="7"/><rect x="1.3" y="4.7" width="7" height="1.6"/><rect x="10.4" y="7" width="1.6" height="7"/><rect x="7.7" y="9.7" width="7" height="1.6"/></g>` },
  { id: "linked", name: "Linked squares", note: "Two overlapping frames: sheets that are worked on together.",
    svg: (a, k) => `<rect x="1.2" y="1.2" width="8.6" height="8.6" rx="1.2" fill="none" stroke="${a}" stroke-width="2.2"/><rect x="6.2" y="6.2" width="8.6" height="8.6" rx="1.2" fill="${a}"/><rect x="6.2" y="6.2" width="3.6" height="3.6" fill="${k}" fill-opacity=".9"/>` },
  { id: "bubble", name: "Chat + cursor", note: "A speech bubble with a crosshair inside: chat about the drawing.",
    svg: (a, k) => `<path d="M2.5 1.5 H13.5 A1.5 1.5 0 0 1 15 3 V10.5 A1.5 1.5 0 0 1 13.5 12 H7 L3.5 15 V12 H2.5 A1.5 1.5 0 0 1 1 10.5 V3 A1.5 1.5 0 0 1 2.5 1.5Z" fill="${a}"/><g fill="${k}"><rect x="7.2" y="3.2" width="1.6" height="7.2"/><rect x="4.4" y="6" width="7.2" height="1.6"/></g>` },
  { id: "nodes", name: "Team nodes", note: "Three connected points: the team, linked up.",
    svg: (a, k) => `<g stroke="${a}" stroke-width="1.6" stroke-linecap="round"><line x1="4" y1="4.5" x2="12" y2="4.5"/><line x1="4" y1="4.5" x2="8" y2="12"/><line x1="12" y1="4.5" x2="8" y2="12"/></g><circle cx="4" cy="4.5" r="2.6" fill="${a}"/><circle cx="12" cy="4.5" r="2.6" fill="${a}"/><circle cx="8" cy="12" r="2.6" fill="${a}"/><circle cx="8" cy="12" r="1.1" fill="${k}"/>` },
  { id: "monogram", name: "CW monogram", note: "Bold CW letters on a tile: simple and readable at 16 px.",
    svg: (a, k) => `<rect x="0" y="0" width="16" height="16" rx="3.5" fill="${a}"/><path fill="${k}" d="M2 4h4v1.8H3.8v4.4H6V12H2z M7 4h1.7l.6 5 .9-3h1.6l.9 3 .6-5H15l-1.3 8h-1.8l-1-3.2-1 3.2H8.3z"/>` },
];
module.exports = { palettes, logos };
