using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CadFifa;

/// <summary>
/// Draws the players, ball and scoreboard as transient graphics: they animate on screen
/// without ever touching the drawing database (no undo spam, no regen).
/// </summary>
internal sealed class Renderer : IDisposable
{
    const short HomeColour = 1;       // red
    const short HomeKeeperColour = 30; // orange
    const short AwayColour = 5;       // blue
    const short AwayKeeperColour = 6; // magenta
    const short CursorColour = 2;     // yellow

    readonly Point3d _origin;
    readonly IntegerCollection _viewports = new();
    readonly List<Polyline> _players = new();
    readonly Polyline _ball = Disk(Match.BallRadius, 7);
    readonly Circle _ballEdge = new() { Radius = Match.BallRadius, ColorIndex = 250 };
    readonly Circle _cursor = new() { Radius = Match.PlayerRadius + 0.7, ColorIndex = CursorColour };
    readonly Line _facing = new() { ColorIndex = CursorColour };
    readonly Line _chargeBar = new() { ColorIndex = 1 };
    readonly MText _scoreboard = new();
    readonly MText _banner = new();
    readonly List<Drawable> _all = new();
    bool _disposed;

    public Renderer(Database db, Point3d origin, Match match)
    {
        _origin = origin;
        foreach (var p in match.Players)
        {
            short colour = p.Side == Side.Home
                ? (p.IsKeeper ? HomeKeeperColour : HomeColour)
                : (p.IsKeeper ? AwayKeeperColour : AwayColour);
            _players.Add(Disk(Match.PlayerRadius, colour));
        }

        foreach (var t in new[] { _scoreboard, _banner })
        {
            t.SetDatabaseDefaults(db);
            t.Attachment = AttachmentPoint.MiddleCenter;
            t.ColorIndex = 7;
        }
        _scoreboard.TextHeight = 3.5;
        _scoreboard.Location = At(0, Match.HalfWidth + 6);
        _banner.TextHeight = 6;
        _banner.Location = At(0, 12);
        _banner.ColorIndex = CursorColour;

        _all.AddRange(_players);
        _all.Add(_cursor);
        _all.Add(_facing);
        _all.Add(_chargeBar);
        _all.Add(_ball);
        _all.Add(_ballEdge);
        _all.Add(_scoreboard);
        _all.Add(_banner);

        Update(match);
        var tm = TransientManager.CurrentTransientManager;
        foreach (var d in _all)
            tm.AddTransient(d, TransientDrawingMode.DirectTopmost, 128, _viewports);
    }

    public void Draw(Match match)
    {
        if (_disposed) return;
        Update(match);
        var tm = TransientManager.CurrentTransientManager;
        foreach (var d in _all)
            tm.UpdateTransient(d, _viewports);
    }

    void Update(Match m)
    {
        for (int i = 0; i < m.Players.Count; i++)
            MoveDisk(_players[i], m.Players[i].Pos.X, m.Players[i].Pos.Y, Match.PlayerRadius);

        MoveDisk(_ball, m.Ball.Pos.X, m.Ball.Pos.Y, Match.BallRadius);
        _ballEdge.Center = At(m.Ball.Pos.X, m.Ball.Pos.Y);

        var me = m.Controlled;
        _cursor.Center = At(me.Pos.X, me.Pos.Y);
        _facing.StartPoint = At(me.Pos.X + me.Facing.X * 1.6, me.Pos.Y + me.Facing.Y * 1.6);
        _facing.EndPoint = At(me.Pos.X + me.Facing.X * 3.0, me.Pos.Y + me.Facing.Y * 3.0);

        // Shot power meter under the controlled player.
        double len = Math.Max(0.01, m.Charge * 5.0);
        _chargeBar.StartPoint = At(me.Pos.X - 2.5, me.Pos.Y - 2.6);
        _chargeBar.EndPoint = At(me.Pos.X - 2.5 + len, me.Pos.Y - 2.6);
        _chargeBar.Visible = m.Charge > 0f;

        _scoreboard.Contents = $"HOME  {m.Score[0]} - {m.Score[1]}  AWAY      {m.MatchMinute}'";
        _banner.Contents = m.MessageTimer > 0f ? m.Message : "";
        _banner.Visible = m.MessageTimer > 0f;
    }

    Point3d At(double x, double y) => new(_origin.X + x, _origin.Y + y, _origin.Z);

    /// <summary>A filled circle, drawn the same way as the DONUT command: a two-arc polyline with width = radius.</summary>
    static Polyline Disk(double r, short colour)
    {
        var pl = new Polyline(2);
        pl.AddVertexAt(0, new Point2d(-r / 2, 0), 1, r, r);
        pl.AddVertexAt(1, new Point2d(r / 2, 0), 1, r, r);
        pl.Closed = true;
        pl.ColorIndex = colour;
        return pl;
    }

    void MoveDisk(Polyline pl, double x, double y, double r)
    {
        pl.SetPointAt(0, new Point2d(_origin.X + x - r / 2, _origin.Y + y));
        pl.SetPointAt(1, new Point2d(_origin.X + x + r / 2, _origin.Y + y));
        pl.Elevation = _origin.Z;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var tm = TransientManager.CurrentTransientManager;
        foreach (var d in _all)
        {
            tm.EraseTransient(d, _viewports);
            d.Dispose();
        }
    }
}
