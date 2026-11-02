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

            // Sound: yours, or the built-in jingle (skipped if a video brings its own).
            if (sound != null)
            {
                var player = new MediaPlayer();
                player.Open(new Uri(sound));
                player.Play();
            }
            else if (video == null)
            {
                using var ms = new MemoryStream(Jingle.Wav());
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

            frame.Child = Animation(p);
            win.Show();
            CloseAfter(win, TimeSpan.FromSeconds(4.5));
        }

        private static void CloseAfter(Window w, TimeSpan after)
        {
            var t = new DispatcherTimer { Interval = after };
            t.Tick += (_, _) => { t.Stop(); if (w.IsVisible) w.Close(); };
            t.Start();
        }

        /// <summary>The two Interlock brackets fly in, snap together, spin, then confetti and "You found it!".</summary>
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

            text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, D(.4)) { BeginTime = TimeSpan.FromSeconds(1.6) });
            sub.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, D(.4)) { BeginTime = TimeSpan.FromSeconds(2.0) });

            // Confetti in the scheme's colours, bursting out of the logo and falling.
            var rnd = new Random();
            string[] colours = { p.Accent, p.Accent2, p.Text, Brand.Shade(p.Accent, .8), Brand.Shade(p.Accent2, .8) };
            for (int i = 0; i < 46; i++)
            {
                var bit = new Rectangle
                {
                    Width = rnd.Next(5, 10), Height = rnd.Next(8, 14), Fill = Theme.B(colours[i % colours.Length]), Opacity = 0,
                    RenderTransformOrigin = new Point(.5, .5),
                };
                var turn = new RotateTransform(rnd.Next(360));
                var fly = new TranslateTransform();
                bit.RenderTransform = new TransformGroup { Children = { turn, fly } };
                Canvas.SetLeft(bit, 214);
                Canvas.SetTop(bit, 116);
                canvas.Children.Add(bit);

                double start = 1.1 + rnd.NextDouble() * .25;
                double angle = rnd.NextDouble() * Math.PI * 2, power = 90 + rnd.NextDouble() * 140;
                var begin = TimeSpan.FromSeconds(start);
                bit.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = begin,
                    KeyFrames = { new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))) },
                });
                fly.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(Math.Cos(angle) * power, D(2.2)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                fly.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = begin,
                    KeyFrames =
                    {
                        new EasingDoubleKeyFrame(Math.Sin(angle) * power - 60, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.6)), new CubicEase { EasingMode = EasingMode.EaseOut }),
                        new EasingDoubleKeyFrame(Math.Sin(angle) * power + 170, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2)), new QuadraticEase { EasingMode = EasingMode.EaseIn }),
                    },
                });
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(turn.Angle + rnd.Next(360, 900), D(2.2)) { BeginTime = begin });
            }
            return canvas;
        }
    }
}
