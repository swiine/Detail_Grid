using System.Diagnostics;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using BlockCraft.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using GsProjection = Autodesk.AutoCAD.GraphicsSystem.Projection;
using Cursor = System.Windows.Forms.Cursor;
using WinTimer = System.Windows.Forms.Timer;

namespace BlockCraft.Civil3D;

/// <summary>
/// Plays BlockCraft directly in the AutoCAD viewport - no separate window. Keyboard and mouse input
/// is intercepted from AutoCAD's message loop, the player's eye drives the viewport's perspective
/// camera, and breaking/placing edits the BC_* block references in the drawing as you go.
/// Esc (or starting any other command) swaps back to normal drafting.
/// </summary>
internal sealed class ViewportPlay
{
    private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_CHAR = 0x102, WM_DEADCHAR = 0x103;
    private const int WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_SYSCHAR = 0x106;
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    private const int WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207;
    private const int WM_MOUSEWHEEL = 0x20A, WM_MOUSELAST = 0x20E;
    private const int VK_TAB = 0x09, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_ESCAPE = 0x1B, VK_SPACE = 0x20;
    private const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_F5 = 0x74;
    private const double CameraDistance = 4.5;   // blocks behind the player in third person
    private const double ShoulderOffset = 0.7;   // blocks to the right, so the figure doesn't hide the aim point
    private const double RepeatSeconds = 0.22;
    private const double FieldOfViewDegrees = 75;

    public static ViewportPlay? Active { get; private set; }

    private readonly Document _doc;
    private readonly DrawingWorld _dw;
    private readonly Game _game;
    private readonly InputState _input = new();
    private readonly HashSet<int> _keys = new();
    private readonly WinTimer _timer = new() { Interval = 30 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Entity> _highlight = new();
    private readonly CharacterModel _character;
    private bool _thirdPerson = true;
    private (double X, double Y, double Z, double Yaw, double Pitch)? _lastPose;

    private ViewTableRecord? _view;
    private ViewTableRecord? _savedView;
    private double _lastTime;
    private Point3d? _lastEye, _lastTarget;
    private bool _captured;
    private bool _stopRequested;
    private bool _stopped;
    private bool _leftHeld, _rightHeld;
    private double _leftRepeat, _rightRepeat;
    private (int, int, int)? _highlighted;
    private BlockType _announcedBlock;
    private bool _announcedFly;

    private ViewportPlay(Document doc, DrawingWorld dw)
    {
        _doc = doc;
        _dw = dw;
        _game = new Game(dw.World);
        _character = new CharacterModel(dw.World.Mapping);
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Starts play mode. Must be called from a command (it switches the visual style).</summary>
    public static void Start(Document doc, DrawingWorld dw)
    {
        Active?.Stop();
        var play = new ViewportPlay(doc, dw);
        Active = play;
        play.Begin();
    }

    private Editor Ed => _doc.Editor;

    private void Begin()
    {
        // Build the block lookup now so the first click doesn't stutter.
        using (var tr = _doc.Database.TransactionManager.StartTransaction())
        {
            _dw.Index(tr);
            tr.Commit();
        }

        _savedView = Ed.GetCurrentView();
        _view = Ed.GetCurrentView();
        _view.PerspectiveEnabled = true;
        _view.LensLength = 22; // wide angle, close to a game's field of view
        _view.ViewTwist = 0;

        try
        {
            Ed.Command("._VSCURRENT", "_R"); // realistic shading reads best in first person
        }
        catch (System.Exception)
        {
            // Visual style is cosmetic; keep going with whatever is current.
        }

        CoreApp.PreTranslateMessage += OnMessage;
        _doc.CommandWillStart += OnCommandWillStart;
        AcApp.DocumentManager.DocumentToBeDeactivated += OnDocumentChanging;
        AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentChanging;

        _lastTime = _clock.Elapsed.TotalSeconds;
        _announcedBlock = _game.SelectedBlock;
        UpdateCamera(writeDrawingView: true); // one regen to switch into perspective
        UpdateCharacter(0);
        Capture();
        _timer.Start();

        Ed.WriteMessage(
            "\nBlockCraft PLAY MODE - you are in the viewport." +
            "\n  WASD/arrows move, mouse look, Space jump, Ctrl sprint, F fly (Space up / Shift down)" +
            "\n  Left click break, right click place, middle click pick, 1-9 or wheel choose block" +
            "\n  V or F5 switch between third person (see your character) and first person" +
            "\n  Tab pause (free the mouse), click the drawing to resume, Esc back to drafting" +
            $"\n  Block: {Blocks.Info(_game.SelectedBlock).Name}\n");
    }

    // ------------------------------------------------------------------ loop

    private void Tick()
    {
        if (_stopRequested)
        {
            Stop();
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTime;
        _lastTime = now;

        if (_captured && GetForegroundWindow() != AcApp.MainWindow.Handle)
            Release(); // alt-tabbed away: give the mouse back

        if (!_captured)
            return; // paused: AutoCAD behaves normally until the drawing is clicked again

        _input.Forward = Down('W') || Down(VK_UP);
        _input.Back = Down('S') || Down(VK_DOWN);
        _input.Left = Down('A') || Down(VK_LEFT);
        _input.Right = Down('D') || Down(VK_RIGHT);
        _input.Jump = Down(VK_SPACE);
        _input.Sneak = Down(VK_SHIFT);
        _input.Sprint = Down(VK_CONTROL);

        if (_leftHeld && (_leftRepeat -= dt) <= 0) { _input.BreakPressed = true; _leftRepeat = RepeatSeconds; }
        if (_rightHeld && (_rightRepeat -= dt) <= 0) { _input.PlacePressed = true; _rightRepeat = RepeatSeconds; }

        try
        {
            double oldX = _game.Player.X, oldZ = _game.Player.Z;
            _game.Update(_input, dt);
            ApplyEdits();
            UpdateCharacter(Math.Sqrt(Math.Pow(_game.Player.X - oldX, 2) + Math.Pow(_game.Player.Z - oldZ, 2)));
            UpdateCamera();
            UpdateHighlight();
            Announce();
        }
        catch (System.Exception ex)
        {
            Ed.WriteMessage($"\nBlockCraft stopped: {ex.Message}");
            Stop();
        }
    }

    private bool Down(int vk) => _keys.Contains(vk);

    /// <summary>Writes blocks broken or placed this frame into the drawing.</summary>
    private void ApplyEdits()
    {
        var world = _dw.World;
        if (world.Edits.Count == 0) return;

        var changed = world.Edits.Keys.Select(k => (k.X, k.Y, k.Z)).ToList();
        world.ClearEdits();
        using (_doc.LockDocument())
        using (var tr = _doc.Database.TransactionManager.StartTransaction())
        {
            _dw.Reconcile(tr, changed);
            tr.Commit();
        }
        _highlighted = null; // geometry changed under the highlight
    }

    /// <summary>
    /// Points the viewport camera along the player's eye. Normal frames move the camera through the
    /// graphics system (a redraw only); writing the drawing's saved view would regenerate the model
    /// every frame, so that is done only when entering play mode or as a fallback.
    /// </summary>
    private void UpdateCamera(bool writeDrawingView = false)
    {
        if (_view == null) return;
        var map = _dw.World.Mapping;
        var (cam, look) = CameraInGame();
        var p = _game.Player;
        var (ex, ey, ez) = map.ToDrawing(cam.X, cam.Y, cam.Z);
        // Aim at a point along the player's own line of sight, so the screen centre is the aim point.
        var (tx, ty, tz) = map.ToDrawing(p.X + look.X * Game.Reach, p.EyeY + look.Y * Game.Reach, p.Z + look.Z * Game.Reach);

        var eye = new Point3d(ex, ey, ez);
        var target = new Point3d(tx, ty, tz);
        double distance = eye.DistanceTo(target);

        // Standing still and not looking around: leave the screen alone.
        double tol = map.CellSize * 1e-4;
        if (!writeDrawingView && _lastEye is { } le && _lastTarget is { } lt
            && le.DistanceTo(eye) < tol && lt.DistanceTo(target) < tol)
            return;
        _lastEye = eye;
        _lastTarget = target;

        _view.Target = target;
        _view.ViewDirection = eye - target; // the perspective camera sits at target + direction
        _view.CenterPoint = Point2d.Origin;

        if (!writeDrawingView)
        {
            var gsView = _doc.GraphicsManager.GetCurrentAcGsView(Convert.ToInt32(AcApp.GetSystemVariable("CVPORT")));
            if (gsView != null)
            {
                var r = DrawingRectOnScreen();
                double aspect = Math.Max(1, r.Right - r.Left) / (double)Math.Max(1, r.Bottom - r.Top);
                double fieldWidth = 2 * distance * Math.Tan(FieldOfViewDegrees * Math.PI / 360);
                gsView.SetView(eye, target, Vector3d.ZAxis, fieldWidth, fieldWidth / aspect, GsProjection.Perspective);
                gsView.Update();
                return;
            }
        }

        using (_doc.LockDocument())
            Ed.SetCurrentView(_view);
    }

    /// <summary>
    /// Camera position in game units. First person: the player's eyes. Third person: behind and
    /// over the right shoulder, pulled in if a block is in the way.
    /// </summary>
    private ((double X, double Y, double Z) Cam, (double X, double Y, double Z) Look) CameraInGame()
    {
        var p = _game.Player;
        var look = p.Look;
        var eye = (X: p.X, Y: p.EyeY, Z: p.Z);
        if (!_thirdPerson)
            return (eye, look);

        double rx = Math.Cos(p.Yaw), rz = -Math.Sin(p.Yaw);
        double dx = -look.X * CameraDistance + rx * ShoulderOffset;
        double dy = -look.Y * CameraDistance + 0.4;
        double dz = -look.Z * CameraDistance + rz * ShoulderOffset;
        double full = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double len = full;
        if (VoxelRay.Cast(_dw.World, eye.X, eye.Y, eye.Z, dx, dy, dz, full) is { } hit)
            len = Math.Max(0.2, hit.Distance - 0.3); // stop just short of the wall
        double k = len / full;
        return ((eye.X + dx * k, eye.Y + dy * k, eye.Z + dz * k), look);
    }

    private void UpdateCharacter(double walked)
    {
        var p = _game.Player;
        var pose = (p.X, p.Y, p.Z, p.Yaw, p.Pitch);
        if (_thirdPerson && pose == _lastPose && walked == 0) return;
        _lastPose = pose;
        _character.Update(p, walked, _thirdPerson);
    }

    /// <summary>Outlines the targeted block with a transient wireframe box.</summary>
    private void UpdateHighlight()
    {
        var target = _game.Target is { } t ? (t.X, t.Y, t.Z) : ((int, int, int)?)null;
        if (target == _highlighted) return;
        ClearHighlight();
        _highlighted = target;
        if (target is not { } v) return;

        var map = _dw.World.Mapping;
        var (x0, y0, z0) = map.VoxelCorner(v.Item1, v.Item2, v.Item3);
        double s = map.CellSize, h = map.CellHeight, e = s * 0.01;
        var c = new Point3d[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Point3d(x0 - e + (i & 1) * (s + 2 * e), y0 - e + ((i >> 1) & 1) * (s + 2 * e), z0 - e + ((i >> 2) & 1) * (h + 2 * e));
        int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

        var tm = TransientManager.CurrentTransientManager;
        for (int i = 0; i < edges.GetLength(0); i++)
        {
            var line = new Line(c[edges[i, 0]], c[edges[i, 1]]) { Color = AcColor.FromRgb(20, 20, 20), LineWeight = LineWeight.LineWeight050 };
            tm.AddTransient(line, TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
            _highlight.Add(line);
        }
    }

    private void ClearHighlight()
    {
        var tm = TransientManager.CurrentTransientManager;
        foreach (var e in _highlight)
        {
            tm.EraseTransient(e, new IntegerCollection());
            e.Dispose();
        }
        _highlight.Clear();
    }

    private void Announce()
    {
        var p = _game.Player;
        if (_game.SelectedBlock == _announcedBlock && p.Flying == _announcedFly) return;
        _announcedBlock = _game.SelectedBlock;
        _announcedFly = p.Flying;
        Ed.WriteMessage($"\n[BlockCraft] Block: {Blocks.Info(_announcedBlock).Name}   {(p.Flying ? "FLYING" : "WALKING")}");
    }

    // ------------------------------------------------------------------ input

    private void OnMessage(object? sender, PreTranslateMessageEventArgs e)
    {
        if (_stopped) return;
        var m = e.Message;
        int msg = m.message;
        int vk = (int)(long)m.wParam;

        switch (msg)
        {
            case WM_KEYDOWN or WM_SYSKEYDOWN:
                if (!_captured && vk != VK_ESCAPE) return; // paused: typing goes to AutoCAD
                bool repeat = !_keys.Add(vk);
                if (vk == VK_ESCAPE) _stopRequested = true;
                else if (vk == VK_TAB && !repeat) Release();
                else if (vk == 'F' && !repeat) _input.ToggleFly = true;
                else if ((vk == 'V' || vk == VK_F5) && !repeat) { _thirdPerson = !_thirdPerson; _lastPose = null; }
                else if (vk is >= '1' and <= '9') _input.SelectSlot = vk - '1';
                e.Handled = true;
                return;

            case WM_KEYUP or WM_SYSKEYUP:
                _keys.Remove(vk);
                if (_captured) e.Handled = true;
                return;

            case WM_CHAR or WM_DEADCHAR or WM_SYSCHAR:
                if (_captured || vk == VK_ESCAPE) e.Handled = true;
                return;
        }

        if (msg < WM_MOUSEMOVE || msg > WM_MOUSELAST)
            return;

        if (!_captured)
        {
            // Paused: a left click in the drawing area resumes play; everything else is normal AutoCAD.
            if (msg == WM_LBUTTONDOWN && IsDrawingWindow(m.hwnd))
            {
                Capture();
                e.Handled = true;
            }
            return;
        }

        e.Handled = true;
        switch (msg)
        {
            case WM_MOUSEMOVE:
                var center = DrawingCenterOnScreen();
                var pos = Cursor.Position;
                int dx = pos.X - center.X, dy = pos.Y - center.Y;
                if (dx == 0 && dy == 0) return; // the move caused by re-centring
                _input.MouseDX += dx;
                _input.MouseDY += dy;
                Cursor.Position = center;
                break;
            case WM_LBUTTONDOWN:
                _leftHeld = true; _input.BreakPressed = true; _leftRepeat = RepeatSeconds * 1.5;
                break;
            case WM_LBUTTONUP:
                _leftHeld = false;
                break;
            case WM_RBUTTONDOWN:
                _rightHeld = true; _input.PlacePressed = true; _rightRepeat = RepeatSeconds * 1.5;
                break;
            case WM_RBUTTONUP:
                _rightHeld = false;
                break;
            case WM_MBUTTONDOWN:
                _input.PickPressed = true;
                break;
            case WM_MOUSEWHEEL:
                short delta = (short)(((long)m.wParam >> 16) & 0xFFFF);
                _input.Scroll += delta > 0 ? -1 : 1;
                break;
        }
    }

    private void OnCommandWillStart(object? sender, CommandEventArgs e) => Stop();

    private void OnDocumentChanging(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document == _doc) Stop();
    }

    private void Capture()
    {
        if (_captured) return;
        _captured = true;
        _keys.Clear();
        _lastTime = _clock.Elapsed.TotalSeconds;
        var rect = DrawingRectOnScreen();
        Cursor.Position = new System.Drawing.Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        Cursor.Clip = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        Cursor.Hide();
    }

    private void Release()
    {
        if (!_captured) return;
        _captured = false;
        _keys.Clear();
        _leftHeld = _rightHeld = false;
        Cursor.Clip = System.Drawing.Rectangle.Empty;
        Cursor.Show();
        Ed.WriteMessage("\n[BlockCraft] Paused - click the drawing to resume, Esc to return to drafting.");
    }

    // ------------------------------------------------------------------ stop

    /// <summary>Leaves play mode: saves the world, restores the drafting view and returns input to AutoCAD.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _timer.Stop();
        _timer.Dispose();

        CoreApp.PreTranslateMessage -= OnMessage;
        _doc.CommandWillStart -= OnCommandWillStart;
        AcApp.DocumentManager.DocumentToBeDeactivated -= OnDocumentChanging;
        AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentChanging;

        Release();
        ClearHighlight();
        _character.Dispose();

        try
        {
            using (_doc.LockDocument())
            {
                using (var tr = _doc.Database.TransactionManager.StartTransaction())
                {
                    _dw.Save(tr);
                    tr.Commit();
                }
                if (_savedView != null)
                    Ed.SetCurrentView(_savedView);
            }
        }
        catch (System.Exception ex)
        {
            Ed.WriteMessage($"\nBlockCraft could not save the world ({ex.Message}); run BCSYNC to repair it.");
        }

        _view?.Dispose();
        _savedView?.Dispose();
        if (Active == this) Active = null;
        Ed.WriteMessage($"\nBlockCraft: back to drafting. {_game.BlocksPlaced} placed, {_game.BlocksBroken} broken.\n");
    }

    // ------------------------------------------------------------------ Win32 helpers

    private bool IsDrawingWindow(IntPtr hwnd)
    {
        IntPtr doc = _doc.Window.Handle;
        return hwnd == doc || IsChild(doc, hwnd);
    }

    private RECT DrawingRectOnScreen()
    {
        IntPtr hwnd = _doc.Window.Handle;
        GetClientRect(hwnd, out var r);
        var tl = new POINT { X = r.Left, Y = r.Top };
        var br = new POINT { X = r.Right, Y = r.Bottom };
        ClientToScreen(hwnd, ref tl);
        ClientToScreen(hwnd, ref br);
        return new RECT { Left = tl.X, Top = tl.Y, Right = br.X, Bottom = br.Y };
    }

    private System.Drawing.Point DrawingCenterOnScreen()
    {
        var r = DrawingRectOnScreen();
        return new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
