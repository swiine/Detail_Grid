using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>
    /// The Easter egg: click the logo at the top of the CoWork strip 7 times (within 4 seconds). It plays a sound and an
    /// animation in a small window over Civil 3D that closes by itself (or when clicked) and never blocks anything.
    /// To use your own, put easteregg.wav / easteregg.mp3 and/or easteregg.mp4 next to CoWork.dll (the admin tool's app
    /// folder, then Publish) - otherwise a built-in jingle and the Interlock animation play.
    /// </summary>
    internal static class EasterEgg
    {
        private const int ClicksNeeded = 7;
        private static int _clicks;
        private static DateTime _first;
        private static Window? _open;

        /// <summary>Called on every click of the logo.</summary>
        public static void Click()
        {
            if (DateTime.UtcNow - _first > TimeSpan.FromSeconds(4)) { _clicks = 0; _first = DateTime.UtcNow; }
            if (++_clicks < ClicksNeeded) return;
            _clicks = 0;
            try { Play(); } catch (Exception ex) { Log.Error("Easter egg", ex); }
        }

        private static string Here => System.IO.Path.GetDirectoryName(typeof(EasterEgg).Assembly.Location) ?? "";

        private static string? Find(params string[] names) =>
            names.Select(n => System.IO.Path.Combine(Here, n)).FirstOrDefault(File.Exists);

        public static void Play()
        {
            if (_open != null) return;
            Log.Info("Easter egg found");
            string? video = Find("easteregg.mp4", "easteregg.wmv");
            string? sound = Find("easteregg.wav", "easteregg.mp3");
            // Half the time the Interlock glitch, half the time it's drafted in CAD.
            bool drafted = video == null && Random.Shared.Next(2) == 0;

            // Sound: yours, or the built-in one (skipped if a video brings its own).
            if (sound != null)
            {
                var player = new MediaPlayer();
                player.Open(new Uri(sound));
                player.Play();
            }
            else if (video == null)
            {
                using var ms = new MemoryStream(drafted ? DraftedScript.Wav() : Jingle.Wav());
                new SoundPlayer(ms).Play();
            }

            Palette p = Theme.Current.Palette;
            var win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = false, ShowActivated = false, Width = 440, Height = 320, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Cursor = Cursors.Hand,
            };
            try { new WindowInteropHelper(win).Owner = AcApp.MainWindow.Handle; } catch (Exception ex) { Log.Swallowed(ex); }
            win.MouseLeftButtonUp += (_, _) => win.Close();
            win.Closed += (_, _) => _open = null;
            _open = win;

            var frame = new Border
            {
                CornerRadius = new CornerRadius(18), Background = Theme.B(p.Back), BorderBrush = Theme.B(p.Accent), BorderThickness = new Thickness(2),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, Opacity = .5, ShadowDepth = 4 },
            };
            win.Content = frame;

            if (video != null)
            {
                var media = new MediaElement { Source = new Uri(video), LoadedBehavior = MediaState.Play, Stretch = Stretch.Uniform, Margin = new Thickness(10) };
                media.MediaEnded += (_, _) => win.Close();
                media.MediaFailed += (_, e) => { Log.Error("Easter egg video", e.ErrorException); win.Close(); };
                frame.Child = media;
                win.Show();
                CloseAfter(win, TimeSpan.FromSeconds(30)); // in case the video never ends
                return;
            }

            frame.Child = drafted ? DraftedEgg.Build(p) : Animation(p);
            win.Show();
            CloseAfter(win, TimeSpan.FromSeconds(drafted ? DraftedScript.Close : 5));
        }

        /// <summary>Opacity keyframes: any jump becomes fully on (1) or off (0).</summary>
        private static DoubleAnimationUsingKeyFrames Clamp01(DoubleAnimationUsingKeyFrames k)
        {
            foreach (DiscreteDoubleKeyFrame f in k.KeyFrames) f.Value = f.Value != 0 ? 1 : 0;
            return k;
        }

        /// <summary>Pushes every jump away from rest in one direction (so the colour copies split apart).</summary>
        private static DoubleAnimationUsingKeyFrames Bias(DoubleAnimationUsingKeyFrames k, double by)
        {
            foreach (DiscreteDoubleKeyFrame f in k.KeyFrames) if (f.Value != 0) f.Value += by;
            return k;
        }

        private static void CloseAfter(Window w, TimeSpan after)
        {
            var t = new DispatcherTimer { Interval = after };
            t.Tick += (_, _) => { t.Stop(); if (w.IsVisible) w.Close(); };
            t.Start();
        }

        /// <summary>The two Interlock brackets fly in, snap together, spin, then glitch out while "You found it!" decodes.</summary>
        private static UIElement Animation(Palette p)
        {
            var canvas = new Canvas { Width = 436, Height = 316, ClipToBounds = true };
            const double size = 150, cx = 218 - size / 2, cy = 120 - size / 2;

            // The two brackets as separate shapes (the same geometry as the logo, scaled up).
            Shape Bracket(string colour, string shade, double[] pts)
            {
                var geo = new StreamGeometry();
                using (StreamGeometryContext g = geo.Open())
                {
                    g.BeginFigure(new Point(pts[0] / 16 * size, pts[1] / 16 * size), true, true);
                    for (int i = 2; i < pts.Length; i += 2) g.LineTo(new Point(pts[i] / 16 * size, pts[i + 1] / 16 * size), true, false);
                }
                return new System.Windows.Shapes.Path
                {
                    Data = geo, Fill = Theme.B(colour), Stroke = Theme.B(shade), StrokeThickness = 3,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, Opacity = .45, ShadowDepth = 3 },
                };
            }
            var logo = new Canvas { Width = size, Height = size, RenderTransformOrigin = new Point(.5, .5) };
            Shape a = Bracket(p.Accent, Brand.Shade(p.Accent), new double[] { 3, 3, 10, 3, 10, 5.3, 5.3, 5.3, 5.3, 10, 3, 10 });
            Shape c = Bracket(p.Accent2, Brand.Shade(p.Accent2), new double[] { 13, 13, 6, 13, 6, 10.7, 10.7, 10.7, 10.7, 6, 13, 6 });
            var moveA = new TranslateTransform(-260, -160);
            var moveC = new TranslateTransform(260, 160);
            a.RenderTransform = moveA;
            c.RenderTransform = moveC;
            logo.Children.Add(a);
            logo.Children.Add(c);
            var spin = new RotateTransform(0);
            var pop = new ScaleTransform(1, 1);
            logo.RenderTransform = new TransformGroup { Children = { pop, spin } };
            Canvas.SetLeft(logo, cx);
            Canvas.SetTop(logo, cy);
            canvas.Children.Add(logo);

            var text = new TextBlock
            {
                Text = "You found it!", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Theme.B(p.Text), Opacity = 0,
                FontFamily = new FontFamily("Segoe UI"), Width = 436, TextAlignment = TextAlignment.Center,
            };
            Canvas.SetTop(text, 236);
            canvas.Children.Add(text);
            var sub = new TextBlock
            {
                Text = "CoWork - made with a lot of coffee", FontSize = 12, Foreground = Theme.B(p.Dim), Opacity = 0,
                Width = 436, TextAlignment = TextAlignment.Center,
            };
            Canvas.SetTop(sub, 276);
            canvas.Children.Add(sub);

            Duration D(double s) => new(TimeSpan.FromSeconds(s));
            var snap = new BackEase { Amplitude = .45, EasingMode = EasingMode.EaseOut };
            foreach (var (t, dp, to) in new[] { (moveA, TranslateTransform.XProperty, 0.0), (moveA, TranslateTransform.YProperty, 0.0),
                                                (moveC, TranslateTransform.XProperty, 0.0), (moveC, TranslateTransform.YProperty, 0.0) })
                t.BeginAnimation(dp, new DoubleAnimation(to, D(.7)) { EasingFunction = snap });

            // Click together -> a little pop and a full spin.
            var bump = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(.7) };
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.12))));
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.3)), new BounceEase { Bounces = 2 }));
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, bump.Clone());
            spin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, D(.9)) { BeginTime = TimeSpan.FromSeconds(1.05), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

            text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, D(.15)) { BeginTime = TimeSpan.FromSeconds(1.6) });
            sub.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, D(.4)) { BeginTime = TimeSpan.FromSeconds(2.0) });

            // Glitch: once it has clicked together, the logo splits into its two colours, tears into slices and jitters,
            // scanlines flash, and the text scrambles before it settles.
            var rnd = new Random();
            TimeSpan T(double sec) => TimeSpan.FromSeconds(sec);
            const double g0 = 2.0, g1 = 3.4; // when the glitch runs (after the spin)

            // A run of random jumps between g0 and g1, back to rest at the end.
            DoubleAnimationUsingKeyFrames Jitter(double rest, double amount, double chance = .55)
            {
                var k = new DoubleAnimationUsingKeyFrames();
                k.KeyFrames.Add(new DiscreteDoubleKeyFrame(rest, KeyTime.FromTimeSpan(T(0))));
                for (double t = g0; t < g1; t += .045 + rnd.NextDouble() * .06)
                    k.KeyFrames.Add(new DiscreteDoubleKeyFrame(rnd.NextDouble() < chance ? rest + (rnd.NextDouble() * 2 - 1) * amount : rest, KeyTime.FromTimeSpan(T(t))));
                k.KeyFrames.Add(new DiscreteDoubleKeyFrame(rest, KeyTime.FromTimeSpan(T(g1))));
                return k;
            }

            // Colour split: a copy of the logo in each accent colour, pulled apart while it glitches.
            Point[] shapeA = { new(3, 3), new(10, 3), new(10, 5.3), new(5.3, 5.3), new(5.3, 10), new(3, 10) };
            Point[] shapeC = { new(13, 13), new(6, 13), new(6, 10.7), new(10.7, 10.7), new(10.7, 6), new(13, 6) };
            foreach (var (colour, dir) in new[] { (p.Accent, -1.0), (p.Accent2, 1.0) })
            {
                var ghost = new Canvas { Width = size, Height = size, Opacity = 0, IsHitTestVisible = false };
                foreach (Point[] pts in new[] { shapeA, shapeC })
                {
                    var poly = new Polygon { Fill = Theme.B(colour), Opacity = .75 };
                    foreach (Point q in pts) poly.Points.Add(new Point(q.X / 16 * size, q.Y / 16 * size));
                    ghost.Children.Add(poly);
                }
                var shift = new TranslateTransform();
                ghost.RenderTransform = shift;
                Canvas.SetLeft(ghost, cx);
                Canvas.SetTop(ghost, cy);
                canvas.Children.Insert(0, ghost); // behind the real logo
                ghost.BeginAnimation(UIElement.OpacityProperty, Clamp01(Jitter(0, 1, .8)));
                shift.BeginAnimation(TranslateTransform.XProperty, Bias(Jitter(0, 14), dir * 6));
                shift.BeginAnimation(TranslateTransform.YProperty, Jitter(0, 3));
            }

            // The logo itself twitches.
            var twitch = new TranslateTransform();
            ((TransformGroup)logo.RenderTransform).Children.Add(twitch);
            twitch.BeginAnimation(TranslateTransform.XProperty, Jitter(0, 7, .45));

            // Torn slices: bands of the logo shoved sideways.
            for (int i = 0; i < 6; i++)
            {
                double top = rnd.NextDouble() * .85, h = .04 + rnd.NextDouble() * .12;
                var slice = new Rectangle
                {
                    Width = size, Height = size * h, Opacity = 0, IsHitTestVisible = false,
                    Fill = new VisualBrush(logo) { Viewbox = new Rect(0, top, 1, h), ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill },
                };
                var move = new TranslateTransform();
                slice.RenderTransform = move;
                Canvas.SetLeft(slice, cx);
                Canvas.SetTop(slice, cy + top * size);
                canvas.Children.Add(slice);
                slice.BeginAnimation(UIElement.OpacityProperty, Clamp01(Jitter(0, 1, .35)));
                move.BeginAnimation(TranslateTransform.XProperty, Jitter(0, 40, .9));
            }

            // Scanlines flashing across the window.
            string[] lines = { p.Accent, p.Accent2, p.Text };
            for (int i = 0; i < 7; i++)
            {
                var bar = new Rectangle { Width = 436, Height = 1 + rnd.Next(4), Fill = Theme.B(lines[i % lines.Length]), Opacity = 0, IsHitTestVisible = false };
                Canvas.SetTop(bar, rnd.Next(20, 300));
                canvas.Children.Add(bar);
                var flick = Clamp01(Jitter(0, 1, .25));
                foreach (DiscreteDoubleKeyFrame f in flick.KeyFrames) f.Value *= .7;
                bar.BeginAnimation(UIElement.OpacityProperty, flick);
            }

            // The text scrambles, then settles.
            string final = text.Text;
            text.Text = "";
            const string noise = "#%&@$*/\\<>[]{}=+?!01";
            var started = DateTime.MinValue;
            var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
            tick.Tick += (_, _) =>
            {
                if (started == DateTime.MinValue) started = DateTime.UtcNow;
                double done = (DateTime.UtcNow - started).TotalSeconds / .9; // 0..1: letters lock in left to right
                if (done >= 1 || !canvas.IsLoaded) { text.Text = final; tick.Stop(); return; }
                text.Text = new string(final.Select((ch, n) => ch == ' ' || n < final.Length * done ? ch : noise[rnd.Next(noise.Length)]).ToArray());
            };
            var startTick = new System.Windows.Threading.DispatcherTimer { Interval = T(1.6) };
            startTick.Tick += (_, _) => { startTick.Stop(); tick.Start(); };
            startTick.Start();
            var textShift = new TranslateTransform();
            text.RenderTransform = textShift;
            textShift.BeginAnimation(TranslateTransform.XProperty, Jitter(0, 5, .3));

            return canvas;
        }
    }
}
