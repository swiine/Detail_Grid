using System.Globalization;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Blocks;
using netDxf.Tables;
using PavementBuildup.Core;
using DxfLayer = netDxf.Tables.Layer;

namespace PavementBuildup.Export;

public static class DxfExport
{
    public static void Write(Buildup buildup, DetailSettings settings, CadStandard standard, Stream stream)
    {
        double unitsPerMm = DetailLayout.ResolveUnitsPerMm(settings.UnitsOverride, standard.DrawingUnits, null);
        var g = DetailLayout.Build(buildup, settings, standard, unitsPerMm);
        var doc = NewDocument(unitsPerMm);
        Draw(doc, g, standard, e => doc.Entities.Add(e));
        doc.Save(stream);
    }

    public static void WriteMany(IReadOnlyList<Buildup> buildups, DetailSettings settings, CadStandard standard, Stream stream, int columns = 3)
    {
        if (buildups.Count == 0) throw new ArgumentException("There are no details to save.");
        double unitsPerMm = DetailLayout.ResolveUnitsPerMm(settings.UnitsOverride, standard.DrawingUnits, null);
        var doc = NewDocument(unitsPerMm);
        var placed = new List<(DetailGeometry G, DetailExtents.Box Box, Buildup B)>();
        foreach (var b in buildups)
        {
            DetailGeometry g;
            try { g = DetailLayout.Build(b, settings, standard, unitsPerMm); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { throw new ArgumentException($"{b.Name}: {ex.Message}", ex); }
            placed.Add((g, DetailExtents.Of(g), b));
        }
        columns = Math.Max(1, columns);
        double cellW = placed.Max(p => p.Box.Width), gap = placed.Max(p => p.G.TextHeight) * 6;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double y = 0;
        for (int row = 0; row * columns < placed.Count; row++)
        {
            var cells = placed.Skip(row * columns).Take(columns).ToList();
            double rowH = cells.Max(c => c.Box.Height);
            for (int i = 0; i < cells.Count; i++)
            {
                var (g, box, b) = cells[i];
                var block = new Block(UniqueBlockName(b.Name, names));
                Draw(doc, g, standard, e => block.Entities.Add(e));
                double x = i * (cellW + gap) - box.MinX;
                doc.Entities.Add(new Insert(block, new Vector2(x, y - box.MaxY)));
            }
            y -= rowH + gap;
        }
        doc.Save(stream);
    }

    private static string UniqueBlockName(string title, HashSet<string> used)
    {
        var bad = "<>/\\\":;?*|,=`".ToCharArray();
        var name = "TTW_PAVE_" + new string(title.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (name.Length > 200) name = name[..200];
        var unique = name;
        for (int i = 2; !used.Add(unique); i++) unique = $"{name}_{i}";
        return unique;
    }

    private static DxfDocument NewDocument(double unitsPerMm)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2010) { BuildDimensionBlocks = true };
        doc.DrawingVariables.InsUnits = unitsPerMm switch
        {
            0.001 => netDxf.Units.DrawingUnits.Meters,
            0.1 => netDxf.Units.DrawingUnits.Centimeters,
            _ => netDxf.Units.DrawingUnits.Millimeters,
        };
        return doc;
    }

    private static void Draw(DxfDocument doc, DetailGeometry g, CadStandard standard, Action<EntityObject> add)
    {
        var layers = Enum.GetValues<DetailElement>().ToDictionary(e => e, e => LayerFor(doc, standard.Layer(e)));

        var left = g.Edges.Count > 0 ? g.Edges[0] : new List<Pt> { new(0, 0), new(0, -1) };
        var right = g.Edges.Count > 1 ? g.Edges[1] : new List<Pt> { new(g.Width, 0), new(g.Width, -1) };

        foreach (var band in g.Bands.Append(g.Subgrade).OfType<Band>())
        {
            if (band.Hatch is { } spec)
            {
                var loop = EdgeProfile.Band(left, right, band.Top, band.Bottom);
                var boundary = new Polyline2D(loop.Select(p => new Polyline2DVertex(p.X, p.Y)), true);
                var hatch = new Hatch(HatchPatterns.For(spec.Pattern, band.HatchScale, spec.AngleDeg),
                    new[] { new HatchBoundaryPath(new EntityObject[] { boundary }) }, false)
                {
                    Layer = band.Existing ? layers[DetailElement.ExistingHatch]
                        : string.IsNullOrWhiteSpace(spec.Layer) ? layers[DetailElement.Hatch]
                        : LayerFor(doc, new LayerStyle { Name = spec.Layer.Trim(), Color = "7" }),
                };
                if (Color(spec.Color) is { } c) hatch.Color = c;
                add(hatch);
            }
            if (band.Existing)
                add(new Polyline2D(new[]
                {
                    new Polyline2DVertex(0, band.Top), new Polyline2DVertex(g.Width, band.Top),
                    new Polyline2DVertex(g.Width, band.Bottom), new Polyline2DVertex(0, band.Bottom),
                }, true) { Layer = layers[DetailElement.ExistingOutline] });
        }

        foreach (var y in g.InterfaceLevels.Concat(g.LiftLevels))
            add(new Line(new Vector2(EdgeProfile.XAt(left, y), y), new Vector2(EdgeProfile.XAt(right, y), y))
                { Layer = layers[DetailElement.Outline] });
        foreach (var y in g.MembraneLevels)
        {
            var pl = new Polyline2D(new[] { new Polyline2DVertex(EdgeProfile.XAt(left, y), y), new Polyline2DVertex(EdgeProfile.XAt(right, y), y) })
                { Layer = layers[DetailElement.Membrane] };
            pl.SetConstantWidth(Math.Max(0, standard.MembraneWidthMm) * g.AnnotationScale);
            add(pl);
        }
        foreach (var edge in g.Edges)
            add(new Polyline2D(edge.Select(p => new Polyline2DVertex(p.X, p.Y))) { Layer = layers[DetailElement.Outline] });

        foreach (var bar in g.BarLines)
        {
            var pl = new Polyline2D(new[] { new Polyline2DVertex(0, bar.Y), new Polyline2DVertex(g.Width, bar.Y) })
                { Layer = layers[DetailElement.Reinforcement] };
            pl.SetConstantWidth(bar.Diameter);
            add(pl);
        }
        foreach (var bar in g.Bars)
            add(Dot(bar.Center, bar.Diameter, layers[DetailElement.Reinforcement]));

        foreach (var label in g.Labels)
        {
            var leader = layers[DetailElement.Leader];
            add(new Polyline2D(new[]
            {
                new Polyline2DVertex(label.Anchor.X, label.Anchor.Y),
                new Polyline2DVertex(label.Elbow.X, label.Elbow.Y),
                new Polyline2DVertex(label.TextAt.X - g.TextHeight * 0.5, label.TextAt.Y),
            }) { Layer = leader });
            add(Dot(label.Anchor, 0.6 * g.AnnotationScale, leader));
            foreach (var extra in label.ExtraAnchors)
            {
                add(new Line(new Vector2(extra.X, extra.Y), new Vector2(label.Elbow.X, label.Elbow.Y)) { Layer = leader });
                add(Dot(extra, 0.6 * g.AnnotationScale, leader));
            }
            add(Text(label.Text, label.TextAt, g.TextHeight, false, false, layers[DetailElement.Text]));
        }

        if (g.Dimensions.Count > 0)
        {
            var style = new DimensionStyle("TTW_PAVEMENT")
            {
                DimScaleOverall = g.AnnotationScale,
                TextHeight = standard.DimensionTextHeightMm,
                ArrowSize = standard.DimensionArrowSizeMm,
                ExtLineExtend = 1.0,
                ExtLineOffset = 0.6,
                TextOffset = 0.6,
            };
            foreach (var d in g.Dimensions)
            {
                var dim = new LinearDimension(new Vector2(d.RefX, d.Top), new Vector2(d.RefX, d.Bottom), d.RefX - d.X, 90, style)
                {
                    UserText = d.Text,
                    Layer = layers[DetailElement.Dimension],
                };
                add(dim);
            }
        }

        foreach (var t in g.Titles)
            add(Text(t.Text, t.At, t.Height, t.Underline, t.Centered, layers[DetailElement.Title]));
    }

    private static MText Text(string text, Pt at, double height, bool underline, bool centered, DxfLayer layer)
    {
        string escaped = text.Replace(@"\", @"\\").Replace("{", @"\{").Replace("}", @"\}").Replace("\r", "").Replace("\n", @"\P");
        return new MText(underline ? @"\L" + escaped + @"\l" : escaped, new Vector2(at.X, at.Y), height)
        {
            AttachmentPoint = centered ? MTextAttachmentPoint.MiddleCenter : MTextAttachmentPoint.MiddleLeft,
            Layer = layer,
        };
    }

    private static Polyline2D Dot(Pt c, double diameter, DxfLayer layer)
    {
        double r = diameter / 4;
        var pl = new Polyline2D(new[] { new Polyline2DVertex(c.X - r, c.Y, 1), new Polyline2DVertex(c.X + r, c.Y, 1) }, true) { Layer = layer };
        pl.SetConstantWidth(diameter / 2);
        return pl;
    }

    private static DxfLayer LayerFor(DxfDocument doc, LayerStyle style)
    {
        var name = string.IsNullOrWhiteSpace(style.Name) ? "0" : style.Name.Trim();
        if (doc.Layers.TryGetValue(name, out var existing))
            return existing;
        var layer = new DxfLayer(name) { IsVisible = true, Plot = style.Plot };
        if (Color(style.Color) is { } c && !c.IsByLayer && !c.IsByBlock) layer.Color = c;
        if (style.LineWeightMm >= 0) layer.Lineweight = Lineweight(style.LineWeightMm);
        return doc.Layers.Add(layer);
    }

    private static AciColor? Color(string? text)
    {
        var s = (text ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0) return null;
        if (s == "BYLAYER") return AciColor.ByLayer;
        if (s == "BYBLOCK") return AciColor.ByBlock;
        if (short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci) && aci is >= 1 and <= 255)
            return new AciColor(aci);
        var rgb = s.Split(',');
        if (rgb.Length == 3 && rgb.All(p => byte.TryParse(p.Trim(), out _)))
            return new AciColor(byte.Parse(rgb[0].Trim()), byte.Parse(rgb[1].Trim()), byte.Parse(rgb[2].Trim()));
        return null;
    }

    private static Lineweight Lineweight(double mm)
    {
        var all = Enum.GetValues<Lineweight>().Where(w => (short)w >= 0).ToArray();
        int target = (int)Math.Round(mm * 100);
        return all.OrderBy(w => Math.Abs((short)w - target)).First();
    }
}
