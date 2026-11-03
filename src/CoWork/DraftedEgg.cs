using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CoWork.Core;

namespace CoWork
{
    /// <summary>
    /// The "Drafted in CAD" Easter egg: ZOOM EXTENTS onto a drafting grid, a crosshair draws the Interlock logo as two
    /// polylines (endpoint snaps, a dimension readout following the cursor, the command line typing PLINE), HATCHes them
    /// in the scheme's colours with a sweep, a regen flash, and "_CoWork". Timings come from <see cref="DraftedScript"/>
    /// so the sound track (made from the same times) stays in step.
    /// </summary>
    internal static class DraftedEgg
    {
        private const double Wd = 436, Ht = 316, DrawH = 226, Size = 130;
        private static readonly double Ox = (Wd - Size) / 2, Oy = 30;

        private static Point P((double X, double Y) q) => new(Ox + q.X / 16 * Size, Oy + q.Y / 16 * Size);
        private static double Ease(double t) { t = Math.Clamp(t, 0, 1); return 1 - Math.Pow(1 - t, 3); }
        private static double Clamp01(double t) => Math.Clamp(t, 0, 1);
        private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public static UIElement Build(Palette p)
        {
            Brush text = Theme.B(p.Text), dim = Theme.B(p.Dim), snapB = Theme.B("#F2A541");
            var root = new Canvas { Width = Wd, Height = Ht, ClipToBounds = true, Clip = new RectangleGeometry(new Rect(0, 0, Wd, Ht), 16, 16) };

            // The drawing area (zooms in at the start).
            var world = new Canvas { Width = Wd, Height = DrawH, ClipToBounds = true, RenderTransformOrigin = new Point(.5, .45) };
            var zoom = new ScaleTransform(1.3, 1.3);
            world.RenderTransform = zoom;
            root.Children.Add(world);

            for (double x = 0; x <= Wd; x += 10)
                world.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = DrawH, Stroke = dim, StrokeThickness = x % 50 == 0 ? .8 : .4, Opacity = x % 50 == 0 ? .28 : .13 });
            for (double y = 0; y <= DrawH; y += 10)
                world.Children.Add(new Line { X1 = 0, X2 = Wd, Y1 = y, Y2 = y, Stroke = dim, StrokeThickness = y % 50 == 0 ? .8 : .4, Opacity = y % 50 == 0 ? .28 : .13 });

            // UCS icon, bottom left.
            var ucs = new Canvas { Opacity = .7 };
            ucs.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = 26, Y2 = 0, Stroke = Theme.B("#E0675A"), StrokeThickness = 1.5 });
            ucs.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = 0, Y2 = -26, Stroke = Theme.B("#5FBF7F"), StrokeThickness = 1.5 });
            ucs.Children.Add(Label("X", 28, -7, 9, dim));
            ucs.Children.Add(Label("Y", -3, -38, 9, dim));
            Canvas.SetLeft(ucs, 18); Canvas.SetTop(ucs, 180);
            world.Children.Add(ucs);

            Point[] a = DraftedScript.BracketA.Select(P).ToArray(), c = DraftedScript.BracketC.Select(P).ToArray();

            // Hatch (pattern lines sweeping across, then solid fill), under the outlines.
            (Polygon Pattern, Polygon Solid, RectangleGeometry Sweep, Rect Box) Hatch(Point[] pts, string colour)
            {
                var pen = new Pen(Theme.B(colour), 1.1);
                var lines = new DrawingBrush(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 6), new Point(6, 0))))
                { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 6, 6), ViewboxUnits = BrushMappingMode.Absolute };
                var box = new Rect(pts.Min(q => q.X), pts.Min(q => q.Y), pts.Max(q => q.X) - pts.Min(q => q.X), pts.Max(q => q.Y) - pts.Min(q => q.Y));
                var sweep = new RectangleGeometry(new Rect(box.X, box.Y, 0, box.Height));
                var pattern = new Polygon { Points = new PointCollection(pts), Fill = lines, Clip = sweep };
                var solid = new Polygon { Points = new PointCollection(pts), Fill = Theme.B(colour), Opacity = 0 };
                world.Children.Add(solid);
                world.Children.Add(pattern);
                return (pattern, solid, sweep, box);
            }
            var hatchA = Hatch(a, p.Accent);
            var hatchC = Hatch(c, p.Accent2);

            var lineA = new Polyline { Stroke = Theme.B(p.Accent), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Miter };
            var lineC = new Polyline { Stroke = Theme.B(p.Accent2), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Miter };
            world.Children.Add(lineA);
            world.Children.Add(lineC);

            // Cursor, snap marker and its tooltip, dynamic input.
            var hair1 = new Line { Stroke = text, StrokeThickness = 1 };
            var hair2 = new Line { Stroke = text, StrokeThickness = 1 };
            var pick = new Rectangle { Width = 8, Height = 8, Stroke = text, StrokeThickness = 1 };
            var snap = new Rectangle { Width = 11, Height = 11, Stroke = snapB, StrokeThickness = 2, Visibility = Visibility.Hidden };
            var tip = Tip("Endpoint", "#2B2F33", "#F2A541");
            var dyn = Tip("", "#1E2226", p.Text);
            foreach (UIElement e in new UIElement[] { hair1, hair2, pick, snap, tip, dyn }) world.Children.Add(e);

            // Regen flash over the drawing.
            var flash = new Rectangle { Width = Wd, Height = DrawH, Fill = text, Opacity = 0, IsHitTestVisible = false };
            world.Children.Add(flash);

            // Command line.
            var cmdBox = new Border { Width = Wd - 20, Height = 34, Background = Theme.B("#141A1D"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3), Opacity = .95 };
            var cmd = new StackPanel();
            var above = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 10.5, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis };
            var now = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 10.5, Foreground = text, TextTrimming = TextTrimming.CharacterEllipsis };
            cmd.Children.Add(above);
            cmd.Children.Add(now);
            cmdBox.Child = cmd;
            Canvas.SetLeft(cmdBox, 10); Canvas.SetTop(cmdBox, DrawH - 40);
            root.Children.Add(cmdBox);

            var title = new TextBlock { Text = "CoWork", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = text, Width = Wd, TextAlignment = TextAlignment.Center, Opacity = 0 };
            var sub = new TextBlock { Text = "made with a lot of coffee", FontSize = 12, Foreground = dim, Width = Wd, TextAlignment = TextAlignment.Center, Opacity = 0 };
            Canvas.SetTop(title, 234); Canvas.SetTop(sub, 274);
            root.Children.Add(title);
            root.Children.Add(sub);

            Point rest = new(355, 150), start = new(380, 200);
            Point inA = P((4.15, 7)), inC = P((11.85, 9.3));
            var clock = Stopwatch.StartNew();

            void Frame(object? sender, EventArgs e)
            {
                if (!root.IsLoaded && clock.Elapsed > TimeSpan.FromSeconds(1)) { System.Windows.Media.CompositionTarget.Rendering -= Frame; return; }
                double t = clock.Elapsed.TotalSeconds;
                double zs = 1 + .3 * (1 - Ease(t / DraftedScript.Zoom));
                zoom.ScaleX = zoom.ScaleY = zs;

                // Where the cursor is, what's drawn, and which point it's heading for.
                Point cur; Point? target = null; Point? from = null; double segU = 0;
                Polyline? live = null; Point[]? shape = null; int seg = -1;
                if (t < DraftedScript.StartA) { cur = Lerp(start, a[0], Ease((t - .15) / (DraftedScript.StartA - .15))); target = a[0]; segU = Clamp01((t - .15) / (DraftedScript.StartA - .15)); goto placed; }
                else if (t < DraftedScript.StartA + 6 * DraftedScript.Seg) { shape = a; live = lineA; seg = (int)((t - DraftedScript.StartA) / DraftedScript.Seg); segU = (t - DraftedScript.StartA - seg * DraftedScript.Seg) / DraftedScript.Seg; }
                else if (t < DraftedScript.StartC) { double u = (t - DraftedScript.StartA - 6 * DraftedScript.Seg) / DraftedScript.Gap; cur = Lerp(a[6], c[0], Ease(u)); target = c[0]; segU = u; goto placed; }
                else if (t < DraftedScript.DrawnAt) { shape = c; live = lineC; seg = (int)((t - DraftedScript.StartC) / DraftedScript.Seg); segU = (t - DraftedScript.StartC - seg * DraftedScript.Seg) / DraftedScript.Seg; }
                else if (t < DraftedScript.PickA) { double u = (t - DraftedScript.DrawnAt) / (DraftedScript.PickA - DraftedScript.DrawnAt); cur = Lerp(c[6], inA, Ease(u)); goto placed; }
                else if (t < DraftedScript.PickC) { cur = Lerp(inA, inC, Ease((t - DraftedScript.PickA) / (DraftedScript.PickC - DraftedScript.PickA))); goto placed; }
                else { cur = Lerp(inC, rest, Ease((t - DraftedScript.PickC - .2) / .6)); goto placed; }

                if (shape != null)
                {
                    seg = Math.Min(seg, 5);
                    from = shape[seg];
                    target = shape[seg + 1];
                    cur = Lerp(shape[seg], shape[seg + 1], Ease(segU));
                    live!.Points = new PointCollection(shape.Take(seg + 1).Append(cur));
                }
                else cur = a[0];

            placed:
                if (t >= DraftedScript.StartA + 6 * DraftedScript.Seg && lineA.Points.Count < 7) lineA.Points = new PointCollection(a);
                if (t >= DraftedScript.DrawnAt && lineC.Points.Count < 7) lineC.Points = new PointCollection(c);

                hair1.X1 = cur.X - 70; hair1.X2 = cur.X + 70; hair1.Y1 = hair1.Y2 = cur.Y;
                hair2.Y1 = cur.Y - 70; hair2.Y2 = cur.Y + 70; hair2.X1 = hair2.X2 = cur.X;
                Canvas.SetLeft(pick, cur.X - 4); Canvas.SetTop(pick, cur.Y - 4);

                // Endpoint snap as it nears each corner.
                bool snapping = target != null && segU > .55 && t < DraftedScript.DrawnAt + .05;
                snap.Visibility = tip.Visibility = snapping ? Visibility.Visible : Visibility.Hidden;
                if (snapping) { Canvas.SetLeft(snap, target!.Value.X - 5.5); Canvas.SetTop(snap, target.Value.Y - 5.5); Canvas.SetLeft(tip, target.Value.X + 9); Canvas.SetTop(tip, target.Value.Y + 8); }

                // Dimension readout while drawing a segment.
                if (from != null && t < DraftedScript.DrawnAt)
                {
                    double dx = cur.X - from.Value.X, dy = from.Value.Y - cur.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy) / (Size / 16) * 1.25, ang = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                    ((TextBlock)dyn.Child).Text = $"{len:0.00}  <  {ang:0}°";
                    dyn.Visibility = Visibility.Visible;
                    Canvas.SetLeft(dyn, cur.X + 12); Canvas.SetTop(dyn, cur.Y - 26);
                }
                else dyn.Visibility = Visibility.Hidden;

                // Hatch sweeps, then turns solid.
                foreach (var (h, t0) in new[] { (hatchA, DraftedScript.PickA), (hatchC, DraftedScript.PickC) })
                {
                    double u = Ease((t - t0) / DraftedScript.HatchSweep);
                    h.Sweep.Rect = new Rect(h.Box.X, h.Box.Y, h.Box.Width * u, h.Box.Height);
                    h.Solid.Opacity = .95 * Clamp01((t - t0 - DraftedScript.HatchSweep * .6) / .35);
                    h.Pattern.Opacity = 1 - Clamp01((t - t0 - DraftedScript.HatchSweep) / .4) * .8;
                }

                // Regen flash, the crosshair fades away, then the name.
                double f = t - DraftedScript.Final;
                flash.Opacity = f > 0 && f < .3 ? .22 * Math.Sin(f / .3 * Math.PI) : 0;
                double cursorGone = Clamp01((t - DraftedScript.Final - .3) / .4);
                hair1.Opacity = hair2.Opacity = pick.Opacity = 1 - cursorGone;
                title.Opacity = Clamp01((f - .15) / .35);
                sub.Opacity = Clamp01((f - .4) / .4);

                var (up, line) = DraftedScript.CommandLine(t);
                above.Text = up;
                now.Text = line + ((int)(t * 2.5) % 2 == 0 ? "_" : "");
            }
            System.Windows.Media.CompositionTarget.Rendering += Frame;
            root.Unloaded += (_, _) => System.Windows.Media.CompositionTarget.Rendering -= Frame;
            return root;
        }

        private static TextBlock Label(string s, double x, double y, double size, Brush b)
        {
            var t = new TextBlock { Text = s, FontSize = size, Foreground = b };
            Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
            return t;
        }

        private static Border Tip(string s, string back, string fore) => new()
        {
            Background = Theme.B(back), BorderBrush = Theme.B(fore), BorderThickness = new Thickness(.8), Padding = new Thickness(4, 1, 4, 1),
            Visibility = Visibility.Hidden,
            Child = new TextBlock { Text = s, FontFamily = new FontFamily("Segoe UI"), FontSize = 9.5, Foreground = Theme.B(fore) },
        };
    }
}
