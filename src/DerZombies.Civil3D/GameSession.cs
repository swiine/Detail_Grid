using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// GetAsyncKeyState (and swallowed so AutoCAD doesn't act on them), and the
    /// cursor position comes from the editor's PointMonitor.
    /// </summary>
    internal sealed class GameSession : IDisposable
    {
        public static GameSession? Current { get; private set; }

        private readonly Document _doc;
        private readonly Vector3d _origin;
        private readonly WinTimer _timer = new WinTimer { Interval = 15 };
        private readonly Stopwatch _clock = new Stopwatch();
        private Game _game = null!;
        private Renderer _renderer = null!;
        private Dictionary<char, List<ObjectId>> _doorEntities = new Dictionary<char, List<ObjectId>>();
        private Vec2? _mouse;
        private bool _paused;
        private readonly HashSet<int> _wasDown = new HashSet<int>();
        private bool _disposed;

        // Virtual-key codes
        private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
            VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

        private static readonly HashSet<int> SwallowedKeys = new HashSet<int>
        {
            'W', 'A', 'S', 'D', 'E', 'R', 'Q', 'V', 'G', 'F', 'P',
            VK_SPACE, VK_ESCAPE, VK_RETURN, VK_LEFT, VK_UP, VK_RIGHT, VK_DOWN,
        };

        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102,
            WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203,
            WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private GameSession(Document doc, Vector3d origin)
        {
            _doc = doc;
            _origin = origin;
        }

        public static void Start(Document doc, Vector3d origin, bool buildCivil)
        {
            Current?.Dispose();
            var s = new GameSession(doc, origin);
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
                _doorEntities = MapBuilder.Build(_doc.Database, _game.Map, _origin);
                if (buildCivil)
                {
                    try { ed.WriteMessage("\n" + CivilSite.Build(_doc.Database, _game.Map, _origin)); }
                    catch (Exception ex) { ed.WriteMessage($"\nCivil 3D surface/points skipped: {ex.Message}"); }
                }
            }

            ZoomToGame();
            _renderer = new Renderer(_doc.Database, _game, _origin);
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
                if (Pressed('P')) _paused = !_paused;
                if (_game.IsGameOver && Pressed(VK_RETURN)) { NewGame(buildCivil: false); return; }

                if (!_paused)
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
                _doc.Editor.UpdateScreen();
            }
            catch { /* the document may already be closing */ }
            _timer.Dispose();
            if (Current == this) Current = null;
        }
    }
}
