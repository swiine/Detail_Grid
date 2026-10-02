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
/// The Drawing display: animates the match in model space with transient graphics, so the
/// game never writes to the drawing while you play (no undo spam, no regen).
///
/// To keep it smooth, every animation frame of every player is built once, up front, as an
/// anonymous block. During play each player is a single transient block reference that only
/// moves, turns and swaps which frame it points at. That is ~30 transient updates a frame
/// instead of ~230 separate shapes, and AutoCAD caches the block graphics, which is what
/// stops the flicker. The blocks are erased again when the game closes (see <see cref="Blocks"/>).
/// </summary>
internal sealed class Renderer : IDisposable
{
    const double S = Look.S;
    const double BallR = Look.BallR;

    // Animation frames per player: standing, a running cycle, tackled on the floor, sliding.
    const int RunFrames = 8;
    const int PoseStand = 0, PoseDown = RunFrames + 1, PoseSlide = RunFrames + 2, PoseCount = RunFrames + 3;
    const int BallFrames = 6;
    // A player switches to the running cycle above Start and back to standing below Stop (m/s).
    // Two thresholds stop a player hovering at one speed from flipping between them every frame.
    const float RunStart = 1.4f, RunStop = 0.8f;

    // Draw order within the transient layer: higher sub-modes paint on top.
    const int ZShadow = 100, ZPlayer = 110, ZBall = 120, ZCursor = 125, ZHud = 130;

    static readonly short[] CursorColour = { 2, 4 }; // P1 yellow, P2 cyan

    readonly Point3d _origin;
    readonly IntegerCollection _viewports = new();
    readonly List<(Entity e, int z)> _all = new();
    readonly List<ObjectId> _blocks = new();
    readonly ObjectId[][] _poses;          // [player][pose] block definitions
    readonly BlockReference[] _players;
    readonly bool[] _running;
    readonly ObjectId[] _ballFrames = new ObjectId[BallFrames];
    readonly BlockReference _ball;
    readonly Polyline _ballShadow;
    double _ballAngle;
    readonly List<Cursor> _cursors = new();
    readonly MText _scoreboard, _banner;
    string _scoreText = "", _bannerText = "";
    bool _disposed;
    TransientDrawingMode _mode;

    /// <summary>The marker, tag and power bar that show a human-controlled player.</summary>
    sealed class Cursor
    {
        public BlockReference Marker = null!; // ring + direction arrow
        public MText Tag = null!;
        public Polyline PowerBack = null!, Power = null!;
    }

    /// <summary>Anonymous block definitions created for the animation; erase them when done.</summary>
    public IReadOnlyList<ObjectId> Blocks => _blocks;

    /// <param name="mode">How AutoCAD paints the transients; switchable later with <see cref="SetMode"/>.</param>
    public Renderer(Database db, Point3d origin, Match match, TransientDrawingMode mode)
    {
        _mode = mode;
        _origin = origin;
        int n = match.Players.Count;
        _poses = new ObjectId[n][];
        _players = new BlockReference[n];
        _running = new bool[n];

        var cursorBlocks = new List<ObjectId>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            for (int i = 0; i < n; i++)
            {
                var p = match.Players[i];
                _poses[i] = new ObjectId[PoseCount];
                for (int k = 0; k < PoseCount; k++)
                    _poses[i][k] = MakeBlock(tr, bt, FigureFrame(p, k));
            }

            for (int f = 0; f < BallFrames; f++)
                _ballFrames[f] = MakeBlock(tr, bt, BallFrame(f));

            foreach (var c in match.Controllers)
                cursorBlocks.Add(MakeBlock(tr, bt, CursorFrame(CursorColour[c.Index])));

            tr.Commit();
        }

        // Shadows sit under everything, so the ball's is drawn on its own.
        _ballShadow = Add(Disk(BallR, Rgb(Look.Shadow)), ZShadow);
        for (int i = 0; i < n; i++)
            _players[i] = Add(new BlockReference(Point3d.Origin, _poses[i][PoseStand]), ZPlayer);
        _ball = Add(new BlockReference(Point3d.Origin, _ballFrames[0]), ZBall);

        foreach (var c in match.Controllers)
        {
            short colour = CursorColour[c.Index];
            _cursors.Add(new Cursor
            {
                Marker = Add(new BlockReference(Point3d.Origin, cursorBlocks[c.Index]), ZCursor),
                Tag = Add(Text(db, $"P{c.Index + 1}", 1.0 * S, Color.FromColorIndex(ColorMethod.ByAci, colour)), ZCursor),
                PowerBack = Add(Stroke(0.35, Rgb((60, 60, 60))), ZCursor),
                Power = Add(Stroke(0.35, Rgb((255, 120, 0))), ZCursor + 1),
            });
        }

        // Scoreboard above the pitch, banner (GOAL!, CORNER...) just above the centre circle.
        _scoreboard = Add(Text(db, "", 3.5, Rgb((255, 255, 255))), ZHud);
        _scoreboard.Location = At(0, Match.HalfWidth + 6);
        _banner = Add(Text(db, "", 7.0, Color.FromColorIndex(ColorMethod.ByAci, 2)), ZHud);
        _banner.Location = At(0, 14);

        Update(match);
        var tm = TransientManager.CurrentTransientManager;
        foreach (var (e, z) in _all)
            tm.AddTransient(e, _mode, z, _viewports);
    }

    // ---------------------------------------------------------------- building frames

    ObjectId MakeBlock(Transaction tr, BlockTable bt, IEnumerable<Entity> entities)
    {
        var btr = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
        var id = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);
        foreach (var e in entities)
        {
            e.Layer = "0";
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }
        _blocks.Add(id);
        return id;
    }

    /// <summary>One animation frame of a player, at the origin facing +X.</summary>
    static IEnumerable<Entity> FigureFrame(Footballer p, int pose)
    {
        bool sliding = pose == PoseSlide, down = pose == PoseDown;
        double swing = pose >= 1 && pose <= RunFrames ? Math.Sin((pose - 1) * 2 * Math.PI / RunFrames) : 0;
        var b = Look.Pose(Vector2.Zero, Vector2.UnitX, swing, sliding, down);

        // Shadow stays centred: it turns with the block, so an offset would swing around.
        yield return DiskAt(Vector2.Zero, Look.ShadowR, Rgb(Look.Shadow));
        yield return StrokeAt(b.LegL0, b.LegL1, Look.LegW, Rgb(Look.Socks(p)));
        yield return StrokeAt(b.LegR0, b.LegR1, Look.LegW, Rgb(Look.Socks(p)));
        yield return StrokeAt(b.ArmL0, b.ArmL1, Look.ArmW, Rgb(Look.Arms(p)));
        yield return StrokeAt(b.ArmR0, b.ArmR1, Look.ArmW, Rgb(Look.Arms(p)));
        yield return DiskAt(b.Torso, Look.TorsoR, Rgb(Look.Shirt(p)));
        yield return StrokeAt(b.ShoulderL, b.ShoulderR, Look.ShoulderW, Rgb(Look.Shirt(p)));
        yield return DiskAt(b.Hair, Look.HairR, Rgb(Look.Hair(p)));
        yield return DiskAt(b.Head, Look.HeadR, Rgb(Look.Skin(p)));
    }

    /// <summary>The ball rolling along +X; frames step the panels through a third of a turn.</summary>
    static IEnumerable<Entity> BallFrame(int frame)
    {
        yield return DiskAt(Vector2.Zero, BallR, Rgb(Look.BallWhite));
        double spin = frame * (2 * Math.PI / 3) / BallFrames;
        for (int i = 0; i < 3; i++)
        {
            double a = spin + i * 2 * Math.PI / 3;
            yield return DiskAt(new Vector2((float)(Math.Sin(a) * 0.35), (i - 1) * 0.26f), 0.15, Rgb(Look.BallPanel));
        }
    }

    /// <summary>Ring around the player plus an arrow pointing the way they face (+X).</summary>
    static IEnumerable<Entity> CursorFrame(short colour)
    {
        yield return new Circle(Point3d.Origin, Vector3d.ZAxis, 1.25 * S) { ColorIndex = colour };
        yield return new Solid(new Point3d(2.3 * S, 0, 0), new Point3d(1.55 * S, 0.45 * S, 0),
                               new Point3d(1.55 * S, -0.45 * S, 0)) { ColorIndex = colour };
    }

    // ---------------------------------------------------------------- per frame

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
        {
            var p = m.Players[i];
            float speed = p.Vel.Length();
            if (_running[i] ? speed < RunStop : speed > RunStart) _running[i] = !_running[i];

            int pose = p.SlideTimer > 0f ? PoseSlide
                : p.StunTimer > 0f ? PoseDown
                : _running[i] ? 1 + (int)(p.RunPhase / (2 * Math.PI) * RunFrames) % RunFrames
                : PoseStand;
            Place(_players[i], p.Pos, p.Facing, _poses[i][pose]);
        }

        // Ball: turned to its direction of travel, cycling frames as it spins.
        var ball = m.Ball;
        if (ball.Vel.LengthSquared() > 0.01f) _ballAngle = Math.Atan2(ball.Vel.Y, ball.Vel.X);
        double third = 2 * Math.PI / 3;
        int frame = (int)((ball.Spin % third + third) % third / third * BallFrames) % BallFrames;
        _ball.Position = At(ball.Pos.X, ball.Pos.Y);
        _ball.Rotation = _ballAngle;
        SetBlock(_ball, _ballFrames[frame]);
        MoveDisk(_ballShadow, ball.Pos.X + 0.25, ball.Pos.Y - 0.25, BallR);

        foreach (var c in m.Controllers)
        {
            var cur = _cursors[c.Index];
            var p = c.Player;
            Place(cur.Marker, p.Pos, p.Facing, cur.Marker.BlockTableRecord);
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

    void Place(BlockReference br, Vector2 pos, Vector2 facing, ObjectId block)
    {
        br.Position = At(pos.X, pos.Y);
        br.Rotation = Math.Atan2(facing.Y, facing.X);
        SetBlock(br, block);
    }

    static void SetBlock(BlockReference br, ObjectId block)
    {
        if (br.BlockTableRecord != block) br.BlockTableRecord = block;
    }

    // ---------------------------------------------------------------- helpers

    Point3d At(double x, double y) => new(_origin.X + x, _origin.Y + y, _origin.Z);

    T Add<T>(T e, int z) where T : Entity
    {
        _all.Add((e, z));
        return e;
    }

    static Color Rgb((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>A filled circle, drawn the same way as the DONUT command: a two-arc polyline with width = radius.</summary>
    static Polyline Disk(double r, Color colour) => DiskAt(Vector2.Zero, r, colour);

    static Polyline DiskAt(Vector2 c, double r, Color colour)
    {
        var pl = new Polyline(2);
        pl.AddVertexAt(0, new Point2d(c.X - r / 2, c.Y), 1, r, r);
        pl.AddVertexAt(1, new Point2d(c.X + r / 2, c.Y), 1, r, r);
        pl.Closed = true;
        pl.Color = colour;
        return pl;
    }

    /// <summary>A thick straight stroke (a wide two-vertex polyline).</summary>
    static Polyline Stroke(double width, Color colour) =>
        StrokeAt(Vector2.Zero, new Vector2(0.01f, 0f), width, colour);

    static Polyline StrokeAt(Vector2 a, Vector2 b, double width, Color colour)
    {
        var pl = new Polyline(2);
        pl.AddVertexAt(0, new Point2d(a.X, a.Y), 0, width, width);
        pl.AddVertexAt(1, new Point2d(b.X, b.Y), 0, width, width);
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

    /// <summary>Removes the transients. The block definitions in <see cref="Blocks"/> are erased by the caller, under a document lock.</summary>
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
