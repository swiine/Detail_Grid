using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>
    /// PARKED (not used right now): the Easter egg plays the Interlock animation instead. To bring the game back, call
    /// SurveyRunWindow.Open() from EasterEgg.Play() (the rules and art are in CoWork.Core SurveyRun, with tests, and a
    /// browser version is in docs/survey-run).
    /// Survey Run, the Easter egg game: Space / Up / click to jump (hold for higher), Down to duck, M for sound,
    /// Esc to close. It gets faster as you go and switches to the night shift every 700 points. Your best goes on
    /// the office high-score table in the shared folder. It's a small separate window, so Civil 3D stays usable.
    /// </summary>
    internal sealed class SurveyRunWindow : Window
    {
        private static SurveyRunWindow? _open;

        private const double GroundY = 168;   // the road, in canvas pixels from the top
        private const double CanvasH = 200;

        private readonly Palette _day;
        private readonly Canvas _canvas = new() { Width = SurveyRun.Width, Height = CanvasH, ClipToBounds = true };
        private readonly TextBlock _score = new() { FontFamily = new FontFamily("Consolas"), FontSize = 15, FontWeight = FontWeights.Bold };
        private readonly TextBlock _middle = new() { FontFamily = new FontFamily("Consolas"), FontSize = 14, TextAlignment = TextAlignment.Center, Width = SurveyRun.Width };
        private readonly Border _frame;
        private readonly Dictionary<(string[], bool), BitmapSource> _sprites = new();
        private readonly (double X, double Y, int Kind)[] _scenery;
        private readonly SoundPlayer? _hop, _point, _bonk, _start;

        private SurveyRun? _game;
        private DateTime _last = DateTime.UtcNow;
        private DateTime _overAt;
        private DateTime _flashUntil;
        private int _best;
        private bool _muted, _night, _saved;
        private string _table = "";

        public static void Open()
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new SurveyRunWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
            _open.Activate();
        }

        private SurveyRunWindow()
        {
            _day = Theme.Current.Palette;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = "Survey Run";
            try { new WindowInteropHelper(this).Owner = AcApp.MainWindow.Handle; } catch (Exception ex) { Log.Swallowed(ex); }

            var rnd = new Random();
            _scenery = Enumerable.Range(0, 9).Select(i => (i * 75.0 + rnd.Next(40), 30.0 + rnd.Next(70), rnd.Next(3))).ToArray();
            try
            {
                _hop = Sound(Jingle.Hop); _point = Sound(Jingle.Point); _bonk = Sound(Jingle.Bonk); _start = Sound(Jingle.Tune);
            }
            catch (Exception ex) { Log.Swallowed(ex); }
            _best = MyBest();

            var title = new TextBlock { Text = "SURVEY RUN", FontFamily = new FontFamily("Consolas"), FontSize = 13, FontWeight = FontWeights.Bold };
            var help = new TextBlock { Text = "Space jump · ↓ duck · M sound · Esc close", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            var close = new TextBlock { Text = "  ✕", FontSize = 13, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0) };
            close.MouseLeftButtonUp += (_, _) => Close();
            var top = new DockPanel { Margin = new Thickness(4, 0, 4, 6) };
            DockPanel.SetDock(close, Dock.Right);
            top.Children.Add(close);
            DockPanel.SetDock(title, Dock.Left);
            top.Children.Add(title);
            top.Children.Add(help);
            title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            // Drawn at 600 x 200 and shown 1.5x bigger, with crisp pixels.
            _canvas.LayoutTransform = new ScaleTransform(1.5, 1.5);
            RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(_canvas, EdgeMode.Aliased);
            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(_canvas);
            _frame = new Border
            {
                CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Padding = new Thickness(12, 8, 12, 12), Child = stack,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, Opacity = .5, ShadowDepth = 4 },
                Margin = new Thickness(16),
            };
            Content = _frame;
            foreach (TextBlock t in new[] { title, help, close }) t.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = _score });

            _canvas.MouseLeftButtonDown += (_, _) => Press();
            _canvas.MouseLeftButtonUp += (_, _) => _game?.Release();
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Deactivated += (_, _) => _game?.Duck(false);
            CompositionTarget.Rendering += OnFrame;
            Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
            ApplyColours();
            Draw();
        }

        // ------------------------------------------------------------------ input

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Space or Key.Up or Key.W: if (!e.IsRepeat) Press(); break;
                case Key.Down or Key.S: _game?.Duck(true); break;
                case Key.M: _muted = !_muted; break;
                default: return;
            }
            e.Handled = true;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Space or Key.Up or Key.W) _game?.Release();
            if (e.Key is Key.Down or Key.S) _game?.Duck(false);
        }

        /// <summary>Jump - or start / restart.</summary>
        private void Press()
        {
            if (_game == null || (_game.Over && DateTime.UtcNow - _overAt > TimeSpan.FromSeconds(.5)))
            {
                _game = new SurveyRun();
                _saved = false;
                _last = DateTime.UtcNow;
                Play(_start);
                _game.Jump();
                return;
            }
            _game.Jump();
        }

        // ------------------------------------------------------------------ loop

        private void OnFrame(object? sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - _last).TotalSeconds;
            _last = now;
            SurveyRun? g = _game;
            if (g != null && !g.Over)
            {
                g.Step(dt);
                if (g.Hopped) Play(_hop);
                if (g.Milestone) { Play(_point); _flashUntil = now.AddSeconds(.7); }
                if (g.Over) GameOver(g);
                bool night = g.Score / 700 % 2 == 1;
                if (night != _night) { _night = night; ApplyColours(); }
            }
            Draw();
        }

        private void GameOver(SurveyRun g)
        {
            _overAt = DateTime.UtcNow;
            Play(_bonk);
            if (g.Score > _best) _best = g.Score;
            if (_saved) return;
            _saved = true;
            string data = ChatHub.DataFolder;
            if (data.Length == 0) { _table = ""; return; }
            try
            {
                SurveyRun.SaveBest(data, Environment.UserName, ChatHub.Settings.Name, g.Score);
                _table = string.Join("\n", SurveyRun.TopScores(data, 3).Select((b, i) => $"{i + 1}. {b.Name,-16} {b.Score,6}"));
                Log.Info($"Survey Run: {g.Score}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _table = ""; }
        }

        private int MyBest()
        {
            try
            {
                return SurveyRun.TopScores(ChatHub.DataFolder, int.MaxValue)
                    .FirstOrDefault(b => string.Equals(b.User, Rooms.Safe(Environment.UserName), StringComparison.OrdinalIgnoreCase))?.Score ?? 0;
            }
            catch (Exception ex) { Log.Swallowed(ex); return 0; }
        }

        // ------------------------------------------------------------------ drawing

        private Palette Colours => _night ? _day with { Back = _day.Text, Text = _day.Back, Dim = _day.Dim } : _day;

        private void ApplyColours()
        {
            Palette p = Colours;
            _frame.Background = Theme.B(p.Back);
            _frame.BorderBrush = Theme.B(p.Accent);
            _score.Foreground = Theme.B(p.Text);
            _middle.Foreground = Theme.B(p.Text);
        }

        private void Draw()
        {
            Palette p = Colours;
            SurveyRun? g = _game;
            double distance = g?.Distance ?? 0, time = g?.Time ?? 0;
            _canvas.Children.Clear();

            // Far away: survey control marks drifting past slowly.
            foreach (var (x, y, kind) in _scenery)
            {
                double sx = ((x - distance * 0.15) % (SurveyRun.Width + 80) + SurveyRun.Width + 80) % (SurveyRun.Width + 80) - 40;
                Add(Mark(kind, p), sx, y);
            }

            // The road: an edge line, the centreline dashes and a few stones, all scrolling.
            Add(new Rectangle { Width = SurveyRun.Width, Height = 2, Fill = Theme.B(p.Text) }, 0, GroundY);
            double dashOffset = distance % 40;
            for (double x = -dashOffset; x < SurveyRun.Width; x += 40)
                Add(new Rectangle { Width = 20, Height = 2, Fill = Theme.B(p.Dim) }, x, GroundY + 14);
            for (int i = 0; i < 12; i++)
            {
                double x = ((i * 53 + 17) - distance) % SurveyRun.Width;
                if (x < 0) x += SurveyRun.Width;
                Add(new Rectangle { Width = i % 3 == 0 ? 4 : 2, Height = 2, Fill = Theme.B(p.Dim) }, x, GroundY + 5 + i % 4 * 2);
            }

            if (g != null)
            {
                foreach (SurveyRun.Obstacle o in g.Obstacles)
                {
                    string[] art = SurveyRun.Art.For(o.Kind, time);
                    if (o.Kind == SurveyRun.Kind.Pegs)
                    {
                        Sprite(art, o.X, o.Y, p);
                        Sprite(art, o.X + 12, o.Y, p);
                    }
                    else Sprite(art, o.X, o.Y, p);
                }
                Sprite(SurveyRun.Art.Player(g), SurveyRun.PlayerX, g.Y, p);
            }
            else Sprite(SurveyRun.Art.RunA, SurveyRun.PlayerX, 0, p);

            // Score (blinks on every hundred).
            int score = g?.Score ?? 0;
            bool hide = DateTime.UtcNow < _flashUntil && (int)((_flashUntil - DateTime.UtcNow).TotalMilliseconds / 120) % 2 == 0;
            _score.Text = (_best > 0 ? $"HI {_best:00000}   " : "") + (hide ? "     " : $"{score:00000}");
            _score.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Add(_score, SurveyRun.Width - _score.DesiredSize.Width - 4, 2);

            string middle = g == null
                ? "SURVEY RUN\n\nPress Space or click to start"
                : g.Over
                    ? "OUT OF BOUNDS\n\nSpace to go again" + (_table.Length > 0 ? "\n\nOffice best\n" + _table : "")
                    : "";
            if (middle.Length > 0)
            {
                _middle.Text = middle;
                Add(_middle, 0, g?.Over == true && _table.Length > 0 ? 14 : 48);
            }
        }

        private void Add(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            _canvas.Children.Add(e);
        }

        /// <summary>Draws a sprite with its bottom at height y above the road.</summary>
        private void Sprite(string[] art, double x, double y, Palette p)
        {
            BitmapSource bmp = Bitmap(art, p);
            var img = new Image { Source = bmp, Width = bmp.PixelWidth * SurveyRun.Art.Scale, Height = bmp.PixelHeight * SurveyRun.Art.Scale };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Add(img, Math.Round(x), Math.Round(GroundY - y - img.Height));
        }

        private BitmapSource Bitmap(string[] art, Palette p)
        {
            if (_sprites.TryGetValue((art, _night), out BitmapSource? cached)) return cached;
            int w = art.Max(r => r.Length), h = art.Length;
            var pixels = new uint[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < art[y].Length; x++)
                    if (SurveyRun.Art.Colour(art[y][x], p) is { } hex)
                    {
                        var c = (Color)ColorConverter.ConvertFromString(hex);
                        pixels[y * w + x] = 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
                    }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            bmp.Freeze();
            _sprites[(art, _night)] = bmp;
            return bmp;
        }

        /// <summary>Background marks: a control-point triangle, a benchmark cross or a small flag.</summary>
        private static UIElement Mark(int kind, Palette p)
        {
            Brush b = Theme.B(p.Dim);
            var c = new Canvas { Width = 12, Height = 12, Opacity = .55 };
            switch (kind)
            {
                case 0:
                    c.Children.Add(new Polygon { Points = new PointCollection { new(6, 1), new(11, 10), new(1, 10) }, Stroke = b, StrokeThickness = 1.5 });
                    break;
                case 1:
                    var h = new Rectangle { Width = 10, Height = 2, Fill = b }; Canvas.SetTop(h, 5); Canvas.SetLeft(h, 1);
                    var v = new Rectangle { Width = 2, Height = 10, Fill = b }; Canvas.SetLeft(v, 5); Canvas.SetTop(v, 1);
                    c.Children.Add(h); c.Children.Add(v);
                    break;
                default:
                    var pole = new Rectangle { Width = 2, Height = 11, Fill = b }; Canvas.SetLeft(pole, 2);
                    var flag = new Polygon { Points = new PointCollection { new(4, 0), new(11, 2.5), new(4, 5) }, Fill = Theme.B(p.Accent2), Opacity = .8 };
                    c.Children.Add(pole); c.Children.Add(flag);
                    break;
            }
            return c;
        }

        // ------------------------------------------------------------------ sound

        private static SoundPlayer Sound((double Hz, double Seconds)[] tune)
        {
            var player = new SoundPlayer(new MemoryStream(Jingle.Wav(tune, volume: 0.12)));
            player.Load();
            return player;
        }

        private void Play(SoundPlayer? s)
        {
            if (_muted || s == null) return;
            try { s.Play(); } catch (Exception ex) { Log.Swallowed(ex); }
        }
    }
}
