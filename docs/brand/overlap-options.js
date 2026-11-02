// Revised "Overlap" ideas (the first one looked like an eye). Same 16 x 16 grid.
module.exports = [
  { id: "rings", name: "A1. Linked rings", note: "Two outlined rings linked together - no filled middle, so no eye.",
    svg: (a, k) => `<circle cx="5.6" cy="8" r="4.1" fill="none" stroke="${a}" stroke-width="2.2"/><circle cx="10.4" cy="8" r="4.1" fill="none" stroke="${a}" stroke-width="2.2" stroke-opacity=".6"/>` },
  { id: "diagonal", name: "A2. Diagonal pair", note: "Two circles stacked corner to corner, the overlap bright instead of dark.",
    svg: (a, k) => `<circle cx="6" cy="6" r="4.6" fill="${a}" fill-opacity=".55"/><circle cx="10" cy="10" r="4.6" fill="${a}"/><path d="M5.94 10.6 A4.6 4.6 0 0 1 10.6 5.94 A4.6 4.6 0 0 1 5.94 10.6Z" fill="#FFFFFF" fill-opacity=".85"/>` },
  { id: "people", name: "A3. Two people", note: "Two head-and-shoulders shapes side by side: the team.",
    svg: (a, k) => `<circle cx="5" cy="5" r="2.6" fill="${a}" fill-opacity=".6"/><path d="M0.6 14 C0.6 10.4 2.6 8.6 5 8.6 C7.4 8.6 9.4 10.4 9.4 14Z" fill="${a}" fill-opacity=".6"/><circle cx="11" cy="5.6" r="2.8" fill="${a}"/><path d="M6.2 15 C6.2 11.2 8.4 9.3 11 9.3 C13.6 9.3 15.8 11.2 15.8 15Z" fill="${a}"/>` },
  { id: "tile", name: "A4. Overlap tile", note: "The overlap idea on a solid tile, so it fills the corner like the other icons.",
    svg: (a, k) => `<rect x="0" y="0" width="16" height="16" rx="3.5" fill="${a}"/><circle cx="6.2" cy="8" r="3.8" fill="none" stroke="${k}" stroke-width="1.8"/><circle cx="9.8" cy="8" r="3.8" fill="none" stroke="${k}" stroke-width="1.8"/>` },
];
