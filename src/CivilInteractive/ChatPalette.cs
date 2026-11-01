using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CivilInteractive.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilInteractive
{
    /// <summary>The CivilInteractive palette: room tabs, messages, who's online, and the drafting button.</summary>
    public sealed class ChatPalette : UserControl
    {
        private static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }

        // Teal on slate: CivilInteractive's own look.
        private static readonly Brush Back = B("#1F2A2E"), Card = B("#263439"), Text = B("#E8F0EE"), Dim = B("#8FA3A0"),
            Accent = B("#22B3A0"), Mine = B("#1E4A47"), Field = B("#172024"), Edge = B("#35484D"), Warn = B("#E57A6E"), Good = B("#5CC98A");

        private static readonly string[] NameColours = { "#5CC9F5", "#F5A35C", "#8BE07A", "#D58BF5", "#F57A8B", "#F5D35C", "#5CF5C9", "#A9B4F5" };

        private readonly WrapPanel _tabs = new() { Margin = new Thickness(6, 6, 6, 2) };
        private readonly TextBlock _online = new() { Margin = new Thickness(8, 2, 8, 4), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly StackPanel _list = new() { Margin = new Thickness(6) };
        private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        private readonly TextBox _input = new();
        private readonly Button _send = new() { Content = "Send" };
        private readonly Button _draft = new() { Content = "Draft together" };
        private readonly TextBlock _live = new() { Margin = new Thickness(8, 2, 8, 0), FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private readonly TextBlock _status = new() { Margin = new Thickness(8, 2, 8, 6), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
        private ChatMessage? _previous;

        public Room? Current { get; private set; }
        public Func<bool> IsShown { get; set; } = () => true;

        public ChatPalette()
        {
            Background = Back;
            Foreground = Text;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            _online.Foreground = Dim;
            _status.Foreground = Dim;
            _scroll.Content = _list;
            _scroll.Background = Card;

            _input.AcceptsReturn = true;
            _input.TextWrapping = TextWrapping.Wrap;
            _input.MinHeight = 30;
            _input.MaxHeight = 110;
            _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _input.Padding = new Thickness(5);
            _input.Background = Field;
            _input.Foreground = Text;
            _input.CaretBrush = Text;
            _input.BorderBrush = Edge;
            _input.ToolTip = "Enter sends - Shift+Enter for a new line";
            _input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; SendNow(); }
            };
            StyleButton(_send, filled: true);
            _send.Click += (_, _) => SendNow();
            StyleButton(_draft, filled: false);
            _draft.ToolTip = "Edit this drawing live with others (multiplayer drafting)";
            _draft.Click += (_, _) => Commands.Run(LiveDrafting.IsActive ? "CILIVELEAVE" : "CILIVE");

            var buttons = new DockPanel { Margin = new Thickness(6, 2, 6, 2) };
            DockPanel.SetDock(_draft, Dock.Left);
            buttons.Children.Add(_draft);
            var inputRow = new DockPanel { Margin = new Thickness(6, 4, 6, 2) };
            DockPanel.SetDock(_send, Dock.Right);
            inputRow.Children.Add(_send);
            inputRow.Children.Add(_input);

            var grid = new Grid();
            for (int i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Put(grid, _tabs, 0);
            Put(grid, _online, 1);
            Put(grid, new Border { BorderBrush = Edge, BorderThickness = new Thickness(0, 1, 0, 1), Child = _scroll }, 2);
            Put(grid, _live, 3);
            Put(grid, inputRow, 4);
            Put(grid, buttons, 5);
            Put(grid, _status, 6);
            Content = grid;

            ChatHub.RoomAdded += _ => RefreshTabs();
            ChatHub.RoomLoaded += r => { if (r == Current) ShowAll(); };
            ChatHub.MessageAdded += (r, m) => { if (r == Current) Add(m, scroll: true); };
            ChatHub.OnlineChanged += r => { if (r == Current) RefreshOnline(); };
            ChatHub.StatusChanged += RefreshStatus;
            ChatHub.JobRoomActivated += Select;
            LiveDrafting.Changed += RefreshLive;

            Select(ChatHub.Rooms.FirstOrDefault(r => r.Id == ChatHub.CurrentJobRoom) ?? ChatHub.Rooms[0]);
            RefreshStatus();
            RefreshLive();
        }

        private static void Put(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

        private static void StyleButton(Button b, bool filled)
        {
            b.Padding = new Thickness(12, 4, 12, 4);
            b.Margin = new Thickness(filled ? 6 : 0, 0, 0, 0);
            b.FontWeight = FontWeights.SemiBold;
            b.Background = filled ? Accent : Brushes.Transparent;
            b.Foreground = filled ? Brushes.White : Accent;
            b.BorderBrush = Accent;
        }

        public void FocusInput() => Dispatcher.BeginInvoke(new Action(() => { _input.Focus(); Keyboard.Focus(_input); }));

        public void Select(Room room)
        {
            Current = room;
            if (IsShown()) room.Unread = 0;
            RefreshTabs();
            RefreshOnline();
            ShowAll();
            CiUi.UpdateTitle();
        }

        public void OnShown()
        {
            if (Current != null) Current.Unread = 0;
            RefreshTabs();
            CiUi.UpdateTitle();
            _scroll.ScrollToEnd();
        }

        public void RefreshTabs()
        {
            _tabs.Children.Clear();
            foreach (Room r in ChatHub.Rooms)
            {
                bool on = r == Current;
                var label = new TextBlock
                {
                    Text = r.Title + (r.Unread > 0 ? $"  {r.Unread}" : ""),
                    Foreground = on ? Brushes.White : r.Unread > 0 ? Accent : Text,
                    FontWeight = on || r.Unread > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                };
                var tab = new Border
                {
                    Child = label, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(12),
                    Background = on ? Accent : Card, Cursor = Cursors.Hand,
                    ToolTip = r.Id == Core.Rooms.Lobby ? "Everyone" : $"Everyone working on job {r.Id[4..]} (from the drawing's folder)",
                };
                Room target = r;
                tab.MouseLeftButtonUp += (_, _) => { Select(target); FocusInput(); };
                _tabs.Children.Add(tab);
            }
        }

        private void RefreshOnline()
        {
            if (Current == null) return;
            var names = Current.Online.Select(p => p.Name).ToList();
            _online.Text = names.Count == 0 ? "Nobody else here right now" : $"Here ({names.Count}): " + string.Join(", ", names);
            _online.ToolTip = string.Join("\n", Current.Online.Select(p => $"{p.Name} ({p.User}) on {p.Machine}"));
        }

        private void RefreshStatus()
        {
            if (ChatHub.Problem == null)
            {
                _status.Text = $"● Connected as {ChatHub.Settings.Name}";
                _status.Foreground = Good;
            }
            else
            {
                _status.Text = "● " + ChatHub.Problem;
                _status.Foreground = Warn;
            }
            _status.ToolTip = $"Folder: {ChatHub.DataFolder}\nCIFOLDER changes it, CINAME your name, CIPOPUPS the pop-ups.";
            _send.IsEnabled = ChatHub.Connected;
        }

        public void RefreshLive()
        {
            if (!LiveDrafting.IsActive)
            {
                _live.Visibility = Visibility.Collapsed;
                _draft.Content = "Draft together";
                return;
            }
            var people = new[] { ChatHub.Settings.Name + " (you)" }.Concat(LiveDrafting.People.Select(p => p.Name + (p.Host ? " (host)" : "")));
            _live.Text = $"● Drafting live in {Path.GetFileName(LiveDrafting.DrawingPath)} with: {string.Join(", ", people)}";
            _live.Foreground = Good;
            _live.Visibility = Visibility.Visible;
            _draft.Content = LiveDrafting.IsHost ? "End drafting session" : "Leave drafting session";
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
                    Text = Current.Id == Core.Rooms.Lobby ? "No messages yet - say hello!" : $"No messages yet for job {Current.Id[4..]}.",
                    Foreground = Dim, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap,
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
                    Foreground = Dim, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2),
                });

            var body = new StackPanel();
            if (!sameRun)
            {
                var head = new TextBlock { Margin = new Thickness(0, 0, 0, 1) };
                head.Inlines.Add(new Run(m.Name) { FontWeight = FontWeights.SemiBold, Foreground = NameColour(m.User) });
                head.Inlines.Add(new Run("  " + local.ToString("HH:mm")) { Foreground = Dim, FontSize = 10.5 });
                body.Children.Add(head);
            }
            body.Children.Add(new TextBox
            {
                Text = m.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = Text, Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0),
            });
            if (m.Button is { } b)
            {
                var action = new Button { Content = b.Label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 2) };
                StyleButton(action, filled: true);
                action.Margin = new Thickness(0, 4, 0, 2);
                string command = b.Command;
                action.Click += (_, _) => Commands.Run(command);
                body.Children.Add(action);
            }
            else if (!string.IsNullOrEmpty(m.Drawing) && (!sameRun || m.Drawing != _previous?.Drawing))
            {
                var link = new Hyperlink(new Run(Path.GetFileName(m.Drawing))) { Foreground = Dim, ToolTip = "Open " + m.Drawing };
                string path = m.Drawing!;
                link.Click += (_, _) => OpenDrawing(path);
                var line = new TextBlock { FontSize = 10.5, Foreground = Dim, Margin = new Thickness(0, 1, 0, 0) };
                line.Inlines.Add(new Run("from "));
                line.Inlines.Add(link);
                body.Children.Add(line);
            }
            _list.Children.Add(new Border
            {
                Child = body, Background = mine ? Mine : Brushes.Transparent, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, sameRun ? 1 : 5, 7, 4), Margin = new Thickness(0, sameRun ? 0 : 3, 0, 0),
            });
            _previous = m;
            while (_list.Children.Count > 600) _list.Children.RemoveAt(0);
            if (scroll && (atBottom || mine)) _scroll.ScrollToEnd();
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
                if (!File.Exists(path)) { MessageBox.Show("Can't find that drawing:\n" + path, "CivilInteractive"); return; }
                Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension.Open(docs, path, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open the drawing: " + ex.Message, "CivilInteractive");
            }
        }
    }
}
