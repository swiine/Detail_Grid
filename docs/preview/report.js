// ---------- Calculation report (HTML + Excel), same content as the app's report ----------
const RP_CSS = `
.rp { --water:#1F252B; --orange:#E2621B; --ink:#1E1711; --muted:#6F6A5D; --line:#DED7C4; --ok:#325442; --ok-bg:#E3ECE5; --bad:#A84933; --bad-bg:#F5E1DA; --warn:#8A6414; --warn-bg:#F4EAD0;
  --concrete:#CFCBBE; --soil:#E8DDC0; --soil-line:#B9A77C; --water-fill:#BFE0F4; --steel:#A84933; --surface:#FFFDF8; --hover:#EFEADB; --select:#FCE7D9; --alabaster:#F5F4F1;
  font-family: "Arial Nova", Arial, Helvetica, sans-serif; color: var(--ink); background: #fff; font-size: 12.5px; line-height: 1.45; padding: 0 0 28px; }
.rp * { box-sizing: border-box; }
.rp .rp-head { background: var(--water); color: #F5F4F1; border-bottom: 4px solid var(--orange); display: flex; justify-content: space-between; align-items: center; gap: 16px; padding: 18px 28px; }
.rp .rp-head h1 { margin: 0; font-size: 21px; font-weight: 400; }
.rp .rp-head small { color: #F08A4B; display: block; margin-top: 2px; }
.rp .rp-head .logo { height: 34px; width: auto; flex: none; }
.rp .rp-meta { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 6px 18px; padding: 14px 28px; border-bottom: 1px solid var(--line); background: #FBF9F2; }
.rp .rp-meta span { display: block; color: var(--muted); font-size: 11px; }
.rp section { padding: 6px 28px; }
.rp h2 { color: var(--water); font-size: 15px; margin: 16px 0 6px; border-bottom: 2px solid var(--orange); padding-bottom: 3px; }
.rp h3.step { color: var(--water); font-size: 13.5px; margin: 14px 0 4px; }
.rp .hint { color: var(--muted); font-size: 11.5px; margin: 0 0 6px; }
.rp table { border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; margin: 4px 0 8px; }
.rp th, .rp td { text-align: left; padding: 4px 6px; border-bottom: 1px solid var(--line); vertical-align: top; }
.rp th { background: var(--water); color: #F5F4F1; font-size: 11.5px; }
.rp td.n { text-align: right; white-space: nowrap; }
.rp td.note { color: var(--muted); font-size: 11.5px; }
.rp tr.tot td { border-top: 2px solid var(--orange); font-weight: 700; }
.rp tr.cur td { background: var(--select); font-weight: 700; }
.rp td.hi { background: var(--bad-bg); color: var(--bad); }
.rp .pill { display: inline-block; font-size: 10.5px; font-weight: 700; padding: 1px 6px; }
.rp .pill.ok { background: var(--ok-bg); color: var(--ok); } .rp .pill.bad { background: var(--bad-bg); color: var(--bad); } .rp .pill.info { background: var(--warn-bg); color: var(--warn); }
.rp .verdict { display: block; border: 1px solid; padding: 10px 12px; margin: 8px 0; }
.rp .verdict.ok { background: var(--ok-bg); border-color: #9DB8A5; } .rp .verdict.bad { background: var(--bad-bg); border-color: #D9A898; }
.rp .verdict b.big { font-size: 15px; display: block; grid-row: auto; margin-bottom: 2px; }
.rp td.v { text-align: right; }
.rp .whatif th:not(:first-child) { text-align: right; }
.rp .kv td { white-space: normal; }
.rp .kv { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 24px; }
.rp .two { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr); gap: 18px; align-items: start; }
.rp .pic { border: 1px solid var(--line); padding: 6px; background: #fff; }
.rp .pic svg { width: 100%; height: auto; display: block; }
.rp .mark { display: inline-grid; place-items: center; width: 18px; height: 18px; border: 1px solid var(--steel); border-radius: 50%; color: var(--steel); font-weight: 700; font-size: 10.5px; }
.rp .warnbox { background: var(--warn-bg); color: #5E4512; padding: 8px 10px; font-size: 11.5px; }
.rp .sign { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 18px; margin-top: 10px; }
.rp .sign div { border-top: 1px solid var(--ink); padding-top: 4px; color: var(--muted); font-size: 11px; height: 54px; }
.rp .tablewrap { overflow-x: auto; }
.rp .bar { display: none; }
.rp .btn, .rp button { display: none; }
.rp a { color: #A9440F; }
@media (max-width: 640px) { .rp .rp-meta, .rp .kv, .rp .two, .rp .sign { grid-template-columns: minmax(0, 1fr); } .rp section, .rp .rp-head, .rp .rp-meta { padding-left: 14px; padding-right: 14px; } }
@media print { .rp { font-size: 10.5px; } .rp h2 { break-after: avoid; } .rp table { break-inside: auto; } .rp tr { break-inside: avoid; } }
`;

function reportName() { return safe(S.wall) + " - calculation report"; }
function inputRows() {
  const inp = inputs(), rows = [];
  const add = (group, name, value, unit, src = "") => rows.push({ group, name, value, unit, src });
  const auto = S.custom ? "Custom" : "Automatic";
  add("Wall", "Wall type", TYPES[S.type].name, "");
  add("Wall", F.H[0], inp.H, "m", "Entered");
  add("Wall", F.slope[0], inp.slope, "°", "Entered");
  add("Wall", F.length[0], inp.length, "m", "Entered");
  add("Wall", "Site", S.site === "cut" ? "Cut (digging out behind)" : "Fill (building up behind)", "");
  if (S.type === "cantilever") add("Wall", "Shear key", inp.key > 0 ? `${Math.round(inp.key * 1000)} mm deep` : "None", "", S.custom ? "Custom" : S.keyMode === "auto" ? "Automatic" : "Entered");
  if (S.type === "cantilever") add("Wall", "Footing", S.footing === "toe" ? "In front only (no heel)" : "Under the retained soil (heel)", "", "Entered");
  add("Wall", "Drawing direction", S.toeRight ? "Faces right" : "Faces left", "", "Entered");
  for (const k of TYPE_FIELDS[S.type]) add("Wall", F[k][0], inp[k], F[k][1], auto);
  if (S.type === "cantilever") add("Wall", "Base width", +(inp.toe + inp.t2 + inp.heel).toFixed(3), "m", auto);
  if (S.type === "post") add("Wall", "Post size", inp.post, "", auto);
  for (const k of ["phi", "gamma", "phiF", "gammaF", ...(S.type !== "post" ? ["cF", "delta", "qa"] : [])]) add("Soil", F[k][0], inp[k], F[k][1], "Entered");
  add("Loads", F.q[0], inp.q, F.q[1], "Entered");
  add("Loads", "Drainage", S.drained ? "Drained backfill, ag pipe" : `Not drained, water ${inp.hw} m`, "", "Entered");
  add("Loads", F.kh[0], inp.kh, "", "Entered");
  if (S.type !== "post") add("Loads", "Passive soil in front", S.usePassive ? "Half passive counted" : "Ignored", "", "Entered");
  if (S.type === "cantilever") { add("Concrete", F.fc[0], inp.fc, "MPa", "Entered"); add("Concrete", F.cover[0], inp.cover, "mm", "Entered"); add("Concrete", "Bar size", S.bar === "auto" ? "Auto" : "N" + S.bar, "", "Entered"); }
  add("Design", "Spare capacity", MARGINS.find(m => m[0] === S.margin)[1], "", "Entered");
  for (const k of RATE_KEYS[S.type]) add("Rates", F["r_" + k][0], num("r_" + k), F["r_" + k][1], +num("r_" + k) === DEFAULT_RATES[k] ? "Default rate" : "Entered");
  return rows;
}
function reportBody() {
  const r = result(), inp = inputs(), cost = costOf(inp, r), L = num("length"), name = safe(S.wall);
  const logo = document.querySelector(".header .logo").outerHTML;
  const failing = r.checks.filter(c => !c.pass);
  const checks = r.checks.map(c => `<tr><td>${c.name}</td><td class="n">${fmt(c.value, c.unit === "kPa" || c.unit === "mm" ? 0 : 2)} ${c.unit}</td><td class="n">${c.unit === "FoS" ? "≥ " : "≤ "}${fmt(c.limit, c.unit === "kPa" || c.unit === "mm" ? 0 : 2)}</td><td class="n">${Math.round(c.util * 100)}%</td><td>${c.pass ? '<span class="pill ok">PASS</span>' : '<span class="pill bad">FAIL</span>'}</td><td class="note">${esc(c.note)}</td></tr>`).join("");
  const groups = {}; for (const row of inputRows()) (groups[row.group] ||= []).push(row);
  const inputsHtml = Object.entries(groups).map(([g, rows]) => `<div><h3 class="step">${g}</h3><table>${rows.map(x => `<tr><td>${x.name}</td><td class="n">${esc(x.value)} ${x.unit}</td><td class="note">${x.src}</td></tr>`).join("")}</table></div>`).join("");
  return `<div class="rp">
  <div class="rp-head"><div><h1>Retaining wall calculation</h1><small>${esc(TYPES[S.type].name)} · ET Walls 1.0.0 (preview)</small></div>${logo}</div>
  <div class="rp-meta">
    <div><span>Project</span>${esc(S.project)}</div><div><span>Job no.</span>${esc(S.job)}</div><div><span>Wall</span>${esc(S.wall)}</div>
    <div><span>Designed by</span>${esc(S.by)}</div><div><span>Checked by</span>${esc(S.chk)}</div><div><span>Date / revision</span>${today} / A</div>
  </div>
  <section><h2>1: Summary</h2>
    <div class="verdict ${r.pass ? "ok" : "bad"}"><b class="big">${r.pass ? "All checks pass." : `${failing.length} check${failing.length > 1 ? "s fail" : " fails"}: ${failing.map(c => c.name).join(", ")}.`}</b>
      ${TYPES[S.type].name}, ${inp.H} m retained, ${L} m long, ${S.custom ? "custom size" : "sized automatically for the lowest cost"}. Governing check: <b>${r.worst.name}</b> at ${Math.round(r.worst.util * 100)}% of capacity.</div>
    <div class="kv">
      <table><tr><td>Size</td><td class="v">${esc(sizeText(currentG()))}</td></tr>${r.r.reo ? `<tr><td>Main bars</td><td class="v">${esc(r.r.reo.bars ?? r.r.reo.schedule[0].bars)}</td></tr><tr><td>Steel</td><td class="v">${Math.round(r.r.reo.totalKg)} kg ${S.type === "post" ? "per pier" : "per metre"}</td></tr>` : ""}</table>
      <table><tr><td>Estimated cost per metre</td><td class="v"><b>${money(cost.perM)}</b></td></tr><tr><td>Estimated cost for ${L} m</td><td class="v"><b>${money(cost.perM * L)}</b></td></tr><tr><td>Spare capacity setting</td><td class="v">${MARGINS.find(m => m[0] === S.margin)[1]}</td></tr></table>
    </div>
    <p class="hint">Excel workbook with the same numbers: <a href="${encodeURI(name)}.xlsx">${esc(name)}.xlsx</a> (same folder).</p>
  </section>
  <section><h2>2: Typical wall section</h2><div class="pic">${sectionSvg({ mode: "full" })}</div><p class="hint">Drawn to the CAD standard (TTW.json) at the scale shown. Letters in brackets are the bar marks in section 5.</p></section>
  <section><h2>3: Inputs</h2><div class="kv">${inputsHtml}</div></section>
  <section><h2>4: Checks</h2><div class="tablewrap"><table><tr><th>Check</th><th>Value</th><th>Needs</th><th>Used</th><th></th><th>Working</th></tr>${checks}</table></div>
    <p class="hint">${S.type === "post" ? "Sleeper bending between posts and the pier concrete are not checked." : "Global (overall slope) stability is not checked here. Use Slide or Geo5 where it matters."}</p></section>
  <section><h2>5: Reinforcement</h2>${r.r.reo ? reoTable(r).replace(/<h3 class="step">Reinforcement<\/h3>/, "") : "<p>Unreinforced gravity wall: no bars needed.</p>"}</section>
  <section><h2>6: Cost estimate</h2>${costSection(r).replace(/<h3 class="step">Cost estimate<\/h3>/, "")}</section>
  <section><h2>7: What if</h2>${whatIf().replace(/<h3 class="step">What if…<\/h3>/, "")}</section>
  <section><h2>8: Method and notes</h2>
    <div class="warnbox">Preview method, to be replaced by the company spreadsheet method. Earth pressure: Rankine (Ka with backfill slope), surcharge as a uniform pressure, Seed–Whitman earthquake increment. Stability: factors of safety 1.5 sliding, 2.0 overturning (1.1 and 1.5 with earthquake), resultant in the middle third, bearing against the allowable pressure. Posts: Broms short free-head pile, factor of safety 2.0 (1.5 earthquake). Reinforcement: AS 3600-2018 rectangular stress block, φ 0.85 bending, φ 0.7 shear (no ligatures), minimum steel, crack-control spacing 300 mm, Lsy.tb development lengths, laps 1.25 Lsy.t. Costs: quantities per metre times the rates in section 3; a guide for comparing options, not a quote.</div>
    <p>An engineer must check and sign off every result before it is used.</p>
    <div class="sign"><div>Designed: ${esc(S.by)}</div><div>Checked: ${esc(S.chk)}</div><div>Date</div></div>
  </section></div>`;
}
function reportHtmlFile() {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>${esc(reportName())}</title><style>body{margin:0;background:#E6ECEE}.rp{max-width:900px;margin:0 auto}${RP_CSS}</style></head><body>${reportBody()}</body></html>`;
}
function openReport() {
  if (!result()) { const fp = firstProblem(); return toast(`Fix this first: ${fp.p}`); }
  let ov = document.getElementById("reportOverlay");
  if (!ov) { ov = document.createElement("div"); ov.id = "reportOverlay"; ov.className = "overlay"; ov.setAttribute("role", "dialog"); ov.setAttribute("aria-label", "Calculation report"); document.body.appendChild(ov);
    ov.addEventListener("click", e => { const a = e.target.closest("[data-rp]")?.dataset.rp; if (a === "close") closeReport(); if (a === "html") saveHtml(); if (a === "xlsx") saveXlsx(); }); }
  const name = safe(S.wall);
  ov.innerHTML = `<style>${RP_CSS}</style><div class="rp-bar"><b>Calculation report</b><span class="sp"></span>
    <button class="btn primary" data-rp="html">Download report (.html)</button><button class="btn primary" data-rp="xlsx">Download Excel (.xlsx)</button><button class="btn" data-rp="close">Close</button>
    <small>In the app both files are saved to Standalone Tool\\${esc(exportFolder())}${esc(name)}.html and .xlsx, linked to each other.</small></div>
    <div class="rp-sheet">${reportBody()}</div>`;
  ov.hidden = false; ov.scrollTop = 0; document.body.style.overflow = "hidden";
  ov.querySelector("[data-rp=html]").focus();
}
function closeReport() { const ov = document.getElementById("reportOverlay"); if (ov) ov.hidden = true; document.body.style.overflow = ""; }
document.addEventListener("keydown", e => { if (e.key === "Escape") closeReport(); });
// In the claude.ai viewer, files go through the downloads capability (the viewer confirms the save).
// Opened as a local file, a normal browser download is used.
let dlNs;
async function saveBlob(fileName, blob) {
  if (window.claude?.use) {
    if (dlNs === undefined) dlNs = await window.claude.use("downloads").catch(() => null);
    if (dlNs) {
      try { await dlNs.save({ filename: fileName, data: blob }); toast(`Saved ${fileName}.`); }
      catch (e) { toast(e?.code === "declined" ? "Not saved." : e?.code === "rate_limited" ? "A save is already waiting for you to confirm." : `Couldn't save here (${e?.code || "unavailable"}). Open the downloaded preview file instead.`); }
      return;
    }
  }
  try {
    const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = fileName; document.body.appendChild(a); a.click();
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1500);
    toast(`Saving ${fileName}.`);
  } catch (_) { toast("This view blocks downloads. Open the downloaded preview file in your browser instead."); }
}
function saveHtml() { saveBlob(safe(S.wall) + ".html", new Blob([reportHtmlFile()], { type: "text/html" })); }
let xlsxLoading = null;
function loadXlsx() {
  if (window.XLSX) return Promise.resolve();
  return xlsxLoading ||= new Promise((ok, fail) => { const s = document.createElement("script"); s.src = "https://cdnjs.cloudflare.com/ajax/libs/xlsx/0.18.5/xlsx.full.min.js"; s.onload = ok; s.onerror = () => { xlsxLoading = null; fail(); }; document.head.appendChild(s); });
}
async function saveXlsx() {
  try { await loadXlsx(); } catch (_) { return toast("Couldn't load the Excel writer (needs internet). The HTML report still works."); }
  const r = result(), inp = inputs(), cost = costOf(inp, r), L = num("length"), name = safe(S.wall), X = XLSX;
  const wb = X.utils.book_new();
  const sheet = (rows, widths, title) => { const ws = X.utils.aoa_to_sheet(rows); ws["!cols"] = widths.map(w => ({ wch: w })); X.utils.book_append_sheet(wb, ws, title); return ws; };
  sheet([["ET Walls (Engineering Technologies) - retaining wall calculation (preview)"], [], ["Project", S.project], ["Job no.", S.job], ["Wall", S.wall], ["Designed by", S.by], ["Checked by", S.chk], ["Date", today], [],
    ["Wall type", TYPES[S.type].name], ["Retained height (m)", inp.H], ["Wall length (m)", L], ["Sizing", S.custom ? "Custom" : "Automatic, lowest cost"], ["Size (mm)", sizeText(currentG())],
    ["Result", r.pass ? "All checks pass" : "Fails: " + r.checks.filter(c => !c.pass).map(c => c.name).join(", ")], ["Governing check", r.worst.name], ["Governing use", Math.round(r.worst.util * 1000) / 10 + "%"],
    ["Cost per metre ($)", Math.round(cost.perM)], ["Cost for the wall ($)", Math.round(cost.perM * L)], [], ["HTML report", name + ".html (same folder)"], ["Check every result. Preview method: see the HTML report, section 8."]], [26, 60], "Summary");
  sheet([["Group", "Input", "Value", "Unit", "Source"], ...inputRows().map(x => [x.group, x.name, x.value, x.unit, x.src])], [12, 32, 30, 8, 14], "Inputs");
  sheet([["Check", "Value", "Unit", "Needs", "Used %", "Result", "Working"], ...r.checks.map(c => [c.name, +c.value.toFixed(3), c.unit, (c.unit === "FoS" ? ">= " : "<= ") + +c.limit.toFixed(3), Math.round(c.util * 1000) / 10, c.pass ? "PASS" : "FAIL", c.note])], [28, 10, 8, 12, 9, 8, 90], "Checks");
  if (r.r.reo) sheet([["Mark", "Where", "Bars", "Needs (mm²)", "Gives (mm²)", "kg", "Note"], ...r.r.reo.schedule.map(s => [s.mark, s.where, s.bars, Math.round(s.req), Math.round(s.prov), +s.kg.toFixed(1), s.note]), [], ["", "Total steel", "", "", "", +r.r.reo.totalKg.toFixed(1), S.type === "post" ? "per pier" : "per metre of wall"]], [6, 36, 16, 12, 12, 8, 60], "Reinforcement");
  // Cost sheet with live formulas, so rates can be changed in Excel.
  const rows = [["Item", "Qty per m", "Unit", "Rate ($)", "$ per m", "$ for wall"]];
  cost.items.forEach((i, k) => { const n = k + 2; rows.push([i.name, +i.qty.toFixed(3), i.unit, i.rate, { t: "n", v: i.cost, f: `B${n}*D${n}` }, { t: "n", v: i.cost * L, f: `E${n}*$B$${cost.items.length + 3}` }]); });
  const last = cost.items.length + 1;
  rows.push(["Total", "", "", "", { t: "n", v: cost.perM, f: `SUM(E2:E${last})` }, { t: "n", v: cost.perM * L, f: `SUM(F2:F${last})` }]);
  rows.push(["Wall length (m)", L]);
  const opts = autoSizes()?.options ?? [];
  if (opts.length) { rows.push([], ["Options that pass every check", "Size (mm)", "", "", "$ per m", "Highest check %"]); for (const o of opts) rows.push([o.label, sizeText(o.g), "", "", Math.round(o.cost), Math.round(o.util * 1000) / 10]); }
  sheet(rows, [40, 52, 8, 10, 12, 14], "Cost");
  const keepIds = ["slide", "over", "ecc", "bear", "stemM", "heelM", "embed", "postM"];
  const wKey = S.type === "cantilever" ? "heel" : S.type === "post" ? "Dp" : "Bg";
  const wVals = [-0.4, -0.2, 0, 0.2, 0.4].map(d => +(inp[wKey] + d).toFixed(2)).filter(v => v > 0.2);
  const wRows = wVals.map(v => { const x = { ...inp, [wKey]: v }, rr = runAll(x); return [v, ...rr.checks.filter(c => keepIds.includes(c.id)).map(c => Math.round(c.util * 1000) / 10), Math.round(costOf(x, rr).perM), rr.pass ? "PASS" : "FAIL"]; });
  sheet([[wKey === "heel" ? "Heel (m)" : wKey === "Dp" ? "Pier embedment (m)" : "Base width (m)", ...r.checks.filter(c => keepIds.includes(c.id)).map(c => c.name + " %"), "$ per m", "Result"], ...wRows], [18, 14, 14, 14, 14, 14, 14, 10, 8], "What if");
  const out = X.write(wb, { bookType: "xlsx", type: "array" });
  saveBlob(name + ".xlsx", new Blob([out], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }));
}
