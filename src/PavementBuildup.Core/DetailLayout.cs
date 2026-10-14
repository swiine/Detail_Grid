namespace PavementBuildup.Core;

public readonly record struct Pt(double X, double Y);

public sealed record Band(string Label, double Top, double Bottom, HatchSpec? Hatch, double HatchScale, bool Existing = false);

public sealed record LabelPlacement(string Text, Pt Anchor, Pt Elbow, Pt TextAt)
{
    public IReadOnlyList<Pt> ExtraAnchors { get; init; } = Array.Empty<Pt>();
    public int LineCount => Text.Split('\n').Length;
}

public sealed record DimPlacement(double Top, double Bottom, double X, string Text, double RefX = 0);

public sealed record BarPlacement(Pt Center, double Diameter);

public sealed record BarLine(double Y, double Diameter);

public sealed record TextPlacement(string Text, Pt At, double Height, bool Underline, bool Centered = false);

public sealed class DetailGeometry
{
    public double Width { get; init; }
    public double TextHeight { get; init; }
    public double TitleHeight { get; init; }
    public double AnnotationScale { get; init; }

    public List<Band> Bands { get; } = new();
    public Band? Subgrade { get; set; }
    public List<double> InterfaceLevels { get; } = new();
    public List<double> LiftLevels { get; } = new();
    public List<double> MembraneLevels { get; } = new();
    public List<List<Pt>> Edges { get; } = new();
    public List<LabelPlacement> Labels { get; } = new();
    public List<DimPlacement> Dimensions { get; } = new();
    public List<TextPlacement> Titles { get; } = new();
    public List<BarPlacement> Bars { get; } = new();
    public List<BarLine> BarLines { get; } = new();
}

public static partial class DetailLayout
{
    public const string UseStandardUnits = "STANDARD";
    public const string AutoUnits = "AUTO";

    public static readonly string[] StandardUnitChoices = { "M", "MM", "CM", AutoUnits };

    public static readonly string[] SettingsUnitChoices = { UseStandardUnits, "M", "MM", "CM", AutoUnits };

    public static double ResolveUnitsPerMm(string? settingsUnits, string? standardUnits, string? insunits)
    {
        string choice = (settingsUnits ?? "").Trim().ToUpperInvariant();
        if (choice.Length == 0 || choice == UseStandardUnits)
            choice = (standardUnits ?? "").Trim().ToUpperInvariant();
        if (choice.Length == 0)
            choice = "M";
        if (choice == AutoUnits)
            choice = string.IsNullOrWhiteSpace(insunits) ? "M" : insunits;
        return UnitsPerMm(choice);
    }

    public static double UnitsPerMm(string units) => units.Trim().ToUpperInvariant() switch
    {
        "MM" or "MILLIMETERS" or "MILLIMETRES" => 1.0,
        "CM" or "CENTIMETERS" or "CENTIMETRES" => 0.1,
        "M" or "METERS" or "METRES" => 0.001,
        "IN" or "INCHES" => 1 / 25.4,
        "FT" or "FEET" => 1 / 304.8,
        _ => throw new ArgumentException($"Unknown drawing units \"{units}\" (use M, MM or CM)."),
    };

    public static DetailGeometry Build(Buildup buildup, DetailSettings settings, CadStandard standard, double unitsPerMm)
    {
        if (buildup.Layers.Count == 0)
            throw new ArgumentException("The build-up has no layers.", nameof(buildup));
        if (buildup.Layers.Any(l => l.ThicknessMm < 0 || double.IsNaN(l.ThicknessMm)))
            throw new ArgumentException("Layer thicknesses cannot be negative.", nameof(buildup));
        if (!(settings.ScaleDenominator > 0) || !(settings.WidthMm > 0))
            throw new ArgumentException("Scale and width must be greater than zero.", nameof(settings));
        if (!(standard.TextHeightMm > 0) || !(standard.TitleHeightMm > 0))
            throw new ArgumentException("The CAD standard's text heights must be greater than zero.", nameof(standard));
        var barErrors = buildup.Layers.SelectMany(ReinforcementParser.Check).ToList();
        if (barErrors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, barErrors), nameof(buildup));

        double u = unitsPerMm;
        double annot = settings.ScaleDenominator * u;
        double textH = standard.TextHeightMm * annot;
        double titleH = standard.TitleHeightMm * annot;
        double width = settings.WidthMm * u;
        double hatchFactor = settings.ScaleDenominator * u * settings.HatchScaleMultiplier;

        var g = new DetailGeometry { Width = width, TextHeight = textH, TitleHeight = titleH, AnnotationScale = annot };

        var anchors = new List<(string Text, double Y, double X, double[] More)>();
        var coverDims = new List<DimPlacement>();
        var dimSegments = new List<(double Top, double Bottom)>();
        double y = 0;
        g.InterfaceLevels.Add(0);
        foreach (var layer in buildup.Layers)
        {
            double t = layer.TotalMm * u;
            string label = LabelText(layer, standard);
            if (t <= 0)
            {
                g.MembraneLevels.Add(y);
                anchors.Add((label, y, double.NaN, Array.Empty<double>()));
                continue;
            }

            var hatch = MaterialLibrary.Resolve(layer, standard);
            double scale = hatch is null ? 0 : hatch.BaseScale * hatchFactor * (layer.HatchScale > 0 ? layer.HatchScale : 1);
            g.Bands.Add(new Band(label, y, y - t, hatch, scale, BuildupParser.IsExisting(layer.Description)));
            int layerCount = Math.Max(1, layer.Lifts);
            double layerT = t / layerCount;
            anchors.Add((label, y - layerT / 2, double.NaN,
                Enumerable.Range(1, layerCount - 1).Select(k => y - layerT * k - layerT / 2).ToArray()));
            foreach (var mat in layer.Reinforcement)
                AddReinforcement(g, mat, top: y, bottom: y - t, u, width, standard, anchors, coverDims);
            int lifts = Math.Max(1, layer.Lifts);
            double each = t / lifts;
            for (int k = 0; k < lifts; k++)
            {
                if (k > 0) g.LiftLevels.Add(y - k * each);
                dimSegments.Add((y - k * each, y - (k + 1) * each));
            }
            y -= t;
            g.InterfaceLevels.Add(y);
        }
        double formation = y;

        double bottom = formation;
        if (buildup.ShowSubgrade && settings.SubgradeDepthMm > 0)
        {
            double depth = settings.SubgradeDepthMm * u;
            var spec = MaterialLibrary.ToSpec(BaseHatch(buildup.SubgradeText, standard));
            g.Subgrade = new Band(buildup.SubgradeText, formation, formation - depth, spec, spec is null ? 0 : spec.BaseScale * hatchFactor,
                BuildupParser.IsExisting(buildup.SubgradeText));
            string text = SubgradeLabel(buildup.SubgradeText, standard);
            text = standard.UpperCaseLabels ? text.ToUpperInvariant() : text;
            if (!string.IsNullOrWhiteSpace(text))
                anchors.Add((text, formation - depth / 2, double.NaN, Array.Empty<double>()));
            bottom = formation - depth;
        }

        double breakSize = 2.5 * annot;
        double edgeBottom = g.Subgrade is { Existing: true } ? formation : bottom;
        bool canBreak = settings.ShowBreakLines && (0 - edgeBottom) > breakSize * 3;
        foreach (double x in new[] { 0.0, width })
            g.Edges.Add(Edge(x, 0, edgeBottom, canBreak ? breakSize : 0));

        double anchorX = width * 0.85;
        double elbowX = width + 6 * annot;
        double textX = elbowX + 4 * annot;
        double lineH = textH * 1.6;
        double? previous = null;
        int previousLines = 1;
        foreach (var (unwrapped, anchorY, ax, more) in anchors.OrderByDescending(a => a.Y))
        {
            string text = WrapLabel(unwrapped, standard.LabelWrapChars);
            int lines = text.Split('\n').Length;
            double minGap = (previousLines + lines) * lineH / 2 + textH * 0.2;
            double ly = previous is null ? anchorY : Math.Min(anchorY, previous.Value - minGap);
            double x = double.IsNaN(ax) ? anchorX : ax;
            g.Labels.Add(new LabelPlacement(text, new Pt(x, anchorY), new Pt(elbowX, ly), new Pt(textX, ly))
            {
                ExtraAnchors = more.Select(m => new Pt(x, m)).ToArray(),
            });
            previous = ly;
            previousLines = lines;
        }

        if (settings.ShowDimensions)
        {
            double dimX = -8 * annot;
            foreach (var (top, bot) in dimSegments)
                g.Dimensions.Add(new DimPlacement(top, bot, dimX, DimText((top - bot) / u, standard)));
            if (dimSegments.Count > 1)
                g.Dimensions.Add(new DimPlacement(0, formation, dimX - 10 * annot, DimText(buildup.TotalThicknessMm, standard)));
        }
        if (settings.ShowDimensions && standard.ShowCoverDimension)
            g.Dimensions.AddRange(coverDims);

        if (settings.ShowTitle)
        {
            double lowestLabel = g.Labels.Count > 0 ? Math.Min(bottom, g.Labels[^1].TextAt.Y) : bottom;
            double ty = lowestLabel - 8 * annot;
            var tokens = new[]
            {
                ("name", buildup.Name),
                ("scale", CadStandard.Number(settings.ScaleDenominator)),
                ("total", CadStandard.Number(buildup.TotalThicknessMm)),
            };
            double cx = width / 2;
            string title = CadStandard.Fill(standard.TitleFormat, tokens);
            double scaleH = standard.ScaleHeightMm * annot;
            var lines = new List<(string Text, double Height, bool Underline)>();
            if (title.Length > 0)
            {
                foreach (var part in SplitTitle(standard.UpperCaseLabels ? title.ToUpperInvariant() : title, titleH, width))
                    lines.Add((part, titleH, true));
            }
            if (CadStandard.Fill(standard.ScaleFormat, tokens) is { Length: > 0 } scaleLine)
                lines.Add((scaleLine, scaleH, false));
            if (CadStandard.Fill(standard.TotalFormat, tokens) is { Length: > 0 } totalLine)
                lines.Add((totalLine, textH, false));
            double previousH = 0;
            foreach (var (text, h, underline) in lines)
            {
                ty -= previousH == 0 ? h / 2 : (previousH + h) / 2 * 1.6;
                g.Titles.Add(new TextPlacement(text, new Pt(cx, ty), h, underline, Centered: true));
                previousH = h;
            }
        }

        return g;
    }

    public const double TextWidthFactor = 0.65;

    public static IReadOnlyList<string> SplitTitle(string title, double height, double width)
    {
        title = title.Trim();
        if (title.Length * height * TextWidthFactor <= width || !title.Contains(' '))
            return new[] { title };
        int middle = title.Length / 2, best = -1;
        for (int i = 0; i < title.Length; i++)
            if (title[i] == ' ' && (best < 0 || Math.Abs(i - middle) < Math.Abs(best - middle)))
                best = i;
        return new[] { title[..best].Trim(), title[(best + 1)..].Trim() };
    }

    private static void AddReinforcement(DetailGeometry g, Reinforcement mat, double top, double bottom, double u, double width,
        CadStandard standard, List<(string, double, double, double[])> anchors, List<DimPlacement> coverDims)
    {
        double dir = mat.Face == BarFace.Bottom ? 1 : -1;
        double face = mat.Face == BarFace.Bottom ? bottom : top;
        double d = mat.DiameterMm * u;
        double barY = face + dir * (mat.CoverMm * u + d / 2);

        double spacing = mat.SpacingMm * u;
        int count = Math.Max(1, (int)Math.Floor(width / spacing));
        double first = (width - (count - 1) * spacing) / 2;
        var xs = Enumerable.Range(0, count).Select(i => first + i * spacing).ToList();
        foreach (var x in xs)
            g.Bars.Add(new BarPlacement(new Pt(x, barY), d));

        if (mat.HasTransverse)
        {
            double td = mat.TransverseDiameterMm * u;
            g.BarLines.Add(new BarLine(face + dir * (mat.CoverMm * u + d + td / 2), td));
        }

        double labelX = xs.MinBy(x => Math.Abs(x - width * 0.7));
        anchors.Add((standard.ReinforcementLabel(mat), barY, labelX, Array.Empty<double>()));

        if (mat.CoverMm > 0)
        {
            double dimAt = xs.MinBy(x => Math.Abs(x - width * 0.15));
            double edge = face + dir * mat.CoverMm * u;
            string text = CadStandard.Fill(standard.CoverDimensionFormat ?? "{cover}", ("cover", CadStandard.Number(mat.CoverMm)));
            coverDims.Add(new DimPlacement(Math.Max(face, edge), Math.Min(face, edge), dimAt, text, dimAt));
        }
    }

    public static HatchRule BaseHatch(string? baseText, CadStandard standard)
    {
        var text = baseText ?? "";
        if (BuildupParser.IsSubgradeNote(BuildupParser.Core(text)))
            return standard.SubgradeHatch;
        return standard.HatchRules.FirstOrDefault(r => r.Matches(BuildupParser.Core(text))) ?? standard.SubgradeHatch;
    }

    public static string LabelText(PavementLayer layer, CadStandard standard)
    {
        string d = standard.UpperCaseLabels ? layer.Description.ToUpperInvariant() : layer.Description;
        if (layer.ThicknessMm <= 0)
            return CadStandard.Fill(standard.MembraneLabelFormat, ("description", d));
        string label = CadStandard.Fill(standard.LabelFormat, ("thickness", CadStandard.Number(layer.ThicknessMm)), ("description", d));
        return layer.Lifts > 1 ? $"{layer.Lifts} x {label}" : label;
    }

    public static string SubgradeLabel(string? subgradeText, CadStandard standard)
    {
        var text = subgradeText ?? "";
        var note = standard.SubgradeNote?.Trim() ?? "";
        if (note.Length == 0 || text.Contains('\n') || BuildupParser.IsExisting(text))
            return text;
        return BareSubgradeRegex().IsMatch(text.Trim()) ? note : text;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:the\s+)?(?:natural|existing\s+ground|in[\s-]?situ|prepared)?\s*(?:subgrade|formation|sub-grade|natural\s+ground)\.?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BareSubgradeRegex();

    public static string WrapLabel(string text, int maxChars)
    {
        if (maxChars <= 0) return text;
        var output = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var current = "";
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
                {
                    output.Add(current);
                    current = word;
                }
                else
                    current = current.Length == 0 ? word : current + " " + word;
            }
            output.Add(current);
        }
        return string.Join("\n", output);
    }

    private static string DimText(double mm, CadStandard standard) =>
        CadStandard.Fill(standard.DimensionFormat, ("thickness", CadStandard.Number(mm)));

    private static List<Pt> Edge(double x, double top, double bottom, double s)
    {
        if (s <= 0)
            return new() { new(x, top), new(x, bottom) };

        double mid = (top + bottom) / 2;
        return new()
        {
            new(x, top),
            new(x, mid + s),
            new(x + s * 0.5, mid + s * 0.25),
            new(x - s * 0.5, mid - s * 0.25),
            new(x, mid - s),
            new(x, bottom),
        };
    }
}
