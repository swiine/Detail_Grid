using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

public sealed class BuildupParseResult
{
    public List<PavementLayer> Layers { get; } = new();

    public string? SubgradeText { get; set; }

    public string? Name { get; set; }

    public Buildup ToBuildup(string? name = null) => new()
    {
        Name = !string.IsNullOrWhiteSpace(name) ? name.Trim() : Name ?? "PAVEMENT BUILD-UP",
        Layers = Layers.Select(l => l.Clone()).ToList(),
        SubgradeText = SubgradeText ?? "SUBGRADE",
        ShowSubgrade = true,
    };
}

public static partial class BuildupParser
{
    [GeneratedRegex(@"\[(?<r>[^\]]*)\]\s*$")]
    private static partial Regex ReinforcementRegex();

    [GeneratedRegex(@"^(?<n>\d+)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)\s*(?:mm\b)?\s*[-:–]?\s*(?<d>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MultipleRegex();

    [GeneratedRegex(@"^(?<t>\d+(?:[.,]\d+)?)\s*(?:mm\b)?\s*[-:–]?\s*(?<d>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingThicknessRegex();

    [GeneratedRegex(@"^(?<d>.*?)\s*[-:–]?\s*(?<t>\d+(?:[.,]\d+)?)\s*mm$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingThicknessRegex();

    [GeneratedRegex(@"^\s*(?:[-•*–]|\(?[a-z0-9]{1,2}[.)])\s+", RegexOptions.IgnoreCase)]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^(?:on\s+top\s+of|laid\s+on|bedded\s+on|placed\s+on|onto|upon|over|on)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectorRegex();

    [GeneratedRegex(@"\s+(?:on\s+top\s+of|laid\s+on|bedded\s+on|placed\s+on|onto|upon|over|on)\s+(?=\d|existing\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectorSplitRegex();

    [GeneratedRegex(@"\[([^\]]*)\]")]
    private static partial Regex SquareBracketRegex();

    [GeneratedRegex(@"^\s*(?:\d+\s*[x×*]\s*)?\d+(?:[.,]\d+)?\s*(?:mm\b)?\s*[-:–]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingTokenRegex();

    [GeneratedRegex(@"\s*[-:–]?\s*\d+(?:[.,]\d+)?\s*mm\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTokenRegex();

    [GeneratedRegex(@"\d\s*(?:mm)?\s*@\s*\d")]
    private static partial Regex BarNotationRegex();

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex BracketRegex();

    private static readonly string[] SubgradeWords =
        { "subgrade", "sub-grade", "sub grade", "formation", "natural ground", "existing ground", "in-situ", "insitu", "in situ" };

    public static List<PavementLayer> Parse(string? text)
    {
        var errors = new List<string>();
        var layers = Parse(text, errors);
        if (errors.Count > 0)
            throw new FormatException(string.Join(Environment.NewLine, errors));
        return layers;
    }

    public static List<PavementLayer> Parse(string? text, List<string> errors) => ParseBuildup(text, errors).Layers;

    public static BuildupParseResult ParseBuildup(string? text, List<string> errors)
    {
        var result = new BuildupParseResult();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        string? bracketTitle = null;
        text = SquareBracketRegex().Replace(text, m =>
        {
            if (BarNotationRegex().IsMatch(m.Groups[1].Value))
                return m.Value;
            bracketTitle ??= m.Groups[1].Value.Trim();
            return " ";
        });

        bool first = true;
        bool lastWasBase = false;
        foreach (var raw in Split(text).SelectMany(SplitAtConnectors))
        {
            var part = TrimEnd(BulletRegex().Replace(raw, ""));
            if (part.Length == 0)
                continue;

            if (first)
            {
                first = false;
                int c = part.IndexOf(':');
                if (c > 0 && Core(part).Contains(':') && !char.IsDigit(part[0]) && part[(c + 1)..].Trim() is { Length: > 0 } rest && char.IsDigit(rest[0]))
                {
                    result.Name = part[..c].Trim();
                    part = TrimEnd(BulletRegex().Replace(rest, ""));
                }
            }

            part = TrimEnd(ConnectorRegex().Replace(part, ""));
            if (part.Length == 0)
                continue;

            List<Reinforcement> bars = new();
            var rm = ReinforcementRegex().Match(part);
            if (rm.Success)
            {
                try
                {
                    bars = ReinforcementParser.Parse(rm.Groups["r"].Value);
                }
                catch (FormatException ex)
                {
                    errors.Add(ex.Message);
                    continue;
                }
                part = TrimEnd(part[..rm.Index]);
            }

            var notes = BracketNotes(part);
            var core = TrimEnd(Core(part));

            int lifts = 1;
            var m = MultipleRegex().Match(core);
            if (m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 9)
            {
                lifts = n;
            }
            else
            {
                m = LeadingThicknessRegex().Match(core);
                if (!m.Success || m.Groups["d"].Value.Trim().Length == 0)
                {
                    var trailing = TrailingThicknessRegex().Match(core);
                    if (trailing.Success && trailing.Groups["d"].Value.Trim().Length > 0)
                        m = trailing;
                }
            }

            if (!m.Success || !TryParseNumber(m.Groups["t"].Value, out var thickness))
            {
                if (bars.Count > 0)
                {
                    errors.Add($"\"{part}\" has reinforcement but no thickness.");
                    continue;
                }
                if (IsBaseNote(core))
                {
                    result.SubgradeText = part;
                    lastWasBase = true;
                }
                else if (result.Layers.Count == 0 && result.SubgradeText is null)
                {
                    result.Name ??= part.TrimEnd(':').Trim();
                }
                else if (lastWasBase)
                {
                    result.SubgradeText += "\n" + part;
                }
                else
                {
                    var last = result.Layers[^1];
                    last.Description += "\n" + part;
                }
                continue;
            }

            var description = m.Groups["d"].Value.Trim();
            if (description.Length == 0 && notes.Length == 0)
            {
                errors.Add($"\"{part}\" has a thickness but no material description.");
                continue;
            }
            if (notes.Length > 0)
            {
                var lead = LeadingTokenRegex().Match(part);
                var trail = TrailingTokenRegex().Match(part);
                description = char.IsDigit(core[0]) && lead.Success ? part[lead.Length..].Trim()
                    : trail.Success ? part[..trail.Index].Trim()
                    : (description + " " + notes).Trim();
            }

            result.Layers.Add(new PavementLayer { Description = description, ThicknessMm = thickness, Lifts = lifts, Reinforcement = bars });
            lastWasBase = false;
        }

        if (!string.IsNullOrWhiteSpace(bracketTitle))
            result.Name = bracketTitle;
        if (result.Layers.Count == 0 && errors.Count == 0)
            errors.Add("No courses with a thickness were found. Use e.g. \"60mm THICK PAVERS, 30mm THICK MORTAR, 2 x 150mm DGB20 ROAD BASE\".");
        return result;
    }

    public static string Format(IEnumerable<PavementLayer> layers) =>
        string.Join("; ", layers.Select(l =>
            ((l.Lifts > 1 ? $"{l.Lifts} x " : "") +
             $"{l.ThicknessMm.ToString("0.##", CultureInfo.InvariantCulture)} {l.Description.Replace("\n", ", ")}" +
             (l.Reinforcement.Count > 0 ? $" [{ReinforcementParser.Format(l.Reinforcement)}]" : "")).Trim()));

    public static string Format(Buildup b)
    {
        var text = Format(b.Layers);
        bool customSubgrade = b.ShowSubgrade && !string.IsNullOrWhiteSpace(b.SubgradeText)
                              && !string.Equals(b.SubgradeText.Trim(), "SUBGRADE", StringComparison.OrdinalIgnoreCase)
                              && IsBaseNote(b.SubgradeText);
        if (customSubgrade)
            text += "; " + b.SubgradeText.Trim().Replace("\n", ", ");
        bool namedTitle = !string.IsNullOrWhiteSpace(b.Name) && !string.Equals(b.Name.Trim(), "PAVEMENT BUILD-UP", StringComparison.OrdinalIgnoreCase);
        return namedTitle ? $"[{b.Name.Trim()}] {text}" : text;
    }

    public static string Core(string text)
    {
        var firstLine = text.Split('\n')[0];
        string prev;
        do { prev = firstLine; firstLine = BracketRegex().Replace(firstLine, " "); } while (firstLine != prev);
        return Regex.Replace(firstLine, @"\s{2,}", " ").Trim();
    }

    private static string BracketNotes(string text) =>
        string.Join(" ", BracketRegex().Matches(text).Select(m => m.Value));

    public static bool IsExisting(string? text) =>
        Core(text ?? "").Contains("existing", StringComparison.OrdinalIgnoreCase);

    public static bool IsBaseNote(string text)
    {
        var t = Core(text).ToLowerInvariant();
        return IsSubgradeNote(t) || t.Contains("existing", StringComparison.Ordinal);
    }

    public static IEnumerable<string> SplitItems(string text) => Split(text).SelectMany(SplitAtConnectors);

    private static IEnumerable<string> SplitAtConnectors(string segment)
    {
        var masked = new StringBuilder(segment);
        int depth = 0;
        for (int i = 0; i < masked.Length; i++)
        {
            char c = masked[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);
            else if (depth > 0) masked[i] = '_';
        }
        int startAt = 0;
        foreach (Match m in ConnectorSplitRegex().Matches(masked.ToString()))
        {
            yield return segment[startAt..m.Index];
            startAt = m.Index + m.Length;
        }
        yield return segment[startAt..];
    }

    public static bool IsSubgradeNote(string text)
    {
        var t = text.ToLowerInvariant();
        return SubgradeWords.Any(w => t.Contains(w, StringComparison.Ordinal));
    }

    private static IEnumerable<string> Split(string text)
    {
        bool list = HasListSeparator(text);
        if (list)
            text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        var current = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);

            bool split = false;
            if (depth == 0)
            {
                if (c is ';' or '|' or '\r' or '\n')
                    split = true;
                else if (c == ',')
                    split = !(i > 0 && char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1]));
                else if (!list && c == '/' && i > 0 && char.IsWhiteSpace(text[i - 1]) && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
                    split = true;
            }

            if (split)
            {
                yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        yield return current.ToString();
    }

    private static bool HasListSeparator(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && (c == ';' || (c == ',' && !(i > 0 && char.IsDigit(text[i - 1]) && i + 1 < text.Length && char.IsDigit(text[i + 1])))))
                return true;
        }
        return false;
    }

    private static string TrimEnd(string s)
    {
        s = s.Trim();
        while (s.EndsWith('.') && !(s.Length >= 2 && char.IsDigit(s[^2])))
            s = s[..^1].TrimEnd();
        return s;
    }

    private static bool TryParseNumber(string s, out double value) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
