using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

/// <summary>A build-up read from free text, plus what the reader had to interpret to get there.</summary>
public sealed class SmartReadResult
{
    public BuildupParseResult Parsed { get; init; } = new();

    /// <summary>Plain-English notes on what was interpreted ("Converted 0.15m to 150mm", ...), for the user to check.</summary>
    public List<string> Notes { get; } = new();

    /// <summary>The tidied text actually read (shown in the quick-entry box, editable).</summary>
    public string Text { get; init; } = "";
}

/// <summary>
/// The built-in build-up reader: turns build-ups written any reasonable way into courses, without any
/// online AI. It tidies the text into the strict form <see cref="BuildupParser"/> reads, step by step,
/// and says what it interpreted. Already-tidy text passes through unchanged.
/// </summary>
/// <remarks>
/// Handles: thickness anywhere in a line ("DGB20 ROAD BASE 150mm THICK", "Basecourse - thickness 150"),
/// metres / cm / inches, "two layers of 150mm", "150mm x 2", "300mm in 2 lifts", ranges ("100-150mm"),
/// min./nom./approx., sentences and "then / followed by / underlain by", "comprising / consisting of",
/// tables pasted from Excel or Word, build-ups written bottom-up, membranes and seals with no thickness,
/// and total lines. Anything in (round brackets) stays a label note and isn't interpreted.
/// </remarks>
public static partial class SmartReader
{
    public static SmartReadResult Read(string? text, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SmartReadResult();

        var notes = new List<string>();
        var t = NormaliseCharacters(text);
        bool bottomUp = false;
        t = BottomUpCueRegex().Replace(t, _ => { bottomUp = true; return " "; });
        t = Tables(t, notes);
        t = Lists(t);
        t = IntroRegex().Replace(t, ": ");
        t = Sentences(t);
        t = OutsideBrackets(t, UnitRegex(), m => Unit(m, notes));
        t = NumberWordRegex().Replace(t, m => NumberWords[m.Groups[1].Value.ToLowerInvariant()]);
        t = OutsideBrackets(t, RangeRegex(), m =>
        {
            notes.Add($"Read the range {m.Value.Trim()} as {Num(m.Groups[2].Value)}mm (range kept as a note).");
            return $"{Num(m.Groups[2].Value)}mm ({Num(m.Groups[1].Value)}-{Num(m.Groups[2].Value)}mm)";
        });
        t = Multiples(t, notes);
        // "... over subgrade", "on existing slab": what it sits on is its own item.
        t = OutsideBrackets(t, BaseConnectorRegex(), _ => "; ");
        t = OutsideBrackets(t, MembraneConnectorRegex(), _ => "; ");

        // Item by item: put the thickness first, membranes as 0mm, drop totals.
        var items = new List<string>();
        int firstBase = -1, firstCourse = -1;
        foreach (var raw in BuildupParser.SplitItems(t))
        {
            var item = FixItem(raw, notes, out var kind);
            if (item.Length == 0)
                continue;
            if (kind == ItemKind.Course && firstCourse < 0) firstCourse = items.Count;
            if (kind == ItemKind.Base && firstBase < 0) firstBase = items.Count;
            items.Add(item);
        }

        // Subgrade first, surface last: written bottom-up.
        if (firstBase >= 0 && firstCourse >= 0 && firstBase < firstCourse)
            bottomUp = true;

        var tidy = string.Join("; ", items);
        var parsed = BuildupParser.ParseBuildup(tidy, errors);
        if (parsed.Name is { } name && GenericHeadingRegex().IsMatch(name))
            parsed.Name = null;
        if (bottomUp && parsed.Layers.Count > 1)
        {
            parsed.Layers.Reverse();
            notes.Add("Written bottom-up: the layers have been put in top-to-bottom order.");
        }

        var result = new SmartReadResult { Parsed = parsed, Text = BottomUpText(parsed, tidy, bottomUp) };
        result.Notes.AddRange(notes.Distinct());
        return result;
    }

    // ------------------------------------------------------------------ steps ----------------------

    private static string NormaliseCharacters(string s) => s
        .Replace('×', 'x').Replace('–', '-').Replace('—', '-').Replace('−', '-')
        .Replace(' ', ' ').Replace('‘', '\'').Replace('’', '\'')
        .Replace('“', '"').Replace('”', '"').Replace('•', '-');

    /// <summary>Rows from Excel/Word (tab or | separated): "Pavers | 60" → "60mm Pavers". A header like "Thickness (m)" sets the unit.</summary>
    private static string Tables(string s, List<string> notes)
    {
        var lines = s.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count(l => l.Contains('\t') || l.Contains('|')) < 2)
            return s;

        double factor = 1;
        var rows = new List<string>();
        foreach (var line in lines)
        {
            var cells = line.Split('\t', '|').Select(c => c.Trim())
                .Where(c => c.Length > 0 && !EmptyCellRegex().IsMatch(c)).ToList();
            if (cells.Count == 0)
                continue;
            int numeric = cells.FindIndex(c => TableNumberRegex().IsMatch(c));
            if (numeric < 0)
            {
                bool header = cells.Any(c => HeaderWordRegex().IsMatch(c));
                if (header)
                {
                    if (cells.Any(c => MetreHeaderRegex().IsMatch(c))) factor = 1000;
                    else if (cells.Any(c => CentimetreHeaderRegex().IsMatch(c))) factor = 10;
                    continue;
                }
                rows.Add(string.Join(" ", cells)); // e.g. "Subgrade | compacted to 98%" or a title
                continue;
            }

            var m = TableNumberRegex().Match(cells[numeric]);
            double value = double.Parse(m.Groups["v"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            string unit = m.Groups["u"].Value.ToLowerInvariant();
            double mm = unit switch { "m" => value * 1000, "cm" => value * 10, "mm" => value, _ => value * factor };
            var description = string.Join(" ", cells.Where((_, i) => i != numeric));
            rows.Add($"{Num(mm)}mm {description}".Trim());
        }
        notes.Add("Read the text as a table (one layer per row).");
        return string.Join("; ", rows);
    }

    /// <summary>
    /// A numbered or bulleted list (2+ lines starting "1." / "a)" / "-"): each numbered line is an item, and
    /// lines without a number are wrapped text belonging to the item above (or a heading before the list).
    /// </summary>
    private static string Lists(string s)
    {
        var lines = s.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count(l => ListLineRegex().IsMatch(l)) < 2)
            return s;

        var items = new List<string>();
        bool inList = false;
        foreach (var line in lines)
        {
            if (ListLineRegex().IsMatch(line))
            {
                items.Add(ListLineRegex().Replace(line, ""));
                inList = true;
            }
            else if (inList && items.Count > 0)
                items[^1] += " " + line;           // wrapped text
            else
                items.Add(line);                   // heading before the list
        }
        return string.Join("; ", items.Select(i => i.Trim().TrimEnd(',', ';')));
    }

    /// <summary>Sentences and "then / followed by / underlain by" separate layers.</summary>
    private static string Sentences(string s)
    {
        s = OutsideBrackets(s, SentenceEndRegex(), _ => "; ");
        return OutsideBrackets(s, SequenceWordRegex(), _ => "; ");
    }

    private static string Unit(Match m, List<string> notes)
    {
        var value = double.Parse(m.Groups["v"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = m.Groups["u"].Value.ToLowerInvariant();
        double? mm = unit switch
        {
            "mm" or "millimetre" or "millimetres" or "millimeter" or "millimeters" => value,
            "cm" or "centimetre" or "centimetres" or "centimeter" or "centimeters" => value * 10,
            "m" or "metre" or "metres" or "meter" or "meters" => value <= 3 ? value * 1000 : null, // >3 m isn't a pavement layer
            "inch" or "inches" or "\"" => Math.Round(value * 25.4),
            _ => null,
        };
        if (mm is null)
            return m.Value;
        if (!unit.StartsWith("mm") && !unit.StartsWith("millim"))
            notes.Add($"Converted {m.Value.Trim()} to {Num(mm.Value)}mm.");
        return $"{Num(mm.Value)}mm";
    }

    /// <summary>"2 layers of 150mm", "two 150mm layers", "150mm x 2", "300mm in 2 lifts" → "2 x 150mm".</summary>
    private static string Multiples(string s, List<string> notes)
    {
        s = LayersOfRegex().Replace(s, m => Lifts(m.Value, m.Groups["n"].Value, m.Groups["t"].Value, notes));
        s = CountThenThicknessRegex().Replace(s, m => Lifts(m.Value, m.Groups["n"].Value, m.Groups["t"].Value, notes));
        s = ThicknessTimesRegex().Replace(s, m => Lifts(m.Value, m.Groups["n"].Value, m.Groups["t"].Value, notes));
        s = TotalInLayersRegex().Replace(s, m =>
        {
            double total = double.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture);
            int n = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (n < 1) return m.Value;
            notes.Add($"Read {m.Value.Trim()} as {n} x {Num(total / n)}mm (total split equally, check this).");
            return $"{n} x {Num(total / n)}mm ";
        });
        return s;
    }

    private static string Lifts(string original, string count, string thickness, List<string> notes)
    {
        int n = int.Parse(count, CultureInfo.InvariantCulture);
        if (n < 1) return original;
        var result = $"{n} x {Num(double.Parse(thickness, CultureInfo.InvariantCulture))}mm ";
        if (!original.TrimStart().StartsWith(result.Trim(), StringComparison.OrdinalIgnoreCase))
            notes.Add($"Read {original.Trim()} as {result.Trim()}.");
        return result;
    }

    private enum ItemKind { Course, Base, Other }

    /// <summary>One item: thickness to the front, qualifiers into a note, membranes as 0mm, totals dropped.</summary>
    private static string FixItem(string raw, List<string> notes, out ItemKind kind)
    {
        kind = ItemKind.Other;
        var item = StripBullet(raw.Trim()).TrimEnd('.', ';', ',').Trim();
        item = ConnectorRegex().Replace(item, "").Trim(); // "over 150mm FCR" → "150mm FCR"
        item = ArticleRegex().Replace(item, "").Trim();   // "the subgrade" → "subgrade"
        if (item.Length == 0)
            return "";

        // "HEADING: 60mm ..." is read by the parser as name + first course.
        var heading = HeadingRegex().Match(Mask(item));
        if (heading.Success)
        {
            var head = item[..heading.Length].Trim().TrimEnd(':').Trim();
            var rest = FixItem(item[heading.Length..], notes, out kind);
            return rest.Length == 0 ? head + ":" : $"{head}: {rest}";
        }

        var core = BuildupParser.Core(item);
        if (TotalLineRegex().IsMatch(core))
        {
            notes.Add($"Ignored the total line \"{item}\".");
            return "";
        }

        // min. / nom. / approx. before a thickness: keep as a note on the label.
        string qualifier = "";
        item = OutsideBrackets(item, QualifierRegex(), m => { qualifier = m.Groups[1].Value.Trim().TrimEnd('.').ToUpperInvariant(); return ""; });
        if (qualifier.Length > 0)
            item = $"{item.Trim()} ({qualifier}.)";

        var lead = LeadingThicknessRegex().Match(StripBullet(item));
        if (lead.Success)
        {
            kind = ItemKind.Course;
            return InLayers(item, notes);
        }

        // Thickness further along the line: move it to the front.
        var masked = Mask(item);
        var tm = ThicknessAnywhereRegex().Match(masked);
        if (tm.Success)
        {
            var before = item[..tm.Index];
            var after = item[(tm.Index + tm.Length)..];
            bool thick = false;
            before = ThicknessWordBeforeRegex().Replace(before, _ => { thick = true; return ""; });
            if (LeadInRegex().IsMatch(before))
                before = ""; // "the pavement will be 40mm of SMA" → "40mm SMA"
            after = LeadingOfRegex().Replace(after, "");
            after = ThicknessWordAfterRegex().Replace(after, _ => { thick = true; return ""; });
            var description = Regex.Replace($"{before.Trim().TrimEnd('-', ':', '=', ',', '@').Trim()} {after.Trim().TrimStart('-', ':', '=', ',').Trim()}", @"\s{2,}", " ").Trim();
            if (description.Length > 0)
            {
                var thickWord = thick ? (IsMostlyUpper(item) ? "THICK " : "thick ") : "";
                var rebuilt = $"{tm.Value.Trim()} {thickWord}{description}";
                notes.Add($"Read \"{item}\" as \"{rebuilt}\".");
                kind = ItemKind.Course;
                return InLayers(rebuilt, notes);
            }
        }

        // "Basecourse - 150" / "Pavers: 60" (a bare number after a separator).
        var bare = TrailingBareNumberRegex().Match(Mask(item));
        if (bare.Success)
        {
            var desc = item[..bare.Index].Trim();
            var rebuilt = $"{Num(double.Parse(bare.Groups["v"].Value, CultureInfo.InvariantCulture))}mm {desc}";
            notes.Add($"Read \"{item}\" as \"{rebuilt}\" (number taken as mm).");
            kind = ItemKind.Course;
            return rebuilt;
        }

        var plain = BuildupParser.Core(ConnectorRegex().Replace(item, ""));
        if (BuildupParser.IsBaseNote(plain))
        {
            kind = ItemKind.Base;
            return item;
        }
        if (MembraneRegex().IsMatch(plain) && !LooksLikeRemark(plain))
        {
            notes.Add($"Treated \"{item}\" as a membrane/seal (drawn as a line).");
            kind = ItemKind.Course;
            return "0mm " + ConnectorRegex().Replace(item, "");
        }
        return item;
    }

    private static bool LooksLikeRemark(string s) => RemarkRegex().IsMatch(s);

    /// <summary>"300mm DGB20 compacted in 2 layers" → "2 x 150mm DGB20".</summary>
    private static string InLayers(string item, List<string> notes)
    {
        var phrase = InLayersAnywhereRegex().Match(Mask(item));
        var single = SingleLeadingThicknessRegex().Match(item);
        if (!phrase.Success || !single.Success)
            return item;
        int n = int.Parse(phrase.Groups["n"].Value, CultureInfo.InvariantCulture);
        double total = double.Parse(single.Groups["t"].Value, CultureInfo.InvariantCulture);
        if (n < 2) return item;
        var rest = (item[single.Length..phrase.Index] + item[(phrase.Index + phrase.Length)..]).Trim().TrimEnd(',').Trim();
        var rebuilt = $"{n} x {Num(total / n)}mm {rest}".Trim();
        notes.Add($"Read \"{item}\" as \"{rebuilt}\" (total split equally, check this).");
        return rebuilt;
    }

    private static bool IsMostlyUpper(string s)
    {
        int upper = s.Count(char.IsUpper), lower = s.Count(char.IsLower);
        return upper >= lower;
    }

    private static string StripBullet(string s) => BulletRegex().Replace(s, "");

    /// <summary>The tidied text, in top-to-bottom order when the original was bottom-up.</summary>
    private static string BottomUpText(BuildupParseResult parsed, string tidy, bool reversed)
    {
        if (!reversed)
            return tidy;
        var b = new Buildup { Name = parsed.Name ?? "PAVEMENT BUILD-UP", Layers = parsed.Layers, SubgradeText = parsed.SubgradeText ?? "SUBGRADE" };
        return BuildupParser.Format(b);
    }

    // ------------------------------------------------------------------ helpers --------------------

    private static readonly Dictionary<string, string> NumberWords = new()
    {
        ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6",
        ["single"] = "1", ["double"] = "2", ["triple"] = "3",
    };

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Num(string v) => Num(double.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture));

    /// <summary>Same length as <paramref name="s"/>, with bracketed text blanked so matches inside brackets are ignored.</summary>
    private static string Mask(string s)
    {
        var sb = new StringBuilder(s);
        int depth = 0;
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth = Math.Max(0, depth - 1);
            else if (depth > 0) sb[i] = '_';
        }
        return sb.ToString();
    }

    /// <summary>Regex replace that leaves anything inside (...) or [...] alone.</summary>
    private static string OutsideBrackets(string s, Regex regex, Func<Match, string> replace)
    {
        var masked = Mask(s);
        var sb = new StringBuilder();
        int at = 0;
        foreach (Match m in regex.Matches(masked))
        {
            sb.Append(s, at, m.Index - at);
            // Re-run the match on the original text (from the same start, so look-arounds still see
            // the surrounding text) so groups hold the real characters.
            var real = regex.Match(s, m.Index);
            sb.Append(real.Success && real.Index == m.Index && real.Length == m.Length ? replace(real) : s.Substring(m.Index, m.Length));
            at = m.Index + m.Length;
        }
        sb.Append(s, at, s.Length - at);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ patterns -------------------

    [GeneratedRegex(@"\b(?:bottom\s*(?:-|to)\s*top|from\s+(?:the\s+)?(?:bottom|subgrade|formation)\s+up(?:wards)?|upwards)\b\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex BottomUpCueRegex();

    [GeneratedRegex(@"\b(?:comprising(?:\s+of)?|consisting\s+of|made\s+up\s+of|constructed\s+(?:of|from|as)|built\s+up\s+as\s+follows|as\s+follows)\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex IntroRegex();

    [GeneratedRegex(@"^\s*(?:proposed\s+|new\s+|typical\s+)?(?:pavement\s*)?(?:build[\s-]*up|construction|make[\s-]*up|profile|details?|section|specification|spec)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericHeadingRegex();

    // ". " ending a sentence (not after min./nom./approx./No./e.g. etc.)
    [GeneratedRegex(@"(?<!\b(?:min|nom|approx|no|nos|typ|max|incl|approx|c|e\.g|i\.e|cl|ref))\.\s+(?=[A-Z0-9\[])")]
    private static partial Regex SentenceEndRegex();

    [GeneratedRegex(@"\s*\b(?:and\s+then|then|followed\s+by|underlain\s+by|over\s+which|beneath\s+which\s+is)\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SequenceWordRegex();

    [GeneratedRegex(@"(?<![\w.])(?<v>\d+(?:[.,]\d+)?)\s*(?<u>millimet(?:re|er)s?|mm|centimet(?:re|er)s?|cm|met(?:re|er)s?|m|inch(?:es)?|"")(?![\w²³/])", RegexOptions.IgnoreCase)]
    private static partial Regex UnitRegex();

    [GeneratedRegex(@"\b(one|two|three|four|five|six|single|double|triple)\b(?=\s*(?:x\b|no\.?\b|nos\b|layers?\b|lifts?\b|courses?\b|\d))", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWordRegex();

    [GeneratedRegex(@"(?<![\w.])(\d+(?:\.\d+)?)\s*(?:-|to)\s*(\d+(?:\.\d+)?)\s*mm\b", RegexOptions.IgnoreCase)]
    private static partial Regex RangeRegex();

    // "2 layers of 150mm (thick) (each)", "2 No. lifts @ 150mm"
    [GeneratedRegex(@"(?<![\w.])(?<n>\d+)\s*(?:no\.?|nos\.?)?\s*(?:layers?|lifts?|courses?|passes)\s*(?:of|@|at|each\s+of)?\s*(?<t>\d+(?:\.\d+)?)\s*mm(?:\s*(?:thick|thk))?(?:\s*each)?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LayersOfRegex();

    // "2 150mm (thick) layers (of)"
    [GeneratedRegex(@"(?<![\w.])(?<n>\d)\s+(?<t>\d+(?:\.\d+)?)\s*mm\s+(?:thick\s+)?(?:layers?|lifts?)\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex CountThenThicknessRegex();

    // "150mm x 2 (layers)", "150mm thick x2"
    [GeneratedRegex(@"(?<![\w.])(?<t>\d+(?:\.\d+)?)\s*mm(?:\s*(?:thick|thk))?\s*(?:x|\*)\s*(?<n>\d+)(?![\d.]|\s*mm)(?:\s*(?:layers?|lifts?|no\.?))?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessTimesRegex();

    // "300mm (thick) (placed/compacted) in 2 (equal) layers", "300mm (2 layers)"
    [GeneratedRegex(@"(?<![\w.])(?<t>\d+(?:\.\d+)?)\s*mm(?:\s*(?:thick|thk))?\s*(?:(?:placed|compacted|laid|constructed)\s+)?(?:in\s+|\(\s*)(?<n>\d+)\s*(?:equal\s+)?(?:layers?|lifts?)\s*\)?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex TotalInLayersRegex();

    [GeneratedRegex(@"^\s*(?:-|\d+[.)]|[a-z][.)]|\([a-z0-9]{1,2}\))?\s*(?:\d+\s*x\s*)?\d+(?:[.,]\d+)?\s*(?:mm\b|\s+[A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingThicknessRegex();

    [GeneratedRegex(@"(?<![\w.])(?:\d+\s*x\s*)?\d+(?:\.\d+)?mm\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessAnywhereRegex();

    [GeneratedRegex(@"\s*\b(?:thickness|thick|thk|depth|deep)\b\s*(?:of|=|:|-|@|at)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessWordBeforeRegex();

    [GeneratedRegex(@"^\s*\b(?:thick|thk|deep|depth)\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessWordAfterRegex();

    // "Basecourse - 150", "Pavers: 60", "DGB thickness 150" - but not "CV-TTW-1030" (no space before the dash).
    [GeneratedRegex(@"(?:\s+-|:|=|\s+(?:thickness|thk|depth))\s*(?<v>\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingBareNumberRegex();

    [GeneratedRegex(@"\b(min(?:imum)?\.?|nom(?:inal)?\.?|approx(?:imately)?\.?|typ(?:ical)?\.?|max(?:imum)?\.?)\s*(?=\d)", RegexOptions.IgnoreCase)]
    private static partial Regex QualifierRegex();

    [GeneratedRegex(@"\s+(?=(?:laid\s+on|placed\s+on|on\s+top\s+of|onto|upon|over|on)\s+(?:a\s+|an\s+|the\s+)?(?:layer\s+of\s+)?(?:geotextile|geo-?fabric|geogrid|geocomposite|membrane|dpm|separation\s+layer|filter\s+fabric)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex MembraneConnectorRegex();

    [GeneratedRegex(@"^(?:a|an|the)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleRegex();

    [GeneratedRegex(@"^\s*(?:the\s+)?(?:new\s+|proposed\s+)?(?:pavement|build[\s-]*up|surface|construction|finish)?\s*(?:will\s+be|shall\s+be|is\s+to\s+be|to\s+be|is|be|use|provide|lay|install)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadInRegex();

    [GeneratedRegex(@"^\s*of\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingOfRegex();

    [GeneratedRegex(@"^\s*(?:total|overall|nett?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalLineRegex();

    [GeneratedRegex(@"\b(?:geotextile|geo-?fabric|geogrid|geocomposite|membrane|dpm|vapou?r\s+barrier|separation\s+layer|filter\s+fabric|tack\s+coat|prime(?:\s+coat)?|primer\s*seal|bond\s+coat|spray(?:ed)?\s+seal|seal\s+coat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MembraneRegex();

    [GeneratedRegex(@"\b(?:refer|see|as\s+per|to\s+suit|tbc|tba|note)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemarkRegex();

    [GeneratedRegex(@"^(?:on\s+top\s+of|laid\s+on|bedded\s+on|placed\s+on|onto|upon|over|on)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectorRegex();

    [GeneratedRegex(@"\s+(?=(?:on\s+top\s+of|laid\s+on|bedded\s+on|placed\s+on|onto|upon|over|on)\s+(?:the\s+)?(?:compacted\s+|prepared\s+|proof[\s-]?rolled\s+)?(?:subgrade|sub-grade|formation|natural\s+ground|existing|in[\s-]?situ)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex BaseConnectorRegex();

    // "HEADING: 60mm ..." (a heading that doesn't start with a number, then a thickness).
    [GeneratedRegex(@"^(?=\D)[^:]{2,80}:\s*(?=(?:\d+\s*x\s*)?\d+(?:\.\d+)?\s*mm\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\s*,?\s*(?:(?:placed|compacted|laid|constructed)\s+)?in\s+(?<n>\d)\s*(?:equal\s+)?(?:layers?|lifts?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InLayersAnywhereRegex();

    [GeneratedRegex(@"^\s*(?<t>\d+(?:\.\d+)?)mm\b", RegexOptions.IgnoreCase)]
    private static partial Regex SingleLeadingThicknessRegex();

    [GeneratedRegex(@"^(?:[-/]+|n/?a|nil|none)$", RegexOptions.IgnoreCase)]
    private static partial Regex EmptyCellRegex();

    [GeneratedRegex(@"^\s*(?:[-•*]|\(?[a-z0-9]{1,2}[.)])\s+", RegexOptions.IgnoreCase)]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*(?:[-•*]|\(?(?:\d{1,2}|[a-z])[.)])\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ListLineRegex();

    [GeneratedRegex(@"^~?\s*(?<v>\d+(?:[.,]\d+)?)\s*(?<u>mm|cm|m)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TableNumberRegex();

    [GeneratedRegex(@"\b(?:thickness|depth|layer|material|description|course|item|mm)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderWordRegex();

    [GeneratedRegex(@"[(\[]\s*m\s*[)\]]|\bmetres?\b|\bmeters?\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetreHeaderRegex();

    [GeneratedRegex(@"[(\[]\s*cm\s*[)\]]|\bcentimet", RegexOptions.IgnoreCase)]
    private static partial Regex CentimetreHeaderRegex();
}
