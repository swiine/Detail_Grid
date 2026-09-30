using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Font = System.Drawing.Font;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CadFifa;

/// <summary>
/// Small modeless "controller" window. It owns the game loop, reads the keyboard while it
/// has focus, and drives the transient graphics in the drawing.
/// </summary>
internal sealed class GameWindow : Form
{
    readonly Document _doc;
    readonly Match _match;
    readonly Renderer _renderer;
    readonly List<ObjectId> _pitchIds;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly HashSet<Keys> _down = new();
    readonly PadState _pad = new();
    readonly Label _score = new();
    readonly Label _status = new();
    bool _paused;
    bool _passQueued, _switchQueued;

    public GameWindow(Document doc, Point3d origin, Difficulty difficulty, List<ObjectId> pitchIds)
    {
        _doc = doc;
        _pitchIds = pitchIds;
        _match = new Match(difficulty);
        _renderer = new Renderer(doc.Database, origin, _match);

        Text = "CAD FIFA";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(40, 120);
        ClientSize = new Size(300, 250);
        KeyPreview = true;
        BackColor = Color.FromArgb(24, 60, 30);
        ForeColor = Color.White;

        _score.SetBounds(10, 8, 280, 34);
        _score.Font = new Font("Consolas", 16f, FontStyle.Bold);
        _status.SetBounds(10, 42, 280, 20);
        _status.ForeColor = Color.Gold;
        var help = new Label
        {
            Bounds = new Rectangle(10, 66, 280, 180),
            Font = new Font("Consolas", 9f),
            Text = "WASD / Arrows  move\n" +
                   "Shift          sprint\n" +
                   "Space (hold)   shoot / tackle\n" +
                   "                 (W/S or Up/Down aims)\n" +
                   "E              pass\n" +
                   "Q              switch player\n" +
                   "P              pause\n" +
                   "R              restart match\n" +
                   "Esc            quit\n\n" +
                   "You are RED, attacking right →",
        };
        Controls.AddRange(new Control[] { _score, _status, help });

        _timer.Tick += (_, _) => Tick();
        Activated += (_, _) => { _paused = false; _clock.Restart(); };
        Deactivate += (_, _) => { _paused = true; _down.Clear(); };
        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
        _timer.Start();
    }

    void OnDocumentClosing(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document == _doc) Close();
    }

    void Tick()
    {
        float dt = (float)Math.Min(0.05, _clock.Elapsed.TotalSeconds);
        _clock.Restart();

        if (!_paused)
        {
            ReadPad();
            _match.Step(dt, _pad);
            _renderer.Draw(_match);
            _doc.Editor.UpdateScreen();
        }

        _score.Text = $"{_match.Score[0]} - {_match.Score[1]}   {_match.MatchMinute}'";
        _status.Text = _paused ? "PAUSED - click here to play" : _match.MessageTimer > 0f ? _match.Message : "";
    }

    void ReadPad()
    {
        float x = 0f, y = 0f;
        if (_down.Contains(Keys.A) || _down.Contains(Keys.Left)) x -= 1f;
        if (_down.Contains(Keys.D) || _down.Contains(Keys.Right)) x += 1f;
        if (_down.Contains(Keys.S) || _down.Contains(Keys.Down)) y -= 1f;
        if (_down.Contains(Keys.W) || _down.Contains(Keys.Up)) y += 1f;
        _pad.Move = new Vector2(x, y);
        _pad.Sprint = _down.Contains(Keys.ShiftKey);
        _pad.ShootHeld = _down.Contains(Keys.Space);
        _pad.PassPressed = _passQueued;
        _pad.SwitchPressed = _switchQueued;
        _passQueued = _switchQueued = false;
    }

    // Let arrow keys and space reach OnKeyDown instead of being eaten for focus navigation.
    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessDialogKey(Keys keyData) => false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.KeyCode;
        if (_down.Add(key))
        {
            switch (key)
            {
                case Keys.E: _passQueued = true; break;
                case Keys.Q: _switchQueued = true; break;
                case Keys.P: _paused = !_paused; _clock.Restart(); break;
                case Keys.R: _match.Restart(); break;
                case Keys.Escape: Close(); return;
            }
        }
        e.Handled = e.SuppressKeyPress = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _down.Remove(e.KeyCode);
        e.Handled = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentClosing;
        _renderer.Dispose();
        try
        {
            if (!_doc.IsDisposed)
            {
                using (_doc.LockDocument())
                    Pitch.Erase(_doc.Database, _pitchIds);
                _doc.Editor.UpdateScreen();
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Drawing is closing; nothing left to clean up.
        }
        Commands.GameClosed();
        base.OnFormClosed(e);
    }
}
