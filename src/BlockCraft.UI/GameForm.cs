using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BlockCraft.Core;

namespace BlockCraft.UI;

/// <summary>
/// The game window: runs the update/render loop, captures mouse-look and keyboard input and
/// draws the HUD. It has no Autodesk dependencies so it also runs outside Civil 3D.
/// </summary>
public sealed class GameForm : Form
{
    private const double BreakRepeatSeconds = 0.22;

    private readonly Game _game;
    private readonly InputState _input = new();
    private readonly HashSet<Keys> _down = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Font _hudFont = new("Consolas", 10f, FontStyle.Bold);
    private readonly Font _bigFont = new("Segoe UI", 14f, FontStyle.Bold);

    private Bitmap? _frame;
    private int[] _pixels = Array.Empty<int>();
    private int _pixelScale = 2;
    private bool _captured;
    private bool _showHelp = true;
    private bool _leftHeld, _rightHeld;
    private double _leftRepeat, _rightRepeat;
    private double _lastTime;
    private double _fps;

    public GameForm(Game game, string title)
    {
        _game = game;
        Text = title;
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BackColor = Color.Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

        _timer.Tick += (_, _) => Tick();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _lastTime = _clock.Elapsed.TotalSeconds;
        _timer.Start();
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
            _frame?.Dispose();
            _hudFont.Dispose();
            _bigFont.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- loop

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTime;
        _lastTime = now;
        if (dt > 0) _fps = _fps * 0.9 + (1 / dt) * 0.1;

        _input.Forward = IsDown(Keys.W) || IsDown(Keys.Up);
        _input.Back = IsDown(Keys.S) || IsDown(Keys.Down);
        _input.Left = IsDown(Keys.A) || IsDown(Keys.Left);
        _input.Right = IsDown(Keys.D) || IsDown(Keys.Right);
        _input.Jump = IsDown(Keys.Space);
        _input.Sneak = IsDown(Keys.ShiftKey);
        _input.Sprint = IsDown(Keys.ControlKey);

        // Holding a mouse button repeats the action, like creative mode.
        if (_leftHeld && (_leftRepeat -= dt) <= 0) { _input.BreakPressed = true; _leftRepeat = BreakRepeatSeconds; }
        if (_rightHeld && (_rightRepeat -= dt) <= 0) { _input.PlacePressed = true; _rightRepeat = BreakRepeatSeconds; }

        if (!_captured)
        {
            _input.MouseDX = _input.MouseDY = 0;
            _input.BreakPressed = _input.PlacePressed = false;
        }

        _game.Update(_input, dt);
        RenderFrame();
        Invalidate();
    }

    private bool IsDown(Keys key) => _down.Contains(key);

    private void RenderFrame()
    {
        int w = Math.Max(1, ClientSize.Width / _pixelScale);
        int h = Math.Max(1, ClientSize.Height / _pixelScale);
        if (_frame == null || _frame.Width != w || _frame.Height != h)
        {
            _frame?.Dispose();
            _frame = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            _pixels = new int[w * h];
        }

        _game.Render(_pixels, w, h);

        var data = _frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(_pixels, y * w, data.Scan0 + y * data.Stride, w);
        }
        finally
        {
            _frame.UnlockBits(data);
        }
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_frame == null)
        {
            g.Clear(Color.Black);
            return;
        }

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_frame, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawCrosshair(g);
        DrawHotbar(g);
        DrawStatus(g);
        if (!_captured || _showHelp) DrawHelp(g);
    }

    private void DrawCrosshair(Graphics g)
    {
        int cx = ClientSize.Width / 2, cy = ClientSize.Height / 2;
        using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 4);
        using var pen = new Pen(Color.White, 2);
        foreach (var p in new[] { shadow, pen })
        {
            g.DrawLine(p, cx - 10, cy, cx + 10, cy);
            g.DrawLine(p, cx, cy - 10, cx, cy + 10);
        }
    }

    private void DrawHotbar(Graphics g)
    {
        var blocks = Blocks.Placeable;
        const int slot = 44, gap = 4;
        int total = blocks.Length * (slot + gap) - gap;
        int x0 = (ClientSize.Width - total) / 2;
        int y0 = ClientSize.Height - slot - 16;

        using var bg = new SolidBrush(Color.FromArgb(150, 20, 20, 20));
        g.FillRectangle(bg, x0 - 6, y0 - 6, total + 12, slot + 12);

        for (int i = 0; i < blocks.Length; i++)
        {
            var info = Blocks.Info(blocks[i]);
            int x = x0 + i * (slot + gap);
            DrawBlockIcon(g, info, x + 6, y0 + 6, slot - 12);

            bool selected = i == _game.SelectedSlot;
            using var border = new Pen(selected ? Color.White : Color.FromArgb(120, 200, 200, 200), selected ? 3 : 1);
            g.DrawRectangle(border, x, y0, slot, slot);

            if (i < 9)
                g.DrawString((i + 1).ToString(), _hudFont, Brushes.White, x + 2, y0 + 1);
        }

        string name = Blocks.Info(_game.SelectedBlock).Name;
        var size = g.MeasureString(name, _bigFont);
        DrawShadowedString(g, name, _bigFont, (ClientSize.Width - size.Width) / 2, y0 - size.Height - 10);
    }

    /// <summary>Draws a small isometric cube in the block's top/side colours.</summary>
    private static void DrawBlockIcon(Graphics g, BlockInfo info, int x, int y, int s)
    {
        float h = s / 2f, q = s / 4f;
        PointF top = new(x + h, y), right = new(x + s, y + q), bottom = new(x + h, y + h), left = new(x, y + q);
        PointF lowLeft = new(x, y + s - q), lowMid = new(x + h, y + s), lowRight = new(x + s, y + s - q);

        using var topBrush = new SolidBrush(Color.FromArgb(Textures.Mul(info.TopColor, 1f) | unchecked((int)0xFF000000)));
        using var leftBrush = new SolidBrush(Color.FromArgb(Textures.Mul(info.SideColor, 0.8f) | unchecked((int)0xFF000000)));
        using var rightBrush = new SolidBrush(Color.FromArgb(Textures.Mul(info.SideColor, 0.62f) | unchecked((int)0xFF000000)));
        g.FillPolygon(topBrush, new[] { top, right, bottom, left });
        g.FillPolygon(leftBrush, new[] { left, bottom, lowMid, lowLeft });
        g.FillPolygon(rightBrush, new[] { bottom, right, lowRight, lowMid });
    }

    private void DrawStatus(Graphics g)
    {
        var p = _game.Player;
        var (ex, ny, el) = _game.PlayerDrawingPosition;
        string mode = p.Flying ? "FLY" : p.InWater ? "SWIM" : "WALK";
        string text =
            $"BlockCraft  {_fps,5:F0} fps   [{mode}]\n" +
            $"World: {_game.World.Source}\n" +
            $"Drawing  X {ex:F2}  Y {ny:F2}  Z {el:F2}   facing {_game.Heading}\n" +
            $"Placed {_game.BlocksPlaced}   Broken {_game.BlocksBroken}   Render 1/{_pixelScale}";
        DrawShadowedString(g, text, _hudFont, 10, 10);
    }

    private void DrawHelp(Graphics g)
    {
        string text = _captured
            ? "F1 hide help"
            : "Click to play";
        string keys =
            "WASD / arrows  move          Mouse      look\n" +
            "Space          jump / up     Shift      fly down\n" +
            "Ctrl           sprint        F          toggle fly\n" +
            "Left click     break         Right click place\n" +
            "Middle click   pick block    1-9 / wheel choose block\n" +
            "+ / -          render quality  F1       help\n" +
            "Esc            release mouse / quit (returns to the drawing)";

        var titleSize = g.MeasureString(text, _bigFont);
        var keySize = g.MeasureString(keys, _hudFont);
        float w = Math.Max(titleSize.Width, keySize.Width) + 32;
        float h = titleSize.Height + keySize.Height + 40;
        float x = (ClientSize.Width - w) / 2, y = ClientSize.Height * 0.18f;

        using var bg = new SolidBrush(Color.FromArgb(170, 10, 10, 14));
        g.FillRectangle(bg, x, y, w, h);
        g.DrawString(text, _bigFont, Brushes.Gold, x + (w - titleSize.Width) / 2, y + 12);
        g.DrawString(keys, _hudFont, Brushes.White, x + 16, y + titleSize.Height + 24);
    }

    private static void DrawShadowedString(Graphics g, string s, Font font, float x, float y)
    {
        using var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.DrawString(s, font, shadow, x + 1.5f, y + 1.5f);
        g.DrawString(s, font, Brushes.White, x, y);
    }

    // ---------------------------------------------------------------- input

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Space or Keys.Tab
        || base.IsInputKey(keyData);

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Keep arrows/space/tab/enter as game keys rather than dialog navigation.
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = true;
        e.SuppressKeyPress = true;
        bool repeat = !_down.Add(e.KeyCode);

        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_captured) ReleaseMouse();
                else Close();
                break;
            case Keys.F when !repeat:
                _input.ToggleFly = true;
                break;
            case Keys.F1 when !repeat:
                _showHelp = !_showHelp;
                break;
            case Keys.Oemplus or Keys.Add when !repeat:
                _pixelScale = Math.Max(1, _pixelScale - 1);
                break;
            case Keys.OemMinus or Keys.Subtract when !repeat:
                _pixelScale = Math.Min(6, _pixelScale + 1);
                break;
            case >= Keys.D1 and <= Keys.D9:
                _input.SelectSlot = e.KeyCode - Keys.D1;
                break;
            case >= Keys.NumPad1 and <= Keys.NumPad9:
                _input.SelectSlot = e.KeyCode - Keys.NumPad1;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _down.Remove(e.KeyCode);
        e.Handled = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _down.Clear();
        _leftHeld = _rightHeld = false;
        ReleaseMouse();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_captured)
        {
            CaptureMouse();
            return;
        }

        switch (e.Button)
        {
            case MouseButtons.Left:
                _leftHeld = true;
                _input.BreakPressed = true;
                _leftRepeat = BreakRepeatSeconds * 1.5;
                break;
            case MouseButtons.Right:
                _rightHeld = true;
                _input.PlacePressed = true;
                _rightRepeat = BreakRepeatSeconds * 1.5;
                break;
            case MouseButtons.Middle:
                _input.PickPressed = true;
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) _leftHeld = false;
        if (e.Button == MouseButtons.Right) _rightHeld = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _input.Scroll += e.Delta > 0 ? -1 : 1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_captured) return;

        var center = ClientCenterOnScreen();
        var pos = Cursor.Position;
        int dx = pos.X - center.X, dy = pos.Y - center.Y;
        if (dx == 0 && dy == 0) return; // the event caused by re-centring

        _input.MouseDX += dx;
        _input.MouseDY += dy;
        Cursor.Position = center;
    }

    private Point ClientCenterOnScreen() => PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));

    private void CaptureMouse()
    {
        if (_captured) return;
        _captured = true;
        _showHelp = false;
        Cursor.Position = ClientCenterOnScreen();
        Cursor.Clip = RectangleToScreen(ClientRectangle);
        Cursor.Hide();
    }

    private void ReleaseMouse()
    {
        if (!_captured) return;
        _captured = false;
        _leftHeld = _rightHeld = false;
        Cursor.Clip = Rectangle.Empty;
        Cursor.Show();
    }
}
