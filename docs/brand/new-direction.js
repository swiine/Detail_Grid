// Logo ideas, round 2: no circles, no people. CAD and drafting shapes on the 16 x 16 grid.
module.exports = [
  { id: "pointers", name: "G. Two pointers", note: "Two mouse pointers, one behind the other: two people in the same drawing at once.",
    svg: (a, k) => `<path d="M1.5 1 V10.6 L4 8.3 L5.8 12.2 L7.4 11.5 L5.7 7.7 H9 Z" fill="${a}" fill-opacity=".5"/><path d="M6.5 4 V13.6 L9 11.3 L10.8 15.2 L12.4 14.5 L10.7 10.7 H14 Z" fill="${a}" stroke="${k}" stroke-width=".9" stroke-linejoin="round"/>` },
  { id: "sync", name: "H. Sync arrows", note: "Two arrows passing each other on a tile: changes going both ways, live.",
    svg: (a, k) => `<rect width="16" height="16" rx="3.5" fill="${a}"/><path fill="${k}" d="M2.5 4.3 H9.6 V2 L13.6 5.2 L9.6 8.4 V6.1 H2.5 Z M13.5 9.9 H6.4 V7.6 L2.4 10.8 L6.4 14 V11.7 H13.5 Z"/>` },
  { id: "sheets", name: "I. Stacked sheets", note: "Two drawing sheets with a folded corner: the sheets you share.",
    svg: (a, k) => `<path d="M1.5 1.5 H8 L10.5 4 V12.5 H1.5 Z" fill="none" stroke="${a}" stroke-width="1.4" stroke-opacity=".6" stroke-linejoin="round"/><path d="M5.5 3.5 H11.5 L14.5 6.5 V15 H5.5 Z" fill="${a}"/><path d="M11.5 3.5 V6.5 H14.5" fill="none" stroke="${k}" stroke-width="1" stroke-linejoin="round"/><rect x="7.4" y="9" width="5.2" height="1.3" fill="${k}"/><rect x="7.4" y="11.6" width="3.8" height="1.3" fill="${k}"/>` },
  { id: "pencils", name: "J. Crossed pencils", note: "Two pencils crossing: drafting together.",
    svg: (a, k) => `<g transform="translate(2 2) rotate(45)"><rect x="0" y="-1.35" width="13.2" height="2.7" fill="${a}" fill-opacity=".55"/><path d="M13.2 -1.35 L16.6 0 L13.2 1.35 Z" fill="${a}" fill-opacity=".55"/></g><g transform="translate(2 14) rotate(-45)"><rect x="0" y="-1.35" width="13.2" height="2.7" fill="${a}"/><path d="M13.2 -1.35 L16.6 0 L13.2 1.35 Z" fill="${a}"/><rect x="2.2" y="-1.35" width="1" height="2.7" fill="${k}"/><path d="M15.4 -0.5 L16.6 0 L15.4 0.5 Z" fill="${k}"/></g>` },
  { id: "diamonds", name: "K. Diamond pair", note: "Two diamonds overlapping: two pieces of work meeting, with sharp corners.",
    svg: (a, k) => `<path d="M6 1 L11 6 L6 11 L1 6 Z" fill="${a}" fill-opacity=".5"/><path d="M10 5 L15 10 L10 15 L5 10 Z" fill="${a}" stroke="${k}" stroke-width=".9" stroke-linejoin="round"/>` },
  { id: "grid", name: "L. Shared grid", note: "A drawing grid on a tile with one cell filled in: the part someone's working on.",
    svg: (a, k) => `<rect width="16" height="16" rx="3.5" fill="${a}"/><g fill="${k}"><rect x="5.3" y="2.5" width="1.2" height="11"/><rect x="9.5" y="2.5" width="1.2" height="11"/><rect x="2.5" y="5.3" width="11" height="1.2"/><rect x="2.5" y="9.5" width="11" height="1.2"/><rect x="6.5" y="6.5" width="3" height="3"/></g>` },
];
