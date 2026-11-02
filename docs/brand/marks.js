// Round 3: six polished marks. Bold abstract shapes with 3D gradients (big app icon, 64 grid),
// each with its own pixel-art cut for the 16 x 16 status-bar icon. No circles, no people.
const tile = (id, c1, c2) => `<defs><linearGradient id="bg-${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></linearGradient>
<linearGradient id="gloss-${id}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".18"/><stop offset=".5" stop-color="#fff" stop-opacity="0"/></linearGradient>
<filter id="sh-${id}" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="2.2" stdDeviation="1.8" flood-color="#000" flood-opacity=".45"/></filter></defs>
<rect width="64" height="64" rx="14" fill="url(#bg-${id})"/><rect width="64" height="64" rx="14" fill="url(#gloss-${id})"/>`;
const grad = (id, c1, c2, x2 = 1, y2 = 1) => `<linearGradient id="${id}" x1="0" y1="0" x2="${x2}" y2="${y2}"><stop offset="0" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></linearGradient>`;
const poly = (pts, fill) => `<polygon points="${pts}" fill="${fill}"/>`;
const px = (w, rows, colours) => rows.map((r, y) => [...r].map((ch, x) => ch === '.' ? '' : `<rect x="${x}" y="${y}" width="1" height="1" fill="${colours[ch]}"/>`).join('')).join('');

module.exports = [
  {
    id: "fold", name: "1. Fold",
    meaning: "A C folded out of a drawing sheet: three bevelled faces, like a sheet with its edge turned over.",
    big: tile("fold", "#1B1F3B", "#3B2160") + `<defs>${grad("f1", "#7CF5C6", "#2FD69A")}${grad("f2", "#2FB8A0", "#1C8C86", 0, 1)}${grad("f3", "#167070", "#0F4F5A")}</defs>
      <g filter="url(#sh-fold)">${poly("14,13 51,13 44,24 25,24", "url(#f1)")}${poly("14,13 25,24 25,40 14,51", "url(#f2)")}${poly("14,51 25,40 44,40 51,51", "url(#f3)")}</g>`,
    small: px(16, [
      "................",
      ".bbbbbbbbbbbbbb.",
      "bbbbbbbbbbbbbbbb",
      "bbbmmmmmmmmmmbbb",
      "bbbtmmmmmmmmbbbb",
      "bbbttmmmmmmbbbbb",
      "bbbttbbbbbbbbbbb",
      "bbbttbbbbbbbbbbb",
      "bbbttbbbbbbbbbbb",
      "bbbttbbbbbbbbbbb",
      "bbbttddddddbbbbb",
      "bbbtddddddddbbbb",
      "bbbddddddddddbbb",
      "bbbbbbbbbbbbbbbb",
      ".bbbbbbbbbbbbbb.",
      "................"], { b: "#26214A", m: "#5CEBB4", t: "#22A394", d: "#13606A" }),
  },
  {
    id: "interlock", name: "2. Interlock",
    meaning: "Two thick brackets that slot together into one square: two people's work making one drawing.",
    big: tile("interlock", "#101826", "#1E2D44") + `<defs>${grad("i1", "#FFC15E", "#FF7A3D")}${grad("i2", "#5EC8FF", "#3D6BFF")}</defs>
      <g filter="url(#sh-interlock)">${poly("11,11 41,11 41,21 21,21 21,41 11,41", "#B4521F")}${poly("11,9 41,9 41,19 21,19 21,39 11,39", "url(#i1)")}
      ${poly("53,53 23,53 23,43 43,43 43,23 53,23", "#2440A8")}${poly("53,51 23,51 23,41 43,41 43,21 53,21", "url(#i2)")}</g>`,
    small: px(16, [
      "................",
      ".bbbbbbbbbbbbbb.",
      "bbbbbbbbbbbbbbbb",
      "bbooooooobbbbbbb",
      "bboooooooobbbbbb",
      "bboobbbbbbbbbbbb",
      "bboobbbbbbbbssbb",
      "bboobbbbbbbbssbb",
      "bboobbbbbbbbssbb",
      "bboobbbbbbbbssbb",
      "bbbbbbbbbbbbssbb",
      "bbbbbbsssssssssb",
      "bbbbbbbssssssssb",
      "bbbbbbbbbbbbbbbb",
      ".bbbbbbbbbbbbbb.",
      "................"], { b: "#16233A", o: "#FFA14C", s: "#4A9CFF" }),
  },
  {
    id: "block", name: "3. Block drop",
    meaning: "An isometric block with a smaller piece dropping onto it: a change landing in the shared drawing, live.",
    big: tile("block", "#0E2A2E", "#173F4A") + `<defs>${grad("b1", "#9AF0FF", "#4CC9E6")}${grad("b2", "#2E9BB8", "#1F7392", 0, 1)}${grad("b3", "#165A73", "#0F3F55", 0, 1)}${grad("b4", "#FFE27A", "#FFB938")}</defs>
      <g filter="url(#sh-block)">${poly("30,24 46,33 30,42 14,33", "url(#b1)")}${poly("14,33 30,42 30,56 14,47", "url(#b2)")}${poly("30,42 46,33 46,47 30,56", "url(#b3)")}
      ${poly("44,7 52,11.5 44,16 36,11.5", "url(#b4)")}${poly("36,11.5 44,16 44,23 36,18.5", "#E09A1E")}${poly("44,16 52,11.5 52,18.5 44,23", "#B8770F")}</g>
      <rect x="43.3" y="25" width="1.4" height="4" fill="#FFD25A" opacity=".7"/><rect x="43.3" y="30.5" width="1.4" height="2" fill="#FFD25A" opacity=".45"/>`,
    small: px(16, [
      "................",
      ".bbbbbbbbbbbyyb.",
      "bbbbbbbbbbbyyyyb",
      "bbbbbbbbbbbooqqb",
      "bbbbbbbbbbbooqqb",
      "bbbbbbbbbbbbbbbb",
      "bbbbbbcccbbbbbbb",
      "bbbbcccccccbbbbb",
      "bbbcccccccccbbbb",
      "bbbmmcccccddbbbb",
      "bbbmmmmcdddddbbb",
      "bbbmmmmmdddddbbb",
      "bbbbmmmmddddbbbb",
      "bbbbbbmmddbbbbbb",
      ".bbbbbbbbbbbbbb.",
      "................"], { b: "#12333A", c: "#7EE3F7", m: "#2584A3", d: "#124C63", y: "#FFDA62", o: "#E09A1E", q: "#B8770F" }),
  },
  {
    id: "slash", name: "4. Live C",
    meaning: "A bold square C cut by a sharp slash: CoWork, and the moment a change goes across.",
    big: tile("slash", "#1A1033", "#2B1450") + `<defs>${grad("s1", "#FF5FA2", "#FF9A3D")}</defs>
      <g filter="url(#sh-slash)">${poly("13,13 51,13 51,25 27,25 27,39 51,39 51,51 13,51", "url(#s1)")}</g>
      <polygon points="37,6 44,6 27,58 20,58" fill="url(#bg-slash)"/>`,
    small: px(16, [
      "................",
      ".bbbbbbbbbbbbbb.",
      "bbbbbbbbbbbbbbbb",
      "bbbpppppbboooobb",
      "bbbpppppbboooobb",
      "bbbppbbbbbbbbbbb",
      "bbbppbbbbbbbbbbb",
      "bbbppbbbbbbbbbbb",
      "bbbppbbbbbbbbbbb",
      "bbbppbbbbbbbbbbb",
      "bbbppbbbbbbbbbbb",
      "bbbpppbboooooobb",
      "bbbpppbboooooobb",
      "bbbbbbbbbbbbbbbb",
      ".bbbbbbbbbbbbbb.",
      "................"], { b: "#22123F", p: "#FF6A97", o: "#FF9446" }),
  },
  {
    id: "layers", name: "5. Layer stack",
    meaning: "Three isometric layers, the top one sliding into place: layers and sheets, kept in sync.",
    big: tile("layers", "#0F1B2D", "#14324A") + `<defs>${grad("l1", "#B5FF7A", "#4FE38A")}${grad("l2", "#3FC6A6", "#2A9C9C")}${grad("l3", "#2B7A9A", "#1E5878")}</defs>
      <g filter="url(#sh-layers)">${poly("32,38 52,48 32,58 12,48", "url(#l3)")}${poly("12,48 32,58 32,61 12,51", "#13405A")}${poly("32,58 52,48 52,51 32,61", "#0E3048")}
      ${poly("32,26 52,36 32,46 12,36", "url(#l2)")}${poly("12,36 32,46 32,49 12,39", "#1D6E70")}${poly("32,46 52,36 52,39 32,49", "#155456")}
      ${poly("36,9 56,19 36,29 16,19", "url(#l1)")}${poly("16,19 36,29 36,32 16,22", "#2E9E5E")}${poly("36,29 56,19 56,22 36,32", "#237A49")}</g>`,
    small: px(16, [
      "................",
      ".bbbbbbbbbbbbbb.",
      "bbbbbbbggggbbbbb",
      "bbbbggggggggggbb",
      "bbbbllllllllllbb",
      "bbbbbbbllllbbbbb",
      "bbbbbbttttbbbbbb",
      "bbbttttttttttbbb",
      "bbbnnnnnnnnnnbbb",
      "bbbbbbnnnnbbbbbb",
      "bbbbbbuuuubbbbbb",
      "bbbuuuuuuuuuubbb",
      "bbbvvvvvvvvvvbbb",
      "bbbbbbvvvvbbbbbb",
      ".bbbbbbbbbbbbbb.",
      "................"], { b: "#11263A", g: "#8EF57E", l: "#2E9E5E", t: "#3FC6A6", n: "#1E7F80", u: "#2B7A9A", v: "#174A66" }),
  },
  {
    id: "spark", name: "6. Spark",
    meaning: "A four-point spark with a crosshair gap at its centre: precise drafting, with energy.",
    big: tile("spark", "#2A1606", "#4A1F0C") + `<defs>${grad("k1", "#FFF08A", "#FF8A2B")}</defs>
      <g filter="url(#sh-spark)"><path d="M32 7 L37.5 26.5 L57 32 L37.5 37.5 L32 57 L26.5 37.5 L7 32 L26.5 26.5 Z" fill="url(#k1)"/></g>
      <rect x="29" y="29" width="6" height="6" fill="#4A1F0C"/><rect x="31.2" y="22" width="1.6" height="4" fill="#4A1F0C" opacity=".6"/><rect x="31.2" y="38" width="1.6" height="4" fill="#4A1F0C" opacity=".6"/>`,
    small: px(16, [
      "................",
      ".bbbbbbbbbbbbbb.",
      "bbbbbbbyybbbbbbb",
      "bbbbbbbyybbbbbbb",
      "bbbbbbyyyybbbbbb",
      "bbbbbbyyyybbbbbb",
      "bbbbbyyyyyybbbbb",
      "byyyyyykkyyyyyyb",
      "booooookkoooooob",
      "bbbbboooooobbbbb",
      "bbbbbboooobbbbbb",
      "bbbbbboooobbbbbb",
      "bbbbbbboobbbbbbb",
      "bbbbbbboobbbbbbb",
      ".bbbbbbbbbbbbbb.",
      "................"], { b: "#3A1A09", y: "#FFE36E", o: "#FF9A33", k: "#3A1A09" }),
  },
];
