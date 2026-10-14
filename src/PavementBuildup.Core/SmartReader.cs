using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

public sealed class SmartReadResult
{
    public BuildupParseResult Parsed { get; init; } = new();

    public List<string> Notes { get; } = new();

    public string Text { get; init; } = "";
}

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
        t = Lines(t);
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
        t = OutsideBrackets(t, BaseConnectorRegex(), _ => "; ");
        t = OutsideBrackets(t, MembraneConnectorRegex(), _ => "; ");

        var items = new List<string>();
        int firstBase = -1, firstCourse = -1;
        var rawItems = BuildupParser.SplitItems(t).ToList();
        List<string>? roles = null;
        var courseRoles = new List<string>();
        if (rawItems.Count(i => RoleLabelRegex().IsMatch(StripBullet(i.Trim()))) >= 2)
        {
            roles = rawItems.Select(RoleOf).ToList();
            rawItems = rawItems.Select(StripRoleLabel).ToList();
            notes.Add("Left out the layer names before the colons (e.g. \"Surface Layer:\"); the material is the label.");
        }

        else if (rawItems.Count > 0 && FirstLabelRegex().Match(StripBullet(rawItems[0].Trim())) is { Success: true } label
                 && !HeadingRegex().IsMatch(Mask(StripBullet(rawItems[0].Trim()))))
        {
            rawItems[0] = label.Groups["rest"].Value;
            rawItems.Insert(0, label.Groups["head"].Value.Trim() + ":");
        }

        bool first = true;
        for (int r = 0; r < rawItems.Count; r++)
        {
            var raw = rawItems[r];
            var item = FixItem(raw, notes, out var kind, first);
            if (item.Length > 0) first = false;
            if (item.Length == 0)
                continue;
            if (kind == ItemKind.Course) courseRoles.Add(roles?[r] ?? "");
            if (kind == ItemKind.Course && firstCourse < 0) firstCourse = items.Count;
            if (kind == ItemKind.Base && firstBase < 0) firstBase = items.Count;
            items.Add(item);
        }

        if (firstBase >= 0 && firstCourse >= 0 && firstBase < firstCourse)
            bottomUp = true;

        var tidy = string.Join("; ", items.Select(MoveRangeNote));
        var parsed = BuildupParser.ParseBuildup(tidy, errors);
        tidy = RestoreCommas(tidy);
        if (parsed.Name is not null) parsed.Name = RestoreCommas(parsed.Name);
        if (parsed.SubgradeText is not null) parsed.SubgradeText = RestoreCommas(parsed.SubgradeText);
        foreach (var layer in parsed.Layers)
            layer.Description = RestoreCommas(layer.Description);
        for (int i = 0; i < errors.Count; i++)
            errors[i] = RestoreCommas(errors[i]);
        if (parsed.Name is { } name && (GenericHeadingRegex().IsMatch(name) || TotalLineRegex().IsMatch(name)))
            parsed.Name = null;
        if (parsed.Name is { } heading && TitleOnly(heading) is var cut && cut != heading)
        {
            parsed.Name = cut;
            notes.Add($"Used \"{cut}\" as the title (left out the description after it).");
        }
        if (roles != null && parsed.Layers.Count == courseRoles.Count)
            for (int i = 0; i < courseRoles.Count; i++)
                parsed.Layers[i].Role = courseRoles[i];
        if (bottomUp && parsed.Layers.Count > 1)
        {
            parsed.Layers.Reverse();
            notes.Add("Written bottom-up: the layers have been put in top-to-bottom order.");
        }

        var result = new SmartReadResult { Parsed = parsed, Text = BottomUpText(parsed, tidy, bottomUp) };
        result.Notes.AddRange(notes.Distinct());
        return result;
    }

    private static string NormaliseCharacters(string s) => MarkdownRegex().Replace(BareThickRegex().Replace(s, "$1mm "), "")
        .Replace('×', 'x').Replace('–', '-').Replace('—', '-').Replace('−', '-')
        .Replace(' ', ' ').Replace('‘', '\'').Replace('’', '\'')
        .Replace('“', '"').Replace('”', '"')
        .Replace("•", "\n- ").Replace("●", "\n- ").Replace("▪", "\n- ");

    private static string Tables(string s, List<string> notes)
    {
        var lines = s.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count(l => l.Contains('\t') || l.Contains('|')) < 2)
            return s;

        double factor = 1;
        int thicknessColumn = -1;
        var rows = new List<string>();
        foreach (var line in lines)
        {
            var all = line.Split('\t', '|').Select(c => c.Trim()).ToList();
            if (all.Count > 0 && all[0].Length == 0) all.RemoveAt(0);
            if (all.Count > 0 && all[^1].Length == 0) all.RemoveAt(all.Count - 1);
            if (all.Count(c => ThicknessHeaderRegex().IsMatch(c)) == 1 && all.All(c => !TableNumberRegex().IsMatch(c)))
                thicknessColumn = all.FindIndex(c => ThicknessHeaderRegex().IsMatch(c));
            var cells = all.Where(c => c.Length > 0 && !EmptyCellRegex().IsMatch(c)).ToList();
            if (cells.Count == 0)
                continue;
            int numeric = thicknessColumn >= 0 && thicknessColumn < all.Count && TableNumberRegex().IsMatch(all[thicknessColumn])
                ? cells.IndexOf(all[thicknessColumn])
                : cells.FindIndex(c => TableUnitNumberRegex().IsMatch(c)) is var u and >= 0 ? u
                : cells.FindLastIndex(c => TableNumberRegex().IsMatch(c));
            if (numeric < 0)
            {
                bool header = cells.Any(c => HeaderWordRegex().IsMatch(c));
                if (header)
                {
                    if (cells.Any(c => MetreHeaderRegex().IsMatch(c))) factor = 1000;
                    else if (cells.Any(c => CentimetreHeaderRegex().IsMatch(c))) factor = 10;
                    continue;
                }
                rows.Add(string.Join(" ", cells));
                continue;
            }

            var m = TableNumberRegex().Match(cells[numeric]);
            double value = double.Parse(m.Groups["v"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            string unit = m.Groups["u"].Value.ToLowerInvariant();
            double mm = unit switch { "m" => value * 1000, "cm" => value * 10, "mm" => value, _ => value * factor };
            var description = string.Join(" ", cells.Where((c, i) => i != numeric && !(i == 0 && RowNumberRegex().IsMatch(c))));
            rows.Add($"{Num(mm)}mm {description}".Trim());
        }
        notes.Add("Read the text as a table (one layer per row).");
        return string.Join("; ", rows);
    }

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
                items[^1] += " " + line;
            else
                items.Add(line);
        }
        return string.Join("; ", items.Select(i => ProtectCommas(i.Trim().TrimEnd(',', ';'))));
    }

    private static string ProtectCommas(string line) =>
        CommaRegex().Replace(line, m =>
        {
            var next = BracketGroupRegex().Replace(line[(m.Index + 1)..], " ").Split(',')[0];
            return ThicknessAheadRegex().IsMatch(next) || RemarkRegex().IsMatch(next.TrimStart().Split(' ')[0]) ? m.Value : KeptComma + " ";
        });

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex BracketGroupRegex();

    private const char KeptComma = '\u201A';

    private static string RestoreCommas(string? s) => s?.Replace(KeptComma, ',') ?? "";

    [GeneratedRegex(@",\s+")]
    private static partial Regex CommaRegex();

    [GeneratedRegex(@"\d\s*(?:mm|cm|m|thk|thick)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessAheadRegex();

    private static string Lines(string s)
    {
        if (!s.Contains('\n'))
            return s;
        var lines = s.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Any(l => l.Count(c => c == '(') != l.Count(c => c == ')')))
            return s;
        if (lines.Count(l => LineItemRegex().IsMatch(l)) < 2)
            return s;
        return string.Join("; ", lines.Select(l => ProtectCommas(l.TrimEnd(',', ';'))));
    }

    [GeneratedRegex(@"^\s*(?:[-•*]|\(?\d{1,2}[.)]|[a-z][.)])?\s*(?:[^:\d]{2,40}:\s*)?(?:\d+\s*x\s*)?\d+(?:[.,]\d+)?\s*(?:mm|cm|m|thk|thick)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LineItemRegex();

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
            "m" or "metre" or "metres" or "meter" or "meters" => value <= 3 ? value * 1000 : null,
            "inch" or "inches" or "\"" => Math.Round(value * 25.4),
            _ => null,
        };
        if (mm is null)
            return m.Value;
        if (!unit.StartsWith("mm") && !unit.StartsWith("millim"))
            notes.Add($"Converted {m.Value.Trim()} to {Num(mm.Value)}mm.");
        return $"{Num(mm.Value)}mm";
    }

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

    private static string FixItem(string raw, List<string> notes, out ItemKind kind, bool first = false)
    {
        var fixedItem = FixItemCore(raw, notes, out kind, first);
        var tiny = TinyThicknessRegex().Match(fixedItem);
        if (tiny.Success && double.Parse(tiny.Groups["t"].Value, CultureInfo.InvariantCulture) is > 0 and < 2)
        {
            notes.Add($"\"{fixedItem}\" is thinner than 2mm: drawn as a line.");
            fixedItem = "0mm" + fixedItem[tiny.Length..];
        }
        return fixedItem;
    }

    private static string FixItemCore(string raw, List<string> notes, out ItemKind kind, bool first)
    {
        kind = ItemKind.Other;
        var item = StripBullet(raw.Trim()).TrimEnd('.', ';', ',').Trim();
        item = ConnectorRegex().Replace(item, "").Trim();
        item = ArticleRegex().Replace(item, "").Trim();
        if (item.Length == 0)
            return "";

        var heading = HeadingRegex().Match(Mask(item));
        if (heading.Success)
        {
            var head = HeadingSeparatorRegex().Replace(item[..heading.Length].Trim(), "").Trim();
            var rest = FixItemCore(item[heading.Length..], notes, out kind, false);
            return rest.Length == 0 ? head + ":" : $"{head}: {rest}";
        }

        var core = BuildupParser.Core(item);
        if (TotalLineRegex().IsMatch(core) && !first)
        {
            notes.Add($"Ignored the total line \"{item}\".");
            return "";
        }
        if (InstructionRegex().IsMatch(core))
        {
            notes.Add($"Skipped \"{item}\" (an instruction, not a layer).");
            return "";
        }
        if (CoatSealRegex().IsMatch(core))
        {
            kind = ItemKind.Course;
            notes.Add($"Treated \"{item}\" as a seal (drawn as a line).");
            return "0mm " + item;
        }

        string qualifier = "";
        item = OutsideBrackets(item, QualifierRegex(), m => { qualifier = m.Groups[1].Value.Trim().TrimEnd('.').ToUpperInvariant(); return ""; });
        if (qualifier.Length > 0)
            item = $"{item.Trim()} ({qualifier}.)";

        var lead = LeadingThicknessRegex().Match(StripBullet(item));
        if (lead.Success)
        {
            kind = ItemKind.Course;
            return InLayers(LeadingOfAfterThicknessRegex().Replace(item, "$1 "), notes);
        }

        var masked = Mask(item);
        var tm = ThicknessAnywhereRegex().Match(masked);
        if (tm.Success)
        {
            var before = item[..tm.Index];
            var after = item[(tm.Index + tm.Length)..];
            bool thick = false;
            before = ThicknessWordBeforeRegex().Replace(before, _ => { thick = true; return ""; });
            string? title = null;
            if (LeadInRegex().IsMatch(before))
                before = "";
            else if (LeadInVerbRegex().Match(before) is { Success: true } verb)
            {
                if (first && verb.Groups["head"].Value.Trim().Length > 0)
                    title = verb.Groups["head"].Value.Trim().TrimEnd(',', '-', ':').Trim();
                before = "";
            }
            after = LeadingOfRegex().Replace(after, "");
            after = ThicknessWordAfterRegex().Replace(after, _ => { thick = true; return ""; });
            var description = Regex.Replace($"{before.Trim().TrimEnd('-', ':', '=', ',', '@').Trim()} {after.Trim().TrimStart('-', ':', '=', ',').Trim()}", @"\s{2,}", " ").Trim();
            if (description.Length > 0)
            {
                var thickWord = thick ? (IsMostlyUpper(item) ? "THICK " : "thick ") : "";
                var rebuilt = $"{tm.Value.Trim()} {thickWord}{description}";
                notes.Add($"Read \"{item}\" as \"{rebuilt}\".");
                kind = ItemKind.Course;
                var layer = InLayers(rebuilt, notes);
                return title is null ? layer : $"{title}: {layer}";
            }
        }

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

    private static string TitleOnly(string name)
    {
        var glued = GluedSentenceRegex().Match(name);
        if (glued.Success)
            return name[..(glued.Index + 1)].Trim();
        var described = DescriptionAfterRegex().Match(name);
        if (described.Success)
            return name[..described.Index].Trim();
        var sentence = SentenceBreakRegex().Match(name);
        return sentence.Success ? name[..sentence.Index].Trim() : name;
    }

    private static string MoveRangeNote(string item)
    {
        var m = RangeFirstRegex().Match(item);
        return m.Success ? $"{m.Groups["t"].Value} {m.Groups["rest"].Value.Trim()} {m.Groups["r"].Value}" : item;
    }

    [GeneratedRegex(@"\)(?=[A-Z])")]
    private static partial Regex GluedSentenceRegex();

    [GeneratedRegex(@"(?<=\))\s+(?=(?:An?|The|This|Ideal|Suitable|Used|Designed)\s+(?:\S+\s+){3})")]
    private static partial Regex DescriptionAfterRegex();

    [GeneratedRegex(@"\.\s+(?=[A-Z])")]
    private static partial Regex SentenceBreakRegex();

    [GeneratedRegex(@"^(?<t>\d+(?:\.\d+)?\s*mm)\s*(?<r>\(\d[^()]*mm\))\s+(?:of\s+)?(?<rest>[^()\n].*)$", RegexOptions.IgnoreCase)]
    private static partial Regex RangeFirstRegex();

    private static string RoleOf(string item)
    {
        var trimmed = StripBullet(item.Trim());
        var m = RoleLabelRegex().Match(trimmed);
        return m.Success ? trimmed[..m.Length].Trim().TrimEnd(':').Trim() : "";
    }

    private static string StripRoleLabel(string item)
    {
        var trimmed = StripBullet(item.Trim());
        var m = RoleLabelRegex().Match(trimmed);
        if (!m.Success)
            return item;
        var label = trimmed[..m.Length].Trim().TrimEnd(':').Trim();
        var rest = trimmed[m.Length..].Trim();
        if (rest.Length == 0)
            return label;
        bool restHasThickness = ThicknessAnywhereRegex().IsMatch(Mask(rest)) || LeadingThicknessRegex().IsMatch(rest);
        if (!restHasThickness && MembraneRegex().IsMatch(label) && !MembraneRegex().IsMatch(rest))
            return $"{label} - {rest}";
        if (!restHasThickness && BuildupParser.IsBaseNote(label) && !rest.Contains(label, StringComparison.OrdinalIgnoreCase))
            return BuildupParser.IsBaseNote(rest) ? $"{label}: {rest}" : $"{label} {rest}";
        var restWords = BuildupParser.Core(ThicknessAnywhereRegex().Replace(Mask(rest), " ")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (restHasThickness && restWords.Length <= 2 && !rest.Contains(label, StringComparison.OrdinalIgnoreCase))
            return $"{rest} {label}";
        return rest;
    }

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

    private static string BottomUpText(BuildupParseResult parsed, string tidy, bool reversed)
    {
        if (!reversed)
            return tidy;
        var b = new Buildup { Name = parsed.Name ?? "PAVEMENT BUILD-UP", Layers = parsed.Layers, SubgradeText = parsed.SubgradeText ?? "SUBGRADE" };
        return BuildupParser.Format(b);
    }

    private static readonly Dictionary<string, string> NumberWords = new()
    {
        ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6",
        ["single"] = "1", ["double"] = "2", ["triple"] = "3",
    };

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Num(string v) => Num(double.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture));

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

    private static string OutsideBrackets(string s, Regex regex, Func<Match, string> replace)
    {
        var masked = Mask(s);
        var sb = new StringBuilder();
        int at = 0;
        foreach (Match m in regex.Matches(masked))
        {
            sb.Append(s, at, m.Index - at);
            var real = regex.Match(s, m.Index);
            sb.Append(real.Success && real.Index == m.Index && real.Length == m.Length ? replace(real) : s.Substring(m.Index, m.Length));
            at = m.Index + m.Length;
        }
        sb.Append(s, at, s.Length - at);
        return sb.ToString();
    }

    [GeneratedRegex(@"\b(?:bottom\s*(?:-|to)\s*top|from\s+(?:the\s+)?(?:bottom|subgrade|formation)\s+up(?:wards)?|upwards)\b\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex BottomUpCueRegex();

    [GeneratedRegex(@"\b(?:comprising(?:\s+of)?|consisting\s+of|made\s+up\s+of|constructed\s+(?:of|from|as)|built\s+up\s+as\s+follows|as\s+follows)\s*:?", RegexOptions.IgnoreCase)]
    private static partial Regex IntroRegex();

    [GeneratedRegex(@"^\s*(?:proposed\s+|new\s+|typical\s+)?(?:pavement\s*)?(?:build[\s-]*up|construction|make[\s-]*up|profile|details?|section|specification|spec)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericHeadingRegex();

    [GeneratedRegex(@"(?<!\b(?:min|nom|approx|no|nos|typ|max|incl|approx|c|e\.g|i\.e|cl|ref))\.\s+(?=[A-Z0-9\[])")]
    private static partial Regex SentenceEndRegex();

    [GeneratedRegex(@"\s*\b(?:and\s+then|then|and\s+finally|finally|lastly|next|followed\s+by|underlain\s+by|over\s+which|beneath\s+which\s+is)\b\s*|\s+and\s+(?=\d|(?:replace|overlay|resurface|reinstate|lay|provide|install|place|construct)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex SequenceWordRegex();

    [GeneratedRegex(@"(?<![\w.])(?<v>\d+(?:[.,]\d+)?)\s*(?<u>millimet(?:re|er)s?|mm|centimet(?:re|er)s?|cm|met(?:re|er)s?|m|inch(?:es)?|"")(?![\w²³/])", RegexOptions.IgnoreCase)]
    private static partial Regex UnitRegex();

    [GeneratedRegex(@"\b(one|two|three|four|five|six|single|double|triple)\b(?=\s*(?:x\b|no\.?\b|nos\b|layers?\b|lifts?\b|courses?\b|\d))", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWordRegex();

    [GeneratedRegex(@"(?<![\w.])(\d+(?:\.\d+)?)\s*(?:-|to)\s*(\d+(?:\.\d+)?)\s*mm\b", RegexOptions.IgnoreCase)]
    private static partial Regex RangeRegex();

    [GeneratedRegex(@"(?<![\w.])(?<n>\d+)\s*(?:no\.?|nos\.?)?\s*(?:layers?|lifts?|courses?|passes)\s*(?:of|@|at|each\s+of)?\s*(?<t>\d+(?:\.\d+)?)\s*mm(?:\s*(?:thick|thk))?(?:\s*each)?\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex LayersOfRegex();

    [GeneratedRegex(@"(?<![\w.])(?<n>\d)\s+(?<t>\d+(?:\.\d+)?)\s*mm\s+(?:thick\s+)?(?:layers?|lifts?)\s*(?:of\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex CountThenThicknessRegex();

    [GeneratedRegex(@"(?<![\w.])(?<t>\d+(?:\.\d+)?)\s*mm(?:\s*(?:thick|thk))?\s*(?:x|\*)\s*(?<n>\d+)(?![\d.]|\s*mm)(?:\s*(?:layers?|lifts?|no\.?))?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessTimesRegex();

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

    [GeneratedRegex(@"^(?=\D)[^:]{2,80}?(?::|\s+-\s+)\s*(?=(?:\d+\s*x\s*)?\d+(?:\.\d+)?\s*mm\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\s*(?::|-)\s*$")]
    private static partial Regex HeadingSeparatorRegex();

    [GeneratedRegex(@"^(?<head>[^:\d\[\]()]{2,60}):\s*(?<rest>\S.*)$")]
    private static partial Regex FirstLabelRegex();

    [GeneratedRegex(@"\*\*|__|(?m)^\s*#{1,6}\s*|`")]
    private static partial Regex MarkdownRegex();

    [GeneratedRegex(@"(?<![\w.])(\d+(?:\.\d+)?)\s*(?=(?:thk|thick)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex BareThickRegex();

    [GeneratedRegex(@"^\s*(?<t>\d+(?:\.\d+)?)mm")]
    private static partial Regex TinyThicknessRegex();

    [GeneratedRegex(@"^\s*(?:mill|plane|cold\s+plane|remove|excavate|strip|cut\s+out|saw\s*cut|scarify|break\s+out|demolish)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionRegex();

    [GeneratedRegex(@"^\s*\d\s*(?:-|\s)?\s*coat\b", RegexOptions.IgnoreCase)]
    private static partial Regex CoatSealRegex();

    [GeneratedRegex(@"^(\s*(?:\d+\s*x\s*)?\d+(?:\.\d+)?\s*mm)\s+of\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingOfAfterThicknessRegex();

    [GeneratedRegex(@"^(?<head>.*?)\s*\b(?:to\s+be|will\s+be|shall\s+be|is|are|comprises?|consists?\s+of|includes?|use|provide|lay|install|replace\s+with|overlay\s+with|resurface\s+with|reinstate\s+with)\s*:?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadInVerbRegex();

    [GeneratedRegex(@"\b(?:thickness|depth|thk)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThicknessHeaderRegex();

    [GeneratedRegex(@"^~?\s*\d+(?:[.,]\d+)?\s*(?:mm|cm|m)$", RegexOptions.IgnoreCase)]
    private static partial Regex TableUnitNumberRegex();

    [GeneratedRegex(@"^\d{1,2}[.)]?$")]
    private static partial Regex RowNumberRegex();

    [GeneratedRegex(@"^(?=\D)[^:;()\[\]]{2,40}:\s*")]
    private static partial Regex RoleLabelRegex();

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
