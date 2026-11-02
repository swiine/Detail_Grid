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
    /// <summary>The CivilInteractive palette: room tabs, messages, who's online, the drafting button, and your friends.</summary>
    public sealed class ChatPalette : UserControl
    {
        private static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }

        // Teal on slate: CivilInteractive's own look.
        private static readonly Brush Back = B("#1F2A2E"), Card = B("#263439"), Text = B("#E8F0EE"), Dim = B("#8FA3A0"),
            Accent = B("#22B3A0"), Mine = B("#1E4A47"), Field = B("#172024"), Edge = B("#35484D"), Warn = B("#E57A6E"), Good = B("#5CC98A");

        private static readonly string[] NameColours = { "#5CC9F5", "#F5A35C", "#8BE07A", "#D58BF5", "#F57A8B", "#F5D35C", "#5CF5C9", "#A9B4F5" };

        private readonly WrapPanel _tabs = new() { Margin = new Thickness(6, 2, 6, 2) };
        private readonly StackPanel _views = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 2) };
        private readonly Button _chatView = new() { Content = "Chat" };
        private readonly Button _friendsView = new() { Content = "Friends" };
        private readonly StackPanel _friends = new() { Margin = new Thickness(6) };
        private readonly ScrollViewer _friendsScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed };
        private readonly FrameworkElement[] _chatOnly;
        private bool _showingFriends;
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

            StyleButton(_chatView, filled: true);
            StyleButton(_friendsView, filled: false);
            _chatView.Margin = _friendsView.Margin = new Thickness(0, 0, 6, 0);
            _chatView.Click += (_, _) => ShowFriends(false);
            _friendsView.Click += (_, _) => ShowFriends(true);
            _views.Children.Add(_chatView);
            _views.Children.Add(_friendsView);
            _friendsScroll.Content = _friends;
            _friendsScroll.Background = Card;

            var messages = new Border { BorderBrush = Edge, BorderThickness = new Thickness(0, 1, 0, 1), Child = _scroll };
            var grid = new Grid();
            for (int i = 0; i < 8; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Put(grid, _views, 0);
            Put(grid, _tabs, 1);
            Put(grid, _online, 2);
            Put(grid, messages, 3);
            Put(grid, new Border { BorderBrush = Edge, BorderThickness = new Thickness(0, 1, 0, 1), Child = _friendsScroll }, 3);
            Put(grid, _live, 4);
            Put(grid, inputRow, 5);
            Put(grid, buttons, 6);
            Put(grid, _status, 7);
            Content = grid;
            _chatOnly = new FrameworkElement[] { _tabs, _online, messages, inputRow };
            ((Border)_friendsScroll.Parent).Visibility = Visibility.Collapsed;
            _friendsScroll.Visibility = Visibility.Visible;

            ChatHub.RoomAdded += _ => RefreshTabs();
            ChatHub.RoomLoaded += r => { if (r == Current) ShowAll(); };
            ChatHub.MessageAdded += (r, m) => { if (r == Current) Add(m, scroll: true); };
            ChatHub.OnlineChanged += r => { if (r == Current) RefreshOnline(); };
            ChatHub.StatusChanged += RefreshStatus;
            ChatHub.JobRoomActivated += Select;
            LiveDrafting.Changed += RefreshLive;
            ChatHub.FriendsChanged += RefreshFriends;
            TeamsLink.Changed += () => { RefreshFriends(); RefreshStatus(); };

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
            if (_showingFriends) ShowFriends(false);
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
                    ToolTip = r.IsTeams ? "Your Teams chat (shows in Teams too)"
                        : r.Id == Core.Rooms.Lobby ? "Everyone"
                        : Core.Rooms.IsPrivate(r.Id) ? "Private - just the two of you"
                        : $"Everyone working on job {r.Id[4..]} (from the drawing's folder)",
                };
                Room target = r;
                tab.MouseLeftButtonUp += (_, _) => { Select(target); FocusInput(); };
                _tabs.Children.Add(tab);
            }
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
                    Text = Current.IsTeams ? (TeamsLink.SignedIn ? "Loading your Teams chat..." : "Sign in to Teams (Friends) to see this chat.")
                        : Current.Id == Core.Rooms.Lobby ? "No messages yet - say hello!"
                        : Core.Rooms.IsPrivate(Current.Id) ? "No messages yet - only the two of you can see this chat."
                        : $"No messages yet for job {Current.Id[4..]}.",
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
                if (!mine && !m.User.StartsWith("teams:", StringComparison.Ordinal)) head.ContextMenu = PersonMenu(m.User, m.Name);
                if (head.ContextMenu != null) { head.Cursor = Cursors.Hand; head.ToolTip = "Click for options"; head.MouseLeftButtonUp += (_, _) => head.ContextMenu.IsOpen = true; }
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

        // ------------------------------------------------------------------ friends

        public void ShowFriends(bool on)
        {
            _showingFriends = on;
            foreach (FrameworkElement e in _chatOnly) e.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            ((Border)_friendsScroll.Parent).Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            StyleButton(_chatView, filled: !on);
            StyleButton(_friendsView, filled: on);
            _chatView.Margin = _friendsView.Margin = new Thickness(0, 0, 6, 0);
            if (on) RefreshFriends();
            else FocusInput();
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

        private void RefreshFriends()
        {
            int onlineFriends = ChatHub.Settings.Friends.Count(f => ChatHub.IsOnline(f.User));
            _friendsView.Content = onlineFriends > 0 ? $"Friends ({onlineFriends} on)" : "Friends";
            if (!_showingFriends) return;
            _friends.Children.Clear();

            _friends.Children.Add(TeamsRow());

            if (ChatHub.Settings.Friends.Count == 0)
                _friends.Children.Add(Note("No friends yet. Add people below, or click someone's name in the chat."));
            foreach (Friend f in ChatHub.Settings.Friends.OrderByDescending(f => ChatHub.IsOnline(f.User)).ThenBy(f => f.Name))
                _friends.Children.Add(FriendCard(f));

            // Add a friend: people online now, or type a login.
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
            var byLogin = new DockPanel { Margin = new Thickness(4, 6, 4, 2) };
            var login = new TextBox { Background = Field, Foreground = Text, CaretBrush = Text, BorderBrush = Edge, Padding = new Thickness(4, 2, 4, 2), ToolTip = "Their Windows login (the name before @ in their work email works too)" };
            void AddTyped()
            {
                string u = login.Text.Trim();
                if (u.Contains('@')) u = u[..u.IndexOf('@')];
                if (u.Length == 0) return;
                string? email = login.Text.Trim().Contains('@') ? login.Text.Trim() : ChatHub.LastSeenOf(u)?.Email;
                ChatHub.AddFriend(u, ChatHub.LastSeenOf(u)?.Name ?? u, email);
            }
            login.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTyped(); };
            var addTyped = SmallButton("Add", AddTyped);
            DockPanel.SetDock(addTyped, Dock.Right);
            byLogin.Children.Add(addTyped);
            byLogin.Children.Add(login);
            _friends.Children.Add(Note("Or type their login or work email:"));
            _friends.Children.Add(byLogin);

            var alerts = new CheckBox
            {
                Content = "Pop up when a friend comes online", IsChecked = ChatHub.Settings.FriendAlerts, Foreground = Dim, Margin = new Thickness(4, 10, 4, 4),
            };
            alerts.Click += (_, _) => ChatHub.SetFriendAlerts(alerts.IsChecked == true);
            _friends.Children.Add(alerts);
        }

        private UIElement TeamsRow()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (!TeamsLink.IsSetUp)
            {
                panel.Children.Add(Note("Teams link: off until IT approves it (see TEAMS-IT-SETUP.md)."));
                return panel;
            }
            var row = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };
            Button b = TeamsLink.SignedIn
                ? SmallButton("Sign out", () => Commands.Run("CITEAMS OUT"))
                : SmallButton("Sign in to Teams", () => Commands.Run("CITEAMS IN"));
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
            row.Children.Add(new TextBlock
            {
                Text = TeamsLink.SignedIn ? $"Teams: signed in as {TeamsLink.Me!.Name}" : "Teams: not signed in",
                Foreground = TeamsLink.SignedIn ? Good : Dim, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            panel.Children.Add(row);
            if (TeamsLink.Problem != null) panel.Children.Add(new TextBlock { Text = TeamsLink.Problem, Foreground = Warn, FontSize = 11, Margin = new Thickness(4, 0, 4, 0), TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private UIElement FriendCard(Friend f)
        {
            bool online = ChatHub.IsOnline(f.User);
            OnlinePerson? seen = ChatHub.LastSeenOf(f.User);
            var body = new StackPanel();

            var head = new TextBlock();
            head.Inlines.Add(new Run("● ") { Foreground = online ? Good : Dim });
            head.Inlines.Add(new Run(f.Name) { FontWeight = FontWeights.SemiBold, Foreground = NameColour(f.User) });
            if (TeamsLink.StatusOf(f) is { } teams) head.Inlines.Add(new Run("   Teams: " + teams) { Foreground = Dim, FontSize = 11 });
            body.Children.Add(head);

            string where;
            if (online && seen != null)
            {
                string? job = seen.Rooms.FirstOrDefault(r => r.StartsWith("job-", StringComparison.Ordinal));
                where = "Online" + (job != null ? $" - job {job[4..]}" : "") + (seen.Drawing != null ? $" - {seen.Drawing}" : "");
            }
            else where = seen == null ? "Not seen yet" : "Last seen " + Ago(seen.Seen);
            body.Children.Add(new TextBlock { Text = where, Foreground = Dim, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });

            var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(SmallButton("Message", () => { Select(ChatHub.OpenPrivate(f.User)); FocusInput(); }));
            if (TeamsLink.SignedIn)
                buttons.Children.Add(SmallButton("Teams chat", async () =>
                {
                    Room? r = await TeamsLink.OpenChatAsync(f);
                    if (r != null) { Select(r); FocusInput(); }
                    else RefreshFriends();
                }));
            if (online && seen?.LiveSession is { } session && session != LiveDrafting.SessionId)
                buttons.Children.Add(SmallButton("Join drafting", () => Commands.Run("CILIVEJOIN " + session)));
            buttons.Children.Add(SmallButton("Remove", () =>
            {
                if (MessageBox.Show($"Remove {f.Name} from your friends?", "CivilInteractive", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    ChatHub.RemoveFriend(f.User);
            }, quiet: true));
            body.Children.Add(buttons);

            return new Border
            {
                Child = body, Background = Back, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5),
                ToolTip = f.User + ((f.Email ?? seen?.Email) is { } email ? "\n" + email : "") + (seen != null ? $"\non {seen.Machine}" : ""),
            };
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

        private static TextBlock Heading(string text) =>
            new() { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Accent, Margin = new Thickness(4, 10, 4, 2) };

        private static TextBlock Note(string text) =>
            new() { Text = text, Foreground = Dim, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 4) };

        private static Button SmallButton(string text, Action click, bool quiet = false)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 5, 2), FontSize = 11.5,
                Background = Brushes.Transparent, Foreground = quiet ? Dim : Accent, BorderBrush = quiet ? Edge : Accent,
            };
            b.Click += (_, _) => click();
            return b;
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
