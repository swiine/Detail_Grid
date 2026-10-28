using System.Globalization;
using System.Net;
using System.Text;
using ETWalls.Core.Design;
using ETWalls.Core.Drawing;

namespace ETWalls.Reports;

/// <summary>
/// The calculation report as one self-contained HTML file (opens in any browser; print it to PDF):
/// 1 Summary, 2 Typical wall section, 3 Inputs, 4 Checks, 5 Reinforcement, 6 Cost estimate, 7 What if, 8 Method and notes.
/// Same layout as the preview page's report. Links to the Excel workbook saved next to it.
/// </summary>
public static class HtmlReport
{
    public const string Version = "ET Walls 1.0.0";

    /// <summary>ET logo (light on dark), drawn in SVG so the report needs no image files.</summary>
    public const string LogoSvg = """<svg class="logo" viewBox="0 0 236 40" role="img" aria-label="ET Engineering Technologies"><rect width="40" height="40" rx="3" fill="#E2621B"/><path fill="#FFFFFF" d="M8 8h24v5H8zM8 8h5v24H8zM8 17.5h12v5H8zM8 27h12v5H8zM23.5 8h5v24h-5z"/><text x="52" y="17" font-family="Arial, Helvetica, sans-serif" font-size="12.5" font-weight="700" letter-spacing="2.6" fill="#F5F4F1">ENGINEERING</text><text x="52" y="33" font-family="Arial, Helvetica, sans-serif" font-size="12.5" letter-spacing="2.6" fill="#F08A4B">TECHNOLOGIES</text></svg>""";

    const string Css = """
body{margin:0;background:#E8E7E3}
.rp{--water:#1F252B;--orange:#E2621B;--ink:#1B1D20;--muted:#64676C;--line:#D9D6CF;--ok:#325442;--ok-bg:#E3ECE5;--bad:#A84933;--bad-bg:#F5E1DA;--warn:#8A6414;--warn-bg:#F4EAD0;--select:#FCE7D9;
 max-width:900px;margin:0 auto;font-family:"Arial Nova",Arial,Helvetica,sans-serif;color:var(--ink);background:#fff;font-size:12.5px;line-height:1.45;padding:0 0 28px}
.rp *{box-sizing:border-box}
.rp-head{background:var(--water);color:#F5F4F1;display:flex;justify-content:space-between;align-items:center;gap:16px;padding:18px 28px;border-bottom:4px solid var(--orange)}
.rp-head h1{margin:0;font-size:21px;font-weight:400}.rp-head small{color:#F08A4B;display:block;margin-top:2px}.rp-head .logo{height:34px;width:auto;flex:none}
.rp-meta{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:6px 18px;padding:14px 28px;border-bottom:1px solid var(--line);background:#FBF9F2}
.rp-meta span{display:block;color:var(--muted);font-size:11px}
.rp section{padding:6px 28px}
.rp h2{color:var(--water);font-size:15px;margin:16px 0 6px;border-bottom:2px solid var(--orange);padding-bottom:3px}
.rp h3{color:var(--water);font-size:13.5px;margin:14px 0 4px}
.rp .hint{color:var(--muted);font-size:11.5px;margin:0 0 6px}
.rp table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums;margin:4px 0 8px}
.rp th,.rp td{text-align:left;padding:4px 6px;border-bottom:1px solid var(--line);vertical-align:top}
.rp th{background:var(--water);color:#F5F4F1;font-size:11.5px}
.rp th.n{text-align:right}.rp td.n,.rp td.v{text-align:right;white-space:nowrap}.rp td.v{white-space:normal}
.rp td.note{color:var(--muted);font-size:11.5px}
.rp tr.tot td{border-top:2px solid var(--orange);font-weight:700}
.rp tr.cur td{background:var(--select);font-weight:700}
.rp td.hi{background:var(--bad-bg);color:var(--bad)}
.rp .pill{display:inline-block;font-size:10.5px;font-weight:700;padding:1px 6px}
.rp .pill.ok{background:var(--ok-bg);color:var(--ok)}.rp .pill.bad{background:var(--bad-bg);color:var(--bad)}.rp .pill.info{background:var(--warn-bg);color:var(--warn)}
.rp .verdict{border:1px solid;padding:10px 12px;margin:8px 0}.rp .verdict.ok{background:var(--ok-bg);border-color:#9DB8A5}.rp .verdict.bad{background:var(--bad-bg);border-color:#D9A898}
.rp .verdict b.big{font-size:15px;display:block;margin-bottom:2px}
.rp .kv{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:0 24px}
.rp .pic{border:1px solid var(--line);padding:6px;background:#fff}.rp .pic svg{width:100%;height:auto;display:block}
.rp .mark{display:inline-grid;place-items:center;width:18px;height:18px;border:1px solid #A9440F;border-radius:50%;color:#A9440F;font-weight:700;font-size:10.5px}
.rp .warnbox{background:var(--warn-bg);color:#5E4512;padding:8px 10px;font-size:11.5px}
.rp .sign{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px;margin-top:10px}.rp .sign div{border-top:1px solid var(--ink);padding-top:4px;color:var(--muted);font-size:11px;height:54px}
.rp .tablewrap{overflow-x:auto}.rp a{color:#A9440F}
@media (max-width:640px){.rp-meta,.rp .kv,.rp .sign{grid-template-columns:minmax(0,1fr)}.rp section,.rp-head,.rp-meta{padding-left:14px;padding-right:14px}}
@media print{body{background:#fff}.rp{font-size:10.5px}.rp h2{break-after:avoid}.rp tr{break-inside:avoid}}
""";

    static string E(string s) => WebUtility.HtmlEncode(s);
    static string G(double v) => v.ToString(CultureInfo.InvariantCulture);
    static string Pct(double u) => Math.Round(u * 100).ToString(CultureInfo.InvariantCulture) + "%";
    static string Pill(bool pass) => pass ? "<span class=\"pill ok\">PASS</span>" : "<span class=\"pill bad\">FAIL</span>";

    /// <param name="excelFileName">The workbook saved next to this report (linked from the summary).</param>
    public static string Build(DesignRun run, string excelFileName, DateTime? date = null)
    {
        var inp = run.Input; var r = run.Result; var reo = r.R.Reo; var L = inp.Length;
        var failing = r.Checks.Where(c => !c.Pass).ToList();
        var sb = new StringBuilder();
        string title = $"{(string.IsNullOrWhiteSpace(inp.WallName) ? "Wall" : inp.WallName)} - calculation report";
        sb.Append($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{E(title)}</title><style>{Css}</style></head><body><div class=\"rp\">");
        sb.Append($"<div class=\"rp-head\"><div><h1>Retaining wall calculation</h1><small>{E(DesignRun.TypeName(inp.Type))} · {Version}</small></div>{LogoSvg}</div>");
        string day = (date ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        sb.Append("<div class=\"rp-meta\">");
        foreach (var (k, v) in new[] { ("Project", inp.Project), ("Job no.", inp.JobNumber), ("Wall", inp.WallName), ("Designed by", inp.DesignedBy), ("Checked by", inp.CheckedBy), ("Date / revision", day + " / A") })
            sb.Append($"<div><span>{k}</span>{E(v)}</div>");
        sb.Append("</div>");

        // 1: Summary
        sb.Append("<section><h2>1: Summary</h2>");
        sb.Append($"<div class=\"verdict {(r.Pass ? "ok" : "bad")}\"><b class=\"big\">{(r.Pass ? "All checks pass." : $"{failing.Count} check{(failing.Count > 1 ? "s fail" : " fails")}: {E(string.Join(", ", failing.Select(c => c.Name)))}.")}</b>");
        sb.Append($"{E(DesignRun.TypeName(inp.Type))}, {G(inp.H)} m retained, {G(L)} m long, {(inp.Custom ? "custom size" : "sized automatically for the lowest cost")}. Governing check: <b>{E(r.Worst.Name)}</b> at {Pct(r.Worst.Util)} of capacity.</div>");
        sb.Append("<div class=\"kv\"><table>");
        sb.Append($"<tr><td>Size</td><td class=\"v\">{E(DesignRun.SizeText(inp))}</td></tr>");
        if (reo is not null) sb.Append($"<tr><td>Main bars</td><td class=\"v\">{E(reo.Bars)}</td></tr><tr><td>Steel</td><td class=\"v\">{Math.Round(reo.TotalKg)} kg {(inp.Type == WallType.Post ? "per pier" : "per metre")}</td></tr>");
        sb.Append("</table><table>");
        sb.Append($"<tr><td>Estimated cost per metre</td><td class=\"v\"><b>{ReportRows.Money(run.Cost.PerMetre)}</b></td></tr><tr><td>Estimated cost for {G(L)} m</td><td class=\"v\"><b>{ReportRows.Money(run.TotalCost)}</b></td></tr><tr><td>Spare capacity setting</td><td class=\"v\">{E(DesignRun.MarginText(inp.Margin))}</td></tr>");
        sb.Append("</table></div>");
        sb.Append($"<p class=\"hint\">Excel workbook with the same numbers: <a href=\"{Uri.EscapeDataString(excelFileName)}\">{E(excelFileName)}</a> (same folder).</p></section>");

        // 2: Typical wall section
        var drawing = SectionBuilder.Build(inp, r);
        sb.Append($"<section><h2>2: Typical wall section</h2><div class=\"pic\">{SvgWriter.Write(drawing)}</div><p class=\"hint\">Drawn to the CAD standard (TTW.json) at the scale shown. Letters in brackets are the bar marks in section 5.</p></section>");

        // 3: Inputs
        sb.Append("<section><h2>3: Inputs</h2><div class=\"kv\">");
        foreach (var grp in ReportRows.Inputs(run).GroupBy(x => x.Group))
        {
            sb.Append($"<div><h3>{grp.Key}</h3><table>");
            foreach (var x in grp) sb.Append($"<tr><td>{E(x.Name)}</td><td class=\"n\">{E(x.Value)} {x.Unit}</td><td class=\"note\">{x.Source}</td></tr>");
            sb.Append("</table></div>");
        }
        sb.Append("</div></section>");

        // 4: Checks
        sb.Append("<section><h2>4: Checks</h2><div class=\"tablewrap\"><table><tr><th>Check</th><th class=\"n\">Value</th><th class=\"n\">Needs</th><th class=\"n\">Used</th><th></th><th>Working</th></tr>");
        foreach (var c in r.Checks)
            sb.Append($"<tr><td>{E(c.Name)}</td><td class=\"n\">{ReportRows.Fmt(c.Value, c.Unit)} {c.Unit}</td><td class=\"n\">{(c.Unit == "FoS" ? "≥ " : "≤ ")}{ReportRows.Fmt(c.Limit, c.Unit)}</td><td class=\"n\">{Pct(c.Util)}</td><td>{Pill(c.Pass)}</td><td class=\"note\">{E(c.Note)}</td></tr>");
        sb.Append("</table></div>");
        sb.Append($"<p class=\"hint\">{(inp.Type == WallType.Post ? "Sleeper bending between posts and the pier concrete are not checked." : "Global (overall slope) stability is not checked here. Use Slide or Geo5 where it matters.")}</p></section>");

        // 5: Reinforcement
        sb.Append("<section><h2>5: Reinforcement</h2>");
        if (reo is null) sb.Append("<p>Unreinforced gravity wall: no bars needed.</p>");
        else
        {
            bool post = inp.Type == WallType.Post;
            sb.Append($"<p class=\"hint\">{(post ? "Minimum pier steel (the steel post carries the bending)." : "Picked automatically: the lightest bar size and spacing that works for each face, max spacing 300 mm.")} Letters match the drawing. {(post ? "mm² per pier, kg per pier" : "mm² per metre, kg per metre of wall")}.</p>");
            sb.Append("<div class=\"tablewrap\"><table><tr><th></th><th>Where</th><th>Bars</th><th class=\"n\">Needs</th><th class=\"n\">Gives</th><th class=\"n\">kg</th><th>Note</th></tr>");
            foreach (var s in reo.Schedule)
                sb.Append($"<tr><td><span class=\"mark\">{s.Mark}</span></td><td>{E(s.Where)}</td><td><b>{E(s.Bars)}</b></td><td class=\"n\">{(s.Req > 0 ? Math.Round(s.Req).ToString(CultureInfo.InvariantCulture) : "–")}</td><td class=\"n\">{(s.Prov > 0 ? Math.Round(s.Prov).ToString(CultureInfo.InvariantCulture) : "–")}</td><td class=\"n\">{s.Kg.ToString("0.0", CultureInfo.InvariantCulture)}</td><td class=\"note\">{E(s.Note)}</td></tr>");
            sb.Append($"<tr class=\"tot\"><td></td><td colspan=\"4\">Total steel (laps included, bends ignored)</td><td class=\"n\">{Math.Round(reo.TotalKg)}</td><td class=\"note\">{(post ? "per pier" : "per metre of wall")}</td></tr></table></div>");
            if (!post) sb.Append($"<p class=\"hint\">Cover {G(inp.Cover)} mm. Bars N grade 500. Laps {G(reo.LapA)} mm for the {E(reo.Bars.Split('@')[0])} main bars (1.25 Lsy.t).</p>");
        }
        sb.Append("</section>");

        // 6: Cost estimate
        sb.Append("<section><h2>6: Cost estimate</h2><p class=\"hint\">Quantities per metre of wall and the rates in section 3. A guide for comparing options, not a quote.</p>");
        sb.Append($"<div class=\"tablewrap\"><table><tr><th>Item</th><th class=\"n\">Per metre</th><th class=\"n\">Rate</th><th class=\"n\">$ per m</th><th class=\"n\">$ for {G(L)} m</th></tr>");
        foreach (var it in run.Cost.Items)
            sb.Append($"<tr><td>{E(it.Name)}</td><td class=\"n\">{it.Qty.ToString(it.Unit == "kg" ? "0.0" : "0.00", CultureInfo.InvariantCulture)} {it.Unit}</td><td class=\"n\">{ReportRows.Rate(it.Rate)} /{it.Unit}</td><td class=\"n\">{ReportRows.Money(it.Cost)}</td><td class=\"n\">{ReportRows.Money(it.Cost * L)}</td></tr>");
        sb.Append($"<tr class=\"tot\"><td colspan=\"3\">Total</td><td class=\"n\">{ReportRows.Money(run.Cost.PerMetre)}</td><td class=\"n\">{ReportRows.Money(run.TotalCost)}</td></tr></table></div>");
        var opts = run.Sizing.Options;
        if (inp.Custom && opts.Count > 0 && run.Cost.PerMetre - opts[0].CostPerMetre > 1)
            sb.Append($"<div class=\"warnbox\">This custom size costs <b>{ReportRows.Money(run.Cost.PerMetre - opts[0].CostPerMetre)} more per metre</b> ({ReportRows.Money((run.Cost.PerMetre - opts[0].CostPerMetre) * L)} over {G(L)} m) than the cheapest size that passes.</div>");
        if (opts.Count > 0)
        {
            var current = Sizes.From(inp);
            sb.Append("<h3>Options compared</h3><p class=\"hint\">All of these pass every check. \"Used\" is the highest check.</p><div class=\"tablewrap\"><table><tr><th>Option</th><th>Size (mm)</th><th class=\"n\">$ per m</th><th class=\"n\">vs this design</th><th class=\"n\">Used</th></tr>");
            foreach (var o in opts)
            {
                var oi = o.Sizes.Apply(inp);
                bool same = Sizes.From(oi) == current;
                double diff = o.CostPerMetre - run.Cost.PerMetre;
                sb.Append($"<tr class=\"{(same ? "cur" : "")}\"><td>{E(o.Label)}</td><td>{E(DesignRun.SizeText(oi))}</td><td class=\"n\">{ReportRows.Money(o.CostPerMetre)}</td><td class=\"n\">{(Math.Abs(diff) < 1 ? "–" : (diff > 0 ? "+" : "−") + ReportRows.Money(Math.Abs(diff)))}</td><td class=\"n\">{Pct(o.Util)}</td></tr>");
            }
            sb.Append("</table></div>");
        }
        sb.Append("</section>");

        // 7: What if
        var wt = WhatIf.Size(inp);
        sb.Append("<section><h2>7: What if</h2><p class=\"hint\">Percent of capacity used. Over 100% fails.</p><div class=\"tablewrap\"><table>");
        sb.Append($"<tr><th>{E(wt.Label)}</th>{string.Concat(wt.Columns.Select(c => $"<th class=\"n\">{E(c)}</th>"))}<th class=\"n\">Cost /m</th><th>Result</th></tr>");
        foreach (var row in wt.Rows)
            sb.Append($"<tr class=\"{(row.Current ? "cur" : "")}\"><td class=\"n\">{row.Value.ToString("0.00", CultureInfo.InvariantCulture)}{(row.Current ? " ◀" : "")}</td>{string.Concat(row.Checks.Select(c => $"<td class=\"n{(c.Pass ? "" : " hi")}\">{Pct(c.Util)}</td>"))}<td class=\"n\">{ReportRows.Money(row.CostPerMetre)}</td><td>{Pill(row.Pass)}</td></tr>");
        sb.Append("</table></div>");
        var phi = WhatIf.BackfillPhi(inp);
        sb.Append($"<div class=\"tablewrap\"><table><tr><th>Backfill φ</th>{string.Concat(phi.Select(p => $"<th class=\"n\">{G(p.Phi)}°</th>"))}</tr>");
        sb.Append($"<tr><td>Worst check</td>{string.Concat(phi.Select(p => $"<td class=\"n{(p.Pass ? "" : " hi")}\">{Pct(p.Worst.Util)}</td>"))}</tr>");
        sb.Append($"<tr><td>Governs</td>{string.Concat(phi.Select(p => $"<td class=\"n\" style=\"font-size:11px\">{E(p.Worst.Name.Replace(" (earthquake)", " EQ"))}</td>"))}</tr></table></div></section>");

        // 8: Method and notes
        sb.Append($"<section><h2>8: Method and notes</h2><div class=\"warnbox\">{E(DesignRun.MethodNote)}</div><p>An engineer must check and sign off every result before it is used.</p>");
        sb.Append($"<div class=\"sign\"><div>Designed: {E(inp.DesignedBy)}</div><div>Checked: {E(inp.CheckedBy)}</div><div>Date</div></div></section>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}
