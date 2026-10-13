using System.Globalization;
using System.Net;
using System.Text;

namespace PavementBuildup.Core.Design;

/// <summary>
/// A design calculation as a TTW-styled HTML page (opens in any browser, prints to PDF): summary first, then
/// each check's inputs, steps and results, the "what if" tables, notes and references.
/// </summary>
public static class CalcReport
{
    public static string Html(CalcResult design, string? project, string? engineer, string? checker = null)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{E(design.Title)}{(string.IsNullOrWhiteSpace(project) ? "" : " - " + E(project))}</title><style>");
        sb.Append(@"
:root{--band:#024360;--alab:#F7F4E7;--line:#DED7C4;--fg:#1E1711;--muted:#6F6A5D;--ok:#325442;--bad:#A84933}
*{box-sizing:border-box}body{margin:0;background:#fff;color:var(--fg);font:10.5pt/1.45 'Arial Nova',Arial,sans-serif}
header{background:var(--band);color:var(--alab);padding:18px 28px;display:flex;justify-content:space-between;align-items:flex-end;gap:16px}
header h1{margin:0;font-size:18pt;font-weight:400}header .sub{color:#8ECAEF;font-size:9pt}
main{padding:18px 28px 32px;max-width:1000px}
.meta{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:4px 16px;border:1px solid var(--line);padding:10px 12px;margin-bottom:16px}
.meta b{display:block;font-size:8pt;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}
.headline{font-size:13pt;font-weight:700;padding:10px 12px;border-left:5px solid var(--ok);background:#F2F6F3;margin:0 0 16px}
.headline.bad{border-color:var(--bad);background:#FBF1EE}
h2{font-size:12pt;color:var(--band);margin:22px 0 6px;border-bottom:2px solid var(--band);padding-bottom:3px}
h3{font-size:10.5pt;margin:14px 0 4px}
table{border-collapse:collapse;width:100%;margin:4px 0 10px;font-variant-numeric:tabular-nums}
th,td{border-bottom:1px solid var(--line);padding:3px 8px;text-align:left;vertical-align:top}
th{font-size:8.5pt;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
td.v{text-align:right;white-space:nowrap}tr.result td{font-weight:700}tr.ok td{color:var(--ok);font-weight:700}
tr.fail td,tr.warn td{color:var(--bad);font-weight:700}td.ref{color:var(--muted);font-size:9pt}
ul{margin:4px 0 0 18px;padding:0}li{margin:2px 0}.muted{color:var(--muted)}
.sign{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:28px}.sign div{border-top:1px solid var(--fg);padding-top:4px;font-size:9pt;color:var(--muted)}
@media print{header{-webkit-print-color-adjust:exact;print-color-adjust:exact}.part{break-inside:avoid}}
");
        sb.Append("</style></head><body>");
        sb.Append($"<header><div><h1>{E(design.Title)}</h1><div class=\"sub\">TTW Pavement design calculation</div></div><div class=\"sub\">{E(design.Reference)}</div></header><main>");
        sb.Append("<div class=\"meta\">");
        sb.Append($"<div><b>Project</b>{E(Blank(project))}</div><div><b>Designed</b>{E(Blank(engineer))}</div><div><b>Checked</b>{E(Blank(checker))}</div>");
        sb.Append($"<div><b>Date</b>{DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}</div></div>");
        sb.Append($"<p class=\"headline{(design.Pass == false ? " bad" : "")}\">{(design.Pass == false ? "✖ " : "✔ ")}{E(design.Headline)}</p>");

        Part(sb, design, summary: true);
        foreach (var (title, columns, rows) in design.Tables)
        {
            sb.Append($"<h3>{E(title)}</h3><table><tr>{string.Concat(columns.Select(c => $"<th>{E(c)}</th>"))}</tr>");
            foreach (var row in rows)
                sb.Append($"<tr{(row[0].StartsWith("►") ? " class=\"result\"" : "")}>{string.Concat(row.Select(c => $"<td>{E(c.TrimStart('►', ' '))}</td>"))}</tr>");
            sb.Append("</table>");
        }
        if (design.Notes.Count > 0)
            sb.Append("<h3>Notes</h3><ul>" + string.Concat(design.Notes.Distinct().Select(n => $"<li>{E(n)}</li>")) + "</ul>");

        if (design.Parts.Count > 0)
        {
            sb.Append("<h2>Calculations</h2>");
            foreach (var part in design.Parts)
            {
                sb.Append("<div class=\"part\">");
                sb.Append($"<h3>{E(part.Title)} <span class=\"muted\">({E(part.Reference)})</span></h3>");
                Part(sb, part, summary: false);
                sb.Append($"<p><b>{(part.Pass == false ? "✖ " : "")}{E(part.Headline)}</b></p>");
                if (part.Notes.Count > 0)
                    sb.Append("<ul class=\"muted\">" + string.Concat(part.Notes.Distinct().Select(n => $"<li>{E(n)}</li>")) + "</ul>");
                sb.Append("</div>");
            }
        }
        sb.Append("<div class=\"sign\"><div>Designed</div><div>Checked</div><div>Approved</div></div>");
        sb.Append("<p class=\"muted\">Calculated with TTW Pavement from the company design workbooks. Results are a design aid: check them before use.</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string Blank(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s.Trim();

    private static void Part(StringBuilder sb, CalcResult r, bool summary)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var inputs = r.Lines.Where(l => l.Kind == LineKind.Input).ToList();
        var steps = r.Lines.Where(l => l.Kind != LineKind.Input).ToList();
        if (inputs.Count > 0)
        {
            sb.Append(summary ? "<h2>Inputs</h2>" : "");
            sb.Append("<table><tr><th>Input</th><th class=\"v\">Value</th><th>Unit</th></tr>");
            foreach (var l in inputs) sb.Append($"<tr><td>{E(l.Label)}</td><td class=\"v\">{E(l.Value)}</td><td>{E(l.Unit)}</td></tr>");
            sb.Append("</table>");
        }
        if (steps.Count > 0)
        {
            sb.Append(summary ? "<h2>Results</h2>" : "");
            sb.Append("<table><tr><th>Step</th><th class=\"v\">Value</th><th>Unit</th><th>Note</th></tr>");
            foreach (var l in steps)
            {
                string cls = l.Kind switch
                {
                    LineKind.Result => " class=\"result\"", LineKind.Ok => " class=\"ok\"",
                    LineKind.Fail => " class=\"fail\"", LineKind.Warning => " class=\"warn\"", _ => "",
                };
                sb.Append($"<tr{cls}><td>{E(l.Label)}</td><td class=\"v\">{E(l.Value)}</td><td>{E(l.Unit)}</td><td class=\"ref\">{E(l.Reference)}</td></tr>");
            }
            sb.Append("</table>");
        }
    }
}
