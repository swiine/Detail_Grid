using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ETWalls.Core.Drawing;
using TextAnchor = ETWalls.Core.Drawing.Anchor;
using Label = ETWalls.Core.Drawing.Label;

namespace ETWalls.Standalone;

/// <summary>
/// Shows a wall section (the same drawing as the report and the DXF) on white "paper".
/// Scroll to zoom, drag to move, double-click to fit.
/// </summary>
internal sealed class SectionView : Control
{
    private SectionDrawing? _drawing;
    private string _message = "Fill in the wall to see it.";
    private float _zoom = 1;
    private PointF _pan;
    private Point? _dragFrom;

    public SectionView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Cursor = Cursors.Hand;
    }

    /// <summary>The drawing to show (null shows the message instead). Keeps the zoom unless asked to fit.</summary>
    public void SetDrawing(SectionDrawing? drawing, string? message = null, bool fit = false)
    {
        _drawing = drawing;
        if (message is not null) _message = message;
        if (fit) { _zoom = 1; _pan = PointF.Empty; }
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        float old = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f), 0.5f, 12f);
        // Zoom about the cursor.
        var c = new PointF(Width / 2f + _pan.X, Height / 2f + _pan.Y);
        _pan = new PointF(_pan.X + (e.X - c.X) * (1 - _zoom / old), _pan.Y + (e.Y - c.Y) * (1 - _zoom / old));
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); _dragFrom = e.Location; }
    protected override void OnMouseUp(MouseEventArgs e) => _dragFrom = null;
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragFrom is not Point from) return;
        _pan = new PointF(_pan.X + e.X - from.X, _pan.Y + e.Y - from.Y);
        _dragFrom = e.Location;
        Invalidate();
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { _zoom = 1; _pan = PointF.Empty; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        if (_drawing is not { } d)
        {
            using var f = Brand.Body();
            TextRenderer.DrawText(g, _message, f, ClientRectangle, Brand.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var (vx, vy, vw, vh) = d.View;
        float pad = 8;
        float s = (float)Math.Min((Width - 2 * pad) / vw, (Height - 2 * pad) / vh) * _zoom;
        if (s <= 0) return;
        float ox = Width / 2f + _pan.X - (float)(vx + vw / 2) * s, oy = Height / 2f + _pan.Y - (float)(vy + vh / 2) * s;
        var view = new Matrix(s, 0, 0, s, ox, oy);
        g.Transform = view;
        float minPen = 1 / s;   // never thinner than a pixel
        Pen PenFor(double w, Color c) => new(c, Math.Max((float)w, minPen)) { LineJoin = LineJoin.Round };

        foreach (var item in d.Items)
        {
            switch (item)
            {
                case Shape sh:
                {
                    var pts = sh.Points.Select(p => new PointF((float)p.X, (float)p.Y)).ToArray();
                    if (sh.Closed)
                    {
                        using var path = new GraphicsPath();
                        path.AddPolygon(pts);
                        if (sh.Fill is Fill.Concrete or Fill.Gravel or Fill.White) g.FillPath(Brushes.White, path);
                        if (sh.Fill == Fill.Ink) { using var ink = new SolidBrush(Ink); g.FillPath(ink, path); }
                        if (Patterns.For(sh.Fill, d.Scale) is { } tile) Hatch(g, path, tile, minPen);
                        if (sh.Stroke > 0) { using var pen = PenFor(sh.Stroke, Ink); if (sh.Dash == Dash.Dashed) pen.DashPattern = new[] { 4f, 2f }; else if (sh.Dash == Dash.DashDot) pen.DashPattern = new[] { 6f, 2f, 1.5f, 2f }; g.DrawPath(pen, path); }
                    }
                    else if (pts.Length > 1)
                    {
                        using var pen = PenFor(sh.Stroke, Ink);
                        if (sh.Dash == Dash.Dashed) pen.DashPattern = new[] { 4f, 2f };
                        g.DrawLines(pen, pts);
                    }
                    break;
                }
                case Circle c:
                {
                    var r = new RectangleF((float)(c.Centre.X - c.Radius), (float)(c.Centre.Y - c.Radius), (float)(2 * c.Radius), (float)(2 * c.Radius));
                    if (c.Fill == Fill.Ink) { using var ink = new SolidBrush(Ink); g.FillEllipse(ink, r); }
                    else g.FillEllipse(Brushes.White, r);
                    if (c.Stroke > 0) { using var pen = PenFor(c.Stroke, Ink); g.DrawEllipse(pen, r); }
                    break;
                }
                case Label t:
                    DrawLabel(g, t);
                    break;
            }
        }
        g.ResetTransform();
    }

    private static readonly Color Ink = Color.FromArgb(17, 17, 17), HatchInk = Color.FromArgb(85, 85, 85);
    private static readonly FontFamily Arial = new("Arial");

    private static void DrawLabel(Graphics g, Label t)
    {
        float em = (float)(t.Height / 0.716);   // cap height -> em size
        using var font = new Font(Arial, em, t.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.World);
        var fmt = StringFormat.GenericTypographic;
        float width = g.MeasureString(t.Text, font, PointF.Empty, fmt).Width;
        float ascent = em * Arial.GetCellAscent(font.Style) / Arial.GetEmHeight(font.Style);
        float x = t.Anchor switch { TextAnchor.Middle => -width / 2, TextAnchor.End => -width, _ => 0 };
        var state = g.Save();
        g.TranslateTransform((float)t.At.X, (float)t.At.Y);
        if (t.Rotation != 0) g.RotateTransform((float)-t.Rotation);
        using var brush = new SolidBrush(Ink);
        g.DrawString(t.Text, font, brush, x, -ascent, fmt);
        g.Restore(state);
    }

    /// <summary>Draws a pattern tile repeatedly inside a shape (tile space: Unit mm per unit, rotated by Angle).</summary>
    private static void Hatch(Graphics g, GraphicsPath shape, PatternTile tile, float minPen)
    {
        var state = g.Save();
        g.SetClip(shape, CombineMode.Intersect);
        using var m = new Matrix();
        m.Rotate((float)-tile.Angle);
        m.Scale((float)tile.Unit, (float)tile.Unit);
        using var inv = m.Clone();
        inv.Invert();
        var b = shape.GetBounds();
        var corners = new[] { new PointF(b.Left, b.Top), new PointF(b.Right, b.Top), new PointF(b.Right, b.Bottom), new PointF(b.Left, b.Bottom) };
        inv.TransformPoints(corners);
        double size = tile.Size;
        int i0 = (int)Math.Floor(corners.Min(p => p.X) / size), i1 = (int)Math.Ceiling(corners.Max(p => p.X) / size);
        int j0 = (int)Math.Floor(corners.Min(p => p.Y) / size), j1 = (int)Math.Ceiling(corners.Max(p => p.Y) / size);
        if ((long)(i1 - i0) * (j1 - j0) > 40000) { g.Restore(state); return; }   // far too fine to see: skip
        using var pen = new Pen(HatchInk, Math.Max((float)tile.Pen, minPen));
        using var dot = new SolidBrush(HatchInk);
        PointF T(double x, double y) { var p = new[] { new PointF((float)x, (float)y) }; m.TransformPoints(p); return p[0]; }
        for (int i = i0; i < i1; i++)
            for (int j = j0; j < j1; j++)
            {
                double ox = i * size, oy = j * size;
                foreach (var (a, c) in tile.Lines) g.DrawLine(pen, T(ox + a.X, oy + a.Y), T(ox + c.X, oy + c.Y));
                foreach (var (c, r) in tile.Dots) { var p = T(ox + c.X, oy + c.Y); float rr = (float)(r * tile.Unit); g.FillEllipse(dot, p.X - rr, p.Y - rr, 2 * rr, 2 * rr); }
                foreach (var poly in tile.Outlines) g.DrawPolygon(pen, poly.Select(q => T(ox + q.X, oy + q.Y)).ToArray());
            }
        g.Restore(state);
    }
}
