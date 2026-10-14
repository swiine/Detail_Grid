using System.Globalization;
using System.Text;

namespace PavementBuildup.Core.Design;

public enum LineKind { Input, Step, Result, Ok, Fail, Warning }

public sealed record CalcLine(string Label, string Value, string Unit = "", LineKind Kind = LineKind.Step, string Reference = "");

public sealed class CalcResult
{
    public string Title { get; init; } = "";
    public string Reference { get; init; } = "";
    public List<CalcLine> Lines { get; } = new();
    public List<string> Notes { get; } = new();
    public Dictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Headline { get; set; } = "";
    public bool? Pass { get; set; }
    public double? ThicknessMm { get; set; }
    public string? BuildupText { get; set; }

    public List<CalcResult> Parts { get; } = new();

    public List<(string Title, string[] Columns, List<string[]> Rows)> Tables { get; } = new();

    public string TablesHeading { get; set; } = "What if";

    public CalcResult WithTitle(string title)
    {
        var c = new CalcResult
        {
            Title = title, Reference = Reference, Headline = Headline, Pass = Pass, ThicknessMm = ThicknessMm,
            BuildupText = BuildupText, TablesHeading = TablesHeading,
        };
        c.Lines.AddRange(Lines);
        c.Notes.AddRange(Notes);
        foreach (var (k, v) in Values) c.Values[k] = v;
        c.Parts.AddRange(Parts);
        c.Tables.AddRange(Tables);
        return c;
    }

    internal void In(string label, double value, string unit = "", string format = "0.###") =>
        Lines.Add(new CalcLine(label, Num(value, format), unit, LineKind.Input));

    internal void InText(string label, string value) => Lines.Add(new CalcLine(label, value, "", LineKind.Input));

    internal double Step(string key, string label, double value, string unit = "", string format = "0.###", string reference = "")
    {
        Values[key] = value;
        Lines.Add(new CalcLine(label, Num(value, format), unit, LineKind.Step, reference));
        return value;
    }

    internal double Result(string key, string label, double value, string unit = "", string format = "0.#")
    {
        Values[key] = value;
        Lines.Add(new CalcLine(label, Num(value, format), unit, LineKind.Result));
        return value;
    }

    internal void Check(string label, bool ok, string detail)
    {
        Pass = Pass is null ? ok : Pass.Value && ok;
        Lines.Add(new CalcLine(label, ok ? "OK" : "NO GOOD", detail, ok ? LineKind.Ok : LineKind.Fail));
    }

    internal void Warn(string text)
    {
        Notes.Add(text);
        Lines.Add(new CalcLine("Warning", text, "", LineKind.Warning));
    }

    public static string Num(double v, string format = "0.###") =>
        double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "-";

    public string ToText(string? project = null, string? engineer = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Title.ToUpperInvariant());
        if (Reference.Length > 0) sb.AppendLine("Reference: " + Reference);
        if (!string.IsNullOrWhiteSpace(project)) sb.AppendLine("Project: " + project);
        if (!string.IsNullOrWhiteSpace(engineer)) sb.AppendLine("Engineer: " + engineer);
        sb.AppendLine("Date: " + DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
        sb.AppendLine();
        int width = Lines.Count == 0 ? 20 : Math.Min(48, Lines.Max(l => l.Label.Length) + 2);
        foreach (var kind in new[] { "INPUTS", "CALCULATION" })
        {
            var lines = Lines.Where(l => (l.Kind == LineKind.Input) == (kind == "INPUTS")).ToList();
            if (lines.Count == 0) continue;
            sb.AppendLine(kind);
            foreach (var l in lines)
                sb.AppendLine($"  {l.Label.PadRight(width)} {l.Value} {l.Unit}".TrimEnd()
                              + (l.Reference.Length > 0 ? $"   [{l.Reference}]" : ""));
            sb.AppendLine();
        }
        sb.AppendLine("RESULT: " + Headline);
        foreach (var n in Notes.Distinct())
            sb.AppendLine("Note: " + n);
        foreach (var (title, columns, rows) in Tables)
        {
            sb.AppendLine();
            sb.AppendLine(title.ToUpperInvariant());
            sb.AppendLine("  " + string.Join(" | ", columns));
            foreach (var row in rows) sb.AppendLine("  " + string.Join(" | ", row));
        }
        foreach (var part in Parts)
        {
            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.Append(part.ToText());
        }
        return sb.ToString();
    }
}
