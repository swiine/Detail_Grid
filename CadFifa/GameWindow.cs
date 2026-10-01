using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Font = System.Drawing.Font;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CadFifa;

/// <summary>
/// Small modeless "controller" window. It owns the game loop, reads the keyboard while it
/// has focus (one or two players), moves the camera and drives the transient graphics.
/// </summary>
internal sealed class GameWindow : Form
{
    // Broadcast camera framing, in metres.
    const float CamHeight = 46f, CamWidth = 80f;
    const float FullHeight = 2 * Match.HalfWidth + 22f, FullWidth = 2 * Match.HalfLength + 16f;

    // Virtual-key codes for telling left and right modifiers apart.
    const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_RCONTROL = 0xA3;

    readonly Document _doc;
    readonly Point3d _origin;
    readonly Match _match;
    readonly Renderer _renderer;
    readonly List<ObjectId> _pitchIds;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly HashSet<Keys> _down = new();
    readonly PadState[] _pads = { new(), new() };
    readonly bool[] _passQueued = new bool[2], _switchQueued = new bool[2];
    readonly Label _score = new();
    readonly Label _status = new();
    bool _paused;
    bool _broadcast = true;
    Vector2 _cam = new(0f, 1.5f);
    Vector2 _appliedCam = new(float.NaN, float.NaN);
    float _appliedHeight;

    bool TwoPlayer => _match.Mode != GameMode.Solo;

    public GameWindow(Document doc, Point3d origin, GameMode mode, Difficulty difficulty, List<ObjectId> pitchIds)
    {
        _doc = doc;
        _origin = origin;
        _pitchIds = pitchIds;
        _match = new Match(mode, difficulty);
        _renderer = new Renderer(doc.Database, origin, _match);

        Text = mode switch
        {
            GameMode.Versus => "CAD FIFA - P1 vs P2",
            GameMode.Coop => "CAD FIFA - P1 + P2 vs CPU",
            _ => "CAD FIFA",
        };
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(40, 120);
        ClientSize = new Size(TwoPlayer ? 470 : 310, 300);
        KeyPreview = true;
        BackColor = Color.FromArgb(24, 60, 30);
        ForeColor = Color.White;

        _score.SetBounds(10, 8, ClientSize.Width - 20, 34);
        _score.Font = new Font("Consolas", 16f, FontStyle.Bold);
        _status.SetBounds(10, 42, ClientSize.Width - 20, 20);
        _status.ForeColor = Color.Gold;
        var help = new Label
        {
            Bounds = new Rectangle(10, 66, ClientSize.Width - 20, 230),
            Font = new Font("Consolas", 9f),
            Text = HelpText(mode),
        };
        Controls.AddRange(new Control[] { _score, _status, help });

        _timer.Tick += (_, _) => Tick();
        Activated += (_, _) => { _paused = false; _clock.Restart(); };
        Deactivate += (_, _) => { _paused = true; _down.Clear(); };
        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
        _timer.Start();
    }

    static string HelpText(GameMode mode)
    {
        const string common =
            "P pause    R restart    C camera (follow / full pitch)    Esc quit";
        if (mode == GameMode.Solo)
        {
            return "WASD / Arrows  move\n" +
                   "Shift          sprint\n" +
                   "Space (hold)   shoot / tackle\n" +
                   "                 (W/S or Up/Down aims)\n" +
                   "E              pass\n" +
                   "Q              switch player\n\n" +
                   "P pause   R restart   C camera\n" +
                   "Esc quit\n\n" +
                   "You are RED, attacking right →";
        }
        string sides = mode == GameMode.Versus
            ? "P1 is RED (attacks →)    P2 is BLUE (attacks ←)"
            : "P1 and P2 are both RED, attacking →";
        return "                P1 (yellow ring)  P2 (cyan ring)\n" +
               "Move            W A S D           Arrow keys\n" +
               "Sprint          Left Shift        Right Shift\n" +
               "Shoot / tackle  Space (hold)      Enter or Num 0 (hold)\n" +
               "Pass            E                 Right Ctrl or Num 1\n" +
               "Switch player   Q                 /  or Num 2\n\n" +
               common + "\n\n" + sides;
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
            ReadPads();
            _match.Step(dt, _pads);
            float height = _broadcast ? CamHeight : FullHeight;
            MoveCamera(dt);
            _renderer.Draw(_match, _cam, height);
            _doc.Editor.UpdateScreen();
        }

        string home = TwoPlayer && _match.Mode == GameMode.Versus ? "P1" : "HOME";
        string away = TwoPlayer && _match.Mode == GameMode.Versus ? "P2" : "AWAY";
        _score.Text = $"{home} {_match.Score[0]} - {_match.Score[1]} {away}   {_match.MatchMinute}'";
        _status.Text = _paused ? "PAUSED - click here to play" : _match.MessageTimer > 0f ? _match.Message : "";
    }

    /// <summary>Broadcast camera: glides after the ball, staying over the pitch.</summary>
    void MoveCamera(float dt)
    {
        Vector2 target;
        float height = _broadcast ? CamHeight : FullHeight;
        float width = _broadcast ? CamWidth : FullWidth;
        if (_broadcast)
        {
            var lead = _match.Ball.Pos + _match.Ball.Vel * 0.25f;
            float maxX = Match.HalfLength + 6f - width / 2f;
            float maxY = Match.HalfWidth + 6f - height / 2f;
            target = new Vector2(Math.Max(-maxX, Math.Min(maxX, lead.X)), Math.Max(-maxY, Math.Min(maxY, lead.Y)));
            _cam += (target - _cam) * Math.Min(1f, dt * 2.5f);
        }
        else
        {
            _cam = new Vector2(0f, 1.5f);
        }

        // Only touch the view when it has actually moved, to keep redraws cheap.
        if (height == _appliedHeight && Vector2.Distance(_cam, _appliedCam) < 0.05f) return;
        _appliedCam = _cam;
        _appliedHeight = height;
        var ed = _doc.Editor;
        using (_doc.LockDocument())
        using (var view = ed.GetCurrentView())
        {
            view.CenterPoint = new Point2d(_origin.X + _cam.X, _origin.Y + _cam.Y);
            view.Height = height;
            view.Width = width;
            ed.SetCurrentView(view);
        }
    }

    [DllImport("user32.dll")]
    static extern short GetKeyState(int virtualKey);

    static bool Held(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    bool Down(params Keys[] keys)
    {
        foreach (var k in keys)
            if (_down.Contains(k)) return true;
        return false;
    }

    void ReadPads()
    {
        if (!TwoPlayer)
        {
            // Solo: either side of the keyboard works.
            Fill(_pads[0],
                Down(Keys.A, Keys.Left), Down(Keys.D, Keys.Right), Down(Keys.S, Keys.Down), Down(Keys.W, Keys.Up),
                sprint: Down(Keys.ShiftKey), shoot: Down(Keys.Space, Keys.Return), player: 0);
            return;
        }
        Fill(_pads[0], Down(Keys.A), Down(Keys.D), Down(Keys.S), Down(Keys.W),
            sprint: Held(VK_LSHIFT), shoot: Down(Keys.Space), player: 0);
        Fill(_pads[1], Down(Keys.Left), Down(Keys.Right), Down(Keys.Down), Down(Keys.Up),
            sprint: Held(VK_RSHIFT), shoot: Down(Keys.Return, Keys.NumPad0), player: 1);
    }

    void Fill(PadState pad, bool left, bool right, bool down, bool up, bool sprint, bool shoot, int player)
    {
        pad.Move = new Vector2((right ? 1f : 0f) - (left ? 1f : 0f), (up ? 1f : 0f) - (down ? 1f : 0f));
        pad.Sprint = sprint;
        pad.ShootHeld = shoot;
        pad.PassPressed = _passQueued[player];
        pad.SwitchPressed = _switchQueued[player];
        _passQueued[player] = _switchQueued[player] = false;
    }

    // Let arrow keys, Enter and space reach OnKeyDown instead of being eaten for focus navigation.
    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessDialogKey(Keys keyData) => false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.KeyCode;
        if (_down.Add(key))
        {
            switch (key)
            {
                case Keys.E: _passQueued[0] = true; break;
                case Keys.Q: _switchQueued[0] = true; break;
                case Keys.ControlKey when TwoPlayer && Held(VK_RCONTROL):
                case Keys.NumPad1 when TwoPlayer:
                    _passQueued[1] = true; break;
                case Keys.OemQuestion when TwoPlayer:
                case Keys.NumPad2 when TwoPlayer:
                    _switchQueued[1] = true; break;
                case Keys.P: _paused = !_paused; _clock.Restart(); break;
                case Keys.R: _match.Restart(); break;
                case Keys.C: _broadcast = !_broadcast; break;
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
