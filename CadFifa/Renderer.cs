using System;
using System.Collections.Generic;
using System.Numerics;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CadFifa;

/// <summary>
/// Draws the players, ball and HUD as transient graphics: they animate on screen
/// without ever touching the drawing database (no undo spam, no regen).
/// </summary>
internal sealed class Renderer : IDisposable
{
    const double S = Look.S;
    const double BallR = Look.BallR;

    // Draw order within the transient layer: higher sub-modes paint on top.
    const int ZShadow = 100, ZLegs = 104, ZArms = 106, ZBody = 108,
              ZHair = 112, ZHead = 113, ZBall = 120, ZBallPanel = 121, ZCursor = 125, ZHud = 130;

    static readonly Color Shadow = Rgb(Look.Shadow);
    static readonly short[] CursorColour = { 2, 4 }; // P1 yellow, P2 cyan

    readonly Point3d _origin;
    readonly IntegerCollection _viewports = new();
    readonly List<(Entity e, int z)> _all = new();
    string _scoreText = "", _bannerText = "";
    readonly List<Figure> _figures = new();
    readonly List<Cursor> _cursors = new();
    readonly Polyline _ballShadow, _ball;
    readonly Polyline[] _ballPanels = new Polyline[3];
    readonly MText _scoreboard, _banner;
    bool _disposed;
    TransientDrawingMode _mode;

    /// <summary>One footballer, built from simple filled shapes seen from above.</summary>
    sealed class Figure
    {
        public Polyline Shadow = null!, LegL = null!, LegR = null!, ArmL = null!, ArmR = null!,
                        Torso = null!, Shoulders = null!, Hair = null!, Head = null!;
    }

    /// <summary>The ring, arrow, tag and power bar that mark a human-controlled player.</summary>
    sealed class Cursor
    {
        public Circle Ring = null!;
        public Solid Arrow = null!;
        public MText Tag = null!;
        public Polyline PowerBack = null!, Power = null!;
    }

    /// <param name="mode">
    /// How AutoCAD paints the transients. <see cref="TransientDrawingMode.Main"/> draws them as part of
    /// the normal, double-buffered scene, so a frame appears all at once. The Direct modes paint straight
    /// onto the screen piece by piece, which flickers badly when hundreds of shapes move every frame.
    /// </param>
    public Renderer(Database db, Point3d origin, Match match, TransientDrawingMode mode)
    {
        _mode = mode;
        _origin = origin;

        foreach (var p in match.Players)
        {
            var f = new Figure
            {
                Shadow = Add(Disk(Look.ShadowR, Shadow), ZShadow),
                LegL = Add(Stroke(Look.LegW, Rgb(Look.Socks(p))), ZLegs),
                LegR = Add(Stroke(Look.LegW, Rgb(Look.Socks(p))), ZLegs),
                ArmL = Add(Stroke(Look.ArmW, Rgb(Look.Arms(p))), ZArms),
                ArmR = Add(Stroke(Look.ArmW, Rgb(Look.Arms(p))), ZArms),
                Torso = Add(Disk(Look.TorsoR, Rgb(Look.Shirt(p))), ZBody),
                Shoulders = Add(Stroke(Look.ShoulderW, Rgb(Look.Shirt(p))), ZBody),
                Hair = Add(Disk(Look.HairR, Rgb(Look.Hair(p))), ZHair),
                Head = Add(Disk(Look.HeadR, Rgb(Look.Skin(p))), ZHead),
            };
            _figures.Add(f);
        }

        _ballShadow = Add(Disk(BallR, Shadow), ZShadow);
        _ball = Add(Disk(BallR, Rgb(Look.BallWhite)), ZBall);
        for (int i = 0; i < _ballPanels.Length; i++)
            _ballPanels[i] = Add(Disk(0.15, Rgb(Look.BallPanel)), ZBallPanel);

        foreach (var c in match.Controllers)
        {
            short colour = CursorColour[c.Index];
            _cursors.Add(new Cursor
            {
                Ring = Add(new Circle { Radius = 1.25 * S, ColorIndex = colour }, ZCursor),
                Arrow = Add(new Solid(Point3d.Origin, Point3d.Origin, Point3d.Origin) { ColorIndex = colour }, ZCursor),
                Tag = Add(Text(db, $"P{c.Index + 1}", 1.0 * S, Color.FromColorIndex(ColorMethod.ByAci, colour)), ZCursor),
                PowerBack = Add(Stroke(0.35, Rgb(60, 60, 60)), ZCursor),
                Power = Add(Stroke(0.35, Rgb(255, 120, 0)), ZCursor + 1),
            });
        }

        // Scoreboard above the pitch, banner (GOAL!, CORNER...) just above the centre circle.
        _scoreboard = Add(Text(db, "", 3.5, Rgb(255, 255, 255)), ZHud);
        _scoreboard.Location = At(0, Match.HalfWidth + 6);
        _banner = Add(Text(db, "", 7.0, Color.FromColorIndex(ColorMethod.ByAci, 2)), ZHud);
        _banner.Location = At(0, 14);

        Update(match);
        var tm = TransientManager.CurrentTransientManager;
        foreach (var (e, z) in _all)
            tm.AddTransient(e, _mode, z, _viewports);
    }

    /// <summary>Re-registers every shape under a different drawing mode.</summary>
    public void SetMode(TransientDrawingMode mode)
    {
        if (_disposed || mode == _mode) return;
        var tm = TransientManager.CurrentTransientManager;
        foreach (var (e, _) in _all)
            tm.EraseTransient(e, _viewports);
        _mode = mode;
        foreach (var (e, z) in _all)
            tm.AddTransient(e, _mode, z, _viewports);
    }

    public void Draw(Match match)
    {
        if (_disposed) return;
        Update(match);
        var tm = TransientManager.CurrentTransientManager;
        foreach (var (e, _) in _all)
            tm.UpdateTransient(e, _viewports);
    }

    void Update(Match m)
    {
        for (int i = 0; i < m.Players.Count; i++)
            Pose(_figures[i], m.Players[i]);

        // Ball, with three dark panels orbiting its centre as it rolls.
        var b = m.Ball.Pos;
        MoveDisk(_ballShadow, b.X + 0.25, b.Y - 0.25, BallR);
        MoveDisk(_ball, b.X, b.Y, BallR);
        var panels = Look.BallPanels(m.Ball);
        for (int i = 0; i < _ballPanels.Length; i++)
        {
            _ballPanels[i].Visible = panels[i].Visible;
            MoveDisk(_ballPanels[i], panels[i].Pos.X, panels[i].Pos.Y, 0.15);
        }

        foreach (var c in m.Controllers)
        {
            var cur = _cursors[c.Index];
            var p = c.Player;
            cur.Ring.Center = At(p.Pos.X, p.Pos.Y);
            var tip = p.Pos + p.Facing * (float)(2.3 * S);
            var baseC = p.Pos + p.Facing * (float)(1.55 * S);
            var perp = new Vector2(-p.Facing.Y, p.Facing.X) * (float)(0.45 * S);
            cur.Arrow.SetPointAt(0, At(tip.X, tip.Y));
            cur.Arrow.SetPointAt(1, At(baseC.X + perp.X, baseC.Y + perp.Y));
            cur.Arrow.SetPointAt(2, At(baseC.X - perp.X, baseC.Y - perp.Y));
            cur.Arrow.SetPointAt(3, At(baseC.X - perp.X, baseC.Y - perp.Y));
            cur.Tag.Location = At(p.Pos.X, p.Pos.Y + 2.4 * S);

            double barY = p.Pos.Y - 2.0 * S, x0 = p.Pos.X - 1.6 * S, full = 3.2 * S;
            SetStroke(cur.PowerBack, x0, barY, x0 + full, barY);
            SetStroke(cur.Power, x0, barY, x0 + Math.Max(0.01, c.Charge * full), barY);
            cur.PowerBack.Visible = cur.Power.Visible = c.Charge > 0f;
        }

        string home = m.Mode == GameMode.Versus ? "P1" : "HOME";
        string away = m.Mode == GameMode.Versus ? "P2" : "AWAY";
        // Only re-set text when it changes: MText re-lays itself out on every assignment.
        string score = $"{home}  {m.Score[0]} - {m.Score[1]}  {away}      {m.MatchMinute}'";
        if (score != _scoreText) _scoreboard.Contents = _scoreText = score;
        string banner = m.MessageTimer > 0f ? m.Message : "";
        if (banner != _bannerText) _banner.Contents = _bannerText = banner;
        _banner.Visible = banner.Length > 0;
    }

    void Pose(Figure f, Footballer p)
    {
        var b = Look.Pose(p);
        MoveDisk(f.Shadow, b.Shadow.X, b.Shadow.Y, Look.ShadowR);
        SetStroke(f.LegL, b.LegL0, b.LegL1);
        SetStroke(f.LegR, b.LegR0, b.LegR1);
        f.LegL.Visible = f.LegR.Visible = b.ShowLegs;
        SetStroke(f.ArmL, b.ArmL0, b.ArmL1);
        SetStroke(f.ArmR, b.ArmR0, b.ArmR1);
        MoveDisk(f.Torso, b.Torso.X, b.Torso.Y, Look.TorsoR);
        SetStroke(f.Shoulders, b.ShoulderL, b.ShoulderR);
        MoveDisk(f.Hair, b.Hair.X, b.Hair.Y, Look.HairR);
        MoveDisk(f.Head, b.Head.X, b.Head.Y, Look.HeadR);
    }

    Point3d At(double x, double y) => new(_origin.X + x, _origin.Y + y, _origin.Z);

    T Add<T>(T e, int z) where T : Entity
    {
        _all.Add((e, z));
        return e;
    }

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    static Color Rgb((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>A filled circle, drawn the same way as the DONUT command: a two-arc polyline with width = radius.</summary>
    static Polyline Disk(double r, Color colour)
    {
        var pl = new Polyline(2);
        pl.AddVertexAt(0, new Point2d(-r / 2, 0), 1, r, r);
        pl.AddVertexAt(1, new Point2d(r / 2, 0), 1, r, r);
        pl.Closed = true;
        pl.Color = colour;
        return pl;
    }

    /// <summary>A thick straight stroke (a wide two-vertex polyline).</summary>
    static Polyline Stroke(double width, Color colour)
    {
        var pl = new Polyline(2);
        pl.AddVertexAt(0, Point2d.Origin, 0, width, width);
        pl.AddVertexAt(1, new Point2d(0.01, 0), 0, width, width);
        pl.Color = colour;
        return pl;
    }

    static MText Text(Database db, string contents, double height, Color colour)
    {
        var t = new MText();
        t.SetDatabaseDefaults(db);
        t.Attachment = AttachmentPoint.MiddleCenter;
        t.TextHeight = height;
        t.Contents = contents;
        t.Color = colour;
        return t;
    }

    void MoveDisk(Polyline pl, double x, double y, double r)
    {
        pl.SetPointAt(0, new Point2d(_origin.X + x - r / 2, _origin.Y + y));
        pl.SetPointAt(1, new Point2d(_origin.X + x + r / 2, _origin.Y + y));
        pl.Elevation = _origin.Z;
    }

    void SetStroke(Polyline pl, Vector2 a, Vector2 b) => SetStroke(pl, a.X, a.Y, b.X, b.Y);

    void SetStroke(Polyline pl, double x1, double y1, double x2, double y2)
    {
        pl.SetPointAt(0, new Point2d(_origin.X + x1, _origin.Y + y1));
        pl.SetPointAt(1, new Point2d(_origin.X + x2, _origin.Y + y2));
        pl.Elevation = _origin.Z;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var tm = TransientManager.CurrentTransientManager;
        foreach (var (e, _) in _all)
        {
            tm.EraseTransient(e, _viewports);
            e.Dispose();
        }
    }
}
