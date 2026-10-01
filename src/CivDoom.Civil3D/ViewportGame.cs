using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CivDoom.Engine;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;
using Gi = Autodesk.AutoCAD.GraphicsInterface;
using WinApp = System.Windows.Forms.Application;

namespace CivDoom.Civil3D;

/// <summary>
/// Plays the game inside the AutoCAD drawing window. Walls become temporary 3D faces standing on your
/// linework, the viewport camera walks through the level in perspective, and monsters, items and the gun
/// are drawn as pixel billboards. Everything is transient graphics: the drawing is never modified, and the
/// original view is restored on exit.
/// </summary>
internal sealed class ViewportGame
{
    private const double TargetFps = 40;
    private const double LensLength = 28;
    private const double MouseSensitivity = 0.0035;

    // Vertical half field of view for a 35mm-style lens (24mm film height).
    private static readonly double TanHalfFovV = 12.0 / LensLength;

    private readonly Document _doc;
    private readonly Editor _ed;
    private readonly Func<int, Game> _newGame;
    private readonly Func<GameContent> _reload;
    private Game _game;
    private int _seed;

    public ViewportGame(Document doc, Func<int, Game> newGame, Func<GameContent> reload)
    {
        _doc = doc;
        _ed = doc.Editor;
        _newGame = newGame;
        _reload = reload;
        _game = newGame(_seed);
    }

    public void Run()
    {
        using ViewTableRecord saved = _ed.GetCurrentView();
        using ViewTableRecord view = _ed.GetCurrentView();
        double aspect = saved.Height > 0 ? Math.Clamp(saved.Width / saved.Height, 0.5, 3) : 1.6;
        ObjectId style = FindVisualStyle(_doc.Database, "Realistic", "Shaded", "Conceptual");
        object? oldModeMacro = TryGetSysVar("MODEMACRO");

        var input = new InputFilter();
        using var scene = new Scene();
        WinApp.AddMessageFilter(input);
        Cursor.Hide();
        _ed.WriteMessage("\nCivDOOM: WASD move, mouse turn, click fire, 1-9/wheel weapons, Shift run, F5 reload files, Esc quit.\n");

        try
        {
            scene.BuildStatic(_game.Level, _game.Theme);
            var clock = Stopwatch.StartNew();
            double last = clock.Elapsed.TotalSeconds, lastHud = 0;
            string? lastMessage = null, lastObjective = null;
            var gi = new GameInput();

            while (true)
            {
                WinApp.DoEvents();
                if (input.Quit) break;

                double now = clock.Elapsed.TotalSeconds;
                double dt = now - last;
                if (dt < 1 / TargetFps)
                {
                    Thread.Sleep(1);
                    continue;
                }
                last = now;

                bool focused = GetForegroundWindow() == AcApp.MainWindow.Handle;
                ReadInput(input, gi, focused);

                foreach (Keys k in input.TakePressed())
                {
                    if (k == Keys.Return && _game.State is GameState.Dead or GameState.Won)
                    {
                        _game = _newGame(++_seed);
                        scene.Clear();
                        scene.BuildStatic(_game.Level, _game.Theme);
                    }
                    else if (k == Keys.F5)
                    {
                        GameContent c = _reload();
                        _game.ReloadMonsters(c.Monsters);
                        _game.ReloadWeapons(c.Weapons);
                        _game.ReloadLook(c.Themes, c.Player);
                        scene.Clear();
                        scene.BuildStatic(_game.Level, _game.Theme);
                    }
                    else if (k is >= Keys.D1 and <= Keys.D9) gi.SelectSlot = k - Keys.D0;
                    else if (k is >= Keys.NumPad1 and <= Keys.NumPad9) gi.SelectSlot = k - Keys.NumPad0;
                }

                _game.Update(focused ? dt : 0, gi);

                if (_game.Message != lastMessage)
                {
                    lastMessage = _game.Message;
                    if (lastMessage != null) _ed.WriteMessage($"\n{lastMessage}");
                    if (_game.State == GameState.Dead) _ed.WriteMessage("\nYOU DIED. Enter: play again, Esc: back to the drawing.");
                    if (_game.State == GameState.Won)
                    {
                        if (_game.CompletionTime > 0)
                        {
                            TimeSpan t = TimeSpan.FromSeconds(_game.CompletionTime);
                            _ed.WriteMessage($"\nLEVEL COMPLETE  |  Time {(int)t.TotalMinutes}:{t.Seconds:00}  |  Kills {_game.Kills}/{_game.Enemies.Count}" +
                                             $"  |  Bosses {_game.BossesKilled}/{_game.BossCount}  |  Damage taken {_game.DamageTaken}  |  {_game.Theme.Name}");
                        }
                        _ed.WriteMessage("\nEnter: play again, Esc: back to the drawing.");
                    }
                }

                if (_game.Objective != lastObjective)
                {
                    lastObjective = _game.Objective;
                    _ed.WriteMessage($"\nOBJECTIVE: {lastObjective}");
                }

                if (now - lastHud > 0.25)
                {
                    lastHud = now;
                    TrySetSysVar("MODEMACRO", HudText());
                }

                Camera(view, style, aspect, out Point3d eye, out Vector3d dir);
                scene.DrawDynamic(_game, eye, dir, aspect);
                _ed.SetCurrentView(view);
                _ed.UpdateScreen();
            }
        }
        finally
        {
            WinApp.RemoveMessageFilter(input);
            Cursor.Show();
            scene.Clear();
            _ed.SetCurrentView(saved);
            _ed.UpdateScreen();
            if (oldModeMacro != null) TrySetSysVar("MODEMACRO", oldModeMacro);
            _ed.WriteMessage("\nBack to the drawing.\n");
        }
    }

    private string HudText()
    {
        Player p = _game.Player;
        string ammo = p.ShotsLeft(p.Weapon) is { } n ? n.ToString() : "--";
        int alive = _game.Enemies.Count(e => e.IsAlive);
        string boss = _game.ActiveBoss is { } b ? $"   {b.Design.Name.ToUpperInvariant()} {Math.Max(0, b.Health) * 100 / b.Design.Health}%" : "";
        return $"CivDOOM   HEALTH {p.Health}%   {p.Weapon.Name.ToUpperInvariant()} {ammo}   HOSTILES {alive}/{_game.Enemies.Count}{boss}   |   {_game.Objective}";
    }

    private void ReadInput(InputFilter input, GameInput gi, bool focused)
    {
        bool D(Keys k) => input.IsDown(k);
        gi.Forward = D(Keys.W) || D(Keys.Up);
        gi.Back = D(Keys.S) || D(Keys.Down);
        gi.StrafeLeft = D(Keys.A);
        gi.StrafeRight = D(Keys.D);
        gi.TurnLeft = D(Keys.Left) || D(Keys.Q);
        gi.TurnRight = D(Keys.Right) || D(Keys.E);
        gi.Run = D(Keys.ShiftKey);
        gi.Fire = D(Keys.Space) || D(Keys.ControlKey) || input.MouseFire;
        gi.SelectSlot = 0;
        gi.CycleWeapon = input.TakeWheel();
        gi.MouseTurn = 0;

        if (!focused) return;
        // Mouse look: measure how far the cursor moved from the window centre, then put it back.
        if (GetWindowRect(AcApp.MainWindow.Handle, out Rect r))
        {
            var center = new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            gi.MouseTurn = -(Cursor.Position.X - center.X) * MouseSensitivity;
            Cursor.Position = center;
        }
    }

    private void Camera(ViewTableRecord view, ObjectId style, double aspect, out Point3d eye, out Vector3d dir)
    {
        Level level = _game.Level;
        Player p = _game.Player;
        double s = level.DrawingScale;
        (Vec2 camPos, double camAngle) = _game.Camera;
        Vec2 at = level.ToDrawing(camPos);
        double bob = _game.ShowPlayerCharacter ? 0 : Math.Abs(Math.Sin(p.BobPhase)) * 0.02 * p.BobAmount * s;
        eye = new Point3d(at.X, at.Y, Renderer.EyeHeight * s + bob);
        dir = new Vector3d(Math.Cos(camAngle), Math.Sin(camAngle), 0);
        Point3d target = eye + dir * s;

        view.Target = target;
        view.ViewDirection = eye - target;
        view.PerspectiveEnabled = true;
        view.LensLength = LensLength;
        view.ViewTwist = 0;
        view.CenterPoint = Point2d.Origin;
        view.Height = 2 * s * TanHalfFovV;
        view.Width = view.Height * aspect;
        view.FrontClipEnabled = false;
        view.BackClipEnabled = false;
        if (!style.IsNull) view.VisualStyleId = style;
    }

    private static ObjectId FindVisualStyle(Database db, params string[] names)
    {
        using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
        var dict = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
        foreach (string n in names)
            if (dict.Contains(n)) return dict.GetAt(n);
        return ObjectId.Null;
    }

    private static object? TryGetSysVar(string name)
    {
        try { return AcApp.GetSystemVariable(name); }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
    }

    private static void TrySetSysVar(string name, object value)
    {
        try { AcApp.SetSystemVariable(name, value); }
        catch (Autodesk.AutoCAD.Runtime.Exception) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    /// <summary>
    /// Captures keyboard and mouse while playing so keystrokes don't land on the command line and clicks
    /// don't select or zoom anything.
    /// </summary>
    private sealed class InputFilter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104,
            WM_SYSKEYUP = 0x105, WM_SYSCHAR = 0x106, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201,
            WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_MBUTTONDBLCLK = 0x209, WM_MOUSEWHEEL = 0x20A,
            WM_MOUSEHWHEEL = 0x20E, WM_CONTEXTMENU = 0x7B;

        private readonly HashSet<Keys> _down = new();
        private readonly Queue<Keys> _pressed = new();
        private int _wheel;

        public bool Quit { get; private set; }
        public bool MouseFire { get; private set; }

        public bool IsDown(Keys k) => _down.Contains(k);

        public IEnumerable<Keys> TakePressed()
        {
            while (_pressed.Count > 0) yield return _pressed.Dequeue();
        }

        public int TakeWheel()
        {
            int w = Math.Sign(-_wheel);
            _wheel = 0;
            return w;
        }

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_KEYDOWN or WM_SYSKEYDOWN:
                {
                    var k = (Keys)(int)(m.WParam.ToInt64() & 0xFFFF);
                    if (k == Keys.Escape) Quit = true;
                    if (_down.Add(k)) _pressed.Enqueue(k); // ignore auto-repeat
                    return true;
                }
                case WM_KEYUP or WM_SYSKEYUP:
                    _down.Remove((Keys)(int)(m.WParam.ToInt64() & 0xFFFF));
                    return true;
                case WM_CHAR or WM_SYSCHAR or WM_CONTEXTMENU or WM_MOUSEHWHEEL:
                    return true;
                case WM_LBUTTONDOWN or WM_LBUTTONDBLCLK:
                    MouseFire = true;
                    return true;
                case WM_LBUTTONUP:
                    MouseFire = false;
                    return true;
                case WM_MOUSEWHEEL:
                    _wheel += (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    return true;
                case >= WM_MOUSEMOVE and <= WM_MBUTTONDBLCLK:
                    return true;
            }
            return false;
        }
    }
}

/// <summary>A flat four-cornered face, corners in order.</summary>
internal readonly record struct Quad(Point3d A, Point3d B, Point3d C, Point3d D);

/// <summary>
/// All faces of one colour, drawn as a single transient mesh. If AutoCAD refuses the mesh, falls back to
/// individual 3D faces.
/// </summary>
internal sealed class QuadBatch : IDisposable
{
    private static readonly IntegerCollection AllViewports = new();
    private static bool _useFaces;

    private readonly int _rgb;
    private readonly byte _alpha;
    private SubDMesh? _mesh;
    private bool _meshShown;
    private readonly List<Face> _faces = new();
    private int _facesShown;

    public QuadBatch(int rgb, byte alpha = 255)
    {
        _rgb = rgb;
        _alpha = alpha;
    }

    private static Gi.TransientManager Tm => Gi.TransientManager.CurrentTransientManager;

    private AcColor Color => AcColor.FromRgb((byte)(_rgb >> 16), (byte)(_rgb >> 8), (byte)_rgb);

    public void Set(List<Quad> quads)
    {
        if (!_useFaces)
        {
            try
            {
                SetMesh(quads);
                return;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                _useFaces = true;
                HideMesh();
            }
        }
        SetFaces(quads);
    }

    private void SetMesh(List<Quad> quads)
    {
        if (quads.Count == 0)
        {
            HideMesh();
            return;
        }

        var pts = new Point3dCollection();
        var faces = new Int32Collection(quads.Count * 5);
        foreach (Quad q in quads)
        {
            int i = pts.Count;
            pts.Add(q.A); pts.Add(q.B); pts.Add(q.C); pts.Add(q.D);
            faces.Add(4); faces.Add(i); faces.Add(i + 1); faces.Add(i + 2); faces.Add(i + 3);
        }

        if (_mesh == null)
        {
            _mesh = new SubDMesh { Color = Color };
            if (_alpha < 255) _mesh.Transparency = new Transparency(_alpha);
        }
        _mesh.SetSubDMesh(pts, faces, 0);
        if (_meshShown) Tm.UpdateTransient(_mesh, AllViewports);
        else _meshShown = Tm.AddTransient(_mesh, Gi.TransientDrawingMode.Main, 128, AllViewports);
    }

    private void HideMesh()
    {
        if (_mesh != null && _meshShown) Tm.EraseTransient(_mesh, AllViewports);
        _meshShown = false;
    }

    private void SetFaces(List<Quad> quads)
    {
        for (int i = 0; i < quads.Count; i++)
        {
            Quad q = quads[i];
            if (i < _faces.Count)
            {
                Face f = _faces[i];
                f.SetVertexAt(0, q.A); f.SetVertexAt(1, q.B); f.SetVertexAt(2, q.C); f.SetVertexAt(3, q.D);
                if (i < _facesShown) Tm.UpdateTransient(f, AllViewports);
                else Tm.AddTransient(f, Gi.TransientDrawingMode.Main, 128, AllViewports);
            }
            else
            {
                var f = new Face(q.A, q.B, q.C, q.D, false, false, false, false) { Color = Color };
                if (_alpha < 255) f.Transparency = new Transparency(_alpha);
                _faces.Add(f);
                Tm.AddTransient(f, Gi.TransientDrawingMode.Main, 128, AllViewports);
            }
        }
        for (int i = quads.Count; i < _facesShown; i++) Tm.EraseTransient(_faces[i], AllViewports);
        _facesShown = quads.Count;
    }

    public void Dispose()
    {
        HideMesh();
        _mesh?.Dispose();
        for (int i = 0; i < _facesShown; i++) Tm.EraseTransient(_faces[i], AllViewports);
        foreach (Face f in _faces) f.Dispose();
        _faces.Clear();
        _facesShown = 0;
    }
}

/// <summary>Turns the game state into coloured quads in drawing coordinates.</summary>
internal sealed class Scene : IDisposable
{
    private const double SpriteDrawDistance = 30;
    private const double HudDistance = 0.25; // in wall heights, in front of the eye
    private const int MaxRectsPerSprite = 250;

    private readonly List<QuadBatch> _static = new();
    private readonly Dictionary<(int Rgb, byte Alpha), QuadBatch> _dynamic = new();
    private readonly Dictionary<(int, byte), List<Quad>> _frame = new();

    /// <summary>Walls (one mesh per colour, in the theme's colours) and a floor under the level.</summary>
    public void BuildStatic(Level level, ThemeDesign theme)
    {
        double s = level.DrawingScale, h = s;
        var byColor = new Dictionary<int, List<Quad>>();
        foreach (Wall w in level.Walls)
        {
            if (w.IsGate) continue; // gates are drawn each frame, so they can open
            Vec2 a = level.ToDrawing(w.A), b = level.ToDrawing(w.B);
            int wallColor = theme.WallBase(w);
            if (!byColor.TryGetValue(wallColor, out List<Quad>? list)) byColor[wallColor] = list = new List<Quad>();
            list.Add(new Quad(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0), new Point3d(b.X, b.Y, h), new Point3d(a.X, a.Y, h)));
        }
        foreach ((int rgb, List<Quad> quads) in byColor)
        {
            var batch = new QuadBatch(rgb);
            batch.Set(quads);
            _static.Add(batch);
        }

        Vec2 min = level.ToDrawing(level.Index.Min - new Vec2(2, 2)), max = level.ToDrawing(level.Index.Max + new Vec2(2, 2));
        double z = -0.002 * s;
        var floor = new QuadBatch(theme.FloorColor);
        floor.Set(new List<Quad>
        {
            new(new Point3d(min.X, min.Y, z), new Point3d(max.X, min.Y, z), new Point3d(max.X, max.Y, z), new Point3d(min.X, max.Y, z)),
        });
        _static.Add(floor);
    }

    public void DrawDynamic(Game game, Point3d eye, Vector3d dir, double aspect)
    {
        foreach (List<Quad> list in _frame.Values) list.Clear();

        Level level = game.Level;
        double s = level.DrawingScale;
        Vector3d right = new(dir.Y, -dir.X, 0);
        Vec2 eye2 = new(eye.X, eye.Y);

        bool Visible(Vec2 worldPos, out Vec2 at)
        {
            at = level.ToDrawing(worldPos);
            Vec2 rel = (at - eye2) / s;
            double depth = rel.X * dir.X + rel.Y * dir.Y;
            return depth > 0.05 && rel.Length < SpriteDrawDistance;
        }

        // Locked gates: yellow/black hazard stripes.
        foreach (Wall gate in level.Gates)
        {
            if (gate.IsOpen) continue;
            Vec2 a = level.ToDrawing(gate.A), b = level.ToDrawing(gate.B);
            int stripes = Math.Max(2, (int)Math.Round(gate.Length * 6));
            for (int i = 0; i < stripes; i++)
            {
                Vec2 p0 = a + (b - a) * (i / (double)stripes), p1 = a + (b - a) * ((i + 1) / (double)stripes);
                Add(i % 2 == 0 ? 0xE8C020 : 0x202020, 255, new Quad(new Point3d(p0.X, p0.Y, 0), new Point3d(p1.X, p1.Y, 0),
                    new Point3d(p1.X, p1.Y, s), new Point3d(p0.X, p0.Y, s)));
            }
        }

        // Finish line: a chequered pad (red while locked) and a flag or barrier.
        if (level.Exit is { } exit)
        {
            Vec2 c = level.ToDrawing(exit);
            double r = Game.ExitRadius * s, cell = 2 * r / 6, z = 0.003 * s;
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                {
                    double x0 = c.X - r + i * cell, y0 = c.Y - r + j * cell;
                    int col = (i + j) % 2 == 0 ? 0x101010 : game.ExitOpen ? 0xF0F0F0 : 0xC02020;
                    Add(col, 255, new Quad(new Point3d(x0, y0, z), new Point3d(x0 + cell, y0, z),
                        new Point3d(x0 + cell, y0 + cell, z), new Point3d(x0, y0 + cell, z)));
                }
            if (Visible(exit, out Vec2 at))
            {
                if (game.ExitOpen) Billboard(at, Art.FinishFlagSprite, 0.75 * s, 0, right);
                else Billboard(at, Art.LockedGateSprite, 0.4 * s, 0, right);
            }
        }

        foreach (Pickup pk in game.Pickups)
        {
            if (pk.Taken || !Visible(pk.Position, out Vec2 at)) continue;
            if (pk.Weapon is { } w) Billboard(at, w.Pickup, w.PickupSize * s, 0, right);
            else Billboard(at, pk.Kind == PickupKind.Health ? Art.MedkitSprite : Art.AmmoSprite, 0.3 * s, 0, right);
        }

        foreach (Enemy e in game.Enemies)
        {
            if (!Visible(e.Position, out Vec2 at)) continue;
            MonsterDesign d = e.Design;
            SpriteImage img = e.State switch
            {
                EnemyState.Dead => d.Dead,
                EnemyState.Attack => d.Attack,
                EnemyState.Chase => ((int)e.WalkPhase & 1) == 0 ? d.Idle : d.Walk,
                _ => d.Idle,
            };
            Billboard(at, img, img.Height * d.PixelSize * s, (e.IsAlive ? d.FloatHeight : 0) * s, right, e.PainTime > 0 ? 0xFFFFFF : -1);
        }

        foreach (Projectile pr in game.Projectiles)
        {
            if (!Visible(pr.Position, out Vec2 at)) continue;
            Billboard(at, pr.Sprite ?? Art.FireballSprite, pr.Size * s, (pr.FromPlayer ? 0.36 : 0.3) * s, right);
        }

        if (game.ShowPlayerCharacter && Visible(game.Player.Position, out Vec2 runner))
        {
            SpriteImage frame = game.PlayerFrame;
            Billboard(runner, frame, game.PlayerLook.Size * frame.Height / game.PlayerLook.Idle.Height * s, 0, right);
        }

        foreach (Effect fx in game.Effects)
        {
            if (!Visible(fx.Position, out Vec2 at)) continue;
            double size = fx.Size * (0.5 + fx.Age / fx.Duration);
            Billboard(at, Art.FireballSprite, size * s, Math.Max(0, 0.35 - size / 2) * s, right);
        }

        DrawHud(game, eye, dir, right, aspect, s);

        foreach ((var key, List<Quad> quads) in _frame)
        {
            if (!_dynamic.TryGetValue(key, out QuadBatch? batch)) _dynamic[key] = batch = new QuadBatch(key.Item1, key.Item2);
            batch.Set(quads);
        }
    }

    /// <summary>Gun, crosshair, health/ammo bars and damage flash, placed just in front of the camera.</summary>
    private void DrawHud(Game game, Point3d eye, Vector3d dir, Vector3d right, double aspect, double s)
    {
        Player p = game.Player;
        double d = HudDistance * s;
        double halfH = d * TanHalfFovV(), halfW = halfH * aspect;
        Point3d center = eye + dir * d;
        Vector3d up = Vector3d.ZAxis;

        void Rect(double x0, double y0, double x1, double y1, int rgb, byte alpha = 255) =>
            Add(rgb, alpha, new Quad(center + right * x0 + up * y0, center + right * x1 + up * y0,
                                     center + right * x1 + up * y1, center + right * x0 + up * y1));

        if (game.ShowPlayerCharacter || game.Fade > 0)
        {
            // Cutscene: no gun or bars, just the fade to black at the end.
            if (game.Fade > 0) Rect(-halfW * 1.05, -halfH * 1.05, halfW * 1.05, halfH * 1.05, 0x000000, (byte)Math.Clamp(game.Fade * 255, 0, 255));
            return;
        }

        if (game.State != GameState.Dead)
        {
            WeaponDesign w = p.Weapon;
            bool firing = p.MuzzleFlashTime > 0;
            SpriteImage original = firing ? w.Fire : w.Hand;
            SpriteImage img = original.Simplified(MaxRectsPerSprite * 2);
            // Size relative to [hand]; a simplified picture has bigger pixels to cover the same area.
            double px = 0.55 * halfH / w.Hand.Height * original.Height / img.Height;
            double bobX = Math.Sin(p.BobPhase) * 0.04 * halfH * p.BobAmount;
            double bobY = -Math.Abs(Math.Cos(p.BobPhase)) * 0.03 * halfH * p.BobAmount - (firing && !w.IsMelee ? 0.03 * halfH : 0);
            double left = -img.Width * px / 2 + bobX;
            double bottom = -0.98 * halfH + bobY;
            foreach (SpriteRect r in img.Rectangles())
            {
                double x0 = left + r.X * px, x1 = x0 + r.W * px;
                double y1 = bottom + (img.Height - r.Y) * px, y0 = y1 - r.H * px;
                Rect(x0, y0, x1, y1, r.Color);
            }
        }

        if (game.ActiveBoss is { } boss)
        {
            double frac = Math.Clamp((double)boss.Health / boss.Design.Health, 0, 1);
            double bw = 0.5 * halfW, y0 = 0.82 * halfH, y1 = 0.88 * halfH;
            Rect(-bw, y0, bw, y1, 0x400808);
            Rect(-bw, y0, -bw + 2 * bw * frac, y1, 0xFF2020);
        }

        // Crosshair.
        double c = 0.012 * halfH, t = 0.003 * halfH;
        Rect(-c, -t, -c / 3, t, 0x9CFF9C); Rect(c / 3, -t, c, t, 0x9CFF9C);
        Rect(-t, -c, t, -c / 3, 0x9CFF9C); Rect(-t, c / 3, t, c, 0x9CFF9C);

        // Health (left) and ammo (right) bars along the bottom.
        double barW = 0.35 * halfW, y0b = -0.9 * halfH, y1b = -0.84 * halfH;
        Rect(-0.92 * halfW, y0b, -0.92 * halfW + barW, y1b, 0x401010);
        Rect(-0.92 * halfW, y0b, -0.92 * halfW + barW * p.Health / Player.MaxHealth, y1b, 0xE03030);
        if (p.ShotsLeft(p.Weapon) is { } shots)
        {
            double frac = Math.Clamp((double)shots / Math.Max(1, p.Weapon.MaxAmmo), 0, 1);
            Rect(0.92 * halfW - barW, y0b, 0.92 * halfW, y1b, 0x403010);
            Rect(0.92 * halfW - barW * frac, y0b, 0.92 * halfW, y1b, 0xE8C040);
        }

        if (p.DamageFlash > 0 || game.State == GameState.Dead)
        {
            // Pushed slightly further out so it sits behind the gun.
            double f = 1.05;
            byte alpha = game.State == GameState.Dead ? (byte)110 : (byte)Math.Clamp(p.DamageFlash * 120, 20, 120);
            Rect(-halfW * f, -halfH * f, halfW * f, halfH * f, 0xFF2020, alpha);
        }
    }

    private static double TanHalfFovV() => 12.0 / 28.0;

    /// <summary>Draws a sprite standing upright at <paramref name="at"/>, facing the camera.</summary>
    private void Billboard(Vec2 at, SpriteImage img, double height, double lift, Vector3d right, int tint = -1)
    {
        img = img.Simplified(MaxRectsPerSprite); // photos would otherwise be thousands of faces
        double px = height / img.Height;
        var basePt = new Point3d(at.X, at.Y, lift);
        foreach (SpriteRect r in img.Rectangles())
        {
            double x0 = (r.X - img.Width / 2.0) * px, x1 = x0 + r.W * px;
            double z1 = (img.Height - r.Y) * px, z0 = z1 - r.H * px;
            int rgb = tint >= 0 ? Blend(r.Color, tint) : r.Color;
            Add(rgb, 255, new Quad(basePt + right * x0 + Vector3d.ZAxis * z0, basePt + right * x1 + Vector3d.ZAxis * z0,
                                   basePt + right * x1 + Vector3d.ZAxis * z1, basePt + right * x0 + Vector3d.ZAxis * z1));
        }
    }

    private static int Blend(int a, int b)
    {
        int r = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) / 2;
        int g = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) / 2;
        int bl = ((a & 0xFF) + (b & 0xFF)) / 2;
        return (r << 16) | (g << 8) | bl;
    }

    private void Add(int rgb, byte alpha, Quad q)
    {
        if (!_frame.TryGetValue((rgb, alpha), out List<Quad>? list)) _frame[(rgb, alpha)] = list = new List<Quad>();
        list.Add(q);
    }

    public void Clear()
    {
        foreach (QuadBatch b in _static) b.Dispose();
        foreach (QuadBatch b in _dynamic.Values) b.Dispose();
        _static.Clear();
        _dynamic.Clear();
    }

    public void Dispose() => Clear();
}
