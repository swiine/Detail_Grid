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
    private readonly string? _monstersFolder;
    private readonly string? _weaponsFolder;
    private readonly Renderer _renderer = new(RenderWidth, RenderHeight);
    private readonly Bitmap _frame = new(RenderWidth, RenderHeight, PixelFormat.Format32bppRgb);
    private readonly HashSet<Keys> _keys = new();
    private readonly GameInput _input = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };
    private readonly Font _hudFont = new(FontFamily.GenericMonospace, 14, FontStyle.Bold);
    private readonly Font _bigFont = new(FontFamily.GenericSansSerif, 32, FontStyle.Bold);

    private Game _game;
    private double _lastTime;
    private bool _mouseCaptured;
    private bool _mouseFire;
    private int _seed;

    /// <param name="levelFactory">Builds a fresh level; called again on restart.</param>
    /// <param name="monstersFolder">Folder of editable monster .txt files (created if missing), or null for the built-in monsters.</param>
    /// <param name="weaponsFolder">Folder of editable weapon .txt files (created if missing), or null for the built-in weapons.</param>
    public GameForm(Func<Level> levelFactory, string? monstersFolder = null, string? weaponsFolder = null, string title = "CivDOOM")
    {
        _levelFactory = levelFactory;
        _monstersFolder = monstersFolder;
        _weaponsFolder = weaponsFolder;
        _game = new Game(levelFactory(), _seed, LoadMonsters(), LoadWeapons());

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
        _input.Fire = Down(Keys.Space) || Down(Keys.ControlKey) || _mouseFire;
        _input.MouseTurn = ReadMouseTurn();

        _game.Update(dt, _input);
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

    private MonsterSet LoadMonsters() => _monstersFolder == null ? MonsterSet.BuiltIn : MonsterSet.Load(_monstersFolder);

    private WeaponSet LoadWeapons() => _weaponsFolder == null ? WeaponSet.BuiltIn : WeaponSet.Load(_weaponsFolder);

    private void Restart()
    {
        _seed++;
        _game = new Game(_levelFactory(), _seed, LoadMonsters(), LoadWeapons());
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
            case Keys.Tab:
            case Keys.M:
                _renderer.ShowMap = !_renderer.ShowMap;
                break;
            case Keys.Enter when _game.State != GameState.Playing:
                Restart();
                break;
            case Keys.F5:
                _game.ReloadMonsters(LoadMonsters());
                _game.ReloadWeapons(LoadWeapons());
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

        DrawHud(g, dest);
    }

    private void DrawHud(Graphics g, Rectangle view)
    {
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        Player p = _game.Player;
        int alive = _game.Enemies.Count(en => en.IsAlive);

        int? shots = p.ShotsLeft(p.Weapon);
        string ammo = shots is { } n ? $"{n}" : "--";
        string slots = string.Join(" ", p.Weapons.Select(w => w.Slot).Distinct());
        string hud = $"HEALTH {p.Health,3}%   {p.Weapon.Name.ToUpperInvariant()} {ammo}   HOSTILES {alive}/{_game.Enemies.Count}   [{slots}]";
        Shadowed(g, hud, _hudFont, p.Health <= 25 ? Brushes.OrangeRed : Brushes.Gold, view.Left + 12, view.Bottom - 30);

        if (_game.Message is { } msg)
            Shadowed(g, msg, _hudFont, Brushes.White, view.Left + 12, view.Top + 10);

        if (!_mouseCaptured && _game.State == GameState.Playing)
        {
            const string hint = "Click to capture mouse  |  WASD move  |  Mouse/Arrows turn  |  Click/Space fire  |  1-9/wheel weapons  |  Shift run  |  Tab map  |  F5 reload files  |  Esc quit";
            SizeF sz = g.MeasureString(hint, Font);
            Shadowed(g, hint, Font, Brushes.LightGray, view.Left + (view.Width - sz.Width) / 2, view.Bottom - 56);
        }

        string? banner = _game.State switch
        {
            GameState.Dead => "YOU DIED",
            GameState.Won => "LEVEL CLEAR",
            _ => null,
        };
        if (banner != null)
        {
            SizeF sz = g.MeasureString(banner, _bigFont);
            Shadowed(g, banner, _bigFont, _game.State == GameState.Dead ? Brushes.Red : Brushes.LimeGreen,
                view.Left + (view.Width - sz.Width) / 2, view.Top + view.Height / 3f);
            const string sub = "Enter: play again   Esc: back to the drawing";
            SizeF s2 = g.MeasureString(sub, _hudFont);
            Shadowed(g, sub, _hudFont, Brushes.White, view.Left + (view.Width - s2.Width) / 2, view.Top + view.Height / 3f + sz.Height);
        }
    }

    private static void Shadowed(Graphics g, string text, Font font, Brush brush, float x, float y)
    {
        g.DrawString(text, font, Brushes.Black, x + 2, y + 2);
        g.DrawString(text, font, brush, x, y);
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
            _hudFont.Dispose();
            _bigFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
