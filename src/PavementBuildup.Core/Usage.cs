using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core;

public sealed record UsageEvent(string Id, DateTime Time, string Tool, string App, string User, int Count = 1, string? Detail = null);

public static class UsageTools
{
    public const string DetailPlaced = "Pavement detail placed";
    public const string DetailEdited = "Pavement detail edited";
    public const string DetailFromNote = "Detail from a note";
    public const string QuickPlace = "Quick place";
    public const string JointsDrawn = "Joint layout drawn";
    public const string StandardSaved = "CAD standard saved";
    public const string DxfSaved = "Detail DXF saved";
    public const string PdfSaved = "Detail PDF saved";
    public const string BatchDetail = "Batch detail (per detail)";
    public const string CalcReport = "Design calculation report";
    public const string DesignSaved = "Design saved";
    public const string JointPlan = "Joint plan (DXF + PDF)";
    public const string RegisterExport = "Project register exported";
    public const string Comparison = "Options comparison";
}

public static class UsageLog
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "usage.jsonl");

    public static string? Override { get; set; }

    public static bool Enabled { get; set; } = true;

    public static void Record(string tool, string app, int count = 1, string? detail = null)
    {
        if (count <= 0 || !Enabled || (Override is null && !UserSettings.Current.CountUsage)) return;
        try
        {
            var path = Override ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var e = new UsageEvent(Guid.NewGuid().ToString("N"), DateTime.Now, tool, app, Environment.UserName, count, detail);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(e, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    public static List<UsageEvent> Read(IEnumerable<string> paths)
    {
        var events = new Dictionary<string, UsageEvent>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                for (string? line; (line = reader.ReadLine()) is not null;)
                {
                    if (line.Trim().Length == 0) continue;
                    try
                    {
                        if (JsonSerializer.Deserialize<UsageEvent>(line) is { } e && e.Tool.Length > 0) events[e.Id] = e;
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return events.Values.OrderBy(e => e.Time).ToList();
    }
}

public sealed class TimeSaved
{
    public const string FileName = "TTW_TimeSaved.json";

    public string Note { get; set; } = "Minutes saved each time a tool is used, compared with doing it by hand. Estimates: adjust them to suit the team.";
    public Dictionary<string, double> Minutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public string? SourcePath { get; private set; }

    public static TimeSaved CreateDefault() => new()
    {
        Minutes = new(StringComparer.OrdinalIgnoreCase)
        {
            [UsageTools.DetailPlaced] = 20,
            [UsageTools.DetailEdited] = 10,
            [UsageTools.DetailFromNote] = 20,
            [UsageTools.QuickPlace] = 15,
            [UsageTools.JointsDrawn] = 45,
            [UsageTools.StandardSaved] = 5,
            [UsageTools.DxfSaved] = 20,
            [UsageTools.PdfSaved] = 15,
            [UsageTools.BatchDetail] = 15,
            [UsageTools.CalcReport] = 60,
            [UsageTools.DesignSaved] = 5,
            [UsageTools.JointPlan] = 30,
            [UsageTools.RegisterExport] = 30,
            [UsageTools.Comparison] = 45,
        },
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true,
    };

    public static TimeSaved LoadCompany()
    {
        var path = StandardStore.FindSupportFile(FileName);
        if (path is null) return CreateDefault();
        try
        {
            var file = JsonSerializer.Deserialize<TimeSaved>(File.ReadAllText(path), Options) ?? CreateDefault();
            var merged = CreateDefault();
            foreach (var (tool, minutes) in file.Minutes) merged.Minutes[tool] = minutes;
            merged.Note = file.Note;
            merged.SourcePath = path;
            return merged;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return CreateDefault(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        SourcePath = path;
    }

    public double For(string tool) => Minutes.TryGetValue(tool, out var m) ? m : 0;
}

public sealed record UsageRow(string Name, int Uses, double Hours);

public sealed record UsageSummary(
    DateTime? From, DateTime? To, int Uses, double Hours, int People,
    IReadOnlyList<UsageRow> ByTool, IReadOnlyList<UsageRow> ByMonth, IReadOnlyList<UsageRow> ByPerson, IReadOnlyList<UsageRow> ByApp,
    IReadOnlyDictionary<string, double> MinutesPerUse);

public static class UsageReport
{
    public static UsageSummary Summarise(IEnumerable<UsageEvent> events, TimeSaved minutes, DateTime? from = null, DateTime? to = null)
    {
        var list = events.Where(e => (from is null || e.Time >= from) && (to is null || e.Time < to)).ToList();
        double Hours(IEnumerable<UsageEvent> es) => es.Sum(e => e.Count * minutes.For(e.Tool)) / 60;
        List<UsageRow> By(Func<UsageEvent, string> key, bool byHours = true)
        {
            var rows = list.GroupBy(key).Select(g => new UsageRow(g.Key, g.Sum(e => e.Count), Hours(g)));
            return (byHours ? rows.OrderByDescending(r => r.Hours).ThenBy(r => r.Name) : rows.OrderBy(r => r.Name)).ToList();
        }
        return new UsageSummary(from, to, list.Sum(e => e.Count), Hours(list), list.Select(e => e.User).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            By(e => e.Tool), By(e => e.Time.ToString("yyyy-MM", CultureInfo.InvariantCulture), byHours: false), By(e => e.User), By(e => e.App),
            list.Select(e => e.Tool).Distinct().ToDictionary(t => t, minutes.For));
    }

    public static string Html(UsageSummary s, string? title = null)
    {
        static string E(string? x) => WebUtility.HtmlEncode(x ?? "");
        string period = s.From is null && s.To is null ? "All time"
            : $"{(s.From is { } f ? f.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "start")} to {(s.To is { } t ? t.AddDays(-1).ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "today")}";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{E(title ?? "TTW Pavement - time saved")}</title><style>");
        sb.Append(@"
:root{--band:#024360;--alab:#F7F4E7;--line:#DED7C4;--fg:#1E1711;--muted:#6F6A5D;--ok:#325442}
*{box-sizing:border-box}body{margin:0;background:#fff;color:var(--fg);font:10.5pt/1.45 'Arial Nova',Arial,sans-serif}
header{background:var(--band);color:var(--alab);padding:18px 28px}header h1{margin:0;font-size:18pt;font-weight:400}header .sub{color:#8ECAEF;font-size:9pt}
main{padding:18px 28px 32px;max-width:1000px}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin:0 0 18px}
.tile{border:1px solid var(--line);border-left:5px solid var(--band);padding:10px 14px}.tile b{display:block;font-size:20pt;color:var(--band)}
.tile span{font-size:8.5pt;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}
h2{font-size:12pt;color:var(--band);margin:22px 0 6px;border-bottom:2px solid var(--band);padding-bottom:3px}
table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}th,td{border-bottom:1px solid var(--line);padding:4px 8px;text-align:left}
th{font-size:8.5pt;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}td.v,th.v{text-align:right}
.bar{height:8px;background:var(--band);border-radius:2px}.muted{color:var(--muted)}
@media print{header,.tile{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
");
        sb.Append("</style></head><body>");
        sb.Append($"<header><h1>{E(title ?? "TTW Pavement - time saved")}</h1><div class=\"sub\">{E(period)}  ·  made {DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}</div></header><main>");
        sb.Append("<div class=\"tiles\">");
        sb.Append($"<div class=\"tile\"><b>{s.Hours:0.#}</b><span>hours saved</span></div>");
        sb.Append($"<div class=\"tile\"><b>{s.Hours / 7.6:0.#}</b><span>working days saved (7.6 h)</span></div>");
        sb.Append($"<div class=\"tile\"><b>{s.Uses}</b><span>tool uses</span></div>");
        sb.Append($"<div class=\"tile\"><b>{s.People}</b><span>people</span></div>");
        sb.Append("</div>");

        void Table(string heading, string first, IReadOnlyList<UsageRow> rows, bool bars, bool minutesColumn = false)
        {
            if (rows.Count == 0) return;
            double max = rows.Max(r => r.Hours);
            sb.Append($"<h2>{E(heading)}</h2><table><tr><th>{E(first)}</th><th class=\"v\">Uses</th>");
            if (minutesColumn) sb.Append("<th class=\"v\">Min / use</th>");
            sb.Append("<th class=\"v\">Hours saved</th>");
            if (bars) sb.Append("<th style=\"width:30%\"></th>");
            sb.Append("</tr>");
            foreach (var r in rows)
            {
                sb.Append($"<tr><td>{E(r.Name)}</td><td class=\"v\">{r.Uses}</td>");
                if (minutesColumn) sb.Append($"<td class=\"v\">{(s.MinutesPerUse.TryGetValue(r.Name, out var m) ? m.ToString("0.#", CultureInfo.InvariantCulture) : "-")}</td>");
                sb.Append($"<td class=\"v\">{r.Hours:0.0}</td>");
                if (bars) sb.Append($"<td><div class=\"bar\" style=\"width:{(max > 0 ? r.Hours / max * 100 : 0):0}%\"></div></td>");
                sb.Append("</tr>");
            }
            sb.Append($"<tr><td><b>Total</b></td><td class=\"v\"><b>{rows.Sum(r => r.Uses)}</b></td>{(minutesColumn ? "<td></td>" : "")}<td class=\"v\"><b>{rows.Sum(r => r.Hours):0.0}</b></td>{(bars ? "<td></td>" : "")}</tr>");
            sb.Append("</table>");
        }
        if (s.Uses == 0) sb.Append("<p class=\"muted\">No tool use recorded in this period yet.</p>");
        Table("By tool", "Tool", s.ByTool, bars: true, minutesColumn: true);
        Table("By month", "Month", s.ByMonth, bars: true);
        Table("By person", "Person", s.ByPerson, bars: true);
        Table("By program", "Program", s.ByApp, bars: false);
        sb.Append("<p class=\"muted\">Hours saved = uses x minutes saved per use (estimates in _support\\_Standards\\TTW_TimeSaved.json, set by the team). " +
                  "Each person's use is logged on their own PC; reports can combine several people's logs.</p>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}
