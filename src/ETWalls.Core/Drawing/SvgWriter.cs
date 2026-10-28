using System.Globalization;
using System.Net;
using System.Text;

namespace ETWalls.Core.Drawing;

/// <summary>Writes a section drawing as SVG (paper mm), for the HTML report.</summary>
public static class SvgWriter
{
    const string Ink = "#111", HatchInk = "#555";
    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Write(SectionDrawing d, double maxWidthPx = 0)
    {
        var (vx, vy, vw, vh) = d.View;
        var sb = new StringBuilder();
        string style = maxWidthPx > 0 ? $" style=\"max-width:{F(maxWidthPx)}px\"" : "";
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"cad\" viewBox=\"{F(vx)} {F(vy)} {F(vw)} {F(vh)}\" role=\"img\" aria-label=\"Typical wall section, scale 1:{d.Scale}\" font-family=\"Arial, Helvetica, sans-serif\" fill=\"{Ink}\"{style}>");
        sb.Append("<defs>");
        foreach (var fill in new[] { Fill.Earth, Fill.Concrete, Fill.Gravel, Fill.Steel })
            sb.Append(Pattern(Id(fill, d.Scale), Patterns.For(fill, d.Scale)!));
        sb.Append("</defs>");
        sb.Append($"<rect x=\"{F(vx)}\" y=\"{F(vy)}\" width=\"{F(vw)}\" height=\"{F(vh)}\" fill=\"#fff\"/>");
        foreach (var item in d.Items)
        {
            switch (item)
            {
                case Shape s:
                    string pts = string.Join(" ", s.Points.Select(p => $"{F(p.X)},{F(p.Y)}"));
                    string stroke = s.Stroke > 0 ? $" stroke=\"{Ink}\" stroke-width=\"{F(s.Stroke)}\"{(s.Dashed ? " stroke-dasharray=\"1.5 0.8\"" : "")} stroke-linejoin=\"round\"" : " stroke=\"none\"";
                    if (!s.Closed) { sb.Append($"<polyline points=\"{pts}\" fill=\"none\"{stroke}/>"); break; }
                    if (s.Fill is Fill.Concrete or Fill.Gravel or Fill.White)   // cover the soil under it
                        sb.Append($"<polygon points=\"{pts}\" fill=\"#fff\" stroke=\"none\"/>");
                    string fill = s.Fill switch { Fill.None => "none", Fill.White => "#fff", Fill.Ink => Ink, _ => $"url(#{Id(s.Fill, d.Scale)})" };
                    sb.Append($"<polygon points=\"{pts}\" fill=\"{fill}\"{stroke}/>");
                    break;
                case Circle c:
                    string cf = c.Fill == Fill.Ink ? Ink : "#fff";
                    string cs = c.Stroke > 0 ? $" stroke=\"{Ink}\" stroke-width=\"{F(c.Stroke)}\"" : "";
                    sb.Append($"<circle cx=\"{F(c.Centre.X)}\" cy=\"{F(c.Centre.Y)}\" r=\"{F(c.Radius)}\" fill=\"{cf}\"{cs}/>");
                    break;
                case Label t:
                    string anchor = t.Anchor switch { Anchor.Middle => "middle", Anchor.End => "end", _ => "start" };
                    string rot = t.Rotation != 0 ? $" transform=\"rotate({F(-t.Rotation)} {F(t.At.X)} {F(t.At.Y)})\"" : "";
                    string bold = t.Bold ? " font-weight=\"700\"" : "";
                    sb.Append($"<text x=\"{F(t.At.X)}\" y=\"{F(t.At.Y)}\" font-size=\"{F(t.Height / 0.716)}\" text-anchor=\"{anchor}\"{bold}{rot}>{WebUtility.HtmlEncode(t.Text)}</text>");
                    break;
            }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    static string Id(Fill f, int scale) => f == Fill.Earth ? $"cad-earth-{scale}" : "cad-" + f.ToString().ToLowerInvariant();

    static string Pattern(string id, PatternTile t)
    {
        var sb = new StringBuilder();
        string transform = (t.Angle != 0 || t.Unit != 1) ? $" patternTransform=\"rotate({F(-t.Angle)}) scale({t.Unit.ToString("0.####", CultureInfo.InvariantCulture)})\"" : "";
        sb.Append($"<pattern id=\"{id}\" width=\"{F(t.Size)}\" height=\"{F(t.Size)}\" patternUnits=\"userSpaceOnUse\"{transform}>");
        string w = (t.Pen / t.Unit).ToString("0.####", CultureInfo.InvariantCulture);
        if (t.Lines.Count > 0)
        {
            sb.Append($"<g stroke=\"{HatchInk}\" stroke-width=\"{w}\">");
            foreach (var (a, b) in t.Lines) sb.Append($"<line x1=\"{F5(a.X)}\" y1=\"{F5(a.Y)}\" x2=\"{F5(b.X)}\" y2=\"{F5(b.Y)}\"/>");
            sb.Append("</g>");
        }
        if (t.Dots.Count > 0)
        {
            sb.Append($"<g fill=\"{HatchInk}\">");
            foreach (var (c, r) in t.Dots) sb.Append($"<circle cx=\"{F(c.X)}\" cy=\"{F(c.Y)}\" r=\"{F(r)}\"/>");
            sb.Append("</g>");
        }
        if (t.Outlines.Count > 0)
        {
            sb.Append($"<g fill=\"none\" stroke=\"{HatchInk}\" stroke-width=\"{w}\">");
            foreach (var p in t.Outlines) sb.Append($"<polygon points=\"{string.Join(" ", p.Select(q => $"{F(q.X)},{F(q.Y)}"))}\"/>");
            sb.Append("</g>");
        }
        sb.Append("</pattern>");
        return sb.ToString();
    }

    static string F5(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
