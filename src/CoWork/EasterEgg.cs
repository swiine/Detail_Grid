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
    /// The Easter egg: click the logo at the top of the CoWork strip 7 times (within 4 seconds). It opens Survey Run, a
    /// little endless-runner game (see <see cref="SurveyRunWindow"/>) with an office high-score table.
    /// To play your own clip instead, put easteregg.mp4 (and/or easteregg.wav / .mp3) next to CoWork.dll (the admin
    /// tool's app folder, then Publish).
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
            if (video == null && sound == null) { SurveyRunWindow.Open(); return; }

            // Your own clip and/or sound.
            if (sound != null)
            {
                var player = new MediaPlayer();
                player.Open(new Uri(sound));
                player.Play();
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

            win.Close();
            _open = null;
            SurveyRunWindow.Open(); // a sound but no video: play it over the game
        }

        private static void CloseAfter(Window w, TimeSpan after)
        {
            var t = new DispatcherTimer { Interval = after };
            t.Tick += (_, _) => { t.Stop(); if (w.IsVisible) w.Close(); };
            t.Start();
        }
    }
}
