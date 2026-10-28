using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace DoomChat
{
    /// <summary>The WPF chat panel hosted in the DoomChat palette: the CivDOOM lobby.</summary>
    public sealed class ChatPanel : UserControl
    {
        sealed class Theme
        {
            public Brush Bg = null!, Panel = null!, Fg = null!, Muted = null!, Accent = null!, Own = null!, Input = null!, Border = null!, TabBg = null!;
            public static Theme Current()
            {
                bool dark = true;
                try { dark = Convert.ToInt32(AcApp.GetSystemVariable("COLORTHEME")) == 0; } catch { }
                Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }
                return dark
                    ? new Theme { Bg = B("#2B3340"), Panel = B("#3B4453"), Fg = B("#E6E9EE"), Muted = B("#9AA4B2"), Accent = B("#D6402F"), Own = B("#4A3338"), Input = B("#222831"), Border = B("#4A5465"), TabBg = B("#353E4C") }
                    : new Theme { Bg = B("#F5F6F8"), Panel = B("#FFFFFF"), Fg = B("#1F2933"), Muted = B("#66707D"), Accent = B("#B8321F"), Own = B("#F7E4E1"), Input = B("#FFFFFF"), Border = B("#C9CED6"), TabBg = B("#E6E9EE") };
            }
        }

        static readonly string[] NameColors = { "#4FA3E0", "#E0874F", "#5DBB63", "#C678DD", "#E06C75", "#D19A66", "#56B6C2", "#B5BD68" };

        readonly Theme _t = Theme.Current();
        readonly WrapPanel _tabs = new() { Margin = new Thickness(4, 4, 4, 0) };
        readonly TextBlock _online = new() { Margin = new Thickness(8, 4, 8, 4), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly StackPanel _messages = new() { Margin = new Thickness(4) };
        readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        readonly TextBox _input = new();
        readonly Button _send = new() { Content = "Send", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4, 0, 0, 0) };
        readonly Button _host = new() { Content = "Host game", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Start a CivDOOM co-op game (CIVDOOMHOST) and invite the lobby" };
        readonly TextBlock _status = new() { Margin = new Thickness(8, 2, 8, 4), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };

        ChatMessage? _lastRendered;
        public ChannelState? Selected { get; private set; }
        public Func<bool> IsShown { get; set; } = () => true;

        public ChatPanel()
        {
            Background = _t.Bg;
            Foreground = _t.Fg;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;

            _online.Foreground = _t.Muted;
            _status.Foreground = _t.Muted;
            _scroll.Content = _messages;
            _scroll.Background = _t.Panel;

            _input.AcceptsReturn = true;
            _input.TextWrapping = TextWrapping.Wrap;
            _input.MinHeight = 28;
            _input.MaxHeight = 110;
            _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _input.Padding = new Thickness(4);
            _input.Background = _t.Input;
            _input.Foreground = _t.Fg;
            _input.CaretBrush = _t.Fg;
            _input.BorderBrush = _t.Border;
            _input.ToolTip = "Enter to send, Shift+Enter for a new line";
            _input.PreviewKeyDown += OnInputKey;
            _send.Background = _t.Accent;
            _send.Foreground = Brushes.White;
            _send.BorderBrush = _t.Accent;
            _send.FontWeight = FontWeights.SemiBold;
            _send.Click += (s, e) => SendCurrent();

            _host.Background = Brushes.Transparent;
            _host.Foreground = _t.Accent;
            _host.BorderBrush = _t.Accent;
            _host.FontWeight = FontWeights.SemiBold;
            _host.Click += (s, e) => ChatUi.HostClicked();

            var inputRow = new DockPanel { Margin = new Thickness(6, 6, 6, 2), LastChildFill = true };
            DockPanel.SetDock(_send, Dock.Right);
            DockPanel.SetDock(_host, Dock.Left);
            inputRow.Children.Add(_host);
            inputRow.Children.Add(_send);
            inputRow.Children.Add(_input);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var scrollBorder = new Border { BorderBrush = _t.Border, BorderThickness = new Thickness(0, 1, 0, 1), Child = _scroll };
            Add(grid, _tabs, 0); Add(grid, _online, 1); Add(grid, scrollBorder, 2); Add(grid, inputRow, 4); Add(grid, _status, 5);
            Content = grid;

            ChatService.ChannelAdded += ch => RefreshTabs();
            ChatService.ChannelReset += ch => { if (ch == Selected) RenderAll(); };
            ChatService.MessageAdded += (ch, m) => { if (ch == Selected) Append(m, true); };
            ChatService.PresenceChanged += ch => { if (ch == Selected) RefreshOnline(); };
            ChatService.ConnectionChanged += RefreshStatus;

            Select(ChatService.Channels.First());
            RefreshStatus();
        }

        static void Add(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

        public void FocusInput() => Dispatcher.BeginInvoke(new Action(() => { _input.Focus(); Keyboard.Focus(_input); }));

        public void Select(ChannelState ch)
        {
            Selected = ch;
            if (IsShown()) ch.Unread = 0;
            RefreshTabs();
            RefreshOnline();
            RenderAll();
            ChatUi.UpdateTitle();
        }

        /// <summary>Called when the palette becomes visible.</summary>
        public void OnShown()
        {
            if (Selected != null) Selected.Unread = 0;
            RefreshTabs();
            ChatUi.UpdateTitle();
            _scroll.ScrollToEnd();
        }

        public void RefreshTabs()
        {
            _tabs.Children.Clear();
            foreach (var ch in ChatService.Channels)
            {
                bool sel = ch == Selected;
                var label = ch.Title + (ch.Unread > 0 ? $"  ({ch.Unread})" : "");
                var tb = new TextBlock { Text = label, FontWeight = sel || ch.Unread > 0 ? FontWeights.SemiBold : FontWeights.Normal };
                var tab = new Border
                {
                    Child = tb,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 4, 4),
                    CornerRadius = new CornerRadius(3),
                    Background = sel ? _t.Accent : _t.TabBg,
                    Cursor = Cursors.Hand,
                    ToolTip = "Everyone playing CivDOOM"
                };
                tb.Foreground = sel ? Brushes.White : (ch.Unread > 0 ? _t.Accent : _t.Fg);
                var target = ch;
                tab.MouseLeftButtonUp += (s, e) => { Select(target); FocusInput(); };
                _tabs.Children.Add(tab);
            }
        }

        void RefreshOnline()
        {
            if (Selected == null) return;
            var names = Selected.Online.Select(u => u.Display).ToList();
            _online.Text = names.Count == 0 ? "Nobody online in this channel" : $"Online ({names.Count}): " + string.Join(", ", names);
            _online.ToolTip = string.Join("\n", Selected.Online.Select(u => $"{u.Display} ({u.User}) - {u.Machine}"));
        }

        void RefreshStatus()
        {
            var s = ChatService.Endpoint;
            switch (ChatService.State)
            {
                case ConnectionState.Connected:
                    _status.Text = $"● Connected as {ChatService.DisplayName}";
                    _status.Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0xBB, 0x63));
                    break;
                case ConnectionState.Connecting:
                    _status.Text = "● Connecting…";
                    _status.Foreground = _t.Muted;
                    break;
                default:
                    _status.Text = $"● Offline - {ChatService.LastError}. Retrying…";
                    _status.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
                    break;
            }
            _status.ToolTip = (ChatService.UsesFolder ? $"DoomChat folder: {s}" : $"Server: {s}") +
                "\nChange with DOOMCHATFOLDER or DOOMCHATSERVER. Change your name with DOOMCHATNAME.";
            _send.IsEnabled = ChatService.State == ConnectionState.Connected;
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                SendCurrent();
            }
        }

        void SendCurrent()
        {
            var text = _input.Text.Trim();
            if (text.Length == 0 || Selected == null) return;
            if (ChatService.Send(Selected, text)) _input.Clear();
            else _status.Text = "● Not connected - message not sent";
            FocusInput();
        }

        void RenderAll()
        {
            _messages.Children.Clear();
            _lastRendered = null;
            if (Selected == null) return;
            if (Selected.Messages.Count == 0)
                _messages.Children.Add(new TextBlock
                {
                    Text = "No messages yet. Click Host game to start a CivDOOM game, or say hello!",
                    Foreground = _t.Muted, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap
                });
            foreach (var m in Selected.Messages) Append(m, false);
            _scroll.ScrollToEnd();
        }

        void Append(ChatMessage m, bool scroll)
        {
            if (_messages.Children.Count == 1 && _messages.Children[0] is TextBlock) _messages.Children.Clear();
            bool atBottom = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 20;
            bool own = ChatService.IsOwn(m);
            bool grouped = _lastRendered != null && _lastRendered.User == m.User &&
                           (m.Ts - _lastRendered.Ts) < TimeSpan.FromMinutes(5) &&
                           _lastRendered.Ts.LocalDateTime.Date == m.Ts.LocalDateTime.Date;

            if (_lastRendered == null || _lastRendered.Ts.LocalDateTime.Date != m.Ts.LocalDateTime.Date)
            {
                _messages.Children.Add(new TextBlock
                {
                    Text = DayLabel(m.Ts.LocalDateTime.Date),
                    Foreground = _t.Muted, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 2)
                });
            }

            var stack = new StackPanel();
            if (!grouped)
            {
                var header = new TextBlock { Margin = new Thickness(0, 0, 0, 1) };
                header.Inlines.Add(new Run(m.Display) { FontWeight = FontWeights.SemiBold, Foreground = NameBrush(m.User) });
                header.Inlines.Add(new Run("  " + m.Ts.LocalDateTime.ToString("HH:mm")) { Foreground = _t.Muted, FontSize = 10.5 });
                header.ToolTip = $"{m.User} · {m.Ts.LocalDateTime:ddd d MMM yyyy HH:mm:ss}";
                stack.Children.Add(header);
            }
            var doomGame = ChatService.DoomGameId(m);
            stack.Children.Add(new TextBox
            {
                Text = doomGame != null ? m.Text.Substring(ChatService.DoomPrefix.Length) : m.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = _t.Fg, Padding = new Thickness(0), Margin = new Thickness(-2, 0, 0, 0),
                ToolTip = grouped ? m.Ts.LocalDateTime.ToString("HH:mm") : null
            });
            if (doomGame != null)
            {
                var play = new Button
                {
                    Content = "Join game", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 4, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left, Background = _t.Accent, Foreground = Brushes.White,
                    BorderBrush = _t.Accent, FontWeight = FontWeights.SemiBold, ToolTip = $"Join CivDOOM game {doomGame}"
                };
                play.Click += (s, e) => ChatUi.JoinGame(doomGame);
                stack.Children.Add(play);
            }

            _messages.Children.Add(new Border
            {
                Child = stack,
                Background = own ? _t.Own : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, grouped ? 1 : 4, 6, 3),
                Margin = new Thickness(0, grouped ? 0 : 3, 0, 0)
            });
            _lastRendered = m;
            while (_messages.Children.Count > 600) _messages.Children.RemoveAt(0);
            if (scroll && (atBottom || own)) _scroll.ScrollToEnd();
        }

        static string DayLabel(DateTime d) =>
            d == DateTime.Today ? "Today" : d == DateTime.Today.AddDays(-1) ? "Yesterday" : d.ToString("dddd d MMMM yyyy");

        static Brush NameBrush(string user)
        {
            int h = 0;
            foreach (var c in user.ToLowerInvariant()) h = (h * 31 + c) & 0x7fffffff;
            return (SolidColorBrush)new BrushConverter().ConvertFrom(NameColors[h % NameColors.Length])!;
        }
    }
}
