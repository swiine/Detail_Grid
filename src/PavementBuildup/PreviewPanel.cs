using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>Schematic preview of the detail, drawn from the same layout the CAD drawer uses.</summary>
internal sealed class PreviewPanel : Panel
{
    private DetailGeometry? _geometry;
    private string? _error;

    public PreviewPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        ResizeRedraw = true;
    }

    public void UpdatePreview(Buildup buildup, DetailSettings settings, CadStandard standard)
    {
        try
        {
            _geometry = DetailLayout.Build(buildup, settings, standard, 1.0);
            _error = null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            _geometry = null;
            _error = ex.Message;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var gr = e.Graphics;
        gr.SmoothingMode = SmoothingMode.AntiAlias;

        if (_geometry is not { } g)
        {
            TextRenderer.DrawText(gr, _error ?? "Add layers to see a preview.", Font, ClientRectangle, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        // Fit the detail and labels (approximate label width from character count).
        double longest = g.Labels.Count == 0 ? 0 : g.Labels.Max(l => l.Text.Length) * g.TextHeight * 0.75;
        double minX = g.Dimensions.Count > 0 ? g.Dimensions.Min(d => d.X) - g.TextHeight * 3 : -g.TextHeight;
        double maxX = (g.Labels.Count > 0 ? g.Labels[0].TextAt.X + longest : g.Width) + g.TextHeight;
        double minY = new[] { g.Subgrade?.Bottom ?? g.InterfaceLevels[^1] }
            .Concat(g.Labels.Select(l => l.TextAt.Y)).Concat(g.Titles.Select(t => t.At.Y)).Min() - g.TextHeight * 2;
        double maxY = g.TextHeight * 2;

        float pad = 8;
        double scale = Math.Min((Width - 2 * pad) / (maxX - minX), (Height - 2 * pad) / (maxY - minY));
        PointF P(double x, double y) => new((float)(pad + (x - minX) * scale), (float)(pad + (maxY - y) * scale));
        RectangleF R(double top, double bottom) { var a = P(0, top); var b = P(g.Width, bottom); return RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y); }

        float fontPx = Math.Max(6f, (float)(g.TextHeight * scale));
        using var font = new Font(Font.FontFamily, fontPx, GraphicsUnit.Pixel);
        using var outline = new Pen(Color.Black, 1.5f);
        using var thin = new Pen(Color.DimGray, 1f);

        foreach (var band in g.Bands.Concat(g.Subgrade is null ? Array.Empty<Band>() : new[] { g.Subgrade }))
        {
            using var brush = BrushFor(band.Hatch?.Pattern);
            gr.FillRectangle(brush, R(band.Top, band.Bottom));
        }
        foreach (var y in g.InterfaceLevels)
            gr.DrawLine(outline, P(0, y), P(g.Width, y));
        using (var membrane = new Pen(Color.Red, 2.5f))
            foreach (var y in g.MembraneLevels)
                gr.DrawLine(membrane, P(0, y), P(g.Width, y));
        foreach (var edge in g.Edges)
            gr.DrawLines(outline, edge.Select(p => P(p.X, p.Y)).ToArray());

        using (var rebar = new Pen(Color.Firebrick, 2f))
            foreach (var bl in g.BarLines)
                gr.DrawLine(rebar, P(0, bl.Y), P(g.Width, bl.Y));
        foreach (var bar in g.Bars)
        {
            var c = P(bar.Center.X, bar.Center.Y);
            float r = Math.Max(1.5f, (float)(bar.Diameter * scale / 2));
            gr.FillEllipse(Brushes.Firebrick, c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        foreach (var l in g.Labels)
        {
            gr.DrawLines(thin, new[] { P(l.Anchor.X, l.Anchor.Y), P(l.Elbow.X, l.Elbow.Y), P(l.TextAt.X, l.TextAt.Y) });
            var a = P(l.Anchor.X, l.Anchor.Y);
            gr.FillEllipse(Brushes.Black, a.X - 2, a.Y - 2, 4, 4);
            var t = P(l.TextAt.X, l.TextAt.Y);
            gr.DrawString(l.Text, font, Brushes.Black, t.X + 2, t.Y - fontPx * 0.6f);
        }

        foreach (var d in g.Dimensions)
        {
            var top = P(d.X, d.Top);
            var bottom = P(d.X, d.Bottom);
            gr.DrawLine(thin, top, bottom);
            gr.DrawLine(thin, top.X - 3, top.Y, top.X + 3, top.Y);
            gr.DrawLine(thin, bottom.X - 3, bottom.Y, bottom.X + 3, bottom.Y);
        }

        foreach (var t in g.Titles)
        {
            using var f = new Font(Font.FontFamily, Math.Max(6f, (float)(t.Height * scale)), t.Underline ? FontStyle.Underline | FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            var p = P(t.At.X, t.At.Y);
            gr.DrawString(t.Text, f, Brushes.Black, p.X, p.Y - f.Height * 0.5f);
        }
    }

    private static Brush BrushFor(string? pattern) => pattern?.ToUpperInvariant() switch
    {
        "AR-SAND" => new HatchBrush(HatchStyle.Percent20, Color.DimGray, Color.FromArgb(235, 235, 235)),
        "AR-CONC" => new HatchBrush(HatchStyle.DottedGrid, Color.Gray, Color.FromArgb(225, 225, 225)),
        "GRAVEL" => new HatchBrush(HatchStyle.SolidDiamond, Color.Silver, Color.FromArgb(245, 240, 225)),
        "EARTH" => new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.SaddleBrown, Color.FromArgb(240, 230, 215)),
        "ANSI37" => new HatchBrush(HatchStyle.DiagonalCross, Color.Gray, Color.White),
        "SOLID" => new SolidBrush(Color.DimGray),
        null => new SolidBrush(Color.White),
        _ => new HatchBrush(HatchStyle.ForwardDiagonal, Color.Gray, Color.White),
    };
}
