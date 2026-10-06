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

    // Linking words before a course or base: "ON 30mm SCREED", "LAID ON ...", "OVER EXISTING SLAB".
    [GeneratedRegex(@"^(?:on\s+top\s+of|laid\s+on|bedded\s+on|placed\s+on|onto|upon|over|on)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectorRegex();

    // "60mm PAVERS ON 30mm SCREED", "... ON EXISTING SLAB": split before a linking word followed by a thickness or "existing".
    [GeneratedRegex(@"\s+(?=(?:on\s+top\s+of|laid\s+on|bedded\s+on|placed\s+on|onto|upon|over|on)\s+(?:\d|existing\b))", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectorSplitRegex();

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

    /// <summary>Parses what it can and reports courses it could not read in <paramref name="errors"/>.</summary>
    public static List<PavementLayer> Parse(string? text, List<string> errors) => ParseBuildup(text, errors).Layers;

    /// <summary>Courses and the subgrade note (if any).</summary>
    public static BuildupParseResult ParseBuildup(string? text, List<string> errors)
    {
        var result = new BuildupParseResult();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        bool first = true;
        bool lastWasBase = false;
        foreach (var raw in Split(text).SelectMany(SplitAtConnectors))
        {
            var part = TrimEnd(BulletRegex().Replace(raw, ""));
            if (part.Length == 0)
                continue;

            // "PAVEMENT TYPE A: 60mm THICK PAVERS" → name + first course.
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

            // "ON 30mm VARIABLE SCREED", "ON EXISTING CONCRETE SLAB": the linking word isn't part of the course.
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

            // Anything in (round brackets) is a note: ignored for reading the course, kept on the label.
            var notes = BracketNotes(part);
            var core = TrimEnd(Core(part));

            int lifts = 1;
            var m = MultipleRegex().Match(core);
            if (m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1)
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
                    // What the pavement sits on: subgrade, existing slab, existing pavement...
                    result.SubgradeText = part;
                    lastWasBase = true;
                }
                else if (result.Layers.Count == 0 && result.SubgradeText is null)
                {
                    result.Name ??= part.TrimEnd(':').Trim(); // a heading before the courses
                }
                else if (lastWasBase)
                {
                    result.SubgradeText += "\n" + part;     // remark on the base, e.g. "REFER TO DRAWING ..."
                }
                else
                {
                    var last = result.Layers[^1];             // remark on the course above: second label line
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
                description = (description + " " + notes).Trim();

            result.Layers.Add(new PavementLayer { Description = description, ThicknessMm = thickness, Lifts = lifts, Reinforcement = bars });
            lastWasBase = false;
        }

        if (result.Layers.Count == 0 && errors.Count == 0)
            errors.Add("No courses with a thickness were found. Use e.g. \"60mm THICK PAVERS, 30mm THICK MORTAR, 2 x 150mm DGB20 ROAD BASE\".");
        return result;
    }

    /// <summary>Writes layers back to the one-line format accepted by <see cref="Parse(string?)"/>.</summary>
    public static string Format(IEnumerable<PavementLayer> layers) =>
        string.Join("; ", layers.Select(l =>
            ((l.Lifts > 1 ? $"{l.Lifts} x " : "") +
             $"{l.ThicknessMm.ToString("0.##", CultureInfo.InvariantCulture)} {l.Description.Replace("\n", ", ")}" +
             (l.Reinforcement.Count > 0 ? $" [{ReinforcementParser.Format(l.Reinforcement)}]" : "")).Trim()));

    /// <summary>Layers plus a custom subgrade note, so a build-up round-trips through quick entry.</summary>
    public static string Format(Buildup b)
    {
        var text = Format(b.Layers);
        bool customSubgrade = b.ShowSubgrade && !string.IsNullOrWhiteSpace(b.SubgradeText)
                              && !string.Equals(b.SubgradeText.Trim(), "SUBGRADE", StringComparison.OrdinalIgnoreCase)
                              && IsBaseNote(b.SubgradeText);
        return customSubgrade ? text + "; " + b.SubgradeText.Trim().Replace("\n", ", ") : text;
    }

    /// <summary>Text with (bracketed notes) removed and only its first line: what's used to read and hatch a course.</summary>
    public static string Core(string text)
    {
        var firstLine = text.Split('\n')[0];
        string prev;
        do { prev = firstLine; firstLine = BracketRegex().Replace(firstLine, " "); } while (firstLine != prev); // nested
        return Regex.Replace(firstLine, @"\s{2,}", " ").Trim();
    }

    private static string BracketNotes(string text) =>
        string.Join(" ", BracketRegex().Matches(text).Select(m => m.Value));

    /// <summary>What the pavement sits on: subgrade/formation, or something existing ("ON EXISTING CONCRETE SLAB").</summary>
    public static bool IsBaseNote(string text)
    {
        var t = Core(text).ToLowerInvariant();
        return IsSubgradeNote(t) || t.Contains("existing", StringComparison.Ordinal);
    }

    /// <summary>Splits "60mm PAVERS ON 30mm SCREED" before "ON", ignoring anything in brackets.</summary>
    private static IEnumerable<string> SplitAtConnectors(string segment)
    {
        // Blank out bracketed text (same length) so matches inside brackets are ignored.
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
