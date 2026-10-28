// ---------- Wall calculations (preview method: Rankine earth pressure, factors of safety) ----------
// Every check returns { id, name, value, limit, unit, util, pass, note }. util = demand / capacity (<= 1 passes).
const D2R = Math.PI / 180;
const GAMMA_W = 9.81, GAMMA_C = 24;

function kaRankine(phi, beta) {
  const p = phi * D2R, b = Math.min(beta, phi - 0.01) * D2R;
  const cb = Math.cos(b), r = Math.sqrt(Math.max(0, cb * cb - Math.cos(p) ** 2));
  return cb * (cb - r) / (cb + r);
}
const kpRankine = phi => Math.tan((45 + phi / 2) * D2R) ** 2;

function check(id, name, value, limit, unit, util, note) {
  return { id, name, value, limit, unit, util, pass: util <= 1 + 1e-9, note: note || "" };
}

// Stability of a wall that acts as one block (cantilever, gravity, block). Loads per metre run.
// weights: [{ W, x }] (x from toe), back: the virtual back plane { x, h } the soil pushes on.
function stability(inp, weights, back, B, seismic) {
  const beta = inp.slope, Ka = kaRankine(inp.phi, beta);
  const cb = Math.cos(beta * D2R), sb = Math.sin(beta * D2R);
  const H = back.h;
  const loads = []; // { Ph, Pv, y, x }
  const Pa = 0.5 * Ka * inp.gamma * H * H;
  loads.push({ name: "Soil", Ph: Pa * cb, Pv: Pa * sb, y: H / 3, x: back.x });
  if (inp.q > 0) {
    const Pq = Ka * inp.q * H;
    loads.push({ name: "Surcharge", Ph: Pq * cb, Pv: Pq * sb, y: H / 2, x: back.x });
  }
  let U = 0, xU = 0;
  if (!inp.drained && inp.hw > 0) {
    const hw = Math.min(inp.hw, H);
    loads.push({ name: "Water", Ph: 0.5 * GAMMA_W * hw * hw, Pv: 0, y: hw / 3, x: back.x });
    U = 0.5 * GAMMA_W * hw * B; xU = 2 * B / 3;
  }
  if (seismic && inp.kh > 0) {
    loads.push({ name: "Earthquake", Ph: 0.375 * inp.kh * inp.gamma * H * H, Pv: 0, y: 0.6 * H, x: back.x });
    for (const w of weights) loads.push({ name: "Wall inertia", Ph: inp.kh * w.W, Pv: 0, y: w.y ?? H / 2, x: w.x });
  }
  const sumH = loads.reduce((s, l) => s + l.Ph, 0);
  const Mo = loads.reduce((s, l) => s + l.Ph * l.y, 0) + U * xU;
  const sumW = weights.reduce((s, w) => s + w.W, 0);
  const V = sumW + loads.reduce((s, l) => s + l.Pv, 0) - U;
  const Mr = weights.reduce((s, w) => s + w.W * w.x, 0) + loads.reduce((s, l) => s + l.Pv * l.x, 0);

  let passive = 0;
  if (inp.usePassive && inp.D > 0) passive = 0.5 * 0.5 * kpRankine(inp.phiF) * inp.gammaF * inp.D * inp.D; // half mobilised
  const slideR = Math.max(0, V) * Math.tan(inp.delta * D2R) + inp.cF * B + passive;

  const xR = (Mr - Mo) / V;                // resultant from the toe
  const e = B / 2 - xR;
  let qmax;
  if (V <= 0 || xR <= 0) qmax = Infinity;
  else if (Math.abs(e) <= B / 6) qmax = V / B * (1 + 6 * Math.abs(e) / B);
  else qmax = 2 * V / (3 * (B / 2 - Math.abs(e)));
  const qmin = Math.abs(e) <= B / 6 ? V / B * (1 - 6 * Math.abs(e) / B) : 0;

  const fsS = seismic ? 1.1 : 1.5, fsO = seismic ? 1.5 : 2.0;
  const sfx = seismic ? " (earthquake)" : "";
  const FoSs = slideR / sumH, FoSo = Mr / Math.max(1e-9, Mo);
  const checks = [
    check("slide" + sfx, "Sliding" + sfx, FoSs, fsS, "FoS", fsS / FoSs,
      `Resists ${slideR.toFixed(1)} kN/m (base friction δ = ${inp.delta}°${inp.cF > 0 ? ", cohesion" : ""}${passive > 0 ? ", passive" : ""}) vs pushes ${sumH.toFixed(1)} kN/m.`),
    check("over" + sfx, "Overturning" + sfx, FoSo, fsO, "FoS", fsO / FoSo,
      `Restoring ${Mr.toFixed(1)} kNm/m vs overturning ${Mo.toFixed(1)} kNm/m about the toe.`),
    check("ecc" + sfx, "Resultant in middle third" + sfx, Math.abs(e), B / 6, "m", Math.abs(e) / (B / 6),
      `e = ${Math.abs(e).toFixed(3)} m, limit B/6 = ${(B / 6).toFixed(3)} m.`),
    check("bear" + sfx, "Bearing pressure" + sfx, qmax, inp.qa, "kPa", qmax / inp.qa,
      `q max ${isFinite(qmax) ? qmax.toFixed(0) : "∞"} kPa, q min ${qmin.toFixed(0)} kPa, allowable ${inp.qa} kPa.`),
  ];
  return { Ka, loads, sumH, Mo, V, Mr, e, qmax, qmin, checks };
}

// ---------- Reinforcement (AS 3600-2018 style, preview) ----------
// Picks the bar size and spacing for every face, stem cut-off height, starter laps and shrinkage bars.
const BAR_AREA = { 12: 110, 16: 200, 20: 310, 24: 450 };
const SPACINGS = [300, 250, 200, 150, 125, 100];
const FSY = 500;
const roundUp = (v, step) => Math.ceil(v / step) * step;

function phiMuFor(Ast, d, fc, b = 1000) {
  const a2 = Math.max(0.67, 0.85 - 0.0015 * fc);
  return 0.85 * Ast * FSY * (d - Ast * FSY / (2 * a2 * fc * b)) / 1e6;     // kNm per metre
}
function astRequired(M, d, fc, b = 1000) {
  if (M <= 0) return 0;
  const a2 = Math.max(0.67, 0.85 - 0.0015 * fc);
  const A = FSY * FSY / (2 * a2 * fc * b), Bq = -FSY * d, C = M * 1e6 / 0.85;
  const disc = Bq * Bq - 4 * A * C;
  return disc < 0 ? Infinity : (-Bq - Math.sqrt(disc)) / (2 * A);
}
function astMinBending(Dmm, d, fc, b = 1000) { return 0.20 * (Dmm / d) ** 2 * (0.6 * Math.sqrt(fc)) / FSY * b * d; }
function phiVucFor(Ast, d, fc, b = 1000) {
  const beta1 = Math.max(1.1, 1.1 * (1.6 - d / 1000));
  return 0.7 * beta1 * b * d * Math.min(4, Math.cbrt(fc)) * Math.cbrt(Math.min(0.04, Ast / (b * d))) / 1000; // kN per metre
}
// Lsy.tb, AS 3600-2018 Cl 13.1.2.2 (straight bar in tension), rounded up to 50 mm.
function devLength(db, fc, cover, s, topBar = false) {
  const k1 = topBar ? 1.3 : 1.0, k2 = (132 - db) / 100;
  const cd = Math.min(cover, s / 2), k3 = Math.min(1.0, Math.max(0.7, 1 - 0.15 * (cd - db) / db));
  const L = Math.max(0.5 * k1 * k3 * FSY * db / (k2 * Math.sqrt(Math.min(fc, 65))), 0.058 * FSY * k1 * db);
  return roundUp(L, 50);
}
// Lightest bar layout that carries M (kNm/m) in a section Dmm thick. pref = "auto" or a bar size.
function pickBars(M, Dmm, cover, fc, pref, minArea = 0) {
  const sizes = pref === "auto" ? [12, 16, 20, 24] : [Number(pref)];
  let best = null;
  for (const db of sizes) {
    const d = Dmm - cover - db / 2;
    const need = Math.max(astRequired(M, d, fc), M > 0 ? astMinBending(Dmm, d, fc) : 0, minArea);
    const sMax = Math.min(300, 2 * Dmm);                                   // crack control spacing
    const s = SPACINGS.filter(x => x <= sMax).find(x => BAR_AREA[db] * 1000 / x >= need);
    if (!s) continue;
    const prov = BAR_AREA[db] * 1000 / s;
    const score = prov + (s < 150 ? 400 : 0);                              // tight spacing is hard to fix and pour
    if (!best || score < best.score) best = { db, s, d, need, prov, score };
  }
  if (!best) { const db = sizes[sizes.length - 1]; const d = Dmm - cover - db / 2; return { db, s: 100, d, need: astRequired(M, d, fc), prov: BAR_AREA[db] * 10, ok: false, label: `N${db}@100 not enough` }; }
  return { ...best, ok: true, label: `N${best.db}@${best.s}` };
}
const steelKg = (areaPerM, lengthM) => areaPerM * lengthM * 0.00785;   // kg per metre run of wall

// Bearing pressure under the base at x from the toe (kPa), from the service resultant.
function bearingAt(x, V, e, B) {
  if (V <= 0) return 0;
  if (Math.abs(e) <= B / 6) return V / B * (1 + 12 * e * (B / 2 - x) / (B * B));
  const L = 3 * (B / 2 - Math.abs(e)), xx = e >= 0 ? x : B - x;
  return xx < L ? 2 * V / L * (1 - xx / L) : 0;
}
// Bending moment and shear (at d from the face) of a cantilever strip loaded by net(u), u = distance from the face.
function cantileverStrip(net, L, d) {
  const n = 200, du = L / n; let M = 0, V = 0;
  for (let i = 0; i < n; i++) { const u = (i + 0.5) * du, w = net(u); M += w * u * du; if (u >= d) V += w * du; }
  return { M, V };
}

function wallReinforcement(inp, geo, st) {
  const { toe, heel, t1, t2, tb } = inp, { hs, B } = geo;
  const fc = inp.fc, cover = inp.cover, pref = inp.bar ?? "auto";
  const Ka = kaRankine(inp.phi, inp.slope), cb = Math.cos(inp.slope * D2R);
  const hwStem = !inp.drained ? Math.max(0, inp.hw - tb) : 0;
  // Factored stem actions at depth y below the top of the stem (1.5 earth/water, 1.5 surcharge).
  const Mst = y => 1.5 * Ka * cb * (inp.gamma * y ** 3 / 6 + inp.q * y * y / 2) + 1.5 * GAMMA_W * Math.max(0, y - (hs - hwStem)) ** 3 / 6;
  const Vst = y => 1.5 * Ka * cb * (inp.gamma * y * y / 2 + inp.q * y) + 1.5 * GAMMA_W * Math.max(0, y - (hs - hwStem)) ** 2 / 2;
  const stemD = t2 * 1000, tD = (y => (t1 + (t2 - t1) * y / hs) * 1000);

  // A: stem back face, vertical main bars
  const Mstem = Mst(hs);
  const A = pickBars(Mstem, stemD, cover, fc, pref);
  const phiMuA = phiMuFor(A.prov, A.d, fc);
  const dV = A.d / 1000, VstemD = Vst(Math.max(0, hs - dV));
  const phiVA = phiVucFor(A.prov, A.d, fc);
  const LsyA = devLength(A.db, fc, cover, A.s), lapA = roundUp(1.25 * LsyA, 50);
  // Cut every second bar where half the steel is enough (only when the spacing stays <= 300 mm).
  let cut = null;
  if (A.ok && A.s * 2 <= 300 && hs > 1.2) {
    const half = A.prov / 2;
    let lo = 0, hi = hs;
    for (let i = 0; i < 60; i++) { const mid = (lo + hi) / 2; const dmid = tD(mid) - cover - A.db / 2; if (Mst(mid) <= phiMuFor(half, dmid, fc)) lo = mid; else hi = mid; }
    const z = hs - lo + (tD(lo) + LsyA) / 1000;            // theoretical point + D + Lsy.t (Cl 8.1.10)
    if (z < hs - 0.3) cut = roundUp(z * 1000, 50) / 1000;
  }
  // Starters: cogged in the base. Cog anchorage 0.5 Lsy.tb needs room in the base.

  // E: heel, top bars (soil and surcharge push down, bearing pushes up)
  const wDown = 1.2 * GAMMA_C * tb + 1.2 * inp.gamma * hs + 1.5 * inp.q;
  const xHeel0 = toe + t2;
  const heelNet = u => wDown - bearingAt(xHeel0 + u, st.V, st.e, B);
  const Dbase = tb * 1000;
  const dBase = Dbase - cover - 16 / 2;
  const heelA = cantileverStrip(heelNet, heel, dBase / 1000);
  // F: toe, bottom bars (bearing pushes up, base and soil on the toe hold down)
  const wToe = 0.9 * (GAMMA_C * tb + inp.gamma * Math.max(0, inp.D - tb));
  const toeNet = u => 1.5 * bearingAt(toe - u, st.V, st.e, B) - wToe;
  const toeA = cantileverStrip(toeNet, toe, dBase / 1000);
  const shrinkFace = 0.00125 * 1000 * Dbase;                          // 0.0025 bD split over two faces
  const E = pickBars(Math.max(0, heelA.M), Dbase, cover, fc, pref, shrinkFace);
  const Fb = pickBars(Math.max(0, toeA.M), Dbase, inp.cover, fc, pref, shrinkFace);
  const anchorRoom = tb * 1000 - cover - Fb.db;                      // top of base down to the bottom bars
  const anchorNeed = roundUp(0.5 * LsyA, 10);
  const phiMuE = phiMuFor(E.prov, E.d, fc), phiMuF = phiMuFor(Fb.prov, Fb.d, fc);
  const phiVE = phiVucFor(E.prov, E.d, fc), phiVF = phiVucFor(Fb.prov, Fb.d, fc);
  const LsyE = devLength(E.db, fc, cover, E.s, Dbase - cover > 300);

  // B: stem front face vertical, C: stem horizontal each face, G: base longitudinal each face (shrinkage)
  const tAvg = (t1 + t2) / 2 * 1000;
  const Bf = pickBars(0, stemD, cover, fc, "auto", 0.00075 * 1000 * stemD);
  const C = pickBars(0, tAvg, cover, fc, "auto", 0.00125 * 1000 * tAvg);
  const G = pickBars(0, Dbase, cover, fc, "auto", shrinkFace);

  const len = { A: hs + lapA / 1000, B: hs + 0.3, C: 2, E: B - 2 * cover / 1000, F: B - 2 * cover / 1000, G: 2 };
  const schedule = [
    { mark: "A", where: "Stem, back face (soil side), vertical", bars: A.label, req: A.need, prov: A.prov, kg: steelKg(A.prov, cut ? (cut + hs + lapA / 1000) / 2 + lapA / 1000 : len.A),
      note: `Main bars. ${cut ? `Every 2nd bar stops ${cut.toFixed(2)} m above the base.` : "Run full height."} Lap ${lapA} mm with starters.` },
    { mark: "S", where: "Starters from base into stem", bars: A.label, req: A.need, prov: A.prov, kg: steelKg(A.prov, lapA / 1000 + tb + 0.3),
      note: `Cogged under the bottom bars, ${lapA} mm above the base.${anchorNeed > anchorRoom ? " Base too thin for the cog: thicken it." : ""}` },
    { mark: "B", where: "Stem, front face, vertical", bars: Bf.label, req: Bf.need, prov: Bf.prov, kg: steelKg(Bf.prov, len.B), note: "Minimum steel (0.15% split over 2 faces)." },
    { mark: "C", where: "Stem, horizontal, each face", bars: C.label, req: C.need, prov: C.prov, kg: steelKg(C.prov * 2, hs) / 1, note: "Shrinkage and temperature (0.25% bD over 2 faces)." },
    { mark: "E", where: "Base, top (heel), across the wall", bars: E.label, req: E.need, prov: E.prov, kg: steelKg(E.prov, len.E),
      note: heelA.M > 0 ? `Heel M* = ${heelA.M.toFixed(1)} kNm/m. Anchor ${LsyE} mm past the stem.` : "Heel barely bends: minimum steel." },
    { mark: "F", where: "Base, bottom (toe), across the wall", bars: Fb.label, req: Fb.need, prov: Fb.prov, kg: steelKg(Fb.prov, len.F),
      note: toeA.M > 0 ? `Toe M* = ${toeA.M.toFixed(1)} kNm/m. Runs the full base width.` : "Toe barely bends: minimum steel." },
    { mark: "G", where: "Base, along the wall, top and bottom", bars: G.label, req: G.need, prov: G.prov, kg: steelKg(G.prov * 2, B), note: "Shrinkage and temperature." },
  ];
  const totalKg = schedule.reduce((s, r) => s + r.kg, 0);

  const checks = [
    check("stemM", "Stem bending", Mstem, phiMuA, "kNm/m", Mstem / phiMuA,
      `M* = ${Mstem.toFixed(1)} kNm/m at the base of the stem. Needs ${Math.ceil(A.need)} mm²/m, ${A.label} gives ${Math.round(A.prov)} mm²/m (d = ${A.d.toFixed(0)} mm, φMu = ${phiMuA.toFixed(1)}).`),
    check("stemV", "Stem shear", VstemD, phiVA, "kN/m", VstemD / phiVA, `V* = ${VstemD.toFixed(1)} kN/m at d from the base vs φVuc = ${phiVA.toFixed(1)} kN/m (no ligatures).`),
    check("heelM", "Heel bending", Math.max(0, heelA.M), phiMuE, "kNm/m", Math.max(0, heelA.M) / phiMuE,
      `Down ${wDown.toFixed(0)} kPa (1.2 G + 1.5 Q) less the bearing under the heel. ${E.label} top, φMu = ${phiMuE.toFixed(1)}.`),
    check("heelV", "Heel shear", Math.max(0, heelA.V), phiVE, "kN/m", Math.max(0, heelA.V) / phiVE, `V* at d from the stem vs φVuc = ${phiVE.toFixed(1)} kN/m.`),
    check("toeM", "Toe bending", Math.max(0, toeA.M), phiMuF, "kNm/m", Math.max(0, toeA.M) / phiMuF,
      `1.5 × bearing up less 0.9 × base and soil on the toe. ${Fb.label} bottom, φMu = ${phiMuF.toFixed(1)}.`),
    check("toeV", "Toe shear", Math.max(0, toeA.V), phiVF, "kN/m", Math.max(0, toeA.V) / phiVF, `V* at d from the stem vs φVuc = ${phiVF.toFixed(1)} kN/m.`),
    check("anchor", "Starter anchorage in base", anchorNeed, anchorRoom, "mm", anchorNeed / anchorRoom,
      `Cogged starter needs 0.5 Lsy.tb = ${anchorNeed} mm in the base; room ${anchorRoom.toFixed(0)} mm (base less bottom cover and bars).`),
  ];
  if (!A.ok || !E.ok || !Fb.ok) checks.push(check("bars", "Bars fit", 2, 1, "", 2, "No bar size and spacing works. Make the section thicker."));
  return { A, Bf, C, E, F: Fb, G, cut, lapA, LsyA, schedule, totalKg, checks, bars: A.label };
}

// Bored pier for a post wall: minimum longitudinal steel (0.5% of the pier) and a helix.
function pierReinforcement(inp) {
  const area = Math.PI * (inp.dia * 1000) ** 2 / 4, need = 0.005 * area;
  const n = Math.max(6, Math.ceil(need / BAR_AREA[16]));
  const prov = n * BAR_AREA[16];
  const schedule = [
    { mark: "P", where: "Pier, vertical", bars: `${n}-N16`, req: need, prov, kg: prov * (inp.Dp + 0.1) * 0.00785, note: `0.5% of the ${Math.round(inp.dia * 1000)} mm pier, 75 mm cover. Full depth.` },
    { mark: "H", where: "Pier, helix", bars: "R10 helix @ 200", req: 0, prov: 0, kg: 0.617 * Math.PI * (inp.dia - 0.17) * inp.Dp / 0.2, note: "Pitch 200, closer (100) in the top 600 mm." },
  ];
  return { schedule, totalKg: schedule.reduce((s, r) => s + r.kg, 0), checks: [] };
}

function cantilever(inp, seismic = false) {
  const { H, D, tb, toe, heel, t1, t2 } = inp;
  const Ht = H + D, hs = Ht - tb, B = toe + t2 + heel, tanB = Math.tan(inp.slope * D2R);
  const wBack = B - (toe + t1);                       // soil width over the heel at the top of the stem
  const weights = [
    { W: t1 * hs * GAMMA_C, x: toe + t1 / 2, y: tb + hs / 2, part: "stem" },
    { W: (t2 - t1) * hs / 2 * GAMMA_C, x: toe + t1 + (t2 - t1) / 3, y: tb + hs / 3, part: "stem" },
    { W: B * tb * GAMMA_C, x: B / 2, y: tb / 2, part: "base" },
    { W: heel * hs * inp.gamma, x: toe + t2 + heel / 2, y: tb + hs / 2, part: "soil" },
    { W: (t2 - t1) * hs / 2 * inp.gamma, x: toe + t1 + 2 * (t2 - t1) / 3, y: tb + 2 * hs / 3, part: "soil" },
  ];
  if (tanB > 0) weights.push({ W: 0.5 * wBack * wBack * tanB * inp.gamma, x: B - wBack / 3, y: Ht + wBack * tanB / 3, part: "soil" });
  if (D > tb) weights.push({ W: (D - tb) * toe * inp.gamma, x: toe / 2, y: tb + (D - tb) / 2, part: "soil" });
  const back = { x: B, h: Ht + wBack * tanB };
  const st = stability(inp, weights, back, B, seismic);
  if (seismic || inp.noReo) return { kind: "cantilever", B, Ht, hs, back, ...st };
  const reo = wallReinforcement(inp, { hs, B }, st);
  return { kind: "cantilever", B, Ht, hs, back, ...st, reo, checks: [...st.checks, ...reo.checks] };
}

function gravity(inp, seismic = false) {
  const { H, D, Bg, Bt } = inp;
  const Ht = H + D, gw = inp.gammaWall;
  // Front face battered, back face vertical: rectangle Bt wide plus a triangle at the front.
  const weights = [
    { W: Bt * Ht * gw, x: Bg - Bt / 2, y: Ht / 2, part: "wall" },
    { W: (Bg - Bt) * Ht / 2 * gw, x: 2 * (Bg - Bt) / 3, y: Ht / 3, part: "wall" },
  ];
  const back = { x: Bg, h: Ht };
  const st = stability(inp, weights, back, Bg, seismic);
  return { kind: "gravity", B: Bg, Ht, back, ...st };
}

// Sleeper wall: posts cantilever from bored piers. Broms (1964) short free-head pile in cohesionless soil.
const POSTS = { "150UC23": 184e3, "200UC46": 497e3, "250UC73": 992e3, "310UC97": 1440e3 }; // plastic modulus S, mm3
function postWall(inp, seismic = false) {
  const { H, s, dia, Dp } = inp;
  const Ka = kaRankine(inp.phi, inp.slope), Kp = kpRankine(inp.phiF), cb = Math.cos(inp.slope * D2R);
  let P = (0.5 * Ka * inp.gamma * H * H + Ka * inp.q * H) * cb * s;
  let M0 = (0.5 * Ka * inp.gamma * H * H * H / 3 + Ka * inp.q * H * H / 2) * cb * s;
  if (seismic && inp.kh > 0) { P += 0.375 * inp.kh * inp.gamma * H * H * s; M0 += 0.375 * inp.kh * inp.gamma * H * H * s * 0.6 * H; }
  if (!inp.drained && inp.hw > 0) { const h = Math.min(inp.hw, H); P += 0.5 * GAMMA_W * h * h * s; M0 += 0.5 * GAMMA_W * h ** 3 / 3 * s; }
  const e = M0 / P;                                    // height of the push above ground in front
  const cap = L => 0.5 * inp.gammaF * dia * L ** 3 * Kp / (e + L);
  const fsReq = seismic ? 1.5 : 2.0;
  let Lreq = 0.1; while (cap(Lreq) < fsReq * P && Lreq < 20) Lreq += 0.01;
  const FoS = cap(Dp) / P;
  const f = 0.82 * Math.sqrt(P / (inp.gammaF * dia * Kp));
  const Mmax = P * (e + 0.67 * f);
  const Mstar = (seismic ? 1.0 : 1.5) * Mmax;
  const S = POSTS[inp.post] ?? 497e3, phiMs = 0.9 * 300 * S / 1e6;
  const sfx = seismic ? " (earthquake)" : "";
  return {
    kind: "post", Ka, Kp, P, e, Lreq, FoS, Mmax, B: dia, Ht: H + Dp, back: { x: 0, h: H }, reo: seismic ? null : pierReinforcement(inp),
    checks: [
      check("embed" + sfx, "Post embedment" + sfx, FoS, fsReq, "FoS", fsReq / FoS,
        `Push ${P.toFixed(1)} kN per post at ${e.toFixed(2)} m above ground. Broms capacity ${cap(Dp).toFixed(1)} kN with ${Dp.toFixed(2)} m embedment. Needs at least ${Lreq.toFixed(2)} m.`),
      check("postM" + sfx, "Post bending" + sfx, Mstar, phiMs, "kNm", Mstar / phiMs,
        `M* = ${Mstar.toFixed(1)} kNm (max moment ${(0.67 * f).toFixed(2)} m below ground). ${inp.post} φMs = ${phiMs.toFixed(1)} kNm (fy 300 MPa).`),
    ],
  };
}

function analyse(inp, seismic = false) {
  if (inp.type === "post") return postWall(inp, seismic);
  if (inp.type === "gravity" || inp.type === "block") return gravity(inp, seismic);
  return cantilever(inp, seismic);
}

function runAll(inp) {
  const r = analyse(inp, false);
  const checks = [...r.checks];
  if (inp.kh > 0) checks.push(...analyse(inp, true).checks);
  const worst = checks.reduce((a, c) => (c.util > a.util ? c : a), checks[0]);
  return { r, checks, worst, pass: checks.every(c => c.pass) };
}

// ---------- Automatic sizing: start from rules of thumb, grow the part that fails until every check passes ----------
const up50 = v => Math.ceil(v * 20 - 1e-9) / 20;      // round up to 50 mm
function autoSize(inp) {
  const failing = g => new Set(runAll({ ...inp, ...g }).checks.filter(c => !c.pass).map(c => c.id.replace(" (earthquake)", "")));
  const has = (f, ids) => ids.some(i => f.has(i));
  if (inp.type === "post") {
    const names = Object.keys(POSTS);
    const g = { s: 2.4, dia: inp.H <= 1.5 ? 0.45 : 0.6, Dp: up50(Math.max(1.0, inp.H)), post: names[0] };
    for (let i = 0; i < 120; i++) {
      const f = failing(g); if (!f.size) return { g, ok: true };
      if (has(f, ["embed"])) g.Dp = up50(g.Dp + 0.1);
      if (has(f, ["postM"])) { const k = names.indexOf(g.post); if (k === names.length - 1) return { g, ok: false }; g.post = names[k + 1]; }
    }
    return { g, ok: false };
  }
  if (inp.type === "gravity" || inp.type === "block") {
    const D = 0.3, Ht = inp.H + D, Bt = inp.type === "block" ? 0.3 : 0.5;
    const g = { D, Bt, Bg: up50(Math.max(Bt + 0.1, 0.4 * Ht)), gammaWall: inp.type === "block" ? 21 : 23 };
    for (let i = 0; i < 120; i++) {
      if (inp.type === "block") g.Bt = Math.max(0.3, up50(0.45 * g.Bg));   // stepped blocks: the top grows with the base
      if (!failing(g).size) return { g, ok: true };
      g.Bg = up50(g.Bg + 0.05);
    }
    return { g, ok: false };
  }
  const D = 0.5, Ht = inp.H + D;
  const toeOnly = inp.footing === "toe";
  const g = { D, t1: 0.2, t2: Math.max(0.25, up50(Ht / 12)), tb: Math.max(0.35, up50(Ht / 10)), toe: toeOnly ? up50(0.6 * Ht) : Math.max(0.4, up50(0.15 * Ht)), heel: toeOnly ? 0 : Math.max(0.6, up50(0.35 * Ht)) };
  for (let i = 0; i < 120; i++) {
    const f = failing(g); if (!f.size) return { g, ok: true };
    if (toeOnly && has(f, ["bear", "ecc", "slide", "over"])) g.toe = up50(g.toe + 0.1);
    else if (has(f, ["bear", "ecc"])) { g.toe = up50(g.toe + 0.1); g.heel = up50(g.heel + 0.1); }
    else if (has(f, ["slide", "over"])) g.heel = up50(g.heel + 0.1);
    if (has(f, ["stemM", "stemV", "bars"])) g.t2 = up50(g.t2 + 0.05);
    if (has(f, ["heelM", "heelV", "toeM", "toeV", "anchor", "bars"])) g.tb = up50(g.tb + 0.05);
  }
  return { g, ok: false };
}

// ---------- Cost estimate (per metre of wall) ----------
// Placeholder rates (AUD, supplied and installed). Replace with the office's own rates.
const DEFAULT_RATES = {
  concrete: 420,     // $/m3 structural concrete, supplied and placed
  steel: 3.8,        // $/kg reinforcement, supplied and fixed
  formwork: 110,     // $/m2
  excavation: 55,    // $/m3 dig and cart away
  backfill: 45,      // $/m3 compacted backfill behind the wall (cut sites)
  drainage: 95,      // $/m3 free-draining gravel behind the wall
  agpipe: 30,        // $/m ag pipe in sock
  blocks: 650,       // $/m3 of block wall, supplied, laid and filled
  postSteel: 6.5,    // $/kg steel posts, supplied, galvanised and stood
  drilling: 110,     // $/m of bored pier (600 mm); scaled by diameter
  sleepers: 120,     // $/m2 of sleeper face
};
const POST_KG_M = { "150UC23": 23.4, "200UC46": 46.2, "250UC73": 72.9, "310UC97": 96.8 };
function costItems(inp, res, rates) {
  const R = { ...DEFAULT_RATES, ...(rates || {}) };
  const cut = inp.site !== "fill", items = [];
  const add = (key, name, qty, unit, rate) => { if (qty > 1e-9) items.push({ key, name, qty, unit, rate, cost: qty * rate }); };
  const drainZone = h => inp.drained ? 0.3 * h : 0;
  if (inp.type === "cantilever") {
    const { toe, t1, t2, tb, heel, D, H } = inp, hs = H + D - tb, B = toe + t2 + heel;
    add("concrete", "Concrete (stem and base)", (t1 + t2) / 2 * hs + B * tb, "m³", R.concrete);
    if (res.reo) add("steel", "Reinforcement", res.reo.totalKg, "kg", R.steel);
    add("formwork", "Formwork (stem faces, base edges)", 2 * hs + 2 * tb, "m²", R.formwork);
    const digBack = cut ? (heel + t2 + 0.3) * H : 0;
    add("excavation", "Excavation" + (cut ? " (footing and behind the wall)" : " (footing)"), (B + 0.6) * D + digBack, "m³", R.excavation);
    add("drainage", "Drainage gravel behind the stem", drainZone(hs), "m³", R.drainage);
    if (cut) add("backfill", heel > 0 ? "Compacted backfill over the heel" : "Compacted backfill behind the wall", Math.max(0, digBack - drainZone(hs)), "m³", R.backfill);
    if (inp.drained) add("agpipe", "Ag pipe", 1, "m", R.agpipe);
  } else if (inp.type === "gravity" || inp.type === "block") {
    const { Bg, Bt, D, H } = inp, Ht = H + D, vol = (Bg + Bt) / 2 * Ht;
    if (inp.type === "block") {
      add("blocks", "Block wall (supply, lay, fill)", vol, "m³", R.blocks);
      add("concrete", "Levelling pad", 0.15 * (Bg + 0.2), "m³", R.concrete);
    } else {
      add("concrete", "Mass concrete", vol, "m³", R.concrete);
      add("formwork", "Formwork (both faces)", Ht + Math.hypot(Ht, Bg - Bt), "m²", R.formwork);
    }
    const digBack = cut ? 0.6 * H : 0;
    add("excavation", "Excavation" + (cut ? " (footing and working room)" : " (footing)"), (Bg + 0.6) * D + digBack, "m³", R.excavation);
    add("drainage", "Drainage gravel behind the wall", drainZone(Ht), "m³", R.drainage);
    if (cut) add("backfill", "Compacted backfill", Math.max(0, digBack - drainZone(Ht)), "m³", R.backfill);
    if (inp.drained) add("agpipe", "Ag pipe", 1, "m", R.agpipe);
  } else {
    const { H, Dp, dia, s } = inp, per = 1 / s;
    add("postSteel", `Steel posts ${inp.post} (one every ${s} m)`, (POST_KG_M[inp.post] ?? 46.2) * (H + Dp - 0.15) * per, "kg", R.postSteel);
    add("drilling", `Bored piers Ø${Math.round(dia * 1000)}`, Dp * per, "m", R.drilling * (dia / 0.6) ** 2);
    add("concrete", "Pier concrete", Math.PI * dia * dia / 4 * Dp * per, "m³", R.concrete);
    if (res.reo) add("steel", "Pier reinforcement", res.reo.totalKg * per, "kg", R.steel);
    add("sleepers", "Sleepers", H, "m²", R.sleepers);
    add("drainage", "Drainage gravel behind the sleepers", drainZone(H), "m³", R.drainage);
    if (inp.drained) add("agpipe", "Ag pipe", 1, "m", R.agpipe);
  }
  return { items, perM: items.reduce((s, i) => s + i.cost, 0) };
}

// ---------- Cost-based sizing: the cheapest size that passes every check (below the margin) ----------
// Tries many sizes, keeps the ones that pass, ranks them by cost. Returns the cheapest plus a few alternatives.
function optimise(inp, rates, maxUtil = 1) {
  const ok = r => r.checks.every(c => c.util <= maxUtil + 1e-9);
  const cands = [];
  const tryG = (g, label) => {
    const x = { ...inp, ...g }, r = runAll(x);
    if (!ok(r)) return false;
    const cost = costItems(x, r.r, rates);
    cands.push({ g, cost: cost.perM, util: r.worst.util, steelKg: r.r.reo?.totalKg ?? 0, B: r.r.B, label });
    return true;
  };
  const r50 = v => Math.round(v * 20) / 20;
  if (inp.type === "cantilever") {
    const D = inp.D > 0 ? inp.D : 0.5, t1 = 0.2;
    // Stem: the thinnest stem whose bending and shear pass (base sizes don't affect them).
    let t2min = null;
    for (let t = 0.2; t <= 1.2 + 1e-9; t += 0.05) {
      const r = runAll({ ...inp, D, t1, t2: r50(t), tb: 0.6, toe: inp.footing === "toe" ? 2.0 : 0.6, heel: inp.footing === "toe" ? 0 : 1.5 });
      if (r.checks.filter(c => c.id === "stemM" || c.id === "stemV").every(c => c.util <= maxUtil)) { t2min = r50(t); break; }
    }
    const stab = ["slide", "over", "ecc", "bear"];
    const passesStab = g => { const x = { ...inp, ...g, noReo: true }; const cs = [...analyse(x, false).checks, ...(inp.kh > 0 ? analyse(x, true).checks : [])]; return cs.filter(c => stab.includes(c.id.replace(" (earthquake)", ""))).every(c => c.util <= maxUtil); };
    if (t2min !== null && inp.footing === "toe") {
      // Footing in front only (boundary wall): heel 0, so nothing but the wall and the base hold it down. Grow the toe.
      for (const t2 of [t2min, r50(t2min + 0.05), r50(t2min + 0.1)])
        for (let tb = 0.3; tb <= 0.9 + 1e-9; tb += 0.05) {
          let toe = 0.3, found = false;
          for (; toe <= 8 + 1e-9; toe += 0.1) if (passesStab({ D, t1, t2, tb: r50(tb), toe: Math.round(toe * 10) / 10, heel: 0 })) { found = true; break; }
          if (!found) continue;
          for (const tt of [toe, toe + 0.1, toe + 0.2]) tryG({ D, t1, t2, tb: r50(tb), toe: Math.round(tt * 10) / 10, heel: 0 });
        }
    } else if (t2min !== null) {
      for (const t2 of [t2min, r50(t2min + 0.05), r50(t2min + 0.1)])
        for (let tb = 0.3; tb <= 0.8 + 1e-9; tb += 0.05)
          for (let toe = 0.3; toe <= 2.0 + 1e-9; toe += 0.1) {
            // Shortest heel that passes stability, then the full checks at that heel and one step longer.
            let heel = 0.3, found = false;
            for (; heel <= 6 + 1e-9; heel += 0.1) {
              const g = { D, t1, t2, tb: r50(tb), toe: Math.round(toe * 10) / 10, heel: Math.round(heel * 10) / 10, noReo: true };
              const x = { ...inp, ...g };
              const cs = [...analyse(x, false).checks, ...(inp.kh > 0 ? analyse(x, true).checks : [])];
              if (cs.filter(c => stab.includes(c.id.replace(" (earthquake)", ""))).every(c => c.util <= maxUtil)) { found = true; break; }
            }
            if (!found) continue;
            for (const h of [heel, heel + 0.1]) tryG({ D, t1, t2, tb: r50(tb), toe: Math.round(toe * 10) / 10, heel: Math.round(h * 10) / 10 });
          }
    }
  } else if (inp.type === "gravity" || inp.type === "block") {
    const D = inp.D > 0 ? inp.D : 0.3, gw = inp.type === "block" ? 21 : 23;
    const tops = inp.type === "block" ? [0.3, 0.45, 0.6, 0.8, 1.0, 1.2] : [0.3, 0.4, 0.5, 0.6, 0.8];
    for (const Bt of tops) for (let Bg = Bt + 0.1; Bg <= 8; Bg += 0.05) if (tryG({ D, Bt, Bg: r50(Bg), gammaWall: gw })) break;
  } else {
    const s = inp.s > 0 ? inp.s : 2.4;
    for (const dia of [0.45, 0.6, 0.75, 0.9]) for (const post of Object.keys(POSTS))
      for (let Dp = 1.0; Dp <= 8; Dp += 0.1) {
        const g = { s, dia, Dp: Math.round(Dp * 10) / 10, post };
        const r = runAll({ ...inp, ...g });
        const bend = r.checks.filter(c => c.id.startsWith("postM")).every(c => c.util <= maxUtil);
        if (!bend) break;                        // a bigger post is needed; try the next one
        if (tryG(g)) break;
      }
  }
  if (!cands.length) return { g: autoSize(inp).g, ok: false, options: [] };
  cands.sort((a, b) => a.cost - b.cost);
  const best = cands[0], near = cands.filter(c => c.cost <= best.cost * 1.25);
  const options = [{ ...best, label: "Cheapest" }];
  const pick = (label, key) => { const c = near.filter(x => !options.some(o => sameG(o.g, x.g))).sort((a, b) => a[key] - b[key])[0]; if (c && c[key] < options[0][key] - 1e-6) options.push({ ...c, label }); };
  if (inp.type === "post") pick("Smaller pier", "B");
  else pick("Narrowest base (least digging)", "B");
  if (inp.type === "cantilever") pick("Least steel", "steelKg");
  pick("Most spare capacity", "util");
  return { g: best.g, ok: true, options, tried: cands.length };
}
const sameG = (a, b) => JSON.stringify(a) === JSON.stringify(b);

if (typeof module !== "undefined") module.exports = { analyse, runAll, autoSize, optimise, costItems, DEFAULT_RATES, kaRankine, kpRankine };
