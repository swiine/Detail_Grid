namespace PavementBuildup.Core;

public readonly record struct Pt(double X, double Y);

/// <summary>A hatched band (one course, or the subgrade strip).</summary>
public sealed record Band(string Label, double Top, double Bottom, HatchSpec? Hatch, double HatchScale);

/// <summary>A leader from <see cref="Anchor"/> through <see cref="Elbow"/> to text at <see cref="TextAt"/> (middle-left).</summary>
public sealed record LabelPlacement(string Text, Pt Anchor, Pt Elbow, Pt TextAt);

/// <summary>A vertical dimension between two levels at offset <see cref="X"/>.</summary>
public sealed record DimPlacement(double Top, double Bottom, double X, string Text);

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
    /// <summary>Levels of zero-thickness membranes, drawn as their own lines.</summary>
    public List<double> MembraneLevels { get; } = new();
    /// <summary>Left and right edges (each a polyline), with optional break symbols.</summary>
    public List<List<Pt>> Edges { get; } = new();
    public List<LabelPlacement> Labels { get; } = new();
    public List<DimPlacement> Dimensions { get; } = new();
    public List<TextPlacement> Titles { get; } = new();
}

public static class DetailLayout
{
    /// <summary>Drawing units per millimetre for an AutoCAD INSUNITS name (MM, CM, M, IN, FT).</summary>
    public static double UnitsPerMm(string units) => units.Trim().ToUpperInvariant() switch
    {
        "MM" or "MILLIMETERS" or "MILLIMETRES" => 1.0,
        "CM" or "CENTIMETERS" or "CENTIMETRES" => 0.1,
        "M" or "METERS" or "METRES" => 0.001,
        "IN" or "INCHES" => 1 / 25.4,
        "FT" or "FEET" => 1 / 304.8,
        _ => 1.0,
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

        double u = unitsPerMm;
        double annot = settings.ScaleDenominator * u;          // drawing units per paper mm
        double textH = standard.TextHeightMm * annot;
        double titleH = standard.TitleHeightMm * annot;
        double width = settings.WidthMm * u;
        double hatchFactor = settings.ScaleDenominator * u * settings.HatchScaleMultiplier;

        var g = new DetailGeometry { Width = width, TextHeight = textH, TitleHeight = titleH, AnnotationScale = annot };

        // --- courses ---------------------------------------------------------------------------
        var anchors = new List<(string Text, double Y)>();
        double y = 0;
        g.InterfaceLevels.Add(0);
        foreach (var layer in buildup.Layers)
        {
            double t = layer.ThicknessMm * u;
            string label = LabelText(layer, standard);
            if (t <= 0)
            {
                g.MembraneLevels.Add(y);
                anchors.Add((label, y));
                continue;
            }

            var hatch = MaterialLibrary.Resolve(layer, standard);
            double scale = hatch is null ? 0 : hatch.BaseScale * hatchFactor * (layer.HatchScale > 0 ? layer.HatchScale : 1);
            g.Bands.Add(new Band(label, y, y - t, hatch, scale));
            anchors.Add((label, y - t / 2));
            y -= t;
            g.InterfaceLevels.Add(y);
        }
        double formation = y;

        // --- subgrade --------------------------------------------------------------------------
        double bottom = formation;
        if (buildup.ShowSubgrade && settings.SubgradeDepthMm > 0)
        {
            double depth = settings.SubgradeDepthMm * u;
            var spec = MaterialLibrary.ToSpec(standard.SubgradeHatch);
            g.Subgrade = new Band(buildup.SubgradeText, formation, formation - depth, spec, spec is null ? 0 : spec.BaseScale * hatchFactor);
            string text = standard.UpperCaseLabels ? buildup.SubgradeText.ToUpperInvariant() : buildup.SubgradeText;
            if (!string.IsNullOrWhiteSpace(text))
                anchors.Add((text, formation - depth / 2));
            bottom = formation - depth;
        }

        // --- edges with break symbols ------------------------------------------------------------
        double breakSize = 2.5 * annot; // 2.5 mm on paper
        bool canBreak = settings.ShowBreakLines && (0 - bottom) > breakSize * 3;
        foreach (double x in new[] { 0.0, width })
            g.Edges.Add(Edge(x, 0, bottom, canBreak ? breakSize : 0));

        // --- labels: stacked on the right, never closer than ~1.8 text heights --------------------
        double anchorX = width * 0.85;
        double elbowX = width + 6 * annot;
        double textX = elbowX + 4 * annot;
        double minGap = textH * 1.8;
        double? previous = null;
        foreach (var (text, anchorY) in anchors)
        {
            double ly = previous is null ? anchorY : Math.Min(anchorY, previous.Value - minGap);
            g.Labels.Add(new LabelPlacement(text, new Pt(anchorX, anchorY), new Pt(elbowX, ly), new Pt(textX, ly)));
            previous = ly;
        }

        // --- dimensions on the left ----------------------------------------------------------------
        if (settings.ShowDimensions)
        {
            double dimX = -8 * annot;
            foreach (var band in g.Bands)
            {
                double mm = (band.Top - band.Bottom) / u;
                g.Dimensions.Add(new DimPlacement(band.Top, band.Bottom, dimX, DimText(mm, standard)));
            }
            if (g.Bands.Count > 1)
                g.Dimensions.Add(new DimPlacement(0, formation, dimX - 10 * annot, DimText(buildup.TotalThicknessMm, standard)));
        }

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

    public static string LabelText(PavementLayer layer, CadStandard standard)
    {
        string d = standard.UpperCaseLabels ? layer.Description.ToUpperInvariant() : layer.Description;
        return layer.ThicknessMm > 0
            ? CadStandard.Fill(standard.LabelFormat, ("thickness", CadStandard.Number(layer.ThicknessMm)), ("description", d))
            : CadStandard.Fill(standard.MembraneLabelFormat, ("description", d));
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
