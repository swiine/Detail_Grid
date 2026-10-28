using System.Globalization;
using ETWalls.Core.Drawing;
using ETWalls.Core.Standard;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using DxfLayer = netDxf.Tables.Layer;
using Layer = ETWalls.Core.Drawing.Layer;
using Shape = ETWalls.Core.Drawing.Shape;
using Circle = ETWalls.Core.Drawing.Circle;

namespace ETWalls.Export;

/// <summary>
/// Writes the wall section to a DXF (AutoCAD 2010) in metres, at true size, on the CAD standard's layers, so it
/// can be inserted into any drawing (INSERT or drag in). Text and hatches are sized for the drawing scale,
/// the same way the TTW pavement tool does it.
/// </summary>
public static class DxfExport
{
    public static void Write(SectionDrawing d, CadStandard standard, Stream stream) => Build(d, standard).Save(stream);

    public static DxfDocument Build(SectionDrawing d, CadStandard standard)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2010);
        doc.DrawingVariables.InsUnits = netDxf.Units.DrawingUnits.Meters;
        double f = d.Scale / 1000.0;                       // paper mm -> model metres
        Vector2 M(Pt p) => new(p.X * f, -p.Y * f);         // paper y is down, model y is up
        var layers = Enum.GetValues<Layer>().ToDictionary(l => l, l => LayerFor(doc, standard.Layers[l]));

        foreach (var item in d.Items)
        {
            var layer = layers[item.Layer];
            switch (item)
            {
                case Shape s when s.Closed && Pattern(s.Fill, d.Scale) is { } pattern:
                {
                    var paths = new List<HatchBoundaryPath> { Path(s.Points, M) };
                    // A DXF hatch can't cover what's under it, so the soil gets holes where the wall, gravel and pads are.
                    if (s.Fill == Fill.Earth)
                        paths.AddRange(d.Items.OfType<Shape>()
                            .Where(o => o.Closed && o.Fill is Fill.Concrete or Fill.Gravel or Fill.White or Fill.Steel && Inside(Centroid(o.Points), s.Points))
                            .Select(o => Path(o.Points, M)));
                    doc.Entities.Add(new Hatch(pattern, paths, false) { Layer = layers[Layer.Hatch] });
                    if (s.Stroke > 0) doc.Entities.Add(Outline(s, M, layer));
                    break;
                }
                case Shape s when s.Closed && s.Fill == Fill.Ink:
                {
                    var boundary = new Polyline2D(s.Points.Select(p => new Polyline2DVertex(M(p))), true);
                    doc.Entities.Add(new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { boundary }) }, false) { Layer = layer });
                    break;
                }
                case Shape s:
                    if (s.Stroke > 0 || s.Fill is Fill.White) doc.Entities.Add(Outline(s, M, layer));
                    break;
                case Circle c when c.Fill == Fill.Ink:
                    doc.Entities.Add(Dot(M(c.Centre), 2 * c.Radius * f, layer));
                    break;
                case Circle c:
                    doc.Entities.Add(new netDxf.Entities.Circle(M(c.Centre), c.Radius * f) { Layer = layer });
                    break;
                case Label t:
                    doc.Entities.Add(new MText(Escape(t.Text), M(t.At), t.Height * f)
                    {
                        AttachmentPoint = t.Anchor switch { Anchor.Middle => MTextAttachmentPoint.BottomCenter, Anchor.End => MTextAttachmentPoint.BottomRight, _ => MTextAttachmentPoint.BottomLeft },
                        Rotation = t.Rotation,
                        Layer = layer,
                    });
                    break;
            }
        }
        return doc;
    }

    static HatchBoundaryPath Path(Pt[] pts, Func<Pt, Vector2> m) =>
        new(new EntityObject[] { new Polyline2D(pts.Select(p => new Polyline2DVertex(m(p))), true) });

    static Pt Centroid(Pt[] pts) => new(pts.Average(p => p.X), pts.Average(p => p.Y));

    /// <summary>Point in polygon (ray casting).</summary>
    static bool Inside(Pt p, Pt[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    static Polyline2D Outline(Shape s, Func<Pt, Vector2> m, DxfLayer layer)
    {
        var pl = new Polyline2D(s.Points.Select(p => new Polyline2DVertex(m(p))), s.Closed) { Layer = layer };
        if (s.Dashed) { pl.Linetype = netDxf.Tables.Linetype.Dashed; pl.LinetypeScale = 0.01; }
        return pl;
    }

    /// <summary>The DXF hatch for a fill: the standard's scale x drawing scale x 0.001 (metres), as the TTW pavement tool does.</summary>
    static HatchPattern? Pattern(Fill fill, int drawScale)
    {
        double u = drawScale / 1000.0;   // acadiso (mm) pattern on paper -> metres in the model
        return fill switch
        {
            Fill.Earth => HatchPatterns.For("EARTH", Cad.EarthScale * u, Cad.EarthAngle),   // TTW.json: EARTH 0.5, 45°
            Fill.Concrete => HatchPatterns.For("AR-CONC", 0.08 * u, 0),        // TTW.json: AR-CONC 0.08
            Fill.Gravel => HatchPatterns.For("GRAVEL", 0.15 * u, 0),           // TTW.json: GRAVEL 0.15
            Fill.Steel => HatchPatterns.For("ANSI31", 0.5 * u, 0),
            _ => null,
        };
    }

    static string Escape(string text) => text.Replace(@"\", @"\\").Replace("{", @"\{").Replace("}", @"\}").Replace("\r", "").Replace("\n", @"\P");

    /// <summary>A filled dot: a closed two-arc polyline as wide as its radius (like DONUT).</summary>
    static Polyline2D Dot(Vector2 c, double diameter, DxfLayer layer)
    {
        double r = diameter / 4;
        var pl = new Polyline2D(new[] { new Polyline2DVertex(c.X - r, c.Y, 1), new Polyline2DVertex(c.X + r, c.Y, 1) }, true) { Layer = layer };
        pl.SetConstantWidth(diameter / 2);
        return pl;
    }

    static DxfLayer LayerFor(DxfDocument doc, LayerStyle style)
    {
        var name = string.IsNullOrWhiteSpace(style.Name) ? "0" : style.Name.Trim();
        if (doc.Layers.TryGetValue(name, out var existing)) return existing;
        var layer = new DxfLayer(name) { IsVisible = true, Plot = style.Plot };
        if (short.TryParse(style.Color, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci) && aci is >= 1 and <= 255) layer.Color = new AciColor(aci);
        if (style.LineWeightMm >= 0)
        {
            var all = Enum.GetValues<Lineweight>().Where(w => (short)w >= 0).ToArray();
            int target = (int)Math.Round(style.LineWeightMm * 100);
            layer.Lineweight = all.OrderBy(w => Math.Abs((short)w - target)).First();
        }
        return doc.Layers.Add(layer);
    }
}
