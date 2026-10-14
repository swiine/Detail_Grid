using System.Globalization;
using PavementBuildup.Core;
using PavementBuildup.Core.Design;

namespace PavementBuildup.Export;

public sealed record PdfSheet
{
    public string Project { get; init; } = "";
    public string JobNumber { get; init; } = "";
    public string Paper { get; init; } = "A4";
    public string Note { get; init; } = "TTW Pavement  ·  for information: check against the drawing set before use";
}

public static class PdfExport
{
    private const string Blue = "#024360";
    private const double Margin = 10, StripHeight = 20;

    public static void Write(Buildup buildup, DetailSettings settings, CadStandard standard, PdfSheet sheet, Stream stream) =>
        WriteMany(new[] { buildup }, settings, standard, sheet, stream);

    public static void WriteMany(IReadOnlyList<Buildup> buildups, DetailSettings settings, CadStandard standard, PdfSheet sheet, Stream stream)
    {
        if (buildups.Count == 0) throw new ArgumentException("There are no details to save.");
        var pdf = new PdfWriter { Title = buildups.Count == 1 ? buildups[0].Name : $"{buildups.Count} pavement details" };
        var (pw, ph) = Paper(sheet.Paper);
        foreach (var b in buildups)
        {
            DetailGeometry g;
            try { g = DetailLayout.Build(b, settings, standard, 1.0); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { throw new ArgumentException($"{b.Name}: {ex.Message}", ex); }
            pdf.BeginPage(pw, ph);
            var area = Frame(pdf, pw, ph);
            var (scaleText, _) = DrawDetail(pdf, g, standard, settings.ScaleDenominator, area);
            Strip(pdf, pw, b.Name, sheet, scaleText + " @ " + sheet.Paper.ToUpperInvariant());
        }
        pdf.Save(stream);
    }

    private static (double W, double H) Paper(string paper) =>
        string.Equals(paper?.Trim(), "A3", StringComparison.OrdinalIgnoreCase) ? (420, 297) : (297, 210);

    private static (double X, double Y, double W, double H) Frame(PdfWriter pdf, double pw, double ph)
    {
        pdf.Stroke(Blue);
        pdf.LineWidth(0.5);
        pdf.Rect(Margin, Margin, pw - 2 * Margin, ph - 2 * Margin);
        return (Margin + 6, Margin + StripHeight + 6, pw - 2 * Margin - 12, ph - 2 * Margin - StripHeight - 12);
    }

    private static void Strip(PdfWriter pdf, double pw, string title, PdfSheet sheet, string scale)
    {
        double x = Margin, y = Margin, w = pw - 2 * Margin, h = StripHeight;
        pdf.Stroke(Blue);
        pdf.LineWidth(0.35);
        pdf.Line(x, y + h, x + w, y + h);
        pdf.Fill(Blue);
        pdf.Rect(x, y, 34, h, fill: true, stroke: false);
        pdf.Fill("#F7F4E7");
        pdf.Text("TTW", x + 17, y + h / 2 - 3, 6, bold: true, align: PdfWriter.Align.Center);
        pdf.Fill("#1E1711");
        pdf.Text(title, x + 40, y + h - 7, 4, bold: true);
        var job = string.Join("   ·   ", new[] { sheet.Project.Trim(), sheet.JobNumber.Trim().Length > 0 ? "Job " + sheet.JobNumber.Trim() : "" }.Where(s => s.Length > 0));
        if (job.Length > 0) pdf.Text(job, x + 40, y + h - 13, 2.6);
        pdf.Fill("#6F6A5D");
        pdf.Text(sheet.Note, x + 40, y + 3.2, 1.8);
        pdf.Fill("#1E1711");
        double right = x + w - 4;
        pdf.Text(scale, right, y + h - 7, 3, bold: true, align: PdfWriter.Align.Right);
        pdf.Text(DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture), right, y + h - 13, 2.6, align: PdfWriter.Align.Right);
    }

    private static (string ScaleText, double K) DrawDetail(PdfWriter pdf, DetailGeometry g, CadStandard standard, double scaleDenominator,
        (double X, double Y, double W, double H) area)
    {
        var box = DetailExtents.Of(g);
        double th0 = g.TextHeight;
        double maxX = box.MaxX, minX = box.MinX;
        foreach (var l in g.Labels)
            maxX = Math.Max(maxX, l.TextAt.X + l.Text.Split('\n').Max(line => PdfWriter.TextWidth(line, th0)) + th0);
        foreach (var t in g.Titles)
        {
            double w = PdfWriter.TextWidth(t.Text, t.Height, t.Underline);
            maxX = Math.Max(maxX, (t.Centered ? t.At.X + w / 2 : t.At.X + w) + th0);
            if (t.Centered) minX = Math.Min(minX, t.At.X - w / 2 - th0);
        }
        box = box with { MinX = minX, MaxX = maxX };
        double k = 1 / Math.Max(1e-9, g.AnnotationScale);
        double fit = Math.Min(area.W / (box.Width * k), area.H / (box.Height * k));
        bool reduced = fit < 1;
        if (reduced) k *= fit;
        double ox = area.X + (area.W - box.Width * k) / 2 - box.MinX * k;
        double oy = area.Y + (area.H - box.Height * k) / 2 - box.MinY * k;
        (double, double) P(double x, double y) => (ox + x * k, oy + y * k);
        (double, double) Q(Pt p) => P(p.X, p.Y);
        double th = g.TextHeight * k;

        var left = g.Edges.Count > 0 ? g.Edges[0] : new List<Pt> { new(0, 0), new(0, -1) };
        var right = g.Edges.Count > 1 ? g.Edges[1] : new List<Pt> { new(g.Width, 0), new(g.Width, -1) };

        foreach (var band in g.Bands.Append(g.Subgrade).OfType<Band>())
        {
            var loop = EdgeProfile.Band(left, right, band.Top, band.Bottom);
            if (loop.Count < 3) continue;
            var poly = loop.Select(Q).ToList();
            if (band.Hatch is { } spec)
            {
                pdf.Save();
                pdf.Clip(poly);
                if (string.Equals(spec.Pattern, "SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    pdf.Fill("#555555");
                    pdf.Polyline(poly, closed: true, fill: true, stroke: false);
                }
                else
                {
                    pdf.Stroke(band.Existing ? "#9A9A9A" : "#5A5A5A");
                    pdf.LineWidth(0.1);
                    Hatch(pdf, spec.Pattern, band.HatchScale, spec.AngleDeg, loop, P);
                }
                pdf.Restore();
            }
            if (band.Existing)
            {
                pdf.Stroke("#888888");
                pdf.LineWidth(0.25);
                pdf.Polyline(poly, closed: true);
            }
        }

        pdf.Stroke("#111111");
        pdf.LineWidth(0.35);
        foreach (var y in g.InterfaceLevels.Concat(g.LiftLevels))
        {
            var (x1, y1) = P(EdgeProfile.XAt(left, y), y);
            var (x2, y2) = P(EdgeProfile.XAt(right, y), y);
            pdf.Line(x1, y1, x2, y2);
        }
        foreach (var edge in g.Edges) pdf.Polyline(edge.Select(Q).ToList());
        pdf.Stroke("#C0392B");
        pdf.LineWidth(Math.Max(0.5, standard.MembraneWidthMm));
        foreach (var y in g.MembraneLevels)
        {
            var (x1, y1) = P(EdgeProfile.XAt(left, y), y);
            var (x2, y2) = P(EdgeProfile.XAt(right, y), y);
            pdf.Line(x1, y1, x2, y2);
        }

        pdf.Stroke("#A84933");
        pdf.Fill("#A84933");
        foreach (var bar in g.BarLines)
        {
            pdf.LineWidth(Math.Max(0.2, bar.Diameter * k));
            var (x1, y1) = P(0, bar.Y);
            var (x2, y2) = P(g.Width, bar.Y);
            pdf.Line(x1, y1, x2, y2);
        }
        foreach (var bar in g.Bars)
        {
            var (cx, cy) = Q(bar.Center);
            pdf.Circle(cx, cy, Math.Max(0.3, bar.Diameter * k / 2));
        }

        pdf.Stroke("#333333");
        pdf.LineWidth(0.18);
        foreach (var l in g.Labels)
        {
            var end = new Pt(l.TextAt.X - g.TextHeight * 0.5, l.TextAt.Y);
            pdf.Polyline(new[] { Q(l.Anchor), Q(l.Elbow), Q(end) });
            pdf.Fill("#111111");
            foreach (var a in l.ExtraAnchors.Prepend(l.Anchor))
            {
                var (ax, ay) = Q(a);
                var (ex, ey) = Q(l.Elbow);
                pdf.Line(ax, ay, ex, ey);
                pdf.Circle(ax, ay, Math.Max(0.35, th * 0.18));
            }
            var lines = l.Text.Split('\n');
            double first = l.TextAt.Y + (lines.Length - 1) * g.TextHeight * 0.8;
            for (int i = 0; i < lines.Length; i++)
            {
                var (tx, ty) = P(l.TextAt.X, first - i * g.TextHeight * 1.6);
                pdf.Text(lines[i], tx, ty - th / 2, th);
            }
        }

        pdf.Stroke(Blue);
        pdf.Fill(Blue);
        pdf.LineWidth(0.18);
        foreach (var d in g.Dimensions)
        {
            var (x, top) = P(d.X, d.Top);
            var (_, bottom) = P(d.X, d.Bottom);
            var (rx, _) = P(d.RefX, d.Top);
            pdf.Line(x, top, x, bottom);
            foreach (var yy in new[] { top, bottom })
            {
                pdf.Line(x - th * 0.4, yy - th * 0.4, x + th * 0.4, yy + th * 0.4);
                pdf.Line(x, yy, rx, yy);
            }
            pdf.Text(d.Text, x - th * 0.35, (top + bottom) / 2, th * 0.85, align: PdfWriter.Align.Center, angleDeg: 90);
        }

        pdf.Fill(Blue);
        foreach (var t in g.Titles)
        {
            var (tx, ty) = Q(t.At);
            double h = t.Height * k;
            var align = t.Centered ? PdfWriter.Align.Center : PdfWriter.Align.Left;
            pdf.Text(t.Text, tx, ty - h / 2, h, bold: t.Underline, align: align);
            if (t.Underline)
            {
                double w = PdfWriter.TextWidth(t.Text, h, bold: true);
                double x0 = t.Centered ? tx - w / 2 : tx;
                pdf.Stroke(Blue);
                pdf.LineWidth(Math.Max(0.25, h * 0.07));
                pdf.Line(x0, ty - h / 2 - h * 0.25, x0 + w, ty - h / 2 - h * 0.25);
            }
        }
        string scale = reduced ? "NOT TO SCALE (reduced to fit)" : "SCALE 1:" + CadStandard.Number(scaleDenominator);
        return (scale, k);
    }

    private static void Hatch(PdfWriter pdf, string pattern, double scale, double angleDeg, IReadOnlyList<Pt> loop, Func<double, double, (double, double)> map)
    {
        double s = scale > 0 ? scale : 1;
        double minX = loop.Min(p => p.X), maxX = loop.Max(p => p.X), minY = loop.Min(p => p.Y), maxY = loop.Max(p => p.Y);
        var corners = new[] { (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY) };
        double ha = angleDeg * Math.PI / 180;
        int budget = 60000;
        foreach (var line in HatchPatterns.Lines(pattern))
        {
            double a = line.Angle * Math.PI / 180 + ha;
            double ux = Math.Cos(a), uy = Math.Sin(a), vx = -uy, vy = ux;
            double ox = (line.Ox * Math.Cos(ha) - line.Oy * Math.Sin(ha)) * s;
            double oy = (line.Ox * Math.Sin(ha) + line.Oy * Math.Cos(ha)) * s;
            double step = line.Dy * s, shift = line.Dx * s;
            if (Math.Abs(step) < 1e-9) continue;
            double o_v = ox * vx + oy * vy;
            var vs = corners.Select(c => c.Item1 * vx + c.Item2 * vy).ToArray();
            int n0 = (int)Math.Floor((vs.Min() - o_v) / Math.Abs(step)) - 1, n1 = (int)Math.Ceiling((vs.Max() - o_v) / Math.Abs(step)) + 1;
            double dashLength = line.Dashes.Sum(Math.Abs) * s;
            for (int n = n0; n <= n1 && budget > 0; n++)
            {
                double sign = step < 0 ? -1 : 1;
                double px = ox + n * sign * (shift * ux + step * vx), py = oy + n * sign * (shift * uy + step * vy);
                var ts = corners.Select(c => (c.Item1 - px) * ux + (c.Item2 - py) * uy).ToArray();
                double t0 = ts.Min(), t1 = ts.Max();
                if (line.Dashes.Length == 0 || dashLength < 1e-9)
                {
                    var (x1, y1) = map(px + t0 * ux, py + t0 * uy);
                    var (x2, y2) = map(px + t1 * ux, py + t1 * uy);
                    pdf.Segment(x1, y1, x2, y2);
                    budget--;
                    continue;
                }
                for (double t = Math.Floor(t0 / dashLength) * dashLength; t < t1 && budget > 0;)
                {
                    foreach (var dash in line.Dashes)
                    {
                        double len = Math.Abs(dash) * s;
                        if (dash >= 0 && t + len >= t0 && t <= t1)
                        {
                            var (x1, y1) = map(px + t * ux, py + t * uy);
                            var (x2, y2) = map(px + (t + len) * ux, py + (t + len) * uy);
                            pdf.Segment(x1, y1, x2, y2);
                            budget--;
                        }
                        t += len;
                    }
                }
            }
        }
        pdf.StrokePath();
    }

    public static void WriteJoints(JointPlan plan, string title, PdfSheet sheet, Stream stream)
    {
        var pdf = new PdfWriter { Title = title };
        var (pw, ph) = Paper(sheet.Paper);
        pdf.BeginPage(pw, ph);
        var area = Frame(pdf, pw, ph);
        double tableW = 85;
        var draw = (X: area.X + 10, Y: area.Y + 10, W: area.W - tableW - 20, H: area.H - 18);

        var j = plan.Inputs;
        double[] scales = { 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000 };
        double scale = scales.FirstOrDefault(sc => j.LengthM * 1000 / sc <= draw.W && j.WidthM * 1000 / sc <= draw.H);
        if (scale == 0) scale = Math.Ceiling(Math.Max(j.LengthM * 1000 / draw.W, j.WidthM * 1000 / draw.H));
        double k = 1000 / scale;
        double x0 = draw.X + (draw.W - j.LengthM * k) / 2, y0 = draw.Y + (draw.H - j.WidthM * k) / 2;

        pdf.Fill("#F7F4E7");
        pdf.Stroke("#111111");
        pdf.LineWidth(0.6);
        pdf.Rect(x0, y0, j.LengthM * k, j.WidthM * k, fill: true, stroke: true);
        pdf.Stroke("#A84933");
        pdf.LineWidth(0.3);
        pdf.Dash(3, 1.5);
        foreach (var x in plan.CutsAlongX) pdf.Line(x0 + x * k, y0, x0 + x * k, y0 + j.WidthM * k);
        foreach (var y in plan.CutsAlongY) pdf.Line(x0, y0 + y * k, x0 + j.LengthM * k, y0 + y * k);
        pdf.Dash(0, 0);

        pdf.Fill("#024360");
        double th = 2.2;
        bool roomAlong = plan.PanelLengthM * k > PdfWriter.TextWidth("0.00", th) * 1.4;
        for (int i = 0; i < plan.PanelsAlong && (roomAlong || i == 0); i++)
            pdf.Text($"{plan.PanelLengthM:0.00}", x0 + (i + 0.5) * plan.PanelLengthM * k, y0 - 5, th, align: PdfWriter.Align.Center);
        bool roomAcross = plan.PanelWidthM * k > PdfWriter.TextWidth("0.00", th) * 1.4;
        for (int i = 0; i < plan.PanelsAcross && (roomAcross || i == 0); i++)
            pdf.Text($"{plan.PanelWidthM:0.00}", x0 - 3, y0 + (i + 0.5) * plan.PanelWidthM * k, th, align: PdfWriter.Align.Center, angleDeg: 90);
        pdf.Text($"{j.LengthM:0.##} m", x0 + j.LengthM * k / 2, y0 + j.WidthM * k + 3, th * 1.1, bold: true, align: PdfWriter.Align.Center);
        pdf.Text($"{j.WidthM:0.##} m", x0 + j.LengthM * k + 5, y0 + j.WidthM * k / 2, th * 1.1, bold: true, align: PdfWriter.Align.Center, angleDeg: 90);

        double tx = area.X + area.W - tableW, ty = area.Y + area.H - 6;
        pdf.Fill("#024360");
        pdf.Text("JOINT LAYOUT", tx, ty, 3.5, bold: true);
        ty -= 8;
        pdf.Fill("#1E1711");
        foreach (var (label, value) in new[]
        {
            ("Slab", $"{j.LengthM:0.##} x {j.WidthM:0.##} m, {j.ThicknessMm:0} mm"),
            ("Type", JointLayout.Describe(j.Kind)),
            ("Max joint spacing", $"{plan.SpacingLimitM:0.00} m"),
            ("Panels", $"{plan.PanelsAlong} x {plan.PanelsAcross} = {plan.Panels}"),
            ("Panel size", $"{plan.PanelLengthM:0.00} x {plan.PanelWidthM:0.00} m"),
            ("Aspect ratio", $"{plan.Aspect:0.00}"),
            ("Sawcut joints", $"{plan.SawcutLengthM:0.0} m, {plan.SawcutDepthMm} mm deep"),
            ("Isolation joint", $"{plan.PerimeterM:0.0} m round the slab"),
        })
        {
            pdf.Text(label, tx, ty, 2.2);
            pdf.Text(value, tx + 32, ty, 2.2, bold: true);
            ty -= 5;
        }
        ty -= 3;
        pdf.Stroke("#A84933");
        pdf.LineWidth(0.3);
        pdf.Dash(3, 1.5);
        pdf.Line(tx, ty + 0.8, tx + 10, ty + 0.8);
        pdf.Dash(0, 0);
        pdf.Text("Sawcut contraction joint", tx + 13, ty, 2.2);
        ty -= 5;
        pdf.Stroke("#111111");
        pdf.LineWidth(0.6);
        pdf.Line(tx, ty + 0.8, tx + 10, ty + 0.8);
        pdf.Text("Slab edge / isolation joint", tx + 13, ty, 2.2);
        ty -= 8;
        pdf.Fill("#6F6A5D");
        foreach (var note in new[]
        {
            "Spacing from a rule of thumb: the engineer",
            "confirms the layout for each slab. Add joints",
            "at re-entrant corners, pits and columns.",
        })
        {
            pdf.Text(note, tx, ty, 1.9);
            ty -= 3.5;
        }

        Strip(pdf, pw, title, sheet, $"SCALE 1:{scale:0} @ {sheet.Paper.ToUpperInvariant()}");
        pdf.Save(stream);
    }
}
