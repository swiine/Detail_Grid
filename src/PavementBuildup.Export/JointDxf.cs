using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using PavementBuildup.Core;
using PavementBuildup.Core.Design;
using DxfLayer = netDxf.Tables.Layer;

namespace PavementBuildup.Export;

public static class JointDxf
{
    public static void Write(JointPlan plan, CadStandard standard, double scaleDenominator, Stream stream)
    {
        double u = DetailLayout.ResolveUnitsPerMm(null, standard.DrawingUnits, null) * 1000;
        var doc = new DxfDocument(DxfVersion.AutoCad2010) { BuildDimensionBlocks = true };
        doc.DrawingVariables.InsUnits = u switch
        {
            1 => netDxf.Units.DrawingUnits.Meters,
            100 => netDxf.Units.DrawingUnits.Centimeters,
            _ => netDxf.Units.DrawingUnits.Millimeters,
        };
        var j = plan.Inputs;
        double L = j.LengthM * u, W = j.WidthM * u;
        double ann = scaleDenominator * u / 1000;
        var outline = Layer(doc, standard.Layer(DetailElement.Outline));
        var joints = Layer(doc, new LayerStyle { Name = standard.Layer(DetailElement.Outline).Name + "-JOINT", Color = "1" });
        var text = Layer(doc, standard.Layer(DetailElement.Text));
        var dims = Layer(doc, standard.Layer(DetailElement.Dimension));

        doc.Entities.Add(new Polyline2D(new[]
        {
            new Polyline2DVertex(0, 0), new Polyline2DVertex(L, 0), new Polyline2DVertex(L, W), new Polyline2DVertex(0, W),
        }, true) { Layer = outline });

        var dashed = Linetype.Dashed;
        if (!doc.Linetypes.Contains(dashed.Name)) doc.Linetypes.Add(dashed);
        foreach (var x in plan.CutsAlongX)
            doc.Entities.Add(new Line(new Vector2(x * u, 0), new Vector2(x * u, W)) { Layer = joints, Linetype = doc.Linetypes[dashed.Name], LinetypeScale = ann });
        foreach (var y in plan.CutsAlongY)
            doc.Entities.Add(new Line(new Vector2(0, y * u), new Vector2(L, y * u)) { Layer = joints, Linetype = doc.Linetypes[dashed.Name], LinetypeScale = ann });

        var style = new DimensionStyle("TTW_JOINTS")
        {
            DimScaleOverall = ann, TextHeight = standard.DimensionTextHeightMm, ArrowSize = standard.DimensionArrowSizeMm,
            ExtLineExtend = 1.0, ExtLineOffset = 0.6, TextOffset = 0.6, LengthPrecision = 2,
        };
        var xs = new[] { 0.0 }.Concat(plan.CutsAlongX).Append(j.LengthM).ToList();
        for (int i = 0; i + 1 < xs.Count; i++)
            doc.Entities.Add(new LinearDimension(new Vector2(xs[i] * u, 0), new Vector2(xs[i + 1] * u, 0), -8 * ann, 0, style) { Layer = dims });
        doc.Entities.Add(new LinearDimension(new Vector2(0, 0), new Vector2(L, 0), -16 * ann, 0, style) { Layer = dims });
        var ys = new[] { 0.0 }.Concat(plan.CutsAlongY).Append(j.WidthM).ToList();
        for (int i = 0; i + 1 < ys.Count; i++)
            doc.Entities.Add(new LinearDimension(new Vector2(0, ys[i + 1] * u), new Vector2(0, ys[i] * u), 8 * ann, 90, style) { Layer = dims });
        doc.Entities.Add(new LinearDimension(new Vector2(0, W), new Vector2(0, 0), 16 * ann, 90, style) { Layer = dims });

        string note = $"SAWCUT CONTRACTION JOINTS {plan.SawcutDepthMm}mm DEEP (DASHED)\\P"
                      + $"PANELS {plan.PanelLengthM:0.00} x {plan.PanelWidthM:0.00}m ({plan.PanelsAlong} x {plan.PanelsAcross})\\P"
                      + $"ISOLATION JOINT AROUND SLAB EDGE, PITS AND COLUMNS";
        doc.Entities.Add(new MText(note, new Vector2(0, W + 6 * ann), standard.TextHeightMm * ann)
            { Layer = text, AttachmentPoint = MTextAttachmentPoint.BottomLeft });
        doc.Save(stream);
    }

    private static DxfLayer Layer(DxfDocument doc, LayerStyle style)
    {
        var name = string.IsNullOrWhiteSpace(style.Name) ? "0" : style.Name.Trim();
        if (doc.Layers.TryGetValue(name, out var existing)) return existing;
        var layer = new DxfLayer(name);
        if (short.TryParse(style.Color, out var aci) && aci is >= 1 and <= 255) layer.Color = new AciColor(aci);
        return doc.Layers.Add(layer);
    }
}
