using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace CadFifa;

/// <summary>
/// Draws the whole match (pitch, players, ball, HUD) inside the game window.
/// The control is double-buffered: each frame is painted off-screen and shown in one go,
/// which is what keeps it flicker-free. AutoCAD's own display can't promise that for
/// hundreds of shapes moving every frame.
/// </summary>
internal sealed class PitchView : Control
{
    const float L = Match.HalfLength, W = Match.HalfWidth;
    const float Border = 5f;       // grass shown around the lines, metres
    const float HudHeight = 44f;   // pixels reserved above the pitch for the scoreboard

    static readonly Color[] CursorColour = { Color.Yellow, Color.Cyan };

    readonly Dictionary<int, SolidBrush> _brushes = new();
    readonly Font _scoreFont = new("Consolas", 18f, FontStyle.Bold);
    readonly Font _bannerFont = new("Segoe UI", 34f, FontStyle.Bold);
    readonly Font _tagFont = new("Segoe UI", 10f, FontStyle.Bold);
    Match? _match;

    // World-to-screen mapping for the current size.
    float _k, _cx, _cy;

    public PitchView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        // Never take keyboard focus, so the game window keeps getting every key press.
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.FromArgb(20, 52, 26);
    }

    public void Show(Match match)
    {
        _match = match;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_match == null) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Fit the pitch (plus a grass border) below the scoreboard, keeping it to scale.
        float worldW = 2 * (L + Border), worldH = 2 * (W + Border);
        _k = Math.Min(Width / worldW, (Height - HudHeight) / worldH);
        _cx = Width / 2f;
        _cy = HudHeight + (Height - HudHeight) / 2f;

        var state = g.Save();
        g.TranslateTransform(_cx, _cy);
        g.ScaleTransform(_k, -_k); // y up, metres, same as the drawing
        DrawPitch(g);
        DrawShadows(g, _match);
        foreach (var p in _match.Players) DrawPlayer(g, p);
        DrawBall(g, _match.Ball);
        foreach (var c in _match.Controllers) DrawCursor(g, c);
        g.Restore(state);

        // Text is drawn in screen space so it isn't flipped by the y-up transform.
        foreach (var c in _match.Controllers)
        {
            var at = ToScreen(c.Player.Pos + new Vector2(0f, (float)(2.4 * Look.S)));
            DrawCentred(g, $"P{c.Index + 1}", _tagFont, CursorColour[c.Index], at);
        }
        string home = _match.Mode == GameMode.Versus ? "P1" : "HOME";
        string away = _match.Mode == GameMode.Versus ? "P2" : "AWAY";
        DrawCentred(g, $"{home}  {_match.Score[0]} - {_match.Score[1]}  {away}     {_match.MatchMinute}'",
            _scoreFont, Color.White, new PointF(Width / 2f, HudHeight / 2f));
        if (_match.MessageTimer > 0f && _match.Message.Length > 0)
            DrawCentred(g, _match.Message, _bannerFont, Color.Gold, ToScreen(new Vector2(0f, 14f)), shadow: true);
    }

    // ---------------------------------------------------------------- pitch

    void DrawPitch(Graphics g)
    {
        // Mown stripes.
        const int stripes = 14;
        float x0 = -L - Border, stripe = 2 * (L + Border) / stripes;
        for (int i = 0; i < stripes; i++)
        {
            var colour = i % 2 == 0 ? Color.FromArgb(46, 125, 50) : Color.FromArgb(67, 150, 71);
            g.FillRectangle(Brush(colour), x0 + i * stripe, -W - Border, stripe + 0.05f, 2 * (W + Border));
        }

        using var line = new Pen(Color.White, 0.18f);
        g.DrawRectangle(line, -L, -W, 2 * L, 2 * W);
        g.DrawLine(line, 0, -W, 0, W);
        Circle(g, line, Vector2.Zero, 9.15f);
        Spot(g, Vector2.Zero);

        const float arcHalf = 0.927295218f; // acos(5.5 / 9.15): the D outside the box
        foreach (int s in new[] { -1, 1 })
        {
            float gl = s * L;
            Rect(g, line, gl, -Match.PenaltyHalfWidth, gl - s * Match.PenaltyDepth, Match.PenaltyHalfWidth);
            Rect(g, line, gl, -9.16f, gl - s * 5.5f, 9.16f);
            var spot = new Vector2(gl - s * 11f, 0f);
            Spot(g, spot);
            float a0 = s < 0 ? -arcHalf : (float)Math.PI - arcHalf;
            Arc(g, line, spot, 9.15f, a0, a0 + 2 * arcHalf);

            // Goal and net behind the line.
            using var net = new Pen(Color.FromArgb(200, 255, 255, 255), 0.06f);
            for (float y = -Match.GoalHalfWidth + 0.61f; y < Match.GoalHalfWidth; y += 0.61f)
                g.DrawLine(net, gl, y, gl + s * 2f, y);
            Rect(g, line, gl, -Match.GoalHalfWidth, gl + s * 2f, Match.GoalHalfWidth);
        }

        // Corner arcs.
        Arc(g, line, new Vector2(-L, -W), 1f, 0f, (float)Math.PI / 2);
        Arc(g, line, new Vector2(L, -W), 1f, (float)Math.PI / 2, (float)Math.PI);
        Arc(g, line, new Vector2(L, W), 1f, (float)Math.PI, 1.5f * (float)Math.PI);
        Arc(g, line, new Vector2(-L, W), 1f, 1.5f * (float)Math.PI, 2f * (float)Math.PI);
    }

    static void Rect(Graphics g, Pen pen, float x1, float y1, float x2, float y2) =>
        g.DrawRectangle(pen, Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    static void Circle(Graphics g, Pen pen, Vector2 c, float r) => g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);

    void Spot(Graphics g, Vector2 c) => Disk(g, Color.White, c, 0.3f);

    /// <summary>Counter-clockwise arc in radians, built from points so the y-flip can't reverse it.</summary>
    static void Arc(Graphics g, Pen pen, Vector2 c, float r, float from, float to)
    {
        const int n = 24;
        var pts = new PointF[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = from + (to - from) * i / n;
            pts[i] = new PointF(c.X + r * (float)Math.Cos(a), c.Y + r * (float)Math.Sin(a));
        }
        g.DrawLines(pen, pts);
    }

    // ---------------------------------------------------------------- players and ball

    void DrawShadows(Graphics g, Match m)
    {
        // All shadows first, so no player's shadow falls on top of a neighbour.
        var shadow = Rgb(Look.Shadow);
        foreach (var p in m.Players) Disk(g, shadow, Look.Pose(p).Shadow, (float)Look.ShadowR);
        Disk(g, shadow, m.Ball.Pos + new Vector2(0.25f, -0.25f), (float)Look.BallR);
    }

    void DrawPlayer(Graphics g, Footballer p)
    {
        var b = Look.Pose(p);
        if (b.ShowLegs)
        {
            Stroke(g, Rgb(Look.Socks(p)), (float)Look.LegW, b.LegL0, b.LegL1);
            Stroke(g, Rgb(Look.Socks(p)), (float)Look.LegW, b.LegR0, b.LegR1);
        }
        Stroke(g, Rgb(Look.Arms(p)), (float)Look.ArmW, b.ArmL0, b.ArmL1);
        Stroke(g, Rgb(Look.Arms(p)), (float)Look.ArmW, b.ArmR0, b.ArmR1);
        var shirt = Rgb(Look.Shirt(p));
        Disk(g, shirt, b.Torso, (float)Look.TorsoR);
        Stroke(g, shirt, (float)Look.ShoulderW, b.ShoulderL, b.ShoulderR);
        Disk(g, Rgb(Look.Hair(p)), b.Hair, (float)Look.HairR);
        Disk(g, Rgb(Look.Skin(p)), b.Head, (float)Look.HeadR);
    }

    void DrawBall(Graphics g, Ball ball)
    {
        Disk(g, Rgb(Look.BallWhite), ball.Pos, (float)Look.BallR);
        foreach (var (pos, visible) in Look.BallPanels(ball))
            if (visible) Disk(g, Rgb(Look.BallPanel), pos, 0.15f);
    }

    void DrawCursor(Graphics g, Controller c)
    {
        var p = c.Player;
        var colour = CursorColour[c.Index];
        using (var ring = new Pen(colour, 0.2f))
            Circle(g, ring, p.Pos, (float)(1.25 * Look.S));

        var tip = p.Pos + p.Facing * (float)(2.3 * Look.S);
        var baseC = p.Pos + p.Facing * (float)(1.55 * Look.S);
        var perp = new Vector2(-p.Facing.Y, p.Facing.X) * (float)(0.45 * Look.S);
        g.FillPolygon(Brush(colour), new[]
        {
            new PointF(tip.X, tip.Y),
            new PointF(baseC.X + perp.X, baseC.Y + perp.Y),
            new PointF(baseC.X - perp.X, baseC.Y - perp.Y),
        });

        if (c.Charge > 0f)
        {
            // Shot power meter under the player.
            float y = p.Pos.Y - (float)(2.0 * Look.S), x0 = p.Pos.X - (float)(1.6 * Look.S), full = (float)(3.2 * Look.S);
            Stroke(g, Color.FromArgb(60, 60, 60), 0.4f, new Vector2(x0, y), new Vector2(x0 + full, y));
            Stroke(g, Color.FromArgb(255, 120, 0), 0.4f, new Vector2(x0, y), new Vector2(x0 + c.Charge * full, y));
        }
    }

    // ---------------------------------------------------------------- helpers

    void Disk(Graphics g, Color colour, Vector2 c, float r) =>
        g.FillEllipse(Brush(colour), c.X - r, c.Y - r, 2 * r, 2 * r);

    static void Stroke(Graphics g, Color colour, float width, Vector2 a, Vector2 b)
    {
        using var pen = new Pen(colour, width);
        g.DrawLine(pen, a.X, a.Y, b.X, b.Y);
    }

    SolidBrush Brush(Color colour)
    {
        int key = colour.ToArgb();
        if (!_brushes.TryGetValue(key, out var brush))
            _brushes[key] = brush = new SolidBrush(colour);
        return brush;
    }

    static Color Rgb((byte R, byte G, byte B) c) => Color.FromArgb(c.R, c.G, c.B);

    PointF ToScreen(Vector2 w) => new(_cx + w.X * _k, _cy - w.Y * _k);

    static void DrawCentred(Graphics g, string text, Font font, Color colour, PointF at, bool shadow = false)
    {
        var size = g.MeasureString(text, font);
        float x = at.X - size.Width / 2f, y = at.Y - size.Height / 2f;
        if (shadow)
        {
            using var dark = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            g.DrawString(text, font, dark, x + 2, y + 2);
        }
        using var brush = new SolidBrush(colour);
        g.DrawString(text, font, brush, x, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var b in _brushes.Values) b.Dispose();
            _scoreFont.Dispose();
            _bannerFont.Dispose();
            _tagFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
