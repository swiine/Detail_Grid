using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Shape = System.Windows.Shapes.Path;
using System.Windows.Threading;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>The pages that slide out from the CoWork strip.</summary>
    public enum Page { Chat, Friends, Session, Teams, Commands, Settings }

    /// <summary>CoWork's colours: one of the palettes in <see cref="Brand"/> (picked in Settings; applies on restart).</summary>
    internal sealed class Theme
    {
        public Brush Back = null!, Card = null!, Text = null!, Dim = null!, Accent = null!, AccentText = null!, Mine = null!,
            Field = null!, Edge = null!, Good = null!, Warn = null!, Rail = null!;
        public Palette Palette = null!;

        public static readonly Theme Current = Make(Brand.PaletteFor(CiSettings.Load().ThemeName));

        internal static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }

        public static Theme Make(Palette p) => new()
        {
            Palette = p, Back = B(p.Back), Card = B(p.Card), Text = B(p.Text), Dim = B(p.Dim), Accent = B(p.Accent), AccentText = B(p.AccentText),
            Mine = B(p.Mine), Field = B(p.Field), Edge = B(p.Edge), Good = B(p.Good), Warn = B(p.Warn), Rail = B(p.Rail),
        };
    }

    /// <summary>CoWork's logos, drawn on a 16 x 16 grid (the same shapes as docs/brand).</summary>
    internal static class Logos
    {
        public static Drawing Draw(string id, Brush accent, Brush ink)
        {
            var g = new DrawingGroup();
            void Fill(Brush b, Geometry geo, double opacity = 1) =>
                g.Children.Add(new GeometryDrawing(opacity == 1 ? b : new SolidColorBrush(((SolidColorBrush)b).Color) { Opacity = opacity }, null, geo));
            void Stroke(Brush b, double w, Geometry geo) =>
                g.Children.Add(new GeometryDrawing(null, new Pen(b, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo));
            Geometry R(double x, double y, double w, double h, double r = 0) => new RectangleGeometry(new Rect(x, y, w, h), r, r);
            Geometry C(double x, double y, double r) => new EllipseGeometry(new Point(x, y), r, r);
            Geometry P(string d) => Geometry.Parse(d);

            // Keep the full 16 x 16 box so every logo lines up the same.
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, R(0, 0, 16, 16)));
            switch (id)
            {
                case "crosshairs":
                    Fill(accent, R(0, 0, 16, 16, 3.5));
                    foreach (var r in new[] { R(4, 2, 1.6, 7), R(1.3, 4.7, 7, 1.6), R(10.4, 7, 1.6, 7), R(7.7, 9.7, 7, 1.6) }) Fill(ink, r);
                    break;
                case "linked":
                    Stroke(accent, 2.2, R(1.2, 1.2, 8.6, 8.6, 1.2));
                    Fill(accent, R(6.2, 6.2, 8.6, 8.6, 1.2));
                    Fill(ink, R(6.2, 6.2, 3.6, 3.6), 0.9);
                    break;
                case "bubble":
                    Fill(accent, P("M2.5,1.5 H13.5 A1.5,1.5 0 0 1 15,3 V10.5 A1.5,1.5 0 0 1 13.5,12 H7 L3.5,15 V12 H2.5 A1.5,1.5 0 0 1 1,10.5 V3 A1.5,1.5 0 0 1 2.5,1.5 Z"));
                    Fill(ink, R(7.2, 3.2, 1.6, 7.2));
                    Fill(ink, R(4.4, 6, 7.2, 1.6));
                    break;
                case "nodes":
                    Stroke(accent, 1.6, P("M4,4.5 L12,4.5 M4,4.5 L8,12 M12,4.5 L8,12"));
                    Fill(accent, C(4, 4.5, 2.6)); Fill(accent, C(12, 4.5, 2.6)); Fill(accent, C(8, 12, 2.6));
                    Fill(ink, C(8, 12, 1.1));
                    break;
                case "monogram":
                    Fill(accent, R(0, 0, 16, 16, 3.5));
                    Fill(ink, P("M2,4 h4 v1.8 H3.8 v4.4 H6 V12 H2 Z M7,4 h1.7 l0.6,5 0.9,-3 h1.6 l0.9,3 0.6,-5 H15 l-1.3,8 h-1.8 l-1,-3.2 -1,3.2 H8.3 Z"));
                    break;
                default: // overlap
                    Fill(accent, C(6, 8, 5));
                    Fill(accent, C(10, 8, 5), 0.55);
                    Fill(ink, P("M8,3.68 A5,5 0 0 1 8,12.32 A5,5 0 0 1 8,3.68 Z"), 0.85);
                    break;
            }
            return g;
        }

        public static FrameworkElement Image(string id, Brush accent, Brush ink, double size) =>
            new Image { Source = new DrawingImage(Draw(id, accent, ink)), Width = size, Height = size };

        /// <summary>The logo as a 16 x 16 Windows icon (for the status bar).</summary>
        public static System.Drawing.Icon Icon(string id, Palette p)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
                dc.DrawDrawing(Draw(id, Theme.B(p.Accent), Theme.B(p.AccentText)));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            png.Save(ms);
            ms.Position = 0;
            using var gdi = new System.Drawing.Bitmap(ms);
            return System.Drawing.Icon.FromHandle(gdi.GetHicon());
        }
    }

    /// <summary>Line icons (24x24, drawn like the mockups).</summary>
    internal static class Icons
    {
        private static string Circle(double cx, double cy, double r) =>
            FormattableString.Invariant($"M{cx - r},{cy} A{r},{r} 0 1 1 {cx + r},{cy} A{r},{r} 0 1 1 {cx - r},{cy} Z ");

        public static readonly Dictionary<string, string> Paths = new()
        {
            ["chat"] = "M4,5 H20 V16 H9 L4,20 Z",
            ["users"] = Circle(9, 8, 3.2) + "M3,20 C3,16.5 5.7,14 9,14 S15,16.5 15,20 " + Circle(17, 9, 2.5) + "M16,14 C19,14 21,16 21,19",
            ["cowork"] = "M4,20 L8,19 L19,8 L16,5 L5,16 Z M14,7 L17,10",
            ["teams"] = "M6,5 H18 A2,2 0 0 1 20,7 V17 A2,2 0 0 1 18,19 H6 A2,2 0 0 1 4,17 V7 A2,2 0 0 1 6,5 Z M8,9 H16 M12,9 V16",
            ["gear"] = Circle(12, 12, 3) + "M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M5.6,5.6 L7.7,7.7 M16.3,16.3 L18.4,18.4 M5.6,18.4 L7.7,16.3 M16.3,7.7 L18.4,5.6",
            ["save"] = "M5,4 H16 L19,7 V20 H5 Z M8,4 V9 H15 V4 M8,20 V14 H16 V20",
            ["plot"] = "M7,9 V4 H17 V9 M6,18 H4 V10 H20 V18 H18 M7,14 H17 V20 H7 Z",
            ["trash"] = "M5,7 H19 M9,7 V4 H15 V7 M7,7 L8,20 H16 L17,7",
            ["xref"] = "M9,15 L15,9 M10,6 L12,4 A4,4 0 0 1 18,10 L16,12 M14,18 L12,20 A4,4 0 0 1 6,14 L8,12",
            ["layers"] = "M12,4 L20,8 L12,12 L4,8 Z M4,12 L12,16 L20,12 M4,16 L12,20 L20,16",
            ["plus"] = "M12,5 V19 M5,12 H19",
            ["cmd"] = "M5,8 L9,12 L5,16 M11,17 H19",
            ["join"] = "M10,7 L15,12 L10,17 M15,12 H3 M14,4 H20 V20 H14",
            ["zoom"] = Circle(11, 11, 6) + "M20,20 L15.5,15.5",
            ["star"] = "M12,4 L14.4,9 L19.8,9.6 L15.8,13.3 L16.9,18.7 L12,16 L7.1,18.7 L8.2,13.3 L4.2,9.6 L9.6,9 Z",
            ["sheet"] = "M6,3 H15 L19,7 V21 H6 Z M14,3 V8 H19 M9,13 H16 M9,17 H14",
            ["pin"] = "M9,4 H15 L14,10 L17,13 H7 L10,10 Z M12,13 V20",
            ["close"] = "M9,6 L15,12 L9,18",
        };

        /// <summary>Icon names you can pick for your own buttons.</summary>
        public static readonly string[] ForCommands = { "cmd", "save", "plot", "trash", "xref", "layers", "zoom", "star", "sheet", "join" };

        public static FrameworkElement Make(string name, Brush brush, double size = 18)
        {
            var path = new Shape
            {
                Data = Geometry.Parse(Paths.TryGetValue(name, out string? d) ? d : Paths["cmd"]),
                Stroke = brush, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas };
        }
    }

    /// <summary>
    /// The CoWork panel (design 4): a slim strip of icons docked to the edge of the screen. Click an icon and its
    /// page slides out; it tucks away again when the mouse leaves it (unless pinned). Your own commands sit on the
    /// strip below the CoWork icons. It's an AutoCAD palette, so it docks, floats and auto-hides like Properties.
    /// </summary>
    public sealed class CoWorkPanel : UserControl
    {
        public const double RailWidth = 48;
        private static readonly Theme T = Theme.Current;
        private static readonly string[] NameColours = { "#5CC9F5", "#F5A35C", "#8BE07A", "#D58BF5", "#F57A8B", "#F5D35C", "#5CF5C9", "#A9B4F5" };

        // Layout
        private readonly Grid _root = new();
        private readonly DockPanel _rail = new() { LastChildFill = false, Width = RailWidth };
        private readonly StackPanel _railTop = new(), _railCommands = new();
        private readonly Border _fly = new();
        private readonly TextBlock _flyTitle = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _pinButton = new();
        private readonly ContentControl _pageHost = new();
        private readonly Dictionary<Page, FrameworkElement> _pages = new();
        private readonly Dictionary<Page, Border> _railButtons = new();
        private readonly Dictionary<Page, Border> _badges = new();
        private readonly DispatcherTimer _tuck = new() { Interval = TimeSpan.FromMilliseconds(1300) };

        // Chat page
        private readonly WrapPanel _tabs = new() { Margin = new Thickness(6, 6, 6, 2) };
        private readonly TextBlock _online = new() { Margin = new Thickness(8, 2, 8, 4), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly StackPanel _list = new() { Margin = new Thickness(6) };
        private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        private readonly TextBox _input = new();
        private readonly Button _send = new() { Content = "Send" };
        private readonly TextBlock _live = new() { Margin = new Thickness(8, 4, 8, 0), FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private readonly TextBlock _status = new() { Margin = new Thickness(8, 2, 8, 6), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
        private ChatMessage? _previous;
        private readonly System.Windows.Controls.Primitives.Popup _suggest = new() { StaysOpen = false, AllowsTransparency = true };
        private readonly ListBox _suggestList = new();
        private int _atStart = -1;

        // Other pages
        private readonly StackPanel _friends = new() { Margin = new Thickness(6) };
        private readonly StackPanel _session = new() { Margin = new Thickness(8) };
        private readonly StackPanel _teams = new() { Margin = new Thickness(8) };
        private readonly StackPanel _commands = new() { Margin = new Thickness(8) };
        private readonly StackPanel _settings = new() { Margin = new Thickness(8) };
        private string? _confirmRemove;

        public Room? Current { get; private set; }
        public Page Page { get; private set; } = Page.Chat;
        public bool IsExpanded { get; private set; }
        public bool Pinned { get; private set; }
        public Func<bool> IsShown { get; set; } = () => true;

        /// <summary>The page slid out (true) or tucked away (false): the palette resizes to match.</summary>
        public event Action<bool>? ExpandedChanged;

        public CoWorkPanel()
        {
            Background = T.Back;
            Foreground = T.Text;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;

            BuildRail();
            BuildFlyout();
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _root.Children.Add(_fly);
            _root.Children.Add(_rail);
            Content = _root;
            SetRailSide(left: false);

            BuildChatPage();
            _pages[Page.Friends] = Scroll(_friends);
            _pages[Page.Session] = Scroll(_session);
            _pages[Page.Teams] = Scroll(_teams);
            _pages[Page.Commands] = Scroll(_commands);
            _pages[Page.Settings] = Scroll(_settings);

            // Tuck away when the mouse leaves (unless pinned or you're typing in it).
            _tuck.Tick += (_, _) => { _tuck.Stop(); if (!Pinned && !IsKeyboardFocusWithin && !IsMouseOver) Collapse(); };
            MouseLeave += (_, _) => { if (IsExpanded && !Pinned) _tuck.Start(); };
            MouseEnter += (_, _) => _tuck.Stop();

            ChatHub.RoomAdded += _ => RefreshTabs();
            ChatHub.RoomLoaded += r => { if (r == Current) ShowAll(); };
            ChatHub.MessageAdded += (r, m) => { if (r == Current) Add(m, scroll: true); };
            ChatHub.OnlineChanged += r => { if (r == Current) RefreshOnline(); };
            ChatHub.StatusChanged += RefreshStatus;
            ChatHub.JobRoomActivated += r => { if (Page == Page.Chat && !r.Hidden) Select(r, expand: false); };
            ChatHub.RoomsChanged += () => { RefreshTabs(); RefreshBadges(); };
            ChatHub.FriendsChanged += () => { RefreshBadges(); if (IsExpanded) RefreshPage(); };
            LiveDrafting.Changed += () => { RefreshLive(); RefreshBadges(); if (Page == Page.Session) RefreshPage(); };
            TeamsLink.Changed += () => { if (Page is Page.Teams or Page.Friends) RefreshPage(); if (Current?.IsTeams == true) RefreshOnline(); };

            Select(ChatHub.Rooms.FirstOrDefault(r => r.Id == ChatHub.CurrentJobRoom) ?? ChatHub.Rooms[0], expand: false);
            RefreshStatus();
            RefreshLive();
            RefreshBadges();
            Collapse(raise: false);
        }

        // ================================================================== strip and flyout

        private void BuildRail()
        {
            _rail.Background = T.Rail;
            _railTop.Margin = new Thickness(0, 6, 0, 0);
            AddRailPage(Page.Chat, "chat", "Chat");
            AddRailPage(Page.Friends, "users", "Friends");
            AddRailPage(Page.Session, "cowork", "CoWork - edit this drawing together, live");
            AddRailPage(Page.Teams, "teams", "Teams");
            DockPanel.SetDock(_railTop, Dock.Top);
            _rail.Children.Add(_railTop);

            var sep = new Border { Height = 1, Width = 30, Background = T.Edge, Margin = new Thickness(0, 6, 0, 6) };
            DockPanel.SetDock(sep, Dock.Top);
            _rail.Children.Add(sep);
            DockPanel.SetDock(_railCommands, Dock.Top);
            _rail.Children.Add(_railCommands);
            RefreshRailCommands();

            Border gear = RailButton("gear", "Settings", () => Toggle(Page.Settings));
            _railButtons[Page.Settings] = gear;
            gear.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(gear, Dock.Bottom);
            _rail.Children.Add(gear);
        }

        private void AddRailPage(Page page, string icon, string tip)
        {
            Border b = RailButton(icon, tip, () => Toggle(page));
            var badge = new Border
            {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(4, 0, 4, 0), MinWidth = 14, Height = 14,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 1, 0),
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                Child = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
            };
            ((Grid)b.Child).Children.Add(badge);
            _badges[page] = badge;
            _railButtons[page] = b;
            _railTop.Children.Add(b);
        }

        private static Border RailButton(string icon, string tip, Action click)
        {
            var g = new Grid();
            g.Children.Add(new ContentControl { Content = Icons.Make(icon, T.Dim, 20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var b = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 2, 0, 2), Child = g,
                Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Center,
            };
            b.MouseEnter += (_, _) => { if (b.Tag is not true) b.Background = T.Card; };
            b.MouseLeave += (_, _) => { if (b.Tag is not true) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return b;
        }

        private static void SetRailOn(Border b, bool on)
        {
            b.Tag = on;
            b.Background = on ? T.Accent : Brushes.Transparent;
            if (((Grid)b.Child).Children[0] is ContentControl cc && cc.Content is Viewbox vb && vb.Child is Canvas c && c.Children[0] is Shape p)
                p.Stroke = on ? T.AccentText : T.Dim;
        }

        /// <summary>Your own command buttons on the strip, then "+" to add one.</summary>
        public void RefreshRailCommands()
        {
            _railCommands.Children.Clear();
            foreach (MenuCommand mc in ChatHub.Settings.MenuCommands)
            {
                MenuCommand cmd = mc;
                Border b = RailButton(cmd.Icon, $"{cmd.Label}  ({cmd.Command})", () => Commands.Run(cmd.Command));
                var menu = new ContextMenu();
                var remove = new MenuItem { Header = $"Remove \"{cmd.Label}\" from the strip" };
                remove.Click += (_, _) => SaveCommands(ChatHub.Settings.MenuCommands.Where(x => x != cmd).ToList());
                var edit = new MenuItem { Header = "Edit my commands..." };
                edit.Click += (_, _) => ShowPage(Page.Commands);
                menu.Items.Add(edit);
                menu.Items.Add(remove);
                b.ContextMenu = menu;
                _railCommands.Children.Add(b);
            }
            Border add = RailButton("plus", "Add your own command", () => ShowPage(Page.Commands));
            _railButtons[Page.Commands] = add;
            _railCommands.Children.Add(add);
            foreach (var kv in _railButtons) SetRailOn(kv.Value, IsExpanded && kv.Key == Page);
        }

        private void BuildFlyout()
        {
            _fly.Background = T.Back;
            _fly.BorderBrush = Brushes.Black;
            var dock = new DockPanel();
            var head = new DockPanel { Background = T.Rail, Height = 30 };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            brand.Children.Add(new ContentControl { Content = Logos.Image(ChatHub.Settings.LogoName, T.Accent, T.AccentText, 14), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            brand.Children.Add(_flyTitle);
            var close = HeadButton("close", "Tuck away", () => Collapse());
            _pinButton.Child = Icons.Make("pin", T.Dim, 15);
            StyleHead(_pinButton, "Pin open (stop it tucking away)", () => { Pinned = !Pinned; RefreshPin(); });
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(_pinButton, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(_pinButton);
            head.Children.Add(brand);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            dock.Children.Add(_pageHost);
            _fly.Child = dock;
        }

        private static Border HeadButton(string icon, string tip, Action click)
        {
            var b = new Border { Child = Icons.Make(icon, T.Dim, 15) };
            StyleHead(b, tip, click);
            return b;
        }

        private static void StyleHead(Border b, string tip, Action click)
        {
            b.Width = 26; b.Height = 24; b.CornerRadius = new CornerRadius(4); b.Margin = new Thickness(0, 0, 4, 0);
            b.Background = Brushes.Transparent; b.Cursor = Cursors.Hand; b.ToolTip = tip; b.VerticalAlignment = VerticalAlignment.Center;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        }

        private void RefreshPin()
        {
            _pinButton.Background = Pinned ? T.Accent : Brushes.Transparent;
            _pinButton.Child = Icons.Make("pin", Pinned ? T.AccentText : T.Dim, 15);
        }

        /// <summary>Puts the strip on the outside edge: right when docked right (or floating), left when docked left.</summary>
        public void SetRailSide(bool left)
        {
            Grid.SetColumn(_rail, left ? 0 : 1);
            Grid.SetColumn(_fly, left ? 1 : 0);
            _root.ColumnDefinitions[0].Width = left ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            _root.ColumnDefinitions[1].Width = left ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            _fly.BorderThickness = left ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        }

        private void Toggle(Page page)
        {
            if (IsExpanded && Page == page) Collapse();
            else ShowPage(page);
        }

        public void ShowPage(Page page)
        {
            Page = page;
            _pageHost.Content = _pages[page];
            _flyTitle.Text = page switch
            {
                Page.Chat => "Chat",
                Page.Friends => "Friends",
                Page.Session => "CoWork",
                Page.Teams => "Teams",
                Page.Commands => "My commands",
                _ => "Settings",
            };
            if (!IsExpanded)
            {
                IsExpanded = true;
                _fly.Visibility = Visibility.Visible;
                ExpandedChanged?.Invoke(true);
            }
            foreach (var kv in _railButtons) SetRailOn(kv.Value, kv.Key == page);
            RefreshPage();
            if (page == Page.Chat)
            {
                if (Current != null && IsShown()) Current.Unread = 0;
                RefreshTabs();
                RefreshBadges();
                CiUi.UpdateTitle();
                _scroll.ScrollToEnd();
                FocusInput();
            }
        }

        public void Collapse(bool raise = true)
        {
            _tuck.Stop();
            _fly.Visibility = Visibility.Collapsed;
            foreach (var kv in _railButtons) SetRailOn(kv.Value, false);
            if (!IsExpanded && raise) return;
            IsExpanded = false;
            if (raise) ExpandedChanged?.Invoke(false);
        }

        private void RefreshPage()
        {
            switch (Page)
            {
                case Page.Friends: RefreshFriends(); break;
                case Page.Session: RefreshSession(); break;
                case Page.Teams: RefreshTeams(); break;
                case Page.Commands: RefreshCommands(); break;
                case Page.Settings: RefreshSettings(); break;
            }
        }

        /// <summary>Unread messages on the Chat icon, friends online on Friends, a dot on CoWork while you're in a session.</summary>
        public void RefreshBadges()
        {
            int unread = ChatHub.Rooms.Sum(r => r.Unread);
            SetBadge(Page.Chat, unread > 0 ? unread.ToString() : null, T.Warn);
            int on = ChatHub.Settings.Friends.Count(f => ChatHub.IsOnline(f.User));
            SetBadge(Page.Friends, on > 0 ? on.ToString() : null, B("#2E9E5B"));
            SetBadge(Page.Session, LiveDrafting.IsActive ? "●" : null, B("#2E9E5B"));
        }

        private void SetBadge(Page p, string? text, Brush colour)
        {
            Border b = _badges[p];
            b.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            b.Background = colour;
            ((TextBlock)b.Child).Text = text ?? "";
        }

        private static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }

        private static ScrollViewer Scroll(UIElement content) => new()
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // ================================================================== chat page

        private void BuildChatPage()
        {
            _online.Foreground = T.Dim;
            _status.Foreground = T.Dim;
            _scroll.Content = _list;
            _scroll.Background = T.Card;
            _input.AcceptsReturn = true;
            _input.TextWrapping = TextWrapping.Wrap;
            _input.MinHeight = 30;
            _input.MaxHeight = 110;
            _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _input.Padding = new Thickness(5);
            StyleField(_input);
            _input.ToolTip = "Enter sends - Shift+Enter for a new line";
            _input.PreviewKeyDown += (_, e) =>
            {
                if (_suggest.IsOpen)
                {
                    if (e.Key is Key.Enter or Key.Tab) { e.Handled = true; PickSuggestion(); return; }
                    if (e.Key == Key.Down) { e.Handled = true; _suggestList.SelectedIndex = Math.Min(_suggestList.Items.Count - 1, _suggestList.SelectedIndex + 1); return; }
                    if (e.Key == Key.Up) { e.Handled = true; _suggestList.SelectedIndex = Math.Max(0, _suggestList.SelectedIndex - 1); return; }
                    if (e.Key == Key.Escape) { e.Handled = true; _suggest.IsOpen = false; return; }
                }
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; SendNow(); }
            };
            _input.TextChanged += (_, _) => UpdateSuggestions();
            _input.ToolTip = "Enter sends - Shift+Enter for a new line - type @ to mention someone";
            _suggestList.Background = T.Card;
            _suggestList.Foreground = T.Text;
            _suggestList.BorderBrush = T.Edge;
            _suggestList.MaxHeight = 180;
            _suggestList.MinWidth = 180;
            _suggestList.MouseLeftButtonUp += (_, _) => PickSuggestion();
            _suggest.Child = _suggestList;
            _suggest.PlacementTarget = _input;
            _suggest.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            StyleButton(_send, filled: true);
            _send.Margin = new Thickness(6, 0, 0, 0);
            _send.Click += (_, _) => SendNow();

            var inputRow = new DockPanel { Margin = new Thickness(6, 4, 6, 2) };
            DockPanel.SetDock(_send, Dock.Right);
            inputRow.Children.Add(_send);
            inputRow.Children.Add(_input);

            var grid = new Grid();
            for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Put(grid, _tabs, 0);
            Put(grid, _online, 1);
            Put(grid, new Border { BorderBrush = T.Edge, BorderThickness = new Thickness(0, 1, 0, 1), Child = _scroll }, 2);
            Put(grid, _live, 3);
            Put(grid, inputRow, 4);
            Put(grid, _status, 5);
            _pages[Page.Chat] = grid;
        }

        private static void Put(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

        private static void StyleField(Control c)
        {
            c.Background = T.Field;
            c.Foreground = T.Text;
            c.BorderBrush = T.Edge;
            if (c is TextBox tb) tb.CaretBrush = T.Text;
        }

        private static void StyleButton(Button b, bool filled)
        {
            b.Padding = new Thickness(12, 4, 12, 4);
            b.FontWeight = FontWeights.SemiBold;
            b.Background = filled ? T.Accent : Brushes.Transparent;
            b.Foreground = filled ? T.AccentText : T.Accent;
            b.BorderBrush = T.Accent;
        }

        /// <summary>Typing "@Sa..." shows the people it could be.</summary>
        private void UpdateSuggestions()
        {
            int caret = _input.CaretIndex;
            string text = _input.Text;
            int at = caret - 1;
            while (at >= 0 && (char.IsLetterOrDigit(text[at]) || text[at] is '.' or '_' or '-')) at--;
            bool start = at >= 0 && text[at] == '@' && (at == 0 || !char.IsLetterOrDigit(text[at - 1]));
            if (!start || Current?.IsTeams == true) { _suggest.IsOpen = false; return; }
            string typed = text.Substring(at + 1, caret - at - 1);
            var all = ChatHub.MentionCandidates();
            var names = all.Select(c => c.Name).ToList();
            var matches = all.Where(c => typed.Length == 0
                    || c.Name.Split(' ').Any(w => w.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    || c.User.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .Take(8).ToList();
            if (matches.Count == 0) { _suggest.IsOpen = false; return; }
            _atStart = at;
            _suggestList.Items.Clear();
            foreach (var (name, user) in matches)
                _suggestList.Items.Add(new ListBoxItem
                {
                    Content = $"{name}   {Mentions.HandleFor(name, names)}{(ChatHub.IsOnline(user) ? "  ●" : "")}",
                    Tag = Mentions.HandleFor(name, names), Foreground = T.Text,
                });
            _suggestList.SelectedIndex = 0;
            _suggest.IsOpen = true;
        }

        private void PickSuggestion()
        {
            if (_suggestList.SelectedItem is not ListBoxItem item || _atStart < 0) { _suggest.IsOpen = false; return; }
            int caret = _input.CaretIndex;
            string handle = (string)item.Tag + " ";
            _input.Text = _input.Text[.._atStart] + handle + _input.Text[caret..];
            _input.CaretIndex = _atStart + handle.Length;
            _suggest.IsOpen = false;
            _input.Focus();
        }

        public void FocusInput() => Dispatcher.BeginInvoke(new Action(() => { if (Page == Page.Chat && IsExpanded) { _input.Focus(); Keyboard.Focus(_input); } }));

        /// <summary>Switches the chat to a room (and opens the chat page unless told not to).</summary>
        public void Select(Room room, bool expand = true)
        {
            Current = room;
            if (expand) ShowPage(Page.Chat);
            if (IsShown() && IsExpanded && Page == Page.Chat) room.Unread = 0;
            RefreshTabs();
            RefreshOnline();
            ShowAll();
            RefreshBadges();
            CiUi.UpdateTitle();
        }

        /// <summary>The panel was shown again.</summary>
        public void OnShown()
        {
            if (IsExpanded && Page == Page.Chat && Current != null) Current.Unread = 0;
            RefreshTabs();
            RefreshBadges();
            CiUi.UpdateTitle();
            _scroll.ScrollToEnd();
        }

        /// <summary>Whether you'd be looking at this room right now.</summary>
        public bool IsWatching(Room room) => IsShown() && IsExpanded && Page == Page.Chat && Current == room;

        public void RefreshTabs()
        {
            _tabs.Children.Clear();
            foreach (Room r in ChatHub.Rooms.Where(r => !r.Hidden))
            {
                bool on = r == Current;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock
                {
                    Text = r.Title + (r.Unread > 0 ? $"  {r.Unread}" : ""), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = on ? T.AccentText : r.Unread > 0 ? T.Accent : T.Text,
                    FontWeight = on || r.Unread > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                });
                Room target = r;
                bool closable = r.Id != Core.Rooms.Lobby;
                if (closable)
                {
                    // The x closes the tab only - the chat and its history stay.
                    var x = new TextBlock
                    {
                        Text = "✕", FontSize = 10, Margin = new Thickness(7, 0, -3, 0), VerticalAlignment = VerticalAlignment.Center,
                        Foreground = on ? T.AccentText : T.Dim, Cursor = Cursors.Hand, ToolTip = "Close this tab (the chat is kept - reopen it from +)",
                    };
                    x.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseRoom(target); };
                    row.Children.Add(x);
                }
                var tab = new Border
                {
                    Child = row, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(12),
                    Background = on ? T.Accent : T.Card, Cursor = Cursors.Hand,
                    ToolTip = r.IsTeams ? "Your Teams chat (shows in Teams too)"
                        : r.Id == Core.Rooms.Lobby ? "Everyone"
                        : Core.Rooms.IsPrivate(r.Id) ? "Private - just the two of you"
                        : $"Everyone working on job {r.Id[4..]} (from the drawing's folder)",
                };
                tab.MouseLeftButtonUp += (_, _) => { Select(target); FocusInput(); };
                tab.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle && closable) CloseRoom(target); }; // middle-click closes too
                _tabs.Children.Add(tab);
            }

            // Closed chats: "+3" lists them to reopen.
            var hidden = ChatHub.Rooms.Where(r => r.Hidden).ToList();
            if (hidden.Count == 0) return;
            var more = new Border
            {
                Child = new TextBlock { Text = $"+{hidden.Count}", Foreground = T.Dim, FontWeight = FontWeights.SemiBold },
                Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent, BorderBrush = T.Edge, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                ToolTip = "Closed chats - click to reopen one",
            };
            var menu = new ContextMenu();
            foreach (Room h in hidden)
            {
                Room target = h;
                var item = new MenuItem { Header = h.Title + (h.Unread > 0 ? $"  ({h.Unread} new)" : "") };
                item.Click += (_, _) => { ChatHub.SetHidden(target, false); Select(target); };
                menu.Items.Add(item);
            }
            more.ContextMenu = menu;
            more.MouseLeftButtonUp += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
            _tabs.Children.Add(more);
        }

        private void CloseRoom(Room r)
        {
            ChatHub.SetHidden(r, true);
            if (Current == r) Select(ChatHub.Rooms.First(x => x.Id == Core.Rooms.Lobby), expand: false);
            else RefreshTabs();
            RefreshBadges();
            CiUi.UpdateTitle();
        }

        private void RefreshOnline()
        {
            if (Current == null) return;
            if (Current.IsTeams)
            {
                string? st = Current.TeamsFriend is { } tf ? TeamsLink.StatusOf(tf) : null;
                _online.Text = st == null ? "Teams chat" : $"Teams: {st}";
                _online.ToolTip = null;
                return;
            }
            var names = Current.Online.Select(p => p.Name).ToList();
            _online.Text = names.Count == 0 ? "Nobody else here right now" : $"Here ({names.Count}): " + string.Join(", ", names);
            _online.ToolTip = string.Join("\n", Current.Online.Select(p => $"{p.Name} ({p.User}) on {p.Machine}"));
        }

        private void RefreshStatus()
        {
            if (ChatHub.Problem == null)
            {
                _status.Text = $"● Connected as {ChatHub.Settings.Name}";
                _status.Foreground = T.Good;
            }
            else
            {
                _status.Text = "● " + ChatHub.Problem;
                _status.Foreground = T.Warn;
            }
            _status.ToolTip = $"Folder: {ChatHub.DataFolder}";
            _send.IsEnabled = ChatHub.Connected;
        }

        public void RefreshLive()
        {
            if (!LiveDrafting.IsActive) { _live.Visibility = Visibility.Collapsed; return; }
            var people = new[] { ChatHub.Settings.Name + " (you)" }.Concat(LiveDrafting.People.Select(p => p.Name + (p.Host ? " (host)" : "")));
            _live.Text = $"● CoWorking in {Path.GetFileName(LiveDrafting.DrawingPath)} with: {string.Join(", ", people)}";
            _live.Foreground = T.Good;
            _live.Visibility = Visibility.Visible;
        }

        private void SendNow()
        {
            if (Current == null) return;
            if (ChatHub.Send(Current, _input.Text)) _input.Clear();
            FocusInput();
        }

        private void ShowAll()
        {
            _list.Children.Clear();
            _previous = null;
            if (Current == null) return;
            if (Current.Messages.Count == 0)
                _list.Children.Add(new TextBlock
                {
                    Text = Current.IsTeams ? (TeamsLink.SignedIn ? "Loading your Teams chat..." : "Sign in to Teams to see this chat.")
                        : Current.Id == Core.Rooms.Lobby ? "No messages yet - say hello!"
                        : Core.Rooms.IsPrivate(Current.Id) ? "No messages yet - only the two of you can see this chat."
                        : $"No messages yet for job {Current.Id[4..]}.",
                    Foreground = T.Dim, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap,
                });
            foreach (ChatMessage m in Current.Messages) Add(m, scroll: false);
            _scroll.ScrollToEnd();
        }

        private void Add(ChatMessage m, bool scroll)
        {
            if (_list.Children.Count == 1 && _list.Children[0] is TextBlock) _list.Children.Clear();
            bool atBottom = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 20;
            bool mine = ChatHub.IsMine(m);
            DateTime local = m.Time.LocalDateTime;
            bool sameRun = _previous != null && _previous.User == m.User && m.Time - _previous.Time < TimeSpan.FromMinutes(5)
                           && _previous.Time.LocalDateTime.Date == local.Date;

            if (_previous == null || _previous.Time.LocalDateTime.Date != local.Date)
                _list.Children.Add(new TextBlock
                {
                    Text = local.Date == DateTime.Today ? "Today" : local.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : local.ToString("dddd d MMMM"),
                    Foreground = T.Dim, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2),
                });

            var body = new StackPanel();
            if (!sameRun)
            {
                var head = new TextBlock { Margin = new Thickness(0, 0, 0, 1) };
                head.Inlines.Add(new Run(m.Name) { FontWeight = FontWeights.SemiBold, Foreground = NameColour(m.User) });
                head.Inlines.Add(new Run("  " + local.ToString("HH:mm")) { Foreground = T.Dim, FontSize = 10.5 });
                if (!mine && !m.User.StartsWith("teams:", StringComparison.Ordinal))
                {
                    head.ContextMenu = PersonMenu(m.User, m.Name);
                    head.Cursor = Cursors.Hand;
                    head.ToolTip = "Click for options";
                    head.MouseLeftButtonUp += (_, _) => head.ContextMenu.IsOpen = true;
                }
                body.Children.Add(head);
            }
            body.Children.Add(new TextBox
            {
                Text = m.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = T.Text, Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0),
            });
            if (m.Button is { } b)
            {
                var action = new Button { Content = b.Label, HorizontalAlignment = HorizontalAlignment.Left };
                StyleButton(action, filled: true);
                action.Margin = new Thickness(0, 4, 0, 2);
                string command = b.Command;
                action.Click += (_, _) => Commands.Run(command);
                body.Children.Add(action);
            }
            else if (!string.IsNullOrEmpty(m.Drawing) && (!sameRun || m.Drawing != _previous?.Drawing))
            {
                var link = new Hyperlink(new Run(Path.GetFileName(m.Drawing))) { Foreground = T.Dim, ToolTip = "Open " + m.Drawing };
                string path = m.Drawing!;
                link.Click += (_, _) => OpenDrawing(path);
                var line = new TextBlock { FontSize = 10.5, Foreground = T.Dim, Margin = new Thickness(0, 1, 0, 0) };
                line.Inlines.Add(new Run("from "));
                line.Inlines.Add(link);
                body.Children.Add(line);
            }
            bool forMe = ChatHub.MentionsMe(m);
            if (forMe && !sameRun) body.Children.Insert(0, new TextBlock { Text = "@ mentioned you", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = T.Accent });
            _list.Children.Add(new Border
            {
                Child = body, Background = mine || forMe ? T.Mine : Brushes.Transparent, CornerRadius = new CornerRadius(6),
                BorderBrush = forMe ? T.Warn : T.Accent, BorderThickness = mine || forMe ? new Thickness(3, 0, 0, 0) : new Thickness(0),
                Padding = new Thickness(7, sameRun ? 1 : 5, 7, 4), Margin = new Thickness(0, sameRun ? 0 : 3, 0, 0),
            });
            _previous = m;
            while (_list.Children.Count > 600) _list.Children.RemoveAt(0);
            if (scroll && (atBottom || mine)) _scroll.ScrollToEnd();
        }

        private ContextMenu PersonMenu(string user, string name)
        {
            var menu = new ContextMenu();
            var msg = new MenuItem { Header = $"Message {name} privately" };
            msg.Click += (_, _) => { Select(ChatHub.OpenPrivate(user)); FocusInput(); };
            menu.Items.Add(msg);
            var friend = new MenuItem();
            menu.Opened += (_, _) => friend.Header = ChatHub.FriendOf(user) == null ? $"Add {name} as a friend" : $"Remove {name} from friends";
            friend.Click += (_, _) =>
            {
                if (ChatHub.FriendOf(user) == null) ChatHub.AddFriend(user, name, ChatHub.LastSeenOf(user)?.Email);
                else ChatHub.RemoveFriend(user);
            };
            menu.Items.Add(friend);
            return menu;
        }

        // ================================================================== friends page

        private void RefreshFriends()
        {
            _friends.Children.Clear();
            if (ChatHub.Settings.Friends.Count == 0)
                _friends.Children.Add(Note("No friends yet. Add people below, or click someone's name in the chat."));
            foreach (Friend f in ChatHub.Settings.Friends.OrderByDescending(f => ChatHub.IsOnline(f.User)).ThenBy(f => f.Name))
                _friends.Children.Add(FriendCard(f));

            _friends.Children.Add(Heading("Add a friend"));
            var others = ChatHub.OnlineNow.Where(p => ChatHub.FriendOf(p.User) == null).OrderBy(p => p.Name).ToList();
            if (others.Count == 0) _friends.Children.Add(Note("Nobody else online right now who isn't already a friend."));
            foreach (OnlinePerson p in others)
            {
                var row = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };
                var add = SmallButton("+ Add", () => ChatHub.AddFriend(p.User, p.Name, p.Email));
                DockPanel.SetDock(add, Dock.Right);
                row.Children.Add(add);
                row.Children.Add(new TextBlock { Text = $"{p.Name}  ({p.User})", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _friends.Children.Add(row);
            }
            var login = new TextBox { Padding = new Thickness(4, 2, 4, 2), ToolTip = "Their Windows login, or their work email" };
            StyleField(login);
            void AddTyped()
            {
                string text = login.Text.Trim();
                string u = text.Contains('@') ? text[..text.IndexOf('@')] : text;
                if (u.Length == 0) return;
                ChatHub.AddFriend(u, ChatHub.LastSeenOf(u)?.Name ?? u, text.Contains('@') ? text : ChatHub.LastSeenOf(u)?.Email);
            }
            login.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTyped(); };
            _friends.Children.Add(Note("Or type their login or work email:"));
            _friends.Children.Add(Row(login, SmallButton("Add", AddTyped)));
        }

        private UIElement FriendCard(Friend f)
        {
            bool online = ChatHub.IsOnline(f.User);
            OnlinePerson? seen = ChatHub.LastSeenOf(f.User);
            var body = new StackPanel();

            var head = new DockPanel();
            var avatar = Avatar(f.Name, f.User, online ? T.Good : T.Dim);
            avatar.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(avatar, Dock.Left);
            head.Children.Add(avatar);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = f.Name, FontWeight = FontWeights.SemiBold });
            string where;
            if (online && seen != null)
            {
                string? job = seen.Rooms.FirstOrDefault(r => r.StartsWith("job-", StringComparison.Ordinal));
                where = seen.LiveSession != null ? "CoWorking" : "Online";
                where += (job != null ? $" · job {job[4..]}" : "") + (seen.Drawing != null ? $" · {seen.Drawing}" : "");
            }
            else where = seen == null ? "Not seen yet" : "Last seen " + Ago(seen.Seen);
            if (TeamsLink.StatusOf(f) is { } teams) where += " · Teams: " + teams;
            text.Children.Add(new TextBlock { Text = where, Foreground = T.Dim, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            head.Children.Add(text);
            body.Children.Add(head);

            var buttons = new WrapPanel { Margin = new Thickness(36, 5, 0, 0) };
            if (_confirmRemove == f.User)
            {
                buttons.Children.Add(new TextBlock { Text = $"Remove {f.Name}?", Foreground = T.Warn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 2) });
                buttons.Children.Add(SmallButton("Yes", () => { _confirmRemove = null; ChatHub.RemoveFriend(f.User); }));
                buttons.Children.Add(SmallButton("No", () => { _confirmRemove = null; RefreshFriends(); }, quiet: true));
            }
            else
            {
                if (online && seen?.LiveSession is { } session && session != LiveDrafting.SessionId)
                    buttons.Children.Add(SmallButton("Join", () => Commands.Run("CWJOIN " + session), filled: true));
                buttons.Children.Add(SmallButton("Message", () => { Select(ChatHub.OpenPrivate(f.User)); FocusInput(); }));
                if (TeamsLink.SignedIn) buttons.Children.Add(SmallButton("Teams chat", () => OpenTeams(f)));
                buttons.Children.Add(SmallButton("Remove", () => { _confirmRemove = f.User; RefreshFriends(); }, quiet: true));
            }
            body.Children.Add(buttons);

            return new Border
            {
                Child = body, Background = T.Card, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 0, 0, 5),
                ToolTip = f.User + ((f.Email ?? seen?.Email) is { } email ? "\n" + email : "") + (seen != null ? $"\non {seen.Machine}" : ""),
            };
        }

        private async void OpenTeams(Friend f)
        {
            Room? r = await TeamsLink.OpenChatAsync(f);
            if (r != null) { Select(r); FocusInput(); }
            else RefreshPage();
        }

        private static FrameworkElement Avatar(string name, string user, Brush status)
        {
            string initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            var g = new Grid { Width = 28, Height = 28 };
            g.Children.Add(new Ellipse { Fill = NameColour(user) });
            g.Children.Add(new TextBlock
            {
                Text = initials, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#10222B"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            g.Children.Add(new Ellipse
            {
                Width = 11, Height = 11, Fill = status, Stroke = T.Card, StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2),
            });
            return g;
        }

        // ================================================================== CoWork (live session) page

        private void RefreshSession()
        {
            _session.Children.Clear();
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (LiveDrafting.IsActive)
            {
                _session.Children.Add(Heading("You're CoWorking in"));
                _session.Children.Add(new TextBlock { Text = Path.GetFileName(LiveDrafting.DrawingPath), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 6), TextWrapping = TextWrapping.Wrap });
                _session.Children.Add(Avatars(new[] { (ChatHub.Settings.Name + " (you)", Environment.UserName) }
                    .Concat(LiveDrafting.People.Select(p => (p.Name + (p.Host ? " (host)" : ""), p.User)))));
                _session.Children.Add(Note(LiveDrafting.IsHost
                    ? "You're the host: only you save, and your save includes everyone's work."
                    : "Only the host saves. Your copy is read-only."));
                _session.Children.Add(BigButton(LiveDrafting.IsHost ? "End CoWork for everyone" : "Leave CoWork", "join", () => Commands.Run("CWLEAVE"), filled: false));
            }
            else
            {
                _session.Children.Add(Note("Edit a drawing together, live: everyone's changes and crosshairs show up on each other's screens."));
                string? name = doc != null && Path.IsPathRooted(doc.Name) ? Path.GetFileName(doc.Name) : null;
                _session.Children.Add(BigButton(name != null ? $"CoWork in {name}" : "CoWork", "cowork", () => Commands.Run("COWORK"), filled: true));
                if (name == null) _session.Children.Add(Note("Save the drawing on the network first."));
            }

            _session.Children.Add(Heading("Join someone"));
            List<LiveSession> sessions;
            try { sessions = ChatHub.Connected ? LiveFolder.ActiveSessions(ChatHub.DataFolder) : new(); }
            catch { sessions = new(); }
            sessions = sessions.Where(s => s.Id != LiveDrafting.SessionId).ToList();
            if (sessions.Count == 0) _session.Children.Add(Note("Nobody else is CoWorking right now."));
            foreach (LiveSession s in sessions)
            {
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock { Text = Path.GetFileName(s.Drawing), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = $"{s.HostName} · started {s.Started.LocalDateTime:HH:mm}", Foreground = T.Dim, FontSize = 11 });
                _session.Children.Add(new Border
                {
                    Background = T.Card, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5),
                    Child = Row(info, SmallButton("Join", () => Commands.Run("CWJOIN " + s.Id), filled: true)),
                });
            }
        }

        private static UIElement Avatars(IEnumerable<(string Name, string User)> people)
        {
            var p = new StackPanel { Margin = new Thickness(4, 0, 4, 8) };
            foreach (var (name, user) in people)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var a = Avatar(name, user, T.Good);
                a.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(a);
                row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                p.Children.Add(row);
            }
            return p;
        }

        // ================================================================== Teams page

        private void RefreshTeams()
        {
            _teams.Children.Clear();
            if (!TeamsLink.IsSetUp)
            {
                _teams.Children.Add(Note("The Teams link is off until IT approves it (see TEAMS-IT-SETUP.md). Once it's on, you'll see your friends' Teams status here and can chat with them in Teams without leaving Civil 3D."));
                return;
            }
            if (TeamsLink.SignedIn)
            {
                _teams.Children.Add(new TextBlock { Text = $"Signed in as {TeamsLink.Me!.Name}", Foreground = T.Good, Margin = new Thickness(4, 0, 4, 6) });
                _teams.Children.Add(SmallButton("Sign out", () => Commands.Run("CWTEAMS OUT"), quiet: true));
            }
            else
            {
                _teams.Children.Add(Note("Sign in with your work Microsoft account. It opens Microsoft's sign-in page in your browser."));
                _teams.Children.Add(BigButton("Sign in to Teams", "teams", () => Commands.Run("CWTEAMS IN"), filled: true));
            }
            if (TeamsLink.Problem != null)
                _teams.Children.Add(new TextBlock { Text = TeamsLink.Problem, Foreground = T.Warn, FontSize = 11, Margin = new Thickness(4, 4, 4, 0), TextWrapping = TextWrapping.Wrap });
            if (!TeamsLink.SignedIn) return;

            _teams.Children.Add(Heading("Your friends in Teams"));
            if (ChatHub.Settings.Friends.Count == 0) _teams.Children.Add(Note("Add friends first (the people icon)."));
            foreach (Friend f in ChatHub.Settings.Friends)
            {
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock { Text = f.Name, FontWeight = FontWeights.SemiBold });
                info.Children.Add(new TextBlock { Text = TeamsLink.StatusOf(f) ?? "status unknown", Foreground = T.Dim, FontSize = 11 });
                _teams.Children.Add(new Border
                {
                    Background = T.Card, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5),
                    Child = Row(info, SmallButton("Chat", () => OpenTeams(f))),
                });
            }
        }

        // ================================================================== my commands page

        private void RefreshCommands()
        {
            _commands.Children.Clear();
            _commands.Children.Add(Note("These are the buttons on the strip below the CoWork icons. Any command works, typed as you would at the command line."));
            var list = ChatHub.Settings.MenuCommands.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                int at = i;
                MenuCommand mc = list[i];
                var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new ContentControl { Content = Icons.Make(mc.Icon, T.Accent, 16), Margin = new Thickness(0, 0, 8, 0) });
                var words = new StackPanel();
                words.Children.Add(new TextBlock { Text = mc.Label, FontWeight = FontWeights.SemiBold });
                words.Children.Add(new TextBlock { Text = mc.Command, Foreground = T.Dim, FontSize = 11, FontFamily = new FontFamily("Consolas") });
                info.Children.Add(words);
                var moves = new StackPanel { Orientation = Orientation.Horizontal };
                moves.Children.Add(SmallButton("▲", () => { if (at > 0) { (list[at - 1], list[at]) = (list[at], list[at - 1]); SaveCommands(list); } }, quiet: true));
                moves.Children.Add(SmallButton("▼", () => { if (at < list.Count - 1) { (list[at + 1], list[at]) = (list[at], list[at + 1]); SaveCommands(list); } }, quiet: true));
                moves.Children.Add(SmallButton("✕", () => { list.RemoveAt(at); SaveCommands(list); }, quiet: true));
                _commands.Children.Add(new Border
                {
                    Background = T.Card, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 6, 5), Margin = new Thickness(0, 0, 0, 4),
                    Child = Row(info, moves),
                });
            }

            _commands.Children.Add(Heading("Add a command"));
            var label = new TextBox { Padding = new Thickness(4, 2, 4, 2) };
            var command = new TextBox { Padding = new Thickness(4, 2, 4, 2), FontFamily = new FontFamily("Consolas") };
            StyleField(label);
            StyleField(command);
            _commands.Children.Add(Note("Name on the button:"));
            _commands.Children.Add(label);
            _commands.Children.Add(Note("Command (e.g. LAYISO, ZOOM E, -PURGE A * N):"));
            _commands.Children.Add(command);
            _commands.Children.Add(Note("Icon:"));
            string icon = "cmd";
            var icons = new WrapPanel();
            foreach (string name in Icons.ForCommands)
            {
                string n = name;
                var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Child = Icons.Make(n, T.Accent, 17), BorderBrush = T.Accent };
                b.BorderThickness = n == icon ? new Thickness(1.5) : new Thickness(0);
                b.MouseLeftButtonUp += (_, _) =>
                {
                    icon = n;
                    foreach (Border o in icons.Children) o.BorderThickness = new Thickness(0);
                    b.BorderThickness = new Thickness(1.5);
                };
                icons.Children.Add(b);
            }
            _commands.Children.Add(icons);
            var problem = new TextBlock { Foreground = T.Warn, FontSize = 11, Margin = new Thickness(4, 2, 4, 2) };
            _commands.Children.Add(BigButton("Add to the strip", "plus", () =>
            {
                string c = command.Text.Trim(), l = label.Text.Trim();
                if (c.Length == 0) { problem.Text = "Type the command it should run."; return; }
                list.Add(new MenuCommand(l.Length > 0 ? l : c, c, icon));
                SaveCommands(list);
            }, filled: true));
            _commands.Children.Add(problem);
            _commands.Children.Add(SmallButton("Put back the starting set", () => SaveCommands(null), quiet: true));
        }

        private void SaveCommands(List<MenuCommand>? list)
        {
            CiSettings mine = CiSettings.LoadUserOnly();
            mine.Commands = list;
            mine.Save();
            ChatHub.Settings.Commands = list;
            RefreshRailCommands();
            if (Page == Page.Commands && IsExpanded) RefreshCommands();
        }

        // ================================================================== settings page

        private void RefreshSettings()
        {
            _settings.Children.Clear();
            var name = new TextBox { Text = ChatHub.Settings.Name, Padding = new Thickness(4, 2, 4, 2) };
            StyleField(name);
            _settings.Children.Add(Heading("Your name"));
            _settings.Children.Add(Row(name, SmallButton("Save", () =>
            {
                if (name.Text.Trim().Length == 0) return;
                CiSettings mine = CiSettings.LoadUserOnly();
                mine.DisplayName = name.Text.Trim();
                mine.Save();
                ChatHub.Reload();
                RefreshStatus();
            })));

            _settings.Children.Add(Heading("Pop-ups"));
            _settings.Children.Add(Check("New messages while CoWork is tucked away (@mentions always pop up)", ChatHub.Settings.Popups, on =>
            {
                CiSettings mine = CiSettings.LoadUserOnly();
                mine.Popups = on;
                mine.Save();
                ChatHub.Settings.Popups = on;
            }));
            _settings.Children.Add(Check("When a friend comes online", ChatHub.Settings.FriendAlerts, ChatHub.SetFriendAlerts));

            _settings.Children.Add(Heading("Colours"));
            var schemes = new WrapPanel { Margin = new Thickness(4, 0, 4, 2) };
            foreach (Palette pal in Brand.Palettes)
            {
                Palette pick = pal;
                bool on = ChatHub.Settings.ThemeName == pal.Id;
                var chip = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (string hex in new[] { pal.Back, pal.Card, pal.Accent, pal.Text })
                    chip.Children.Add(new Border { Width = 11, Height = 18, Background = B(hex) });
                var b = new Border
                {
                    Child = chip, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = pal.Name, CornerRadius = new CornerRadius(4),
                    BorderBrush = on ? T.Accent : T.Edge, BorderThickness = new Thickness(on ? 2.5 : 1), Padding = new Thickness(2),
                };
                b.MouseLeftButtonUp += (_, _) =>
                {
                    CiSettings mine = CiSettings.LoadUserOnly();
                    mine.Theme = pick.Id;
                    mine.Save();
                    ChatHub.Settings.Theme = pick.Id;
                    CiUi.RefreshIcon();
                    RefreshSettings();
                    themeNote.Visibility = Visibility.Visible;
                };
                schemes.Children.Add(b);
            }
            _settings.Children.Add(schemes);
            _settings.Children.Add(new TextBlock { Text = Brand.PaletteFor(ChatHub.Settings.ThemeName).Name, Foreground = T.Dim, FontSize = 11, Margin = new Thickness(4, 0, 4, 2) });
            _settings.Children.Add(themeNote);

            _settings.Children.Add(Heading("Logo"));
            _settings.Children.Add(Note("Shown on the status bar and at the top of the strip."));
            var logos = new WrapPanel { Margin = new Thickness(4, 0, 4, 4) };
            Palette now = Brand.PaletteFor(ChatHub.Settings.ThemeName);
            foreach (var (id, lname) in Brand.Logos)
            {
                string pick = id;
                bool on = ChatHub.Settings.LogoName == id;
                var b = new Border
                {
                    Child = Logos.Image(id, B(now.Accent), B(now.AccentText), 26), Padding = new Thickness(5), Margin = new Thickness(0, 0, 6, 6),
                    Cursor = Cursors.Hand, ToolTip = lname, CornerRadius = new CornerRadius(6), Background = T.Card,
                    BorderBrush = on ? T.Accent : Brushes.Transparent, BorderThickness = new Thickness(2),
                };
                b.MouseLeftButtonUp += (_, _) =>
                {
                    CiSettings mine = CiSettings.LoadUserOnly();
                    mine.Logo = pick;
                    mine.Save();
                    ChatHub.Settings.Logo = pick;
                    CiUi.RefreshIcon();
                    RefreshSettings();
                };
                logos.Children.Add(b);
            }
            _settings.Children.Add(logos);

            _settings.Children.Add(Heading("Your crosshair colour"));
            _settings.Children.Add(Note("In a CoWork session, others see your cursor, and anything you're editing (locked to you), in this colour."));
            var swatches = new WrapPanel { Margin = new Thickness(4, 0, 4, 4) };
            string mineHex = LiveDrafting.MyColour;
            foreach (var (cname, hex) in CrosshairColours.Choices)
            {
                string pick = hex;
                bool on = string.Equals(hex, mineHex, StringComparison.OrdinalIgnoreCase);
                var sw = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    Background = B(hex), BorderBrush = T.Text, BorderThickness = new Thickness(on ? 3 : 0), ToolTip = cname,
                };
                sw.MouseLeftButtonUp += (_, _) => SetCrosshair(pick);
                swatches.Children.Add(sw);
            }
            _settings.Children.Add(swatches);
            bool custom = !CrosshairColours.Choices.Any(c => string.Equals(c.Hex, mineHex, StringComparison.OrdinalIgnoreCase));
            var wheelToggle = new Expander
            {
                Header = custom ? $"Custom colour ({mineHex})" : "Custom colour (colour wheel)...", Foreground = T.Text,
                Margin = new Thickness(4, 0, 4, 4), IsExpanded = _wheelOpen,
            };
            wheelToggle.Expanded += (_, _) => _wheelOpen = true;
            wheelToggle.Collapsed += (_, _) => _wheelOpen = false;
            wheelToggle.Content = ColourWheel(mineHex, SetCrosshair);
            _settings.Children.Add(wheelToggle);

            _settings.Children.Add(Heading("Shared folder"));
            _settings.Children.Add(new TextBlock { Text = ChatHub.DataFolder, Foreground = T.Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 4) });
            _settings.Children.Add(SmallButton("Change folder...", () => Commands.Run("CWFOLDER"), quiet: true));
            _settings.Children.Add(Note("CoWork " + typeof(CoWorkPanel).Assembly.GetName().Version?.ToString(3)));
        }

        private bool _wheelOpen;

        private void SetCrosshair(string hex)
        {
            CiSettings mine = CiSettings.LoadUserOnly();
            mine.CrosshairColour = hex;
            mine.Save();
            ChatHub.Settings.CrosshairColour = hex;
            LiveDrafting.Touch();
            RefreshSettings();
        }

        /// <summary>
        /// A colour wheel: around = hue, out from the middle = how strong, slider = brightness. The swatch shows the
        /// colour; you can also type "#RRGGBB". Nothing changes until "Use this colour".
        /// </summary>
        private static UIElement ColourWheel(string startHex, Action<string> use)
        {
            const int Size = 170;
            var (hue, sat, val) = CrosshairColours.ToHsv(startHex);
            var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };

            var wheel = new System.Windows.Media.Imaging.WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
            void DrawWheel()
            {
                var px = new int[Size * Size];
                double r = Size / 2.0;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        double dx = x - r + 0.5, dy = y - r + 0.5, d = Math.Sqrt(dx * dx + dy * dy);
                        if (d > r) continue;
                        double h = Math.Atan2(dy, dx) * 180 / Math.PI;
                        int rgb = Convert.ToInt32(CrosshairColours.FromHsv(h, d / r, val).TrimStart('#'), 16);
                        double edge = Math.Clamp(r - d, 0, 1); // soft edge
                        px[y * Size + x] = ((int)(edge * 255) << 24) | rgb;
                    }
                wheel.WritePixels(new Int32Rect(0, 0, Size, Size), px, Size * 4, 0);
            }
            DrawWheel();

            var canvas = new Canvas { Width = Size, Height = Size, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Cross, Background = Brushes.Transparent };
            canvas.Children.Add(new Image { Source = wheel, Width = Size, Height = Size });
            var marker = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
            var markerShadow = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
            canvas.Children.Add(markerShadow);
            canvas.Children.Add(marker);

            var preview = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), BorderBrush = T.Edge, BorderThickness = new Thickness(1), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var hexBox = new TextBox { Width = 80, Padding = new Thickness(4, 2, 4, 2), FontFamily = new FontFamily("Consolas"), Margin = new Thickness(12, 8, 0, 0) };
            StyleField(hexBox);
            var slider = new Slider { Minimum = 0.2, Maximum = 1, Value = Math.Max(0.2, val), Width = Size, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Brightness" };

            string Current() => CrosshairColours.FromHsv(hue, sat, val);
            void Show(bool fromBox = false)
            {
                double r = Size / 2.0, a = hue * Math.PI / 180;
                double mx = r + Math.Cos(a) * sat * r, my = r + Math.Sin(a) * sat * r;
                Canvas.SetLeft(marker, mx - 7); Canvas.SetTop(marker, my - 7);
                Canvas.SetLeft(markerShadow, mx - 8); Canvas.SetTop(markerShadow, my - 8);
                string hex = Current();
                preview.Background = B(hex);
                if (!fromBox) hexBox.Text = hex;
            }
            void PickAt(Point p)
            {
                double r = Size / 2.0, dx = p.X - r, dy = p.Y - r;
                hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                sat = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / r);
                Show();
            }
            canvas.MouseLeftButtonDown += (_, e) => { canvas.CaptureMouse(); PickAt(e.GetPosition(canvas)); };
            canvas.MouseMove += (_, e) => { if (canvas.IsMouseCaptured) PickAt(e.GetPosition(canvas)); };
            canvas.MouseLeftButtonUp += (_, _) => canvas.ReleaseMouseCapture();
            slider.ValueChanged += (_, _) => { val = slider.Value; DrawWheel(); Show(); };
            hexBox.TextChanged += (_, _) =>
            {
                string t = hexBox.Text.Trim();
                if (!t.StartsWith('#')) t = "#" + t;
                if (!CrosshairColours.IsValid(t) || string.Equals(t, Current(), StringComparison.OrdinalIgnoreCase)) return;
                (hue, sat, val) = CrosshairColours.ToHsv(t);
                if (Math.Abs(slider.Value - Math.Max(0.2, val)) > 0.001) { slider.Value = Math.Max(0.2, val); }
                val = CrosshairColours.ToHsv(t).Value;
                DrawWheel();
                Show(fromBox: true);
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(canvas);
            var side = new StackPanel();
            side.Children.Add(preview);
            side.Children.Add(hexBox);
            row.Children.Add(side);
            panel.Children.Add(row);
            panel.Children.Add(slider);
            panel.Children.Add(Note("Pick a bright colour so it stands out on the drawing."));
            panel.Children.Add(SmallButton("Use this colour", () => use(Current()), filled: true));
            Show();
            return panel;
        }

        private readonly TextBlock themeNote = new()
        {
            Text = "The status-bar icon changes now; restart Civil 3D to recolour the strip.", FontSize = 11, Margin = new Thickness(4, 2, 4, 2), Visibility = Visibility.Collapsed,
        };

        private static CheckBox Check(string text, bool on, Action<bool> changed)
        {
            var c = new CheckBox { Content = text, IsChecked = on, Foreground = T.Text, Margin = new Thickness(4, 3, 4, 3) };
            c.Click += (_, _) => changed(c.IsChecked == true);
            return c;
        }

        // ================================================================== helpers

        private static DockPanel Row(UIElement main, UIElement right)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(main);
            return row;
        }

        private static TextBlock Heading(string text) =>
            new() { Text = text.ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = 11, Foreground = T.Accent, Margin = new Thickness(4, 12, 4, 4) };

        private static TextBlock Note(string text) =>
            new() { Text = text, Foreground = T.Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 4) };

        private static Button SmallButton(string text, Action click, bool quiet = false, bool filled = false)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 5, 2), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Background = filled ? T.Accent : Brushes.Transparent, Foreground = filled ? T.AccentText : quiet ? T.Dim : T.Accent,
                BorderBrush = quiet ? T.Edge : T.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            };
            b.Click += (_, _) => click();
            return b;
        }

        private static Button BigButton(string text, string icon, Action click, bool filled)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new ContentControl { Content = Icons.Make(icon, filled ? T.AccentText : T.Accent, 16), Margin = new Thickness(0, 0, 8, 0) });
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button
            {
                Content = content, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 4, 0, 6), FontWeight = FontWeights.SemiBold,
                Background = filled ? T.Accent : Brushes.Transparent, Foreground = filled ? T.AccentText : T.Accent, BorderBrush = T.Accent,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            b.Click += (_, _) => click();
            return b;
        }

        private static string Ago(DateTimeOffset t)
        {
            TimeSpan d = DateTimeOffset.UtcNow - t;
            if (d < TimeSpan.FromMinutes(2)) return "just now";
            if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min ago";
            if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours} h ago";
            if (d < TimeSpan.FromDays(7)) return t.LocalDateTime.ToString("dddd HH:mm");
            return t.LocalDateTime.ToString("d MMM");
        }

        private static Brush NameColour(string user)
        {
            int h = 0;
            foreach (char c in user.ToLowerInvariant()) h = (h * 31 + c) & 0x7fffffff;
            return B(NameColours[h % NameColours.Length]);
        }

        private static void OpenDrawing(string path)
        {
            try
            {
                var docs = AcApp.DocumentManager;
                foreach (Autodesk.AutoCAD.ApplicationServices.Document d in docs)
                    if (string.Equals(d.Name, path, StringComparison.OrdinalIgnoreCase)) { docs.MdiActiveDocument = d; return; }
                if (!File.Exists(path)) { Commands.Write("\nCan't find that drawing: " + path); return; }
                Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension.Open(docs, path, false);
            }
            catch (Exception ex)
            {
                Commands.Write("\nCouldn't open the drawing: " + ex.Message);
            }
        }
    }
}
