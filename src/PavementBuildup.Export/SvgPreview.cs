using System.Globalization;
using System.Net;
using System.Text;
using PavementBuildup.Core;

namespace PavementBuildup.Export;

/// <summary>The detail as an SVG picture (for the web page), drawn from the same layout as the DXF.</summary>
public static class SvgPreview
{
    /// <summary>SVG markup, or null when the build-up can't be laid out (<paramref name="error"/> says why).</summary>
    public static string? Render(Buildup buildup, DetailSettings settings, CadStandard standard, out string? error)
    {
        DetailGeometry g;
        try
        {
            g = DetailLayout.Build(buildup, settings, standard, 1.0);
            error = null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            error = ex.Message;
            return null;
        }

        double th = g.TextHeight;
        double longest = g.Labels.Count == 0 ? 0 : g.Labels.Max(l => l.Text.Split('\n').Max(line => line.Length)) * th * 0.62;
        double titleWidth = g.Titles.Count == 0 ? 0 : g.Titles.Max(t => t.Text.Length * t.Height * 0.62);
        double minX = Math.Min(g.Dimensions.Count > 0 ? g.Dimensions.Min(d => d.X) - th * 3 : -th, g.Width / 2 - titleWidth / 2 - th);
        double maxX = Math.Max(g.Labels.Count > 0 ? g.Labels[0].TextAt.X + longest : g.Width, g.Width / 2 + titleWidth / 2) + th;
        double minY = new[] { g.Subgrade?.Bottom ?? g.InterfaceLevels[^1] }
            .Concat(g.Labels.Select(l => l.TextAt.Y - l.LineCount * th)).Concat(g.Titles.Select(t => t.At.Y - t.Height)).Min() - th * 2;
        double maxY = th * 2;

        var sb = new StringBuilder();
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string X(double x) => N(x - minX);
        string Y(double y) => N(maxY - y);
        double w = maxX - minX, h = maxY - minY;
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(w)} {N(h)}\" font-family=\"Arial Nova, Arial, sans-serif\">");
        sb.Append(Patterns(th));
        sb.Append($"<rect width=\"{N(w)}\" height=\"{N(h)}\" fill=\"#fff\"/>");

        var left = g.Edges.Count > 0 ? g.Edges[0] : new List<Pt> { new(0, 0), new(0, -1) };
        var right = g.Edges.Count > 1 ? g.Edges[1] : new List<Pt> { new(g.Width, 0), new(g.Width, -1) };
        string Points(IEnumerable<Pt> pts) => string.Join(" ", pts.Select(p => X(p.X) + "," + Y(p.Y)));

        foreach (var band in g.Bands.Append(g.Subgrade).OfType<Band>())
        {
            var loop = EdgeProfile.Band(left, right, band.Top, band.Bottom);
            sb.Append($"<polygon points=\"{Points(loop)}\" fill=\"{Fill(band.Hatch?.Pattern)}\" stroke=\"none\"/>");
            if (band.Existing)
                sb.Append($"<polygon points=\"{Points(loop)}\" fill=\"none\" stroke=\"#888\" stroke-width=\"{N(th * 0.12)}\"/>");
        }
        double stroke = th * 0.12;
        foreach (var y in g.InterfaceLevels.Concat(g.LiftLevels))
            sb.Append($"<line x1=\"{X(EdgeProfile.XAt(left, y))}\" y1=\"{Y(y)}\" x2=\"{X(EdgeProfile.XAt(right, y))}\" y2=\"{Y(y)}\" stroke=\"#111\" stroke-width=\"{N(stroke)}\"/>");
        foreach (var y in g.MembraneLevels)
            sb.Append($"<line x1=\"{X(EdgeProfile.XAt(left, y))}\" y1=\"{Y(y)}\" x2=\"{X(EdgeProfile.XAt(right, y))}\" y2=\"{Y(y)}\" stroke=\"#C0392B\" stroke-width=\"{N(stroke * 2.5)}\"/>");
        foreach (var edge in g.Edges)
            sb.Append($"<polyline points=\"{Points(edge)}\" fill=\"none\" stroke=\"#111\" stroke-width=\"{N(stroke)}\"/>");
        foreach (var bar in g.BarLines)
            sb.Append($"<line x1=\"{X(0)}\" y1=\"{Y(bar.Y)}\" x2=\"{X(g.Width)}\" y2=\"{Y(bar.Y)}\" stroke=\"#A84933\" stroke-width=\"{N(bar.Diameter)}\"/>");
        foreach (var bar in g.Bars)
            sb.Append($"<circle cx=\"{X(bar.Center.X)}\" cy=\"{Y(bar.Center.Y)}\" r=\"{N(bar.Diameter / 2)}\" fill=\"#A84933\"/>");

        double thin = th * 0.06;
        foreach (var l in g.Labels)
        {
            sb.Append($"<polyline points=\"{Points(new[] { l.Anchor, l.Elbow, new Pt(l.TextAt.X - th * 0.5, l.TextAt.Y) })}\" fill=\"none\" stroke=\"#333\" stroke-width=\"{N(thin)}\"/>");
            foreach (var a in l.ExtraAnchors.Prepend(l.Anchor))
            {
                sb.Append($"<line x1=\"{X(a.X)}\" y1=\"{Y(a.Y)}\" x2=\"{X(l.Elbow.X)}\" y2=\"{Y(l.Elbow.Y)}\" stroke=\"#333\" stroke-width=\"{N(thin)}\"/>");
                sb.Append($"<circle cx=\"{X(a.X)}\" cy=\"{Y(a.Y)}\" r=\"{N(th * 0.18)}\" fill=\"#111\"/>");
            }
            var lines = l.Text.Split('\n');
            double first = l.TextAt.Y + (lines.Length - 1) * th * 0.8; // middle-left, like the CAD MText
            for (int i = 0; i < lines.Length; i++)
                sb.Append($"<text x=\"{X(l.TextAt.X)}\" y=\"{Y(first - i * th * 1.6)}\" font-size=\"{N(th)}\" dominant-baseline=\"middle\" fill=\"#111\">{WebUtility.HtmlEncode(lines[i])}</text>");
        }

        foreach (var d in g.Dimensions)
        {
            sb.Append($"<line x1=\"{X(d.X)}\" y1=\"{Y(d.Top)}\" x2=\"{X(d.X)}\" y2=\"{Y(d.Bottom)}\" stroke=\"#024360\" stroke-width=\"{N(thin)}\"/>");
            foreach (var y in new[] { d.Top, d.Bottom })
            {
                sb.Append($"<line x1=\"{X(d.X - th * 0.4)}\" y1=\"{Y(y)}\" x2=\"{X(d.X + th * 0.4)}\" y2=\"{Y(y)}\" stroke=\"#024360\" stroke-width=\"{N(thin)}\"/>");
                sb.Append($"<line x1=\"{X(d.X)}\" y1=\"{Y(y)}\" x2=\"{X(d.RefX)}\" y2=\"{Y(y)}\" stroke=\"#024360\" stroke-width=\"{N(thin * 0.6)}\"/>");
            }
            double mid = (d.Top + d.Bottom) / 2;
            sb.Append($"<text x=\"{X(d.X - th * 0.35)}\" y=\"{Y(mid)}\" font-size=\"{N(th * 0.85)}\" text-anchor=\"middle\" fill=\"#024360\" transform=\"rotate(-90 {X(d.X - th * 0.35)} {Y(mid)})\">{WebUtility.HtmlEncode(d.Text)}</text>");
        }

        foreach (var t in g.Titles)
        {
            string anchor = t.Centered ? "middle" : "start";
            string deco = t.Underline ? " text-decoration=\"underline\" font-weight=\"bold\"" : "";
            sb.Append($"<text x=\"{X(t.At.X)}\" y=\"{Y(t.At.Y)}\" font-size=\"{N(t.Height)}\" text-anchor=\"{anchor}\" dominant-baseline=\"middle\" fill=\"#024360\"{deco}>{WebUtility.HtmlEncode(t.Text)}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Fill(string? pattern) => pattern?.ToUpperInvariant() switch
    {
        null => "#fff",
        "SOLID" => "#555",
        "AR-SAND" => "url(#p-sand)",
        "AR-CONC" => "url(#p-conc)",
        "GRAVEL" => "url(#p-gravel)",
        "EARTH" => "url(#p-earth)",
        "ANSI37" => "url(#p-cross)",
        _ => "url(#p-diag)",
    };

    /// <summary>Look-alike fills for the common hatch patterns, sized from the text height.</summary>
    private static string Patterns(double th)
    {
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        double s = th * 1.6, sw = th * 0.05;
        return "<defs>"
            + $"<pattern id=\"p-diag\" width=\"{N(s)}\" height=\"{N(s)}\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\"><rect width=\"{N(s)}\" height=\"{N(s)}\" fill=\"#fff\"/><line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"{N(s)}\" stroke=\"#666\" stroke-width=\"{N(sw)}\"/></pattern>"
            + $"<pattern id=\"p-cross\" width=\"{N(s)}\" height=\"{N(s)}\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\"><rect width=\"{N(s)}\" height=\"{N(s)}\" fill=\"#fff\"/><path d=\"M0 0V{N(s)}M0 0H{N(s)}\" stroke=\"#666\" stroke-width=\"{N(sw)}\"/></pattern>"
            + $"<pattern id=\"p-sand\" width=\"{N(s)}\" height=\"{N(s)}\" patternUnits=\"userSpaceOnUse\"><rect width=\"{N(s)}\" height=\"{N(s)}\" fill=\"#FBF8EE\"/>"
                + string.Concat(new[] { (.1, .2), (.55, .1), (.8, .45), (.3, .6), (.65, .8), (.15, .9), (.45, .4) }.Select(p => $"<circle cx=\"{N(p.Item1 * s)}\" cy=\"{N(p.Item2 * s)}\" r=\"{N(sw * 1.4)}\" fill=\"#555\"/>"))
                + "</pattern>"
            + $"<pattern id=\"p-conc\" width=\"{N(s * 2)}\" height=\"{N(s * 2)}\" patternUnits=\"userSpaceOnUse\"><rect width=\"{N(s * 2)}\" height=\"{N(s * 2)}\" fill=\"#F2F0EA\"/>"
                + $"<path d=\"M{N(s * .2)} {N(s * .3)}l{N(s * .3)} {N(s * .15)}l-{N(s * .2)} {N(s * .25)}z M{N(s * 1.2)} {N(s * 1.1)}l{N(s * .25)} -{N(s * .2)}l{N(s * .1)} {N(s * .3)}z M{N(s * .5)} {N(s * 1.5)}l{N(s * .3)} 0\" fill=\"none\" stroke=\"#666\" stroke-width=\"{N(sw)}\"/>"
                + string.Concat(new[] { (.9, .3), (1.6, .6), (.3, 1.1), (1.1, 1.7), (1.7, 1.5) }.Select(p => $"<circle cx=\"{N(p.Item1 * s)}\" cy=\"{N(p.Item2 * s)}\" r=\"{N(sw * 1.2)}\" fill=\"#666\"/>"))
                + "</pattern>"
            + $"<pattern id=\"p-gravel\" width=\"{N(s * 1.5)}\" height=\"{N(s * 1.5)}\" patternUnits=\"userSpaceOnUse\"><rect width=\"{N(s * 1.5)}\" height=\"{N(s * 1.5)}\" fill=\"#F6F1E2\"/>"
                + $"<path d=\"M{N(s * .1)} {N(s * .2)}l{N(s * .35)} -{N(s * .1)}l{N(s * .1)} {N(s * .3)}l-{N(s * .3)} {N(s * .1)}z M{N(s * .8)} {N(s * .7)}l{N(s * .4)} {N(s * .05)}l-{N(s * .1)} {N(s * .35)}l-{N(s * .35)} -{N(s * .1)}z M{N(s * .2)} {N(s * 1.05)}l{N(s * .3)} {N(s * .1)}l-{N(s * .15)} {N(s * .25)}z\" fill=\"none\" stroke=\"#777\" stroke-width=\"{N(sw)}\"/></pattern>"
            + $"<pattern id=\"p-earth\" width=\"{N(s)}\" height=\"{N(s)}\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\"><rect width=\"{N(s)}\" height=\"{N(s)}\" fill=\"#F3EBDD\"/><path d=\"M0 {N(s * .15)}H{N(s * .5)}M0 {N(s * .3)}H{N(s * .5)}M0 {N(s * .45)}H{N(s * .5)}M{N(s * .6)} {N(s * .5)}V{N(s)}M{N(s * .75)} {N(s * .5)}V{N(s)}M{N(s * .9)} {N(s * .5)}V{N(s)}\" stroke=\"#8B6B4A\" stroke-width=\"{N(sw)}\"/></pattern>"
            + "</defs>";
    }
}
