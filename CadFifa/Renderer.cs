using System;
using System.Collections.Generic;
using System.Linq;
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
/// Everything is painted by one <see cref="Scene"/> drawable, so each frame is a single
/// transient update rather than hundreds of separate ones (which made the figures flicker).
/// </summary>
internal sealed class Renderer : IDisposable
{
    /// <summary>Figures are drawn a bit larger than their physics size so they read at a distance.</summary>
    const double S = 1.7;
    const double BallR = 0.65;

    // Paint order inside the scene: higher values are drawn on top.
    const int ZShadow = 100, ZLegs = 104, ZArms = 106, ZBody = 108,
              ZHair = 112, ZHead = 113, ZBall = 120, ZBallPanel = 121, ZCursor = 125, ZHud = 130;

    static readonly Color Shadow = Rgb(28, 82, 34);
    static readonly Color[] Skin = { Rgb(255, 219, 172), Rgb(241, 194, 125), Rgb(224, 172, 105), Rgb(198, 134, 66), Rgb(141, 85, 36), Rgb(96, 60, 32) };
    static readonly Color[] Hair = { Rgb(30, 20, 15), Rgb(85, 55, 30), Rgb(215, 175, 90), Rgb(160, 70, 30), Rgb(12, 12, 12), Rgb(120, 90, 60) };

    sealed class Kit
    {
        public readonly Color Shirt, Socks;
        public Kit(Color shirt, Color socks) { Shirt = shirt; Socks = socks; }
    }
    static readonly Kit HomeKit = new(Rgb(210, 35, 45), Rgb(245, 245, 245));
    static readonly Kit AwayKit = new(Rgb(35, 90, 210), Rgb(20, 30, 85));
    static readonly Kit HomeKeeperKit = new(Rgb(255, 165, 25), Rgb(255, 165, 25));
    static readonly Kit AwayKeeperKit = new(Rgb(150, 60, 190), Rgb(150, 60, 190));
    static readonly short[] CursorColour = { 2, 4 }; // P1 yellow, P2 cyan

    readonly Point3d _origin;
    readonly IntegerCollection _viewports = new();
    readonly List<(Entity e, int z)> _all = new();
    readonly Scene _scene = new();
    string _scoreText = "", _bannerText = "";
    readonly List<Figure> _figures = new();
    readonly List<Cursor> _cursors = new();
    readonly Polyline _ballShadow, _ball;
    readonly Polyline[] _ballPanels = new Polyline[3];
    readonly MText _scoreboard, _banner;
    bool _disposed;

    /// <summary>
    /// A single transient that paints a list of entities in order. One update per frame
    /// redraws the whole scene at once, instead of every body part refreshing on its own.
    /// </summary>
    sealed class Scene : Drawable
    {
        public Entity[] Items = Array.Empty<Entity>();

        public Scene() : base(IntPtr.Zero, false) { }

        public override bool IsPersistent => false;
        public override ObjectId Id => ObjectId.Null;
        protected override int SubSetAttributes(DrawableTraits traits) => 0;

        protected override bool SubWorldDraw(WorldDraw wd)
        {
            foreach (var e in Items)
                if (e.Visible) wd.Geometry.Draw(e);
            return true;
        }

        protected override void SubViewportDraw(ViewportDraw vd) { }
        protected override int SubViewportDrawLogicalFlags(ViewportDraw vd) => 0;
    }

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

    public Renderer(Database db, Point3d origin, Match match)
    {
        _origin = origin;

        foreach (var p in match.Players)
        {
            var kit = p.Side == Side.Home ? (p.IsKeeper ? HomeKeeperKit : HomeKit)
                                          : (p.IsKeeper ? AwayKeeperKit : AwayKit);
            int look = p.Number * 7 + (int)p.Side * 3;
            var skin = Skin[look % Skin.Length];
            var f = new Figure
            {
                Shadow = Add(Disk(0.75 * S, Shadow), ZShadow),
                LegL = Add(Stroke(0.3 * S, kit.Socks), ZLegs),
                LegR = Add(Stroke(0.3 * S, kit.Socks), ZLegs),
                // Keepers wear long sleeves; outfield players show bare arms.
                ArmL = Add(Stroke(0.24 * S, p.IsKeeper ? kit.Shirt : skin), ZArms),
                ArmR = Add(Stroke(0.24 * S, p.IsKeeper ? kit.Shirt : skin), ZArms),
                Torso = Add(Disk(0.55 * S, kit.Shirt), ZBody),
                Shoulders = Add(Stroke(0.6 * S, kit.Shirt), ZBody),
                Hair = Add(Disk(0.27 * S, Hair[(look / 2) % Hair.Length]), ZHair),
                Head = Add(Disk(0.22 * S, skin), ZHead),
            };
            _figures.Add(f);
        }

        _ballShadow = Add(Disk(BallR, Shadow), ZShadow);
        _ball = Add(Disk(BallR, Rgb(250, 250, 250)), ZBall);
        for (int i = 0; i < _ballPanels.Length; i++)
            _ballPanels[i] = Add(Disk(0.15, Rgb(25, 25, 25)), ZBallPanel);

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

        // Stable sort keeps creation order within a layer.
        _scene.Items = _all.OrderBy(x => x.z).Select(x => x.e).ToArray();
        Update(match);
        TransientManager.CurrentTransientManager.AddTransient(
            _scene, TransientDrawingMode.DirectTopmost, 128, _viewports);
    }

    public void Draw(Match match)
    {
        if (_disposed) return;
        Update(match);
        TransientManager.CurrentTransientManager.UpdateTransient(_scene, _viewports);
    }

    void Update(Match m)
    {
        for (int i = 0; i < m.Players.Count; i++)
            Pose(_figures[i], m.Players[i]);

        // Ball, with three dark panels orbiting its centre as it rolls.
        var b = m.Ball.Pos;
        MoveDisk(_ballShadow, b.X + 0.25, b.Y - 0.25, BallR);
        MoveDisk(_ball, b.X, b.Y, BallR);
        var roll = m.Ball.Vel.LengthSquared() > 0.01f ? Vector2.Normalize(m.Ball.Vel) : Vector2.UnitX;
        for (int i = 0; i < _ballPanels.Length; i++)
        {
            // Project panels on a sphere rolling along its velocity.
            double a = m.Ball.Spin + i * 2.0 * Math.PI / 3.0;
            var side = new Vector2(-roll.Y, roll.X);
            double along = Math.Sin(a) * 0.35, across = (i - 1) * 0.26;
            _ballPanels[i].Visible = Math.Cos(a) > -0.3; // hide panels on the far side
            MoveDisk(_ballPanels[i], b.X + roll.X * along + side.X * across, b.Y + roll.Y * along + side.Y * across, 0.15);
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
        var fwd = p.Facing;
        var side = new Vector2(-fwd.Y, fwd.X);
        var c = p.Pos;
        // Stride grows with speed; legs and arms swing in opposite phase like a real run.
        double stride = Math.Min(1.0, p.Vel.Length() / 7.0);
        double swing = Math.Sin(p.RunPhase) * stride;
        bool sliding = p.SlideTimer > 0f;
        bool down = p.StunTimer > 0f && !sliding;

        MoveDisk(f.Shadow, c.X + 0.2 * S, c.Y - 0.2 * S, 0.75 * S);

        // Legs: from the hips out to the feet. A slide goes in feet first; a player
        // who has been tackled (or missed a slide) lies flat with legs trailing.
        Limb(f.LegL, c, side, fwd, 0.22, sliding ? 1.2 : down ? -1.1 : 0.75 * swing);
        Limb(f.LegR, c, side, fwd, -0.22, sliding ? 0.9 : down ? -1.1 : -0.75 * swing);
        f.LegL.Visible = f.LegR.Visible = sliding || down || stride > 0.05;

        // Arms: from the shoulders, swinging opposite to the legs; thrown back for balance in a slide.
        Limb(f.ArmL, c, side, fwd, 0.68, sliding ? -0.8 : down ? 0.9 : -0.5 * swing, spread: sliding ? 0.4 : 0.12);
        Limb(f.ArmR, c, side, fwd, -0.68, sliding ? -0.8 : down ? 0.9 : 0.5 * swing, spread: sliding ? 0.4 : 0.12);

        MoveDisk(f.Torso, c.X, c.Y, 0.55 * S);
        var sl = c + side * (float)(0.75 * S);
        var sr = c - side * (float)(0.75 * S);
        SetStroke(f.Shoulders, sl.X, sl.Y, sr.X, sr.Y);

        var hair = c - fwd * (float)(0.06 * S);
        var head = c + fwd * (float)(0.06 * S);
        MoveDisk(f.Hair, hair.X, hair.Y, 0.27 * S);
        MoveDisk(f.Head, head.X, head.Y, 0.22 * S);
    }

    /// <summary>A limb rooted <paramref name="offset"/> to the side, reaching <paramref name="reach"/> forward or back.</summary>
    void Limb(Polyline pl, Vector2 c, Vector2 side, Vector2 fwd, double offset, double reach, double spread = 0)
    {
        var root = c + side * (float)(offset * S);
        var end = root + fwd * (float)(reach * S) + side * (float)(Math.Sign(offset) * spread * S);
        if (Math.Abs(reach) < 0.05) end = root + fwd * (float)(0.05 * S);
        SetStroke(pl, root.X, root.Y, end.X, end.Y);
    }

    Point3d At(double x, double y) => new(_origin.X + x, _origin.Y + y, _origin.Z);

    T Add<T>(T e, int z) where T : Entity
    {
        _all.Add((e, z));
        return e;
    }

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

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
        TransientManager.CurrentTransientManager.EraseTransient(_scene, _viewports);
        foreach (var (e, _) in _all)
            e.Dispose();
    }
}
