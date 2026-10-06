namespace PavementBuildup.Core;

public readonly record struct Pt(double X, double Y);

/// <summary>A hatched band (one course, or the subgrade strip).</summary>
/// <remarks><see cref="Existing"/> bands go on the existing layers and get a closed outline.</remarks>
public sealed record Band(string Label, double Top, double Bottom, HatchSpec? Hatch, double HatchScale, bool Existing = false);

/// <summary>A leader from <see cref="Anchor"/> through <see cref="Elbow"/> to text at <see cref="TextAt"/> (middle-left).</summary>
/// <remarks>
/// <see cref="ExtraAnchors"/> are further leader branches to the same elbow, e.g. one to each layer of
/// "2 x 150mm ROAD BASE". <see cref="Text"/> may hold several lines separated by '\n'.
/// </remarks>
public sealed record LabelPlacement(string Text, Pt Anchor, Pt Elbow, Pt TextAt)
{
    public IReadOnlyList<Pt> ExtraAnchors { get; init; } = Array.Empty<Pt>();
    public int LineCount => Text.Split('\n').Length;
}

/// <summary>A vertical dimension between two levels, measured at <see cref="RefX"/>, dimension line at <see cref="X"/>.</summary>
public sealed record DimPlacement(double Top, double Bottom, double X, string Text, double RefX = 0);

/// <summary>A bar cut by the section (drawn as a filled circle).</summary>
public sealed record BarPlacement(Pt Center, double Diameter);

/// <summary>A transverse bar running along the section at level <see cref="Y"/>.</summary>
public sealed record BarLine(double Y, double Diameter);

public sealed record TextPlacement(string Text, Pt At, double Height, bool Underline);

/// <summary>Everything needed to draw a detail, in drawing units, relative to the top-left corner of the surface.</summary>
public sealed class DetailGeometry
{
    public double Width { get; init; }
    public double TextHeight { get; init; }
    public double TitleHeight { get; init; }
    /// <summary>Drawing units per paper millimetre (scale denominator x units per mm).</summary>
    public double AnnotationScale { get; init; }

    public List<Band> Bands { get; } = new();
    public Band? Subgrade { get; set; }
    /// <summary>Horizontal lines at course interfaces (y values), including the surface.</summary>
    public List<double> InterfaceLevels { get; } = new();
    /// <summary>Lines between the layers of a multi-layer course ("2 x 150mm ..."), drawn like interfaces.</summary>
    public List<double> LiftLevels { get; } = new();
    /// <summary>Levels of zero-thickness membranes, drawn as their own lines.</summary>
    public List<double> MembraneLevels { get; } = new();
    /// <summary>Left and right edges (each a polyline), with optional break symbols.</summary>
    public List<List<Pt>> Edges { get; } = new();
    public List<LabelPlacement> Labels { get; } = new();
    public List<DimPlacement> Dimensions { get; } = new();
    public List<TextPlacement> Titles { get; } = new();
    public List<BarPlacement> Bars { get; } = new();
    public List<BarLine> BarLines { get; } = new();
}

public static class DetailLayout
{
    public const string UseStandardUnits = "STANDARD";
    public const string AutoUnits = "AUTO";

    /// <summary>Units a CAD standard may specify.</summary>
    public static readonly string[] StandardUnitChoices = { "M", "MM", "CM", AutoUnits };

    /// <summary>Units the drawing dialog may choose (STANDARD = whatever the CAD standard says).</summary>
    public static readonly string[] SettingsUnitChoices = { UseStandardUnits, "M", "MM", "CM", AutoUnits };

    /// <summary>
    /// Drawing units per millimetre. The dialog's choice wins unless it is STANDARD, then the CAD standard's.
    /// AUTO uses the drawing's INSUNITS (<paramref name="insunits"/>: MM, CM, M, IN, FT, or null when unitless);
    /// a unitless drawing is treated as metres, which is what Civil 3D metric drawings use.
    /// </summary>
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

    /// <summary>Drawing units per millimetre for an AutoCAD INSUNITS name (MM, CM, M, IN, FT).</summary>
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
        double annot = settings.ScaleDenominator * u;          // drawing units per paper mm
        double textH = standard.TextHeightMm * annot;
        double titleH = standard.TitleHeightMm * annot;
        double width = settings.WidthMm * u;
        double hatchFactor = settings.ScaleDenominator * u * settings.HatchScaleMultiplier;

        var g = new DetailGeometry { Width = width, TextHeight = textH, TitleHeight = titleH, AnnotationScale = annot };

        // --- courses ---------------------------------------------------------------------------
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
            // Leader to the middle of the course, or of its top layer so it doesn't land on a layer line.
            // One leader branch to the middle of each layer, so "2 x 150mm" points at both.
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

        // --- subgrade --------------------------------------------------------------------------
        double bottom = formation;
        if (buildup.ShowSubgrade && settings.SubgradeDepthMm > 0)
        {
            double depth = settings.SubgradeDepthMm * u;
            var spec = MaterialLibrary.ToSpec(BaseHatch(buildup.SubgradeText, standard));
            g.Subgrade = new Band(buildup.SubgradeText, formation, formation - depth, spec, spec is null ? 0 : spec.BaseScale * hatchFactor,
                BuildupParser.IsExisting(buildup.SubgradeText));
            string text = standard.UpperCaseLabels ? buildup.SubgradeText.ToUpperInvariant() : buildup.SubgradeText;
            if (!string.IsNullOrWhiteSpace(text))
                anchors.Add((text, formation - depth / 2, double.NaN, Array.Empty<double>()));
            bottom = formation - depth;
        }

        // --- edges with break symbols ------------------------------------------------------------
        double breakSize = 2.5 * annot; // 2.5 mm on paper
        // An existing base has its own closed outline, so the new pavement's edges stop on top of it.
        double edgeBottom = g.Subgrade is { Existing: true } ? formation : bottom;
        bool canBreak = settings.ShowBreakLines && (0 - edgeBottom) > breakSize * 3;
        foreach (double x in new[] { 0.0, width })
            g.Edges.Add(Edge(x, 0, edgeBottom, canBreak ? breakSize : 0));

        // --- labels: stacked on the right, never closer than ~1.8 text heights --------------------
        double anchorX = width * 0.85;
        double elbowX = width + 6 * annot;
        double textX = elbowX + 4 * annot;
        double lineH = textH * 1.6;                // MText line spacing
        double? previous = null;
        int previousLines = 1;
        // Top to bottom (reinforcement labels sit between course labels), stable for equal levels.
        // Labels are centred on their leader, so the gap allows for half of each label's lines.
        foreach (var (text, anchorY, ax, more) in anchors.OrderByDescending(a => a.Y))
        {
            int lines = text.Split('\n').Length;
            double minGap = (previousLines + lines) * lineH / 2 + textH * 0.2; // 1.8 text heights for one-liners
            double ly = previous is null ? anchorY : Math.Min(anchorY, previous.Value - minGap);
            double x = double.IsNaN(ax) ? anchorX : ax;
            g.Labels.Add(new LabelPlacement(text, new Pt(x, anchorY), new Pt(elbowX, ly), new Pt(textX, ly))
            {
                ExtraAnchors = more.Select(m => new Pt(x, m)).ToArray(),
            });
            previous = ly;
            previousLines = lines;
        }

        // --- dimensions on the left ----------------------------------------------------------------
        if (settings.ShowDimensions)
        {
            double dimX = -8 * annot;
            foreach (var (top, bot) in dimSegments) // one per layer, so "2 x 150" shows 150 + 150
                g.Dimensions.Add(new DimPlacement(top, bot, dimX, DimText((top - bot) / u, standard)));
            if (dimSegments.Count > 1)
                g.Dimensions.Add(new DimPlacement(0, formation, dimX - 10 * annot, DimText(buildup.TotalThicknessMm, standard)));
        }
        if (standard.ShowCoverDimension)
            g.Dimensions.AddRange(coverDims);

        // --- title underneath --------------------------------------------------------------------
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
            string title = CadStandard.Fill(standard.TitleFormat, tokens);
            if (title.Length > 0)
            {
                g.Titles.Add(new TextPlacement(standard.UpperCaseLabels ? title.ToUpperInvariant() : title, new Pt(0, ty), titleH, true));
                ty -= titleH * 1.8;
            }
            foreach (var format in new[] { standard.ScaleFormat, standard.TotalFormat })
            {
                string line = CadStandard.Fill(format, tokens);
                if (line.Length == 0) continue;
                g.Titles.Add(new TextPlacement(line, new Pt(0, ty), textH, false));
                ty -= textH * 1.8;
            }
        }

        return g;
    }

    /// <summary>Bars across the width (centred), transverse line, label anchor and cover dimension for one mat.</summary>
    private static void AddReinforcement(DetailGeometry g, Reinforcement mat, double top, double bottom, double u, double width,
        CadStandard standard, List<(string, double, double, double[])> anchors, List<DimPlacement> coverDims)
    {
        double dir = mat.Face == BarFace.Bottom ? 1 : -1;       // +1 = measured up from the bottom face
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

        // Label from the bar nearest 70% across (away from the course label anchor at 85%).
        double labelX = xs.MinBy(x => Math.Abs(x - width * 0.7));
        anchors.Add((standard.ReinforcementLabel(mat), barY, labelX, Array.Empty<double>()));

        // Cover: face to the outside of the bar nearest 15% across, dimension line through the bar.
        if (mat.CoverMm > 0)
        {
            double dimAt = xs.MinBy(x => Math.Abs(x - width * 0.15));
            double edge = face + dir * mat.CoverMm * u;
            string text = CadStandard.Fill(standard.CoverDimensionFormat ?? "{cover}", ("cover", CadStandard.Number(mat.CoverMm)));
            coverDims.Add(new DimPlacement(Math.Max(face, edge), Math.Min(face, edge), dimAt, text, dimAt));
        }
    }

    /// <summary>
    /// Hatch for what the pavement sits on: the standard's subgrade hatch for subgrade/formation, otherwise
    /// the hatch rule its wording matches ("EXISTING CONCRETE SLAB" → concrete).
    /// </summary>
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
        return layer.Lifts > 1 ? $"{layer.Lifts} x {label}" : label; // "2 x 150mm THICK LAYERS OF ..."
    }

    private static string DimText(double mm, CadStandard standard) =>
        CadStandard.Fill(standard.DimensionFormat, ("thickness", CadStandard.Number(mm)));

    /// <summary>Vertical edge from <paramref name="top"/> to <paramref name="bottom"/> with a Z break mid-height.</summary>
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
