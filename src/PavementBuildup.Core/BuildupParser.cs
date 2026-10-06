using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

/// <summary>Courses read from text, plus a subgrade note if the text ended with one.</summary>
public sealed class BuildupParseResult
{
    public List<PavementLayer> Layers { get; } = new();

    /// <summary>"SUBGRADE COMPACTED TO 98% STANDARD MDD" etc., or null when the text had no subgrade note.</summary>
    public string? SubgradeText { get; set; }

    /// <summary>A heading before the courses ("PAVEMENT TYPE A: ..."), or null.</summary>
    public string? Name { get; set; }

    public Buildup ToBuildup(string? name = null) => new()
    {
        Name = !string.IsNullOrWhiteSpace(name) ? name.Trim() : Name ?? "PAVEMENT BUILD-UP",
        Layers = Layers.Select(l => l.Clone()).ToList(),
        SubgradeText = SubgradeText ?? "SUBGRADE",
        ShowSubgrade = true,
    };
}

/// <summary>
/// Parses a build-up written the way it appears in specs and notes, top to bottom, e.g.
/// <c>60mm THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS), 30mm THICK MORTAR,
/// 2 x 150mm THICK LAYERS OF DGB20 ROAD BASE, SUBGRADE COMPACTED TO 98% STANDARD MDD.</c>
/// <list type="bullet">
/// <item>Courses are separated by commas, ';', '|', new lines or " / ". Commas inside (...) or [...]
/// and in decimals ("37,5") don't split.</item>
/// <item>The thickness may lead ("40mm SMA", "40 SMA") or trail ("SMA 40mm").</item>
/// <item>"2 x 150mm ..." is one course of two 150mm layers.</item>
/// <item>An item with no thickness that describes the subgrade/formation becomes the subgrade label.</item>
/// <item>Reinforcement goes in square brackets after a course: <c>250 PQC [H16@150 c50]</c>.</item>
/// </list>
/// The description is kept exactly as written, so labels read the way the spec does.
/// </summary>
public static partial class BuildupParser
{
    // Trailing "[...]" holding reinforcement.
    [GeneratedRegex(@"\[(?<r>[^\]]*)\]\s*$")]
    private static partial Regex ReinforcementRegex();

    // "2 x 150mm THICK LAYERS OF ...", "2x150 ...", "3 × 100 mm ..."
    [GeneratedRegex(@"^(?<n>\d+)\s*[x×*]\s*(?<t>\d+(?:[.,]\d+)?)\s*(?:mm\b)?\s*[-:–]?\s*(?<d>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MultipleRegex();

    // "40 SMA", "40mm SMA", "40 mm - SMA", "40.5mm: SMA"
    [GeneratedRegex(@"^(?<t>\d+(?:[.,]\d+)?)\s*(?:mm\b)?\s*[-:–]?\s*(?<d>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingThicknessRegex();

    // "SMA 10 surface 40mm", "SMA - 40 mm"
    [GeneratedRegex(@"^(?<d>.*?)\s*[-:–]?\s*(?<t>\d+(?:[.,]\d+)?)\s*mm$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingThicknessRegex();

    // Note numbering / bullets: "1. ", "2) ", "a) ", "(b) ", "- ", "• "
    [GeneratedRegex(@"^\s*(?:[-•*–]|\(?[a-z0-9]{1,2}[.)])\s+", RegexOptions.IgnoreCase)]
    private static partial Regex BulletRegex();

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

    /// <summary>Parses what it can and reports courses it could not read in <paramref name="errors"/>.</summary>
    public static List<PavementLayer> Parse(string? text, List<string> errors) => ParseBuildup(text, errors).Layers;

    /// <summary>Courses and the subgrade note (if any).</summary>
    public static BuildupParseResult ParseBuildup(string? text, List<string> errors)
    {
        var result = new BuildupParseResult();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        bool first = true;
        foreach (var raw in Split(text))
        {
            var part = TrimEnd(BulletRegex().Replace(raw, ""));
            if (part.Length == 0)
                continue;

            // "PAVEMENT TYPE A: 60mm THICK PAVERS" → name + first course.
            if (first)
            {
                first = false;
                int colon = part.IndexOf(':');
                if (colon > 0 && !char.IsDigit(part[0]) && part[(colon + 1)..].Trim() is { Length: > 0 } rest && char.IsDigit(rest[0]))
                {
                    result.Name = part[..colon].Trim();
                    part = TrimEnd(BulletRegex().Replace(rest, ""));
                }
            }

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

            int lifts = 1;
            var m = MultipleRegex().Match(part);
            if (m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1)
            {
                lifts = n;
            }
            else
            {
                m = LeadingThicknessRegex().Match(part);
                if (!m.Success || m.Groups["d"].Value.Trim().Length == 0)
                {
                    var trailing = TrailingThicknessRegex().Match(part);
                    if (trailing.Success && trailing.Groups["d"].Value.Trim().Length > 0)
                        m = trailing;
                }
            }

            if (!m.Success || !TryParseNumber(m.Groups["t"].Value, out var thickness))
            {
                if (bars.Count == 0 && IsSubgradeNote(part))
                {
                    result.SubgradeText = part;
                    continue;
                }
                if (result.Layers.Count == 0 && result.Name is null && bars.Count == 0)
                {
                    result.Name = part.TrimEnd(':').Trim(); // a heading line before the courses
                    continue;
                }
                errors.Add($"Could not find a thickness in \"{part}\". Use e.g. \"40mm SMA surface course\" or \"2 x 150mm road base\".");
                continue;
            }

            var description = m.Groups["d"].Value.Trim();
            if (description.Length == 0)
            {
                errors.Add($"\"{part}\" has a thickness but no material description.");
                continue;
            }

            result.Layers.Add(new PavementLayer { Description = description, ThicknessMm = thickness, Lifts = lifts, Reinforcement = bars });
        }

        if (result.Layers.Count == 0 && errors.Count == 0)
            errors.Add("No courses with a thickness were found. Use e.g. \"60mm THICK PAVERS, 30mm THICK MORTAR, 2 x 150mm DGB20 ROAD BASE\".");
        return result;
    }

    /// <summary>Writes layers back to the one-line format accepted by <see cref="Parse(string?)"/>.</summary>
    public static string Format(IEnumerable<PavementLayer> layers) =>
        string.Join("; ", layers.Select(l =>
            ((l.Lifts > 1 ? $"{l.Lifts} x " : "") +
             $"{l.ThicknessMm.ToString("0.##", CultureInfo.InvariantCulture)} {l.Description}" +
             (l.Reinforcement.Count > 0 ? $" [{ReinforcementParser.Format(l.Reinforcement)}]" : "")).Trim()));

    /// <summary>Layers plus a custom subgrade note, so a build-up round-trips through quick entry.</summary>
    public static string Format(Buildup b)
    {
        var text = Format(b.Layers);
        bool customSubgrade = b.ShowSubgrade && !string.IsNullOrWhiteSpace(b.SubgradeText)
                              && !string.Equals(b.SubgradeText.Trim(), "SUBGRADE", StringComparison.OrdinalIgnoreCase)
                              && IsSubgradeNote(b.SubgradeText);
        return customSubgrade ? text + "; " + b.SubgradeText.Trim() : text;
    }

    public static bool IsSubgradeNote(string text)
    {
        var t = text.ToLowerInvariant();
        return SubgradeWords.Any(w => t.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>
    /// Splits on ';', '|', new lines, " / " and commas, except inside (...) or [...] and commas between
    /// digits (decimal commas, "1,000"). When the text uses commas or semicolons to separate courses,
    /// new lines are just wrapping (as in a note copied from a drawing) and don't split.
    /// </summary>
    private static IEnumerable<string> Split(string text)
    {
        if (HasListSeparator(text))
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
                else if (c == '/' && i > 0 && char.IsWhiteSpace(text[i - 1]) && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
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

    /// <summary>Trims whitespace and a sentence-ending full stop ("... MDD." → "... MDD"), but not a decimal point.</summary>
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
