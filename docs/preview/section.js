// ---------- Wall section to the TTW CAD standard (TTW.json) ----------
// One drawing for the website, the report and the app. Drawn on "paper" in mm at a true scale, like a plotted detail:
// black linework (pen 0.25 outline, 0.18 hatch, 0.35 reinforcement), AR-CONC / EARTH / GRAVEL hatches,
// 2.5 mm upper-case text on leaders, mm dimensions with oblique ticks, Z break lines, 5 mm title + "SCALE 1:n".
const CAD = {
  pen: { outline: 0.25, hatch: 0.13, reo: 0.35, thin: 0.18, ground: 0.35, membrane: 0.5 },
  ink: "#111", hatchInk: "#555",
  text: 2.5, title: 5, dimText: 2.5, arrow: 2, breakSize: 5,
  barFormat: (db, s) => `N${db} @ ${s} c/c`,      // standard: {prefix}{diameter} @ {spacing} c/c (prefix N for AS/NZS 4671 500N bars)
  scaleFormat: n => `SCALE 1:${n}`,
  subgradeNote: "FOUNDATION COMPACTED TO 98% SMDD AT ±2% OMC",
};
const UC_DEPTH = { "150UC23": 0.152, "200UC46": 0.203, "250UC73": 0.254, "310UC97": 0.308 };

function sectionSvg(opts = {}) {
  const full = opts.mode !== "compact";
  if (firstProblem()) return `<svg viewBox="0 0 120 40" role="img" aria-label="Wall section"><rect width="120" height="40" fill="#fff"/><text x="60" y="21" text-anchor="middle" font-family="Arial" font-size="4" fill="#6F6A5D">Fix the red boxes to see the wall.</text></svg>`;
  const inp = inputs(), res = analyse(inp), reo = res.reo;
  const tanB = Math.tan(inp.slope * Math.PI / 180);

  // ----- geometry in metres (x from the front of the footing, y up from its underside) -----
  const G = { conc: [], block: [], steel: [], pads: [], bars: [], dots: [], ticks: [], blind: [], mem: [], dashed: [], weep: [] };
  let Ht, xFrontFace, groundFront, backTop, footBack, drain = null, pipe = null, wallTop, bottom = 0, frontUp = null, xFF0 = null;
  const levels = [];                                    // level markers: { x, y, text, side }
  const L = [], R = [];                                   // leader labels: { x, y, text }
  const up = s => s.toUpperCase();
  if (S.type === "cantilever") {
    const { toe, t1, t2, tb, heel, D } = inp; Ht = inp.H + D; const B = toe + t2 + heel, hs = Ht - tb;
    const hk = inp.key > 0 ? inp.key : 0, kw = Math.max(0.3, t2), ch = 0.025;      // shear key under the stem, 25 chamfers
    const outline = [[0, 0]];
    if (hk > 0) outline.push([toe, 0], [toe, -hk], [toe + kw, -hk], [toe + kw, 0]);
    outline.push([B, 0], [B, tb], [toe + t2, tb], [toe + t1, Ht - ch], [toe + t1 - ch, Ht], [toe + ch, Ht], [toe, Ht - ch], [toe, tb], [ch, tb], [0, tb - ch]);
    G.conc.push(outline);
    // 50 blinding under the footing (and under the key)
    const bl = 0.05;
    if (hk > 0) G.blind.push([[0, -bl], [toe, -bl], [toe, 0], [0, 0]], [[toe + kw, -bl], [B, -bl], [B, 0], [toe + kw, 0]], [[toe - bl, -hk - bl], [toe + kw + bl, -hk - bl], [toe + kw + bl, 0], [toe + kw, 0], [toe + kw, -hk], [toe, -hk], [toe, 0], [toe - bl, 0]]);
    else G.blind.push([[0, -bl], [B, -bl], [B, 0], [0, 0]]);
    bottom = -(hk > 0 ? hk + bl : bl);
    xFrontFace = toe; groundFront = D; backTop = toe + t1; footBack = B; wallTop = Ht;
    const backAt = y => toe + t1 + (t2 - t1) * (Ht - y) / hs;
    // Construction joint on a 75 kicker; waterproofing membrane down the back face and onto the heel
    G.dashed.push([[toe, tb + 0.075], [backAt(tb + 0.075), tb + 0.075]]);
    L.push({ x: toe + 0.01, y: tb + 0.075, text: "CONSTRUCTION JOINT ON 75 KICKER" });
    const m = 0.012;
    G.mem.push([[backAt(Ht - 0.1) + m, Ht - 0.1], [backAt(tb) + m, tb + m], [Math.min(B, backAt(tb) + 0.35), tb + m]]);
    R.push({ x: backAt(Ht * 0.85) + m, y: Ht * 0.85, text: "WATERPROOFING MEMBRANE WITH PROTECTION BOARD TO BACK FACE" });
    if (S.drained) {
      drain = [[backAt(tb) + 2 * m, tb + 2 * m], [backAt(tb) + 0.32, tb + 2 * m], [backAt(Ht - 0.3) + 0.32, Ht - 0.3], [backAt(Ht - 0.3) + 2 * m, Ht - 0.3]];
      pipe = [toe + t2 + 0.13, tb + 0.08];
      // Weep holes through the stem, just above the ground in front
      const yw = Math.max(D, tb + 0.15) + 0.1;
      if (yw < Ht - 0.2) { G.weep.push([[toe, yw], [backAt(yw), yw + 0.02]], [[toe, yw + 0.05], [backAt(yw + 0.05), yw + 0.07]]); L.push({ x: toe + 0.02, y: yw + 0.025, text: "Ø50 PVC WEEP HOLES @ 2000 c/c" }); }
    }
    levels.push({ x: toe, y: Ht, text: "TOP OF WALL", side: -1 });
    if (reo) {
      const c = inp.cover / 1000, lap = reo.lapA / 1000, s = reo.schedule, by = m => s.find(x => x.mark === m);
      const bA = y => backAt(y) - c;
      const sBot = hk > 0 ? -hk + c : c + 0.03;                                                        // starters run down into the key
      G.bars.push([[bA(Ht - c), Ht - c], [bA(tb + 0.02), tb + 0.02]]);                                  // A
      G.bars.push([[bA(tb + lap) - 0.02, tb + lap], [toe + t2 - c - 0.02, sBot], [Math.max(c, toe + t2 - c - 0.45), sBot]]); // S
      G.bars.push([[toe + c, Ht - c], [toe + c, c + 0.03], [toe + c + 0.3, c + 0.03]]);                   // B
      G.bars.push([[c, tb - c - 0.1], [c, tb - c], [B - c, tb - c], [B - c, tb - c - 0.12]]);             // E
      G.bars.push([[c, c + 0.12], [c, c], [B - c, c], [B - c, c + 0.12]]);                                // F
      if (hk > 0) G.bars.push([[toe + c, -0.02], [toe + c, -hk + c], [toe + kw - c, -hk + c]]);          // K: key front face
      for (let y = tb + 0.15; y < Ht - 0.05; y += 0.3) G.dots.push([toe + c + 0.016, y], [bA(y) - 0.016, y]);   // C
      for (let x = c + 0.15; x < B - c - 0.05; x += 0.3) G.dots.push([x, tb - c - 0.016], [x, c + 0.016]);     // G
      const lbl = (m, extra) => `${CAD.barFormat(...by(m).bars.replace("N", "").split("@").map(Number))} (${m}) ${extra}`;
      R.push({ x: bA(tb + hs * 0.7), y: tb + hs * 0.7, text: lbl("A", "VERT. BACK FACE") + (reo.cut ? `, ALT. BARS STOP ${Math.round(reo.cut * 1000)} ABOVE BASE` : "") });
      R.push({ x: bA(tb + lap * 0.6) - 0.02, y: tb + lap * 0.6, text: lbl("S", `STARTERS, LAP ${reo.lapA}`) });
      R.push({ x: bA(tb + hs * 0.45) - 0.016, y: tb + 0.15 + 0.3 * Math.round((hs * 0.45 - 0.15) / 0.3), text: lbl("C", "HORIZ. EACH FACE") });
      R.push({ x: toe + t2 + heel * 0.6, y: tb - c, text: lbl("E", "TOP") });
      R.push({ x: toe + t2 + heel * 0.35 + 0.0, y: c + 0.016, text: lbl("G", "LONGIT. TOP & BTM") });
      L.push({ x: toe + c, y: tb + hs * 0.55, text: lbl("B", "VERT. FRONT FACE") });
      L.push({ x: toe * 0.5, y: c, text: lbl("F", "BTM") });
      if (hk > 0) L.push({ x: toe + c, y: -hk * 0.5, text: `${Math.round(kw * 1000)} WIDE x ${Math.round(hk * 1000)} DEEP SHEAR KEY, ${lbl("K", "STARTERS CARRIED DOWN")}` });
      L.push({ x: toe + t1 * 0.5, y: Ht - 0.12, text: `N${inp.fc} CONCRETE, ${inp.cover} COVER, 25 CHAMFERS` });
    }
    L.push({ x: Math.min(0.3, toe * 0.4), y: -bl / 2, text: "50 THICK N15 BLINDING" });
  } else if (S.type === "post") {
    const { H, Dp, dia } = inp; Ht = Dp + H; wallTop = Ht;
    const d = UC_DEPTH[inp.post] ?? 0.2, xc = dia / 2;
    G.conc.push([[0, 0], [dia, 0], [dia, Dp], [0, Dp]]);
    G.steel.push([[xc - d / 2, 0.1], [xc + d / 2, 0.1], [xc + d / 2, Ht], [xc - d / 2, Ht]]);
    for (let y = Dp - 0.1; y < Ht - 0.01; y += 0.2) G.pads.push([[xc + d / 2, y], [xc + d / 2 + 0.075, y], [xc + d / 2 + 0.075, Math.min(Ht, y + 0.2)], [xc + d / 2, Math.min(Ht, y + 0.2)]]);
    xFrontFace = xc - d / 2; groundFront = Dp; backTop = xc + d / 2 + 0.075; footBack = Math.max(dia, backTop);
    if (S.drained) { drain = [[backTop, Dp], [backTop + 0.3, Dp], [backTop + 0.3, Ht - 0.3], [backTop, Ht - 0.3]]; pipe = [backTop + 0.15, Dp + 0.07]; }
    const n = reo?.schedule[0]?.bars ?? "6-N16";
    for (const x of [0.075, dia - 0.075]) G.bars.push([[x, Dp - 0.05], [x, 0.075]]);
    R.push({ x: xc + d / 2, y: Dp + H * 0.75, text: `${inp.post} STEEL POST @ ${Math.round(inp.s * 1000)} c/c, HOT-DIP GALVANISED` });
    R.push({ x: backTop - 0.03, y: Dp + H * 0.4, text: "75 THICK CONCRETE SLEEPERS BETWEEN POSTS" });
    L.push({ x: 0.075, y: Dp * 0.4, text: `Ø${Math.round(dia * 1000)} CONCRETE PIER, ${n} VERT. + R10 HELIX @ 200` });
  } else {
    const { Bg, Bt, D } = inp; Ht = inp.H + D; wallTop = Ht;
    let poly = [[0, 0], [Bg, 0], [Bg, Ht], [Bg - Bt, Ht]];
    if (S.type === "block") {
      // Segmental blocks step back one course (about 200 high) at a time.
      const n = Math.max(1, Math.round(Ht / 0.2)), hc = Ht / n, step = (Bg - Bt) / Math.max(1, n - 1), fx = i => Math.min(Bg - Bt, step * i);
      poly = [[Bg, 0], [Bg, Ht]];
      for (let i = n - 1; i >= 0; i--) poly.push([fx(i), (i + 1) * hc], [fx(i), i * hc]);
      G.conc.push([[-0.1, -0.15], [Bg + 0.1, -0.15], [Bg + 0.1, 0], [-0.1, 0]]);
      for (let i = 1; i < n; i++) G.ticks.push([[fx(i), i * hc], [Bg, i * hc]]);
      // The ground in front follows the steps above the ground line.
      const k = Math.min(n - 1, Math.floor(D / hc));
      frontUp = [[fx(k), (k + 1) * hc]];
      for (let i = k + 1; i < n; i++) frontUp.push([fx(i), i * hc], [fx(i), (i + 1) * hc]);
      xFF0 = fx(k);
    }
    (S.type === "block" ? G.block : G.conc).push(poly);
    xFrontFace = xFF0 ?? (Bg - Bt) * D / Ht; groundFront = D; backTop = Bg; footBack = Bg;
    if (S.drained) { drain = [[Bg, 0.05], [Bg + 0.3, 0.05], [Bg + 0.3, Ht - 0.3], [Bg, Ht - 0.3]]; pipe = [Bg + 0.15, 0.12]; }
    R.push({ x: Bg - Bt * 0.5, y: Ht * 0.7, text: S.type === "block" ? "SEGMENTAL RETAINING BLOCKS, CORES FILLED WITH GRAVEL" : "MASS CONCRETE N20" });
    if (S.type === "block") {
      L.push({ x: -0.05, y: -0.075, text: "150 THICK N20 CONCRETE LEVELLING PAD" });
      G.conc.push([[Bg - Bt - 0.02, Ht], [Bg, Ht], [Bg, Ht + 0.08], [Bg - Bt - 0.02, Ht + 0.08]]);
      L.push({ x: Bg - Bt * 0.6, y: Ht + 0.04, text: "CAPPING UNIT, ADHESIVE FIXED" });
      bottom = -0.15;
    } else {
      G.blind.push([[-0.05, -0.05], [Bg + 0.05, -0.05], [Bg + 0.05, 0], [-0.05, 0]]);
      L.push({ x: 0.05, y: -0.025, text: "50 THICK N15 BLINDING" });
      bottom = -0.05;
    }
    levels.push({ x: Bg - Bt, y: S.type === "block" ? Ht + 0.08 : Ht, text: "TOP OF WALL", side: -1 });
  }
  // Soil extents
  const xL = -Math.max(0.6, Ht * 0.25), xR = footBack + Math.max(0.9, Ht * 0.4), yBot = Math.min(-0.45, bottom - 0.3);
  const topAt = x => wallTop + Math.max(0, x - backTop) * tanB;
  if (drain) R.push({ x: (drain[0][0] + drain[1][0]) / 2, y: (drain[0][1] + drain[2][1]) * 0.55, text: "300 WIDE 20 mm FREE-DRAINING GRAVEL WRAPPED IN GEOTEXTILE" });
  if (pipe) R.push({ x: pipe[0] + 0.05, y: pipe[1], text: "Ø100 SLOTTED AG PIPE IN FILTER SOCK" });
  R.push({ x: xR - 0.25, y: topAt(xR - 0.25) - Math.min(0.6, Ht * 0.3), text: `SELECT BACKFILL φ' ${inp.phi}°, γ ${inp.gamma} kN/m³, IN 200 LAYERS COMPACTED TO 95% SMDD` });
  if (inp.q > 0) R.push({ x: xR - 0.15, y: topAt(xR - 0.15), text: `SURCHARGE ${inp.q} kPa` });
  L.push({ x: xL + 0.15, y: groundFront, text: "FINISHED GROUND LEVEL" });
  for (const lv of levels) L.push({ x: lv.x + 0.01, y: lv.y, text: lv.text });
  if (S.type !== "post") L.push({ x: Math.min(footBack * 0.5, 0.4), y: yBot * 0.5, text: `${CAD.subgradeNote}, ALLOWABLE BEARING ${inp.qa} kPa` });

  // ----- paper: mm at the chosen scale -----
  const geoW = xR - xL, geoH = topAt(xR) - yBot;
  const scales = [10, 20, 25, 50, 100];
  const scale = opts.scale || scales.find(s => geoW * 1000 / s <= (full ? 175 : 110) && geoH * 1000 / s <= 170) || 100;
  const fs = S.toeRight ? -1 : 1;                       // toe on the right: mirror the drawing, not the text
  const k = 1000 / scale, X = x => fs * x * k, Y = y => -y * k;
  const box = { x0: Infinity, y0: Infinity, x1: -Infinity, y1: -Infinity };
  const grow = (x, y) => { box.x0 = Math.min(box.x0, x); box.y0 = Math.min(box.y0, y); box.x1 = Math.max(box.x1, x); box.y1 = Math.max(box.y1, y); };
  const P = pts => pts.map(([x, y]) => { grow(X(x), Y(y)); return `${X(x).toFixed(2)},${Y(y).toFixed(2)}`; }).join(" ");
  const f = v => v.toFixed(2);
  const th = h => (h / 0.716).toFixed(2);                // cap height -> font size
  const tw = (s, h) => s.length * h * 0.8;              // rough width of upper-case Arial at cap height h
  let out = "";
  const line = (x1, y1, x2, y2, w, extra = "") => { grow(x1, y1); grow(x2, y2); return `<line x1="${f(x1)}" y1="${f(y1)}" x2="${f(x2)}" y2="${f(y2)}" stroke="${CAD.ink}" stroke-width="${w}" ${extra}/>`; };
  const text = (x, y, s, h, anchor = "start", extra = "") => {
    const w = tw(s, h), x0 = anchor === "end" ? x - w : anchor === "middle" ? x - w / 2 : x; grow(x0, y - h); grow(x0 + w, y + 0.5);
    return `<text x="${f(x)}" y="${f(y)}" font-size="${th(h)}" text-anchor="${anchor}" ${extra}>${esc(s)}</text>`;
  };
  const tick = (x, y) => line(x - 0.7, y + 0.7, x + 0.7, y - 0.7, CAD.pen.outline);

  // Soil (EARTH), front and back, one polygon under the ground profile
  // The outline runs up the wall's front face, so the whole wall sits inside it (as in the app and its DXF).
  const topFrontX = S.type === "cantilever" ? inp.toe : S.type === "post" ? xFrontFace : inp.Bg - inp.Bt;
  const soil = [[xL, yBot], [xL, groundFront], [xFrontFace, groundFront], ...(frontUp ?? [[topFrontX, wallTop]]), [backTop, wallTop], [xR, topAt(xR)], [xR, yBot]];
  out += `<polygon points="${P(soil)}" fill="url(#cad-earth-${scale})" stroke="none"/>`;
  // Concrete, blocks, sleepers, steel, drainage (each masks the soil under it)
  for (const p of G.blind) out += `<polygon points="${P(p)}" fill="#fff" stroke="none"/><polygon points="${P(p)}" fill="url(#cad-conc)" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}"/>`;
  for (const p of G.conc) out += `<polygon points="${P(p)}" fill="#fff" stroke="none"/><polygon points="${P(p)}" fill="url(#cad-conc)" stroke="${CAD.ink}" stroke-width="${CAD.pen.outline}"/>`;
  for (const p of G.block) out += `<polygon points="${P(p)}" fill="#fff" stroke="${CAD.ink}" stroke-width="${CAD.pen.outline}"/>`;
  for (const [[x1, y1], [x2, y2]] of G.ticks) out += line(X(x1), Y(y1), X(x2), Y(y2), CAD.pen.thin);
  if (drain) out += `<polygon points="${P(drain)}" fill="#fff" stroke="none"/><polygon points="${P(drain)}" fill="url(#cad-gravel)" stroke="${CAD.ink}" stroke-width="${CAD.pen.outline}" stroke-dasharray="2.4 0.8 0.5 0.8"/>`;   // geotextile
  for (const p of G.pads) out += `<polygon points="${P(p)}" fill="#fff" stroke="none"/><polygon points="${P(p)}" fill="url(#cad-conc)" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}"/>`;
  for (const p of G.steel) out += `<polygon points="${P(p)}" fill="url(#cad-steel)" stroke="${CAD.ink}" stroke-width="${CAD.pen.outline}"/>`;
  if (pipe) out += `<circle cx="${f(X(pipe[0]))}" cy="${f(Y(pipe[1]))}" r="${f(Math.max(0.05 * k, 0.8))}" fill="#fff" stroke="${CAD.ink}" stroke-width="${CAD.pen.outline}"/>`;
  // Reinforcement
  for (const p of G.mem) out += `<polyline points="${P(p)}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.membrane}" stroke-linejoin="round"/>`;
  for (const p of G.dashed) out += `<polyline points="${P(p)}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}" stroke-dasharray="1.2 0.8"/>`;
  for (const p of G.weep) out += `<polyline points="${P(p)}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}"/>`;
  for (const b of G.bars) out += `<polyline points="${P(b)}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.reo}" stroke-linejoin="round"/>`;
  for (const [x, y] of G.dots) out += `<circle cx="${f(X(x))}" cy="${f(Y(y))}" r="0.45" fill="${CAD.ink}"/>`;
  // Ground lines (heavier) and break lines at the soil cuts
  out += `<polyline points="${P([[xL, groundFront], [xFrontFace, groundFront]])}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.ground}"/>`;
  out += `<polyline points="${P([[backTop, wallTop], [xR, topAt(xR)]])}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.ground}"/>`;
  const zBreakV = (x, ya, yb) => { const ym = (ya + yb) / 2, s = CAD.breakSize / 2; return `<polyline points="${f(x)},${f(ya)} ${f(x)},${f(ym - s / 2)} ${f(x + s * 0.6)},${f(ym - s * 0.15)} ${f(x - s * 0.6)},${f(ym + s * 0.15)} ${f(x)},${f(ym + s / 2)} ${f(x)},${f(yb)}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}"/>`; };
  const zBreakH = (y, xa, xb) => { const xm = (xa + xb) / 2, s = CAD.breakSize / 2; return `<polyline points="${f(xa)},${f(y)} ${f(xm - s / 2)},${f(y)} ${f(xm - s * 0.15)},${f(y - s * 0.6)} ${f(xm + s * 0.15)},${f(y + s * 0.6)} ${f(xm + s / 2)},${f(y)} ${f(xb)},${f(y)}" fill="none" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}"/>`; };
  out += zBreakV(X(xL), Y(groundFront), Y(yBot)) + zBreakV(X(xR), Y(topAt(xR)), Y(yBot)) + zBreakH(Y(yBot), X(xL), X(xR));
  // Surcharge arrows
  if (inp.q > 0) for (let i = 0; i < 3; i++) { const x = backTop + (i + 0.6) * (xR - backTop) / 3.2, y = Y(topAt(x)); out += line(X(x), y - 7, X(x), y - 1.2, CAD.pen.thin) + `<path d="M${f(X(x) - 0.7)},${f(y - 2.6)} L${f(X(x))},${f(y - 0.6)} L${f(X(x) + 0.7)},${f(y - 2.6)} z" fill="${CAD.ink}"/>`; }

  const drawBox = { ...box };
  // ----- dimensions -----
  const dimH = (xa, xb, yObj, yDim, label) => {
    const a = X(xa), b = X(xb), yo = Y(yObj), yd = yDim, dir = yd > yo ? 1 : -1;
    return line(a, yo + dir * 1, a, yd + dir * 1.5, CAD.pen.thin) + line(b, yo + dir * 1, b, yd + dir * 1.5, CAD.pen.thin) + line(a - 1.5, yd, b + 1.5, yd, CAD.pen.thin) + tick(a, yd) + tick(b, yd) + text((a + b) / 2, yd - 0.9, label, CAD.dimText, "middle");
  };
  const dimV = (ya, yb, xObj, xDim, label) => {
    const a = Y(ya), b = Y(yb), xo = X(xObj), xd = xDim, dir = xd > xo ? 1 : -1, ym = (a + b) / 2;
    grow(xd - 4, ym); grow(xd + 4, ym);
    return line(xo + dir * 1, a, xd + dir * 1.5, a, CAD.pen.thin) + line(xo + dir * 1, b, xd + dir * 1.5, b, CAD.pen.thin) + line(xd, a + 1.5, xd, b - 1.5, CAD.pen.thin) + tick(xd, a) + tick(xd, b)
      + `<text x="${f(xd - 0.9)}" y="${f(ym)}" font-size="${th(CAD.dimText)}" text-anchor="middle" transform="rotate(-90 ${f(xd - 0.9)} ${f(ym)})">${label}</text>`;
  };
  const mm = v => String(Math.round(v * 1000));
  const yDims1 = drawBox.y1 + 7, yDims2 = drawBox.y1 + 14, xDimL = fs > 0 ? drawBox.x0 - 7 : drawBox.x1 + 7;
  if (S.type === "cantilever") {
    const { toe, t1, t2, tb, heel, D, H } = inp, B = toe + t2 + heel;
    out += dimH(0, toe, 0, yDims1, mm(toe)) + dimH(toe, toe + t2, 0, yDims1, mm(t2)) + (heel > 0 ? dimH(toe + t2, B, 0, yDims1, mm(heel)) : "") + dimH(0, B, 0, yDims2, mm(B));
    out += dimV(D, Ht, xFrontFace, xDimL, mm(H)) + dimV(0, D, 0, xDimL, mm(D));
    out += dimH(toe, toe + t1, Ht, Y(Ht) - 6, mm(t1));
    out += dimV(0, tb, B, X(B) + fs * 6, mm(tb));
  } else if (S.type === "post") {
    out += dimV(groundFront, Ht, xFrontFace, xDimL, mm(inp.H)) + dimV(0, groundFront, 0, xDimL, mm(inp.Dp)) + dimH(0, inp.dia, 0, yDims1, "Ø" + mm(inp.dia));
  } else {
    out += dimH(0, inp.Bg, 0, yDims1, mm(inp.Bg)) + dimH(inp.Bg - inp.Bt, inp.Bg, Ht, Y(Ht) - 6, mm(inp.Bt));
    out += dimV(groundFront, Ht, xFrontFace, xDimL, mm(inp.H)) + dimV(0, groundFront, 0, xDimL, mm(inp.D));
  }

  // ----- leaders and labels (two columns, outside the drawing) -----
  const leaderCol = (items, side) => {
    if (!items.length) return "";
    const colX = side > 0 ? box.x1 + 10 : box.x0 - 10, h = CAD.text, gap = 2.2, maxW = full ? 62 : 0;
    const placed = items.map(it => ({ ...it, tx: X(it.x), ty: Y(it.y), lines: wrap(it.text, Math.max(14, Math.floor(maxW / (h * 0.8)))) })).sort((a, b) => a.ty - b.ty);
    let cursor = -Infinity; let s = "";
    for (const it of placed) {
      const hh = it.lines.length * h * 1.6;
      const ly = Math.max(it.ty - hh / 2 + h, cursor + h + gap);
      cursor = ly + (it.lines.length - 1) * h * 1.6;
      const ex = colX - side * 3;                   // landing start
      const ang = Math.atan2(ly - it.ty, ex - it.tx), ah = CAD.arrow, aw = 0.6;
      const ax = it.tx + Math.cos(ang) * ah, ay = it.ty + Math.sin(ang) * ah;
      s += line(it.tx, it.ty, ex, ly - h / 2, CAD.pen.thin) + line(ex, ly - h / 2, colX - side * 0.8, ly - h / 2, CAD.pen.thin);
      s += `<path d="M${f(it.tx)},${f(it.ty)} L${f(ax - Math.sin(ang) * aw)},${f(ay + Math.cos(ang) * aw)} L${f(ax + Math.sin(ang) * aw)},${f(ay - Math.cos(ang) * aw)} z" fill="${CAD.ink}"/>`;
      it.lines.forEach((t, i) => { s += text(colX, ly + i * h * 1.6, t, h, side > 0 ? "start" : "end"); });
    }
    return s;
  };
  if (full) out += leaderCol(R, fs) + leaderCol(L, -fs);
  else {
    // compact: bar marks only, small circles next to the bars
    const marks = [...R.map(it => ({ ...it, side: 1 })), ...L.map(it => ({ ...it, side: -1 }))]
      .map(it => { const m = /\(([A-Z])\)/.exec(it.text); return m ? { ...it, m: m[1] } : null; }).filter(Boolean);
    for (const it of marks) { const cx = X(it.x) + fs * it.side * 6, cy = Y(it.y), mx = cx + fs * it.side * 2.2; out += line(X(it.x), cy, cx, cy, CAD.pen.thin) + `<circle cx="${f(mx)}" cy="${f(cy)}" r="2.2" fill="#fff" stroke="${CAD.ink}" stroke-width="${CAD.pen.thin}"/>` + text(mx, cy + 1, it.m, 2.1, "middle", 'font-weight="700"'); }
  }
  // ----- title -----
  if (full) {
    const tx = drawBox.x0, ty = box.y1 + 12, title = up(opts.title || `Typical wall section (${S.wall})`);
    out += text(tx, ty, title, CAD.title) + line(tx, ty + 1.6, tx + tw(title, CAD.title), ty + 1.6, CAD.pen.reo) + text(tx, ty + 7, CAD.scaleFormat(scale), CAD.text);
    // Notes, as on a drawing sheet
    const notes = sectionNotes(inp);
    let ny = ty + 16;
    out += text(tx, ny, "NOTES:", CAD.text, "start", 'font-weight="700"');
    notes.forEach((n, i) => wrap(n, 95).forEach((ln, j) => { ny += 4; out += text(tx + (j ? 5 : 0), ny, (j ? "" : `${i + 1}. `) + ln, CAD.text); }));
  }
  const m = 4, vb = [box.x0 - m, box.y0 - m, box.x1 - box.x0 + 2 * m, box.y1 - box.y0 + 2 * m];
  return `<svg class="cad" viewBox="${vb.map(f).join(" ")}" role="img" aria-label="Typical wall section, scale 1:${scale}" font-family="Arial, Helvetica, sans-serif" fill="${CAD.ink}" style="max-width:${full ? f(vb[2] * 3.78) + "px" : "none"}">
  <defs>
    ${earthPattern(scale)}
    <pattern id="cad-conc" width="9" height="9" patternUnits="userSpaceOnUse">
      <g fill="${CAD.hatchInk}"><circle cx="1.2" cy="1.5" r="0.18"/><circle cx="5.6" cy="0.9" r="0.14"/><circle cx="3.4" cy="4.2" r="0.2"/><circle cx="7.7" cy="5.1" r="0.15"/><circle cx="1.8" cy="7.4" r="0.16"/><circle cx="6.2" cy="7.9" r="0.2"/><circle cx="8.4" cy="2.6" r="0.12"/><circle cx="4.6" cy="6.3" r="0.12"/></g>
      <g fill="none" stroke="${CAD.hatchInk}" stroke-width="${CAD.pen.hatch}"><path d="M2.6,2.2 l0.9,-0.2 l-0.4,0.8 z"/><path d="M6.4,3.6 l0.7,0.5 l-0.9,0.3 z"/><path d="M3.9,8.1 l0.8,-0.5 l0.1,0.8 z"/><path d="M0.4,5 l0.7,0.1 l-0.4,0.6 z"/></g></pattern>
    <pattern id="cad-gravel" width="7" height="7" patternUnits="userSpaceOnUse">
      <g fill="none" stroke="${CAD.hatchInk}" stroke-width="${CAD.pen.hatch}"><path d="M0.6,0.8 l1.4,-0.3 l0.6,1 l-0.9,0.9 l-1.2,-0.4 z"/><path d="M4,1.2 l1.5,0.2 l0.3,1.2 l-1.3,0.5 l-0.8,-0.8 z"/><path d="M1.5,4 l1.2,-0.2 l0.7,1.1 l-1,0.9 l-1.2,-0.6 z"/><path d="M4.6,4.6 l1.3,0.1 l0.4,1 l-0.9,0.8 l-1.1,-0.5 z"/></g></pattern>
    <pattern id="cad-steel" width="1.6" height="1.6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><line x1="0" y1="0" x2="0" y2="1.6" stroke="${CAD.ink}" stroke-width="${CAD.pen.hatch}"/></pattern>
  </defs>
  <rect x="${f(vb[0])}" y="${f(vb[1])}" width="${f(vb[2])}" height="${f(vb[3])}" fill="#fff"/>${out}</svg>`;
}
// AutoCAD EARTH hatch, as defined in acad.pat (pattern units, 0.25 grid, dashes 0.25 on / 0.25 off):
//   0, 0,0, .25,.25, .25,-.25         90, .03125,.21875, .25,.25, .25,-.25
//   0, 0,.09375, .25,.25, .25,-.25    90, .125,.21875, .25,.25, .25,-.25
//   0, 0,.1875, .25,.25, .25,-.25     90, .21875,.21875, .25,.25, .25,-.25
// TTW.json "Soils" rule: EARTH, scale 0.5, angle 45. As in the TTW pavement tool, the scale is on paper
// (AutoCAD's metric EARTH x 0.5), so one repeat is 6.35 mm on paper at any drawing scale.
const EARTH_RULE = { scale: 0.5, angle: 45 };
function earthPattern(drawScale) {
  const unit = EARTH_RULE.scale * 25.4;                       // paper mm per pattern unit (12.7 mm repeat x 0.5)
  const w = (CAD.pen.hatch / unit).toFixed(4);                // keep the 0.18 pen width on paper
  const seg = (x1, y1, x2, y2) => `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}"/>`;
  // Pattern space is y-up in acad.pat; SVG is y-down, so mirror y (y -> 0.5 - y).
  const Y = y => +(0.5 - y).toFixed(5);
  let g = "";
  for (const y of [0, 0.09375, 0.1875]) { g += seg(0, Y(y), 0.25, Y(y)) + seg(0.25, Y(y + 0.25), 0.5, Y(y + 0.25)); }
  for (const x of [0.03125, 0.125, 0.21875]) {
    g += seg(x, Y(0.21875), x, Y(0.46875));
    const x2 = x + 0.25; g += seg(x2, Y(0.46875), x2, Y(0.5)) + seg(x2, Y(0), x2, Y(0.21875));
  }
  return `<pattern id="cad-earth-${drawScale}" width="0.5" height="0.5" patternUnits="userSpaceOnUse" patternTransform="rotate(${-EARTH_RULE.angle}) scale(${unit.toFixed(4)})"><g stroke="${CAD.hatchInk}" stroke-width="${w}" stroke-linecap="butt">${g}</g></pattern>`;
}
function sectionNotes(inp) {
  const n = ["ALL DIMENSIONS ARE IN MILLIMETRES UNLESS NOTED. DO NOT SCALE."];
  if (S.type === "cantilever") n.push(`CONCRETE N${inp.fc} TO AS 1379, ${inp.cover} COVER. REINFORCEMENT D500N TO AS/NZS 4671. LAPS AS SHOWN, OR 40 BAR DIAMETERS.`);
  if (S.type === "gravity") n.push("MASS CONCRETE N20 TO AS 1379, CAST IN LIFTS OF NO MORE THAN 1.0 m.");
  if (S.type === "block") n.push("BLOCKS TO THE MANUFACTURER'S DETAILS. CORES FILLED WITH 20 mm GRAVEL AS EACH COURSE IS LAID.");
  if (S.type === "post") n.push("STEEL POSTS GRADE 300, HOT-DIP GALVANISED TO AS/NZS 4680. PIERS N25 CONCRETE, 75 COVER.");
  n.push(`FOUNDATION TO BE INSPECTED BY THE ENGINEER BEFORE ${S.type === "post" ? "CONCRETING" : S.type === "block" ? "THE LEVELLING PAD IS POURED" : "BLINDING"}. CONFIRM AN ALLOWABLE BEARING OF ${inp.qa} kPa ON SITE.`);
  if (S.type === "cantilever") n.push("DO NOT BACKFILL UNTIL THE WALL HAS REACHED 75% OF ITS DESIGN STRENGTH (MINIMUM 7 DAYS).");
  n.push("BACKFILL IN 200 LAYERS. LIGHT HAND-HELD COMPACTION ONLY WITHIN 1.0 m OF THE WALL.");
  if (inp.drained) n.push("AG PIPE TO FALL AT 1:100 MIN. AND DISCHARGE TO THE STORMWATER SYSTEM. RODDING POINTS AT 30 m MAX.");
  if (inp.q > 0) n.push(`DESIGNED FOR A SURCHARGE OF ${inp.q} kPa BEHIND THE WALL. NO HEAVIER LOADS WITHIN ${(1.5 * inp.H).toFixed(1)} m.`);
  return n;
}
function wrap(s, n) {
  const words = s.split(" "), lines = []; let cur = "";
  for (const w of words) { if ((cur + " " + w).trim().length > n && cur) { lines.push(cur); cur = w; } else cur = (cur + " " + w).trim(); }
  if (cur) lines.push(cur);
  return lines;
}
// The section replaces the earlier picture everywhere: compact in the step cards, full in the Detail tab and the report.
function wallSvg(opts = {}) {
  if (opts.big) return sectionSvg({ mode: "full", scale: opts.scale });
  return sectionSvg({ mode: "compact" });
}
