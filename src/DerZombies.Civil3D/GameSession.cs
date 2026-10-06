using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using DerZombies.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinTimer = System.Windows.Forms.Timer;

namespace DerZombies.Civil3D
{
    /// <summary>
    /// One running game in one drawing. A WinForms timer on AutoCAD's UI thread
    /// drives the frame loop, keyboard and mouse buttons are read with
    /// GetAsyncKeyState (and swallowed so AutoCAD doesn't act on them). In plan
    /// mode the cursor position comes from the editor's PointMonitor; in
    /// first-person mode the mouse is locked to the window and its movement turns
    /// the camera.
    /// </summary>
    internal sealed class GameSession : IDisposable
    {
        public static GameSession? Current { get; private set; }

        private readonly Document _doc;
        private readonly Vector3d _origin;
        private readonly WinTimer _timer = new WinTimer { Interval = 15 };
        private readonly Stopwatch _clock = new Stopwatch();
        private Game _game = null!;
        private IGameRenderer _renderer = null!;
        private readonly bool _firstPerson;
        private double _yaw;
        private bool _mouseLook = true;
        private bool _recenter = true;
        private ViewTableRecord? _savedView;
        private ObjectId _savedVisualStyle;
        private readonly Dictionary<string, object> _savedVars = new Dictionary<string, object>();
        private const double MouseSensitivity = 0.0035;
        private Dictionary<char, List<ObjectId>> _doorEntities = new Dictionary<char, List<ObjectId>>();
        private Vec2? _mouse;
        private bool _paused;
        private readonly HashSet<int> _wasDown = new HashSet<int>();
        private bool _disposed;

        // Virtual-key codes
        private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
            VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

        private static readonly HashSet<int> SwallowedKeys = new HashSet<int>
        {
            'W', 'A', 'S', 'D', 'E', 'R', 'Q', 'V', 'G', 'F', 'P',
            VK_SPACE, VK_ESCAPE, VK_RETURN, VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN, VK_TAB,
        };

        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102,
            WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203,
            WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        private GameSession(Document doc, Vector3d origin, bool firstPerson)
        {
            _doc = doc;
            _origin = origin;
            _firstPerson = firstPerson;
        }

        public static void Start(Document doc, Vector3d origin, bool buildCivil, bool firstPerson = false)
        {
            Current?.Dispose();
            var s = new GameSession(doc, origin, firstPerson);
            if (firstPerson) s.Enter3D();
            s.NewGame(buildCivil);
            s.Hook();
            Current = s;
        }

        public static void Stop() => Current?.Dispose();

        private void NewGame(bool buildCivil)
        {
            _renderer?.Dispose();
            _game = new Game();
            var ed = _doc.Editor;

            using (_doc.LockDocument())
            {
                MapBuilder.Clear(_doc.Database, includeCivil: buildCivil);
                _doorEntities = _firstPerson
                    ? MapBuilder3D.Build(_doc.Database, _game.Map, _origin)
                    : MapBuilder.Build(_doc.Database, _game.Map, _origin);
                if (buildCivil)
                {
                    // In 3D the castle sits on the summit (elevation 0) with the mountain falling away below.
                    try
                    {
                        ed.WriteMessage("\n" + (_firstPerson
                            ? CivilSite.Build(_doc.Database, _game.Map, _origin, castleElevation: -0.6, cogoPoints: false)
                            : CivilSite.Build(_doc.Database, _game.Map, _origin)));
                    }
                    catch (Exception ex) { ed.WriteMessage($"\nCivil 3D surface/points skipped: {ex.Message}"); }
                }
            }

            if (_firstPerson)
            {
                _yaw = _game.Player.AimAngle;
                _recenter = true;
                _renderer = new Renderer3D(_doc, _game, _origin);
            }
            else
            {
                ZoomToGame();
                _renderer = new Renderer(_doc.Database, _game, _origin);
            }
            _paused = false;
            _clock.Restart();
        }

        private void ZoomToGame()
        {
            var ed = _doc.Editor;
            var map = _game.Map;
            using var view = ed.GetCurrentView();
            view.CenterPoint = new Point2d(_origin.X + (map.Width + 75) / 2, _origin.Y + map.Height / 2 + 2);
            view.Height = map.Height + 120;
            view.Width = map.Width + 120;
            view.ViewDirection = Vector3d.ZAxis;
            view.Target = Point3d.Origin;
            view.ViewTwist = 0;
            ed.SetCurrentView(view);
        }

        private void Hook()
        {
            _timer.Tick += OnTick;
            _timer.Start();
            _doc.Editor.PointMonitor += OnPointMonitor;
            AcApp.PreTranslateMessage += OnPreTranslateMessage;
            AcApp.DocumentManager.DocumentToBeDestroyed += OnDocumentClosing;
            AcApp.DocumentManager.DocumentBecameCurrent += OnDocumentBecameCurrent;
        }

        private void Unhook()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            try { _doc.Editor.PointMonitor -= OnPointMonitor; } catch { /* document already gone */ }
            AcApp.PreTranslateMessage -= OnPreTranslateMessage;
            AcApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentClosing;
            AcApp.DocumentManager.DocumentBecameCurrent -= OnDocumentBecameCurrent;
        }

        // ================================================================== loop

        /// <summary>True while this drawing is active and AutoCAD (any of its windows) is in the foreground.</summary>
        private bool HasFocus
        {
            get
            {
                if (AcApp.DocumentManager.MdiActiveDocument != _doc) return false;
                GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                return pid == (uint)Environment.ProcessId;
            }
        }

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        private bool Pressed(int vk)
        {
            bool down = Down(vk);
            bool was = _wasDown.Contains(vk);
            if (down) _wasDown.Add(vk); else _wasDown.Remove(vk);
            return down && !was;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_disposed) return;
            double dt = _clock.Elapsed.TotalSeconds;
            _clock.Restart();

            try
            {
                if (!HasFocus) return; // auto-pause while AutoCAD is in the background

                if (Pressed(VK_ESCAPE)) { Quit(); return; }
                if (Pressed('P')) { _paused = !_paused; _recenter = true; }
                if (_game.IsGameOver && Pressed(VK_RETURN)) { NewGame(buildCivil: false); return; }

                if (_firstPerson && Pressed(VK_TAB)) { _mouseLook = !_mouseLook; _recenter = true; }

                if (!_paused && _firstPerson)
                {
                    _game.Update(dt, FirstPersonInput(dt));
                    HandleEvents();
                }
                else if (!_paused)
                {
                    var input = new InputState
                    {
                        MoveX = (Down('D') ? 1 : 0) - (Down('A') ? 1 : 0),
                        MoveY = (Down('W') || Down(VK_UP) ? 1 : 0) - (Down('S') || Down(VK_DOWN) ? 1 : 0),
                        AimPoint = _mouse,
                        AimTurn = (Down(VK_LEFT) ? 1 : 0) - (Down(VK_RIGHT) ? 1 : 0),
                        Fire = Down(VK_LBUTTON) || Down(VK_SPACE) || Down('F'),
                        Reload = Down('R'),
                        Interact = Down('E'),
                        SwitchWeapon = Down('Q'),
                        Knife = Down('V') || Down(VK_RBUTTON),
                        Grenade = Down('G'),
                    };
                    if (input.AimTurn != 0) _mouse = null; // keyboard aiming takes over until the mouse moves
                    _game.Update(dt, input);
                    HandleEvents();
                }

                _renderer.Draw(_game, _paused);
                _doc.Editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                _doc.Editor.WriteMessage($"\nDer Eisendrache crashed: {ex.Message}\n");
                Dispose();
            }
        }

        /// <summary>Mouse-look plus WASD relative to where you are facing.</summary>
        private InputState FirstPersonInput(double dt)
        {
            var r3 = (Renderer3D)_renderer;
            if (_mouseLook && GetWindowRect(AcApp.MainWindow.Handle, out var rc) && GetCursorPos(out var pt))
            {
                int cx = (rc.Left + rc.Right) / 2, cy = (rc.Top + rc.Bottom) / 2;
                if (!_recenter)
                {
                    _yaw -= (pt.X - cx) * MouseSensitivity;
                    r3.Pitch = Math.Clamp(r3.Pitch - (pt.Y - cy) * MouseSensitivity, -0.7, 0.7);
                }
                _recenter = false;
                SetCursorPos(cx, cy);
            }
            _yaw += ((Down(VK_LEFT) ? 1 : 0) - (Down(VK_RIGHT) ? 1 : 0)) * 2.6 * dt;

            var forward = Vec2.FromAngle(_yaw);
            var right = new Vec2(forward.Y, -forward.X);
            double ahead = (Down('W') || Down(VK_UP) ? 1 : 0) - (Down('S') || Down(VK_DOWN) ? 1 : 0);
            double strafe = (Down('D') ? 1 : 0) - (Down('A') ? 1 : 0);
            var move = forward * ahead + right * strafe;

            return new InputState
            {
                MoveX = move.X,
                MoveY = move.Y,
                AimAngle = _yaw,
                Fire = Down(VK_LBUTTON) || Down(VK_SPACE) || Down('F'),
                Reload = Down('R'),
                Interact = Down('E'),
                SwitchWeapon = Down('Q'),
                Knife = Down('V') || Down(VK_RBUTTON),
                Grenade = Down('G'),
            };
        }

        // ================================================================== 3D environment

        /// <summary>Shaded visual style, no view-transition animation, no UCS icon / ViewCube / rollover clutter.</summary>
        private void Enter3D()
        {
            var ed = _doc.Editor;
            _savedView = ed.GetCurrentView();
            SetVar("VTENABLE", (short)0);
            SetVar("UCSICON", (short)0);
            SetVar("ROLLOVERTIPS", (short)0);
            SetVar("SELECTIONPREVIEW", (short)0);
            SetVar("NAVVCUBEDISPLAY", (short)0);
            SetVar("NAVBARDISPLAY", (short)0);
            SetVar("GRIDMODE", (short)0);
            using (_doc.LockDocument())
                _savedVisualStyle = SetVisualStyle("Shaded", "Realistic", "Conceptual");
        }

        private void Leave3D()
        {
            using (_doc.LockDocument())
            {
                if (!_savedVisualStyle.IsNull) SetVisualStyle(_savedVisualStyle);
            }
            if (_savedView != null)
            {
                _doc.Editor.SetCurrentView(_savedView);
                _savedView.Dispose();
                _savedView = null;
            }
            foreach (var kv in _savedVars)
            {
                try { AcApp.SetSystemVariable(kv.Key, kv.Value); } catch { /* best effort */ }
            }
            _savedVars.Clear();
        }

        private void SetVar(string name, object value)
        {
            try
            {
                if (!_savedVars.ContainsKey(name)) _savedVars[name] = AcApp.GetSystemVariable(name);
                AcApp.SetSystemVariable(name, value);
            }
            catch { /* not every product has every variable */ }
        }

        /// <summary>Sets the current viewport's visual style to the first name that exists; returns the previous style.</summary>
        private ObjectId SetVisualStyle(params string[] names)
        {
            var db = _doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var dict = (DBDictionary)tr.GetObject(db.VisualStyleDictionaryId, OpenMode.ForRead);
            string? name = names.FirstOrDefault(dict.Contains);
            var previous = name == null ? ObjectId.Null : SetVisualStyle(tr, dict.GetAt(name));
            tr.Commit();
            _doc.Editor.UpdateTiledViewportsFromDatabase();
            return previous;
        }

        private void SetVisualStyle(ObjectId styleId)
        {
            var db = _doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            SetVisualStyle(tr, styleId);
            tr.Commit();
            _doc.Editor.UpdateTiledViewportsFromDatabase();
        }

        private ObjectId SetVisualStyle(Transaction tr, ObjectId styleId)
        {
            var vp = (ViewportTableRecord)tr.GetObject(_doc.Database.CurrentViewportTableRecordId, OpenMode.ForWrite);
            var previous = vp.VisualStyleId;
            vp.VisualStyleId = styleId;
            return previous;
        }

        private void HandleEvents()
        {
            foreach (var ev in _game.DrainEvents())
            {
                switch (ev.Type)
                {
                    case GameEventType.DoorOpened:
                        if (_doorEntities.TryGetValue(ev.Data[0], out var ids))
                        {
                            using (_doc.LockDocument()) MapBuilder.EraseDoor(_doc.Database, ids);
                        }
                        break;
                    case GameEventType.GameOver:
                        var p = _game.Player;
                        _doc.Editor.WriteMessage(
                            $"\nGAME OVER - round {_game.Round}, {p.Kills} kills ({p.Headshots} headshots), {p.TotalPointsEarned:N0} points earned." +
                            "\nPress Enter to play again or Esc to quit.\n");
                        break;
                }
            }
        }

        private void Quit()
        {
            var p = _game.Player;
            _doc.Editor.WriteMessage($"\nDer Eisendrache: you reached round {_game.Round} with {p.Kills} kills. Thanks for playing!\n");
            Dispose();
        }

        // ================================================================== AutoCAD events

        private void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            var pt = e.Context.RawPoint;
            _mouse = new Vec2(pt.X - _origin.X, pt.Y - _origin.Y);
        }

        private void OnPreTranslateMessage(object? sender, PreTranslateMessageEventArgs e)
        {
            if (_disposed || !HasFocus) return;
            var msg = e.Message;
            int m = (int)msg.message;
            switch (m)
            {
                case WM_KEYDOWN:
                case WM_KEYUP:
                case WM_CHAR:
                {
                    int vk = (int)msg.wParam.ToInt64();
                    if (m == WM_CHAR) vk = char.ToUpperInvariant((char)vk);
                    if (SwallowedKeys.Contains(vk)) e.Handled = true;
                    break;
                }
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                case WM_RBUTTONDBLCLK:
                    e.Handled = true; // no selecting or context menus while playing
                    break;
            }
        }

        private void OnDocumentClosing(object? sender, DocumentCollectionEventArgs e)
        {
            if (e.Document == _doc) Dispose();
        }

        private void OnDocumentBecameCurrent(object? sender, DocumentCollectionEventArgs e)
        {
            if (e.Document != _doc) _paused = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unhook();
            try
            {
                _renderer?.Dispose();
                if (_firstPerson) Leave3D();
                _doc.Editor.UpdateScreen();
            }
            catch { /* the document may already be closing */ }
            _timer.Dispose();
            if (Current == this) Current = null;
        }
    }
}
