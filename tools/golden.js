// Regenerates tests/ETWalls.Core.Tests/golden-preview.json from the preview page's calculations:
//   node tools/golden.js > tests/ETWalls.Core.Tests/golden-preview.json
const c = require("../docs/preview/calc.js");
const base = { phi: 30, gamma: 19, slope: 0, phiF: 28, gammaF: 19, cF: 0, delta: 28, qa: 150, q: 10, drained: true, hw: 1, kh: 0, usePassive: false, fc: 32, cover: 50, bar: "auto", site: "cut", footing: "heel",
  H: 2.4, D: 0.5, tb: 0.35, toe: 0.6, heel: 1.5, t1: 0.2, t2: 0.25, Bg: 1.4, Bt: 0.6, gammaWall: 22, s: 2.4, dia: 0.6, Dp: 2.8, post: "200UC46" };
const cases = {
  cantDefault: { type: "cantilever" },
  cantHard: { type: "cantilever", H: 3.5, slope: 15, kh: 0.1, drained: false, hw: 1.5, q: 20, bar: 16, usePassive: true, cF: 5, tb: 0.45, toe: 0.9, heel: 2.4, t2: 0.35, site: "fill" },
  cantToe: { type: "cantilever", footing: "toe", heel: 0, toe: 3.5, tb: 0.5 },
  block: { type: "block", H: 1.2, D: 0.3, Bg: 1.4, Bt: 0.6, gammaWall: 21 },
  gravity: { type: "gravity", H: 1.8, D: 0.3, Bg: 1.9, Bt: 0.5, gammaWall: 23, kh: 0.08 },
  post: { type: "post", H: 1.8 },
  postEq: { type: "post", H: 2.2, kh: 0.1, dia: 0.75, Dp: 3.5, post: "250UC73", drained: false, hw: 0.8 },
};
const out = { runs: {}, opts: {} };
for (const [k, v] of Object.entries(cases)) {
  const inp = { ...base, ...v }, r = c.runAll(inp), cost = c.costItems(inp, r.r, null);
  out.runs[k] = { input: inp, checks: r.checks.map(x => ({ id: x.id, value: x.value, limit: x.limit, util: x.util })),
    schedule: r.r.reo ? r.r.reo.schedule.map(s => ({ mark: s.mark, bars: s.bars, kg: s.kg })) : [], cut: r.r.reo?.cut ?? null,
    costItems: cost.items.map(i => ({ key: i.key, qty: i.qty, cost: i.cost })), costPerM: cost.perM };
}
const optCases = { cant: [{ type: "cantilever" }, 1], cant90: [{ type: "cantilever", H: 3.2 }, 0.9], cantToe: [{ type: "cantilever", footing: "toe", heel: 0 }, 1],
  block: [{ type: "block", H: 1.5, D: 0.3 }, 1], gravity: [{ type: "gravity", H: 2, D: 0.3 }, 1], post: [{ type: "post", H: 1.8 }, 1], cantEq: [{ type: "cantilever", H: 2, kh: 0.1, slope: 10 }, 1] };
for (const [k, [v, m]] of Object.entries(optCases)) {
  const inp = { ...base, ...v }, o = c.optimise(inp, null, m);
  out.opts[k] = { input: inp, margin: m, ok: o.ok, tried: o.tried, g: o.g, options: o.options.map(x => ({ label: x.label, g: x.g, cost: x.cost, util: x.util })) };
}
process.stdout.write(JSON.stringify(out, null, 1));
