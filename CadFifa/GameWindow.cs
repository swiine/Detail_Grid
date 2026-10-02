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
using Autodesk.AutoCAD.GraphicsInterface;
using Font = System.Drawing.Font;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CadFifa;

/// <summary>
/// The modeless game window. It owns the game loop and reads the keyboard while it has
/// focus (one or two players). In Window display it also shows the match itself
/// (flicker-free); in Drawing display it drives transient graphics in model space instead.
/// </summary>
internal sealed class GameWindow : Form
{
    // Virtual-key codes for telling left and right modifiers apart.
    const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_RCONTROL = 0xA3;

    readonly Document _doc;
    readonly Match _match;
    readonly Renderer? _renderer;   // Drawing display
    readonly PitchView? _view;      // Window display
    readonly List<ObjectId> _pitchIds;
    readonly System.Windows.Forms.Timer _timer = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly HashSet<Keys> _down = new();
    readonly PadState[] _pads = { new(), new() };
    readonly bool[] _passQueued = new bool[2], _switchQueued = new bool[2];
    readonly Label _score = new();
    readonly Label _status = new();
    bool _paused;

    /// <summary>
    /// Ways of painting the game, cycled with M. Main is double-buffered with the rest of the
    /// drawing and should be flicker-free; the others are kept as fallbacks for graphics setups
    /// where Main misbehaves (for example, players hidden under the grass).
    /// </summary>
    static readonly (TransientDrawingMode Mode, bool Repaint, string Name)[] DrawModes =
    {
        (TransientDrawingMode.Main, true, "Main"),
        (TransientDrawingMode.DirectShortTerm, false, "Direct"),
        (TransientDrawingMode.Highlight, true, "Highlight"),
        (TransientDrawingMode.DirectTopmost, true, "Topmost (old)"),
    };

    /// <summary>Remembered for the rest of the AutoCAD session, so a mode that works sticks.</summary>
    static int _drawMode;

    bool TwoPlayer => _match.Mode != GameMode.Solo;

    bool InDrawing => _renderer != null;

    /// <param name="inDrawing">
    /// True to animate the players in the drawing itself (needs <paramref name="pitchIds"/> from
    /// <see cref="Pitch.Draw"/>); false to show the whole match inside this window.
    /// </param>
    public GameWindow(Document doc, Point3d origin, GameMode mode, Difficulty difficulty,
                      bool inDrawing, List<ObjectId> pitchIds)
    {
        _doc = doc;
        _pitchIds = pitchIds;
        _match = new Match(mode, difficulty);
        if (inDrawing) _renderer = new Renderer(doc.Database, origin, _match, DrawModes[_drawMode].Mode);
        // AutoCAD needs time to repaint the drawing between frames, so cap that display at ~30 fps.
        _timer.Interval = inDrawing ? 33 : 15;

        Text = mode switch
        {
            GameMode.Versus => "CAD FIFA - P1 vs P2",
            GameMode.Coop => "CAD FIFA - P1 + P2 vs CPU",
            _ => "CAD FIFA",
        };
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 60, 30);
        ForeColor = Color.White;

        // Side panel: score, status line and the controls.
        int panelWidth = TwoPlayer ? 470 : 310;
        var panel = new Panel { Width = panelWidth, Dock = DockStyle.Right };
        _score.SetBounds(10, 8, panelWidth - 20, 34);
        _score.Font = new Font("Consolas", 16f, FontStyle.Bold);
        _status.SetBounds(10, 42, panelWidth - 20, 20);
        _status.ForeColor = Color.Gold;
        var help = new Label
        {
            Bounds = new Rectangle(10, 66, panelWidth - 20, 230),
            Font = new Font("Consolas", 9f),
            Text = HelpText(mode, inDrawing),
        };
        panel.Controls.AddRange(new Control[] { _score, _status, help });

        if (inDrawing)
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 120);
            ClientSize = new Size(panelWidth, 300);
            Controls.Add(panel);
        }
        else
        {
            // A resizable window with the pitch filling everything left of the side panel.
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900 + panelWidth, 640);
            MinimumSize = new Size(500 + panelWidth, 380);
            _view = new PitchView { Dock = DockStyle.Fill };
            Controls.Add(_view);
            Controls.Add(panel);
            _view.Show(_match);
        }

        _timer.Tick += (_, _) => Tick();
        Activated += (_, _) => { _paused = false; _clock.Restart(); };
        Deactivate += (_, _) => { _paused = true; _down.Clear(); };
        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
        _timer.Start();
    }

    static string HelpText(GameMode mode, bool inDrawing)
    {
        string common = inDrawing
            ? "P pause    R restart    M draw mode    Esc quit"
            : "P pause    R restart    Esc quit";
        if (mode == GameMode.Solo)
        {
            return "WASD / Arrows  move\n" +
                   "Shift          sprint\n" +
                   "Space          with ball: hold + release\n" +
                   "                 to shoot (W/S aims)\n" +
                   "               without ball: slide tackle\n" +
                   "E              pass\n" +
                   "Q              switch player\n\n" +
                   "P pause   R restart   Esc quit\n" +
                   (inDrawing ? "M  change draw mode (if it flickers)\n" : "") + "\n" +
                   "You are RED, attacking right →";
        }
        string sides = mode == GameMode.Versus
            ? "P1 is RED (attacks →)    P2 is BLUE (attacks ←)"
            : "P1 and P2 are both RED, attacking →";
        return "                P1 (yellow ring)  P2 (cyan ring)\n" +
               "Move            W A S D           Arrow keys\n" +
               "Sprint          Left Shift        Right Shift\n" +
               "Shoot / slide   Space             Enter or Num 0\n" +
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
            if (_renderer != null)
            {
                _renderer.Draw(_match);
                if (DrawModes[_drawMode].Repaint) _doc.Editor.UpdateScreen();
            }
            _view?.Show(_match);
        }

        string home = TwoPlayer && _match.Mode == GameMode.Versus ? "P1" : "HOME";
        string away = TwoPlayer && _match.Mode == GameMode.Versus ? "P2" : "AWAY";
        // Only touch the labels when their text changes; re-setting them every frame repaints them.
        SetText(_score, $"{home} {_match.Score[0]} - {_match.Score[1]} {away}   {_match.MatchMinute}'");
        SetText(_status, _paused ? "PAUSED - click here to play"
            : _match.MessageTimer > 0f ? _match.Message
            : InDrawing ? $"Draw mode: {DrawModes[_drawMode].Name}  (M to change)" : "");
    }

    static void SetText(Label label, string text)
    {
        if (label.Text != text) label.Text = text;
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
                case Keys.M when _renderer != null:
                    _drawMode = (_drawMode + 1) % DrawModes.Length;
                    _renderer.SetMode(DrawModes[_drawMode].Mode);
                    _doc.Editor.UpdateScreen();
                    break;
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
        _renderer?.Dispose();
        try
        {
            if (_pitchIds.Count > 0 && !_doc.IsDisposed)
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
