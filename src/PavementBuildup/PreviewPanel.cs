using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PavementBuildup.Core;

namespace PavementBuildup;

internal sealed class PreviewPanel : Panel
{
    private DetailGeometry? _geometry;
    private string? _error;

    private float _zoom = 1f;
    private PointF _pan;
    private Point? _dragFrom;

    public PreviewPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        ResizeRedraw = true;
        MouseWheel += (_, e) =>
        {
            float old = _zoom;
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f), 1f, 12f);
            _pan = new PointF(e.X - (e.X - _pan.X) * _zoom / old, e.Y - (e.Y - _pan.Y) * _zoom / old);
            if (_zoom == 1f) _pan = PointF.Empty;
            Invalidate();
        };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragFrom = e.Location; Cursor = Cursors.SizeAll; } };
        MouseUp += (_, _) => { _dragFrom = null; Cursor = Cursors.Default; };
        MouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from || _zoom <= 1f) return;
            _pan = new PointF(_pan.X + e.X - from.X, _pan.Y + e.Y - from.Y);
            _dragFrom = e.Location;
            Invalidate();
        };
        DoubleClick += (_, _) => { _zoom = 1f; _pan = PointF.Empty; Invalidate(); };
        MouseEnter += (_, _) => Focus();
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

        using var probe = new Font(Font.FontFamily, 100f, GraphicsUnit.Pixel);
        using var probeBold = new Font(Font.FontFamily, 100f, FontStyle.Bold | FontStyle.Underline, GraphicsUnit.Pixel);
        double WidthOf(string text, double height, bool bold) =>
            text.Split('\n').Max(line => gr.MeasureString(line, bold ? probeBold : probe).Width) / 100.0 * height;
        double th = g.TextHeight;
        double minX = g.Dimensions.Count > 0 ? g.Dimensions.Min(d => d.X) - th * 3 : -th;
        double maxX = g.Width + th;
        foreach (var l in g.Labels)
            maxX = Math.Max(maxX, l.TextAt.X + WidthOf(l.Text, th, false) + th);
        foreach (var t in g.Titles)
        {
            double w = WidthOf(t.Text, t.Height, t.Underline);
            minX = Math.Min(minX, (t.Centered ? t.At.X - w / 2 : t.At.X) - th);
            maxX = Math.Max(maxX, (t.Centered ? t.At.X + w / 2 : t.At.X + w) + th);
        }
        double minY = new[] { g.Subgrade?.Bottom ?? g.InterfaceLevels[^1] }
            .Concat(g.Labels.Select(l => l.TextAt.Y - l.LineCount * th))
            .Concat(g.Titles.Select(t => t.At.Y - t.Height)).Min() - th;
        double maxY = th * 1.5;

        float pad = 10;
        double fit = Math.Min((Width - 2 * pad) / (maxX - minX), (Height - 2 * pad) / (maxY - minY));
        for (int i = 0; i < 3 && g.TextHeight * fit < 6; i++)
        {
            double grow = 6 / (g.TextHeight * fit);
            double textPart = g.Labels.Count == 0 ? 0 : g.Labels.Max(l => WidthOf(l.Text, th, false)) * (grow - 1);
            fit = Math.Min((Width - 2 * pad) / (maxX - minX + textPart), (Height - 2 * pad) / (maxY - minY));
        }
        double scale = fit * _zoom;
        float offX = (float)((Width - (maxX - minX) * fit) / 2) * _zoom + _pan.X * (_zoom == 1f ? 0 : 1);
        float offY = (float)((Height - (maxY - minY) * fit) / 2) * _zoom + _pan.Y * (_zoom == 1f ? 0 : 1);
        PointF P(double x, double y) => new((float)(offX + (x - minX) * scale), (float)(offY + (maxY - y) * scale));

        float fontPx = Math.Max(6f, (float)(g.TextHeight * scale));
        using var font = new Font(Font.FontFamily, fontPx, GraphicsUnit.Pixel);
        using var outline = new Pen(Color.Black, 1.5f);
        using var thin = new Pen(Color.DimGray, 1f);

        foreach (var band in g.Bands.Concat(g.Subgrade is null ? Array.Empty<Band>() : new[] { g.Subgrade }))
        {
            using var brush = BrushFor(band.Hatch?.Pattern);
            var left = g.Edges.Count > 0 ? g.Edges[0] : new List<Pt> { new(0, 0), new(0, -1) };
            var right = g.Edges.Count > 1 ? g.Edges[1] : new List<Pt> { new(g.Width, 0), new(g.Width, -1) };
            gr.FillPolygon(brush, EdgeProfile.Band(left, right, band.Top, band.Bottom).Select(p => P(p.X, p.Y)).ToArray());
        }
        foreach (var y in g.InterfaceLevels.Concat(g.LiftLevels))
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
            foreach (var anchor in l.ExtraAnchors.Prepend(l.Anchor))
            {
                var a = P(anchor.X, anchor.Y);
                gr.DrawLine(thin, a, P(l.Elbow.X, l.Elbow.Y));
                gr.FillEllipse(Brushes.Black, a.X - 2, a.Y - 2, 4, 4);
            }
            var t = P(l.TextAt.X, l.TextAt.Y);
            var size = gr.MeasureString(l.Text, font);
            gr.DrawString(l.Text, font, Brushes.Black, t.X + 2, t.Y - size.Height / 2);
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
            var size = gr.MeasureString(t.Text, f);
            gr.DrawString(t.Text, f, Brushes.Black, t.Centered ? p.X - size.Width / 2 : p.X, p.Y - size.Height / 2);
        }

        TextRenderer.DrawText(gr, _zoom > 1f ? "Double-click to fit" : "Scroll to zoom, drag to move", Font,
            new Rectangle(0, 0, Width - 6, Height - 4), Color.Gray, TextFormatFlags.Right | TextFormatFlags.Bottom);
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
