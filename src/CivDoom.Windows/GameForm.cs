using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CivDoom.Engine;

namespace CivDoom.Windows;

/// <summary>
/// Hosts a <see cref="Game"/>: runs the loop, reads keyboard/mouse, and blits the software framebuffer.
/// </summary>
public sealed class GameForm : Form
{
    private const int RenderWidth = 400;
    private const int RenderHeight = 250;
    private const double MouseSensitivity = 0.0035;

    private readonly Func<Level> _levelFactory;
    private readonly string? _contentRoot;
    private readonly Renderer _renderer = new(RenderWidth, RenderHeight);
    private readonly Bitmap _frame = new(RenderWidth, RenderHeight, PixelFormat.Format32bppRgb);
    private readonly HashSet<Keys> _keys = new();
    private readonly GameInput _input = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };

    private Game _game;

    static GameForm() => WindowsImageDecoder.Install();
    private double _lastTime;
    private bool _mouseCaptured;
    private bool _mouseFire;
    private int _seed;

    /// <param name="levelFactory">Builds a fresh level; called again on restart.</param>
    /// <param name="monstersFolder">Folder of editable monster .txt files (created if missing), or null for the built-in monsters.</param>
    /// <param name="weaponsFolder">Folder of editable weapon .txt files (created if missing), or null for the built-in weapons.</param>
    /// <param name="contentRoot">
    /// Folder holding monsters\, weapons\, themes\ and player\ (each created if missing), or null for the built-in content.
    /// </param>
    public GameForm(Func<Level> levelFactory, string? contentRoot = null, string title = "CivDOOM")
    {
        _levelFactory = levelFactory;
        _contentRoot = contentRoot;
        _game = new Game(levelFactory(), _seed, LoadContent());

        Text = $"{title} - {_game.Level.Name}";
        ClientSize = new Size(RenderWidth * 3, RenderHeight * 3);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _timer.Tick += (_, _) => Tick();
        Shown += (_, _) => { _lastTime = _clock.Elapsed.TotalSeconds; _timer.Start(); };
        Deactivate += (_, _) => { _keys.Clear(); ReleaseMouse(); };
    }

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTime;
        _lastTime = now;

        _input.Forward = Down(Keys.W) || Down(Keys.Up);
        _input.Back = Down(Keys.S) || Down(Keys.Down);
        _input.StrafeLeft = Down(Keys.A);
        _input.StrafeRight = Down(Keys.D);
        _input.TurnLeft = Down(Keys.Left) || Down(Keys.Q);
        _input.TurnRight = Down(Keys.Right) || Down(Keys.E);
        _input.Run = Down(Keys.ShiftKey);
        _input.Fire = Down(Keys.ControlKey) || _mouseFire;
        _input.Jump = Down(Keys.Space);
        _input.MouseTurn = ReadMouseTurn();

        _game.Update(dt, _input);
        _renderer.Hud.Hint = _mouseCaptured ? null : "CLICK TO CAPTURE MOUSE  -  H HIDES HUD  -  TAB RADAR  -  F5 RELOAD  -  ESC QUIT";
        _input.SelectSlot = 0;
        _input.CycleWeapon = 0;
        _renderer.Render(_game);
        Invalidate();
    }

    private bool Down(Keys k) => _keys.Contains(k);

    private double ReadMouseTurn()
    {
        if (!_mouseCaptured) return 0;
        Point center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        int dx = Cursor.Position.X - center.X;
        Cursor.Position = center;
        return -dx * MouseSensitivity;
    }

    private void CaptureMouse()
    {
        if (_mouseCaptured) return;
        _mouseCaptured = true;
        Cursor.Hide();
        Cursor.Position = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
    }

    private void ReleaseMouse()
    {
        _mouseFire = false;
        if (!_mouseCaptured) return;
        _mouseCaptured = false;
        Cursor.Show();
    }

    private GameContent LoadContent() => _contentRoot == null ? GameContent.BuiltIn : GameContent.Load(_contentRoot);

    private void Restart()
    {
        _seed++;
        _game = new Game(_levelFactory(), _seed, LoadContent());
    }

    protected override bool IsInputKey(Keys keyData) => true; // we want arrows, Tab, etc.

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _keys.Add(e.KeyCode);
        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_mouseCaptured) ReleaseMouse();
                else Close();
                break;
            case Keys.H:
                _renderer.Hud.Visible = !_renderer.Hud.Visible;
                break;
            case Keys.Tab:
            case Keys.M:
                _renderer.ShowMap = !_renderer.ShowMap;
                break;
            case Keys.Enter when _game.State != GameState.Playing:
                Restart();
                break;
            case Keys.F5:
                GameContent content = LoadContent();
                _game.ReloadMonsters(content.Monsters);
                _game.ReloadWeapons(content.Weapons);
                _game.ReloadLook(content.Themes, content.Player);
                break;
            case >= Keys.D1 and <= Keys.D9:
                _input.SelectSlot = e.KeyCode - Keys.D0;
                break;
            case >= Keys.NumPad1 and <= Keys.NumPad9:
                _input.SelectSlot = e.KeyCode - Keys.NumPad0;
                break;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnKeyUp(KeyEventArgs e) => _keys.Remove(e.KeyCode);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!_mouseCaptured) CaptureMouse();
        else if (e.Button == MouseButtons.Left) _mouseFire = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e.Delta != 0) _input.CycleWeapon = e.Delta > 0 ? -1 : 1;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _mouseFire = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is covered by OnPaint; skipping this avoids flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        BitmapData data = _frame.LockBits(new Rectangle(0, 0, RenderWidth, RenderHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            if (data.Stride == RenderWidth * 4)
            {
                Marshal.Copy(_renderer.Pixels, 0, data.Scan0, _renderer.Pixels.Length);
            }
            else
            {
                for (int y = 0; y < RenderHeight; y++)
                    Marshal.Copy(_renderer.Pixels, y * RenderWidth, data.Scan0 + y * data.Stride, RenderWidth);
            }
        }
        finally
        {
            _frame.UnlockBits(data);
        }

        Graphics g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        // Letterbox to keep the aspect ratio.
        float scale = Math.Min((float)ClientSize.Width / RenderWidth, (float)ClientSize.Height / RenderHeight);
        int w = (int)(RenderWidth * scale), h = (int)(RenderHeight * scale);
        var dest = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
        g.Clear(Color.Black);
        g.DrawImage(_frame, dest);

    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        ReleaseMouse();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _frame.Dispose();
        }
        base.Dispose(disposing);
    }
}
