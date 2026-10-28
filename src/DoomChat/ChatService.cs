using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace DoomChat
{
    /// <summary>
    /// Holds chat state for the session: one lobby channel where CivDOOM players talk and games are announced.
    /// All public members and events are used on the AutoCAD UI thread.
    /// </summary>
    public static class ChatService
    {
        /// <summary>The lobby. CivDOOM posts its "Join game" invites here (keep in step with NetLobby.LobbyChannel).</summary>
        public const string Lobby = "doom-lobby";

        static Dispatcher? _ui;
        static IChatTransport? _client;

        public static ChatSettings Settings { get; private set; } = new();
        public static string UserName { get; } = Environment.UserName;
        public static string DisplayName => string.IsNullOrWhiteSpace(Settings.DisplayName) ? UserName : Settings.DisplayName!;
        public static bool UsesFolder => !string.IsNullOrWhiteSpace(Settings.ChatFolder);
        public static string Endpoint => UsesFolder ? Settings.ChatFolder! : Settings.ServerUrl;
        public static ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public static string? LastError { get; private set; }
        public static List<ChannelState> Channels { get; } = new();

        public static event Action<ChannelState>? ChannelAdded;
        public static event Action<ChannelState>? ChannelReset;               // history (re)loaded
        public static event Action<ChannelState, ChatMessage>? MessageAdded;
        public static event Action<ChannelState>? PresenceChanged;
        public static event Action? ConnectionChanged;
        /// <summary>Raised for messages from other people (for notifications).</summary>
        public static event Action<ChannelState, ChatMessage>? IncomingMessage;

        public static void Initialize()
        {
            _ui = Dispatcher.CurrentDispatcher;
            Settings = ChatSettings.Load();
            GetOrAddChannel(Lobby, "Lobby");
            Connect();
        }

        public static void Shutdown()
        {
            _client?.Dispose();
            _client = null;
        }

        public static void Reload()
        {
            Settings = ChatSettings.Load();
            Connect();
        }

        static void Connect()
        {
            _client?.Dispose();
            _client = null;
            IChatTransport c;
            if (UsesFolder)
            {
                c = new FolderChatClient(Settings.ChatFolder!, UserName, DisplayName, Environment.MachineName);
            }
            else
            {
                Uri? uri = null;
                try { uri = new Uri(Settings.ServerUrl); } catch { }
                if (uri == null || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                {
                    SetState(ConnectionState.Disconnected, "Invalid server address - run DOOMCHATSERVER or DOOMCHATFOLDER");
                    return;
                }
                c = new ChatClient(Settings.ServerUrl, UserName, DisplayName, Environment.MachineName);
            }
            c.StateChanged += (st, err) => Ui(() => SetState(st, err));
            c.MessageReceived += m => Ui(() => OnMessage(m));
            c.HistoryReceived += (ch, list) => Ui(() => OnHistory(ch, list));
            c.PresenceReceived += (ch, users) => Ui(() => OnPresence(ch, users));
            foreach (var ch in Channels) c.Join(ch.Name);
            _client = c;
            c.Start();
        }

        static void Ui(Action a)
        {
            var d = _ui;
            if (d == null) return;
            d.BeginInvoke(new Action(() => { try { a(); } catch { } }));
        }

        static void SetState(ConnectionState st, string? err)
        {
            State = st;
            LastError = err;
            ConnectionChanged?.Invoke();
        }

        public static ChannelState GetOrAddChannel(string name, string title)
        {
            var ch = Channels.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (ch != null) return ch;
            ch = new ChannelState(name, title);
            Channels.Add(ch);
            _client?.Join(name);
            ChannelAdded?.Invoke(ch);
            return ch;
        }

        public static bool Send(ChannelState ch, string text)
        {
            if (_client == null || State != ConnectionState.Connected) return false;
            _ = _client.SendMessageAsync(ch.Name, text, null);
            return true;
        }

        /// <summary>Prefix of a CivDOOM game invite (posted by CIVDOOMHOST).</summary>
        public const string DoomPrefix = "[Doom] ";

        /// <summary>The game id in a CivDOOM invite ("... (game 3fa9c2)."), or null if it isn't one.</summary>
        public static string? DoomGameId(ChatMessage m)
        {
            if (!m.Text.StartsWith(DoomPrefix)) return null;
            var match = Regex.Match(m.Text, @"\(game (?<id>[a-z0-9]{4,16})\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : null;
        }

        static void OnHistory(string channel, List<ChatMessage> list)
        {
            var ch = Channels.FirstOrDefault(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
            if (ch == null) return;
            ch.Messages.Clear();
            ch.Messages.AddRange(list.OrderBy(m => m.Id));
            ChannelReset?.Invoke(ch);
        }

        static void OnMessage(ChatMessage m)
        {
            var ch = Channels.FirstOrDefault(c => c.Name.Equals(m.Channel, StringComparison.OrdinalIgnoreCase));
            if (ch == null) return;
            if (ch.Messages.Any(x => x.Id == m.Id)) return;
            ch.Messages.Add(m);
            if (ch.Messages.Count > 500) ch.Messages.RemoveAt(0);
            MessageAdded?.Invoke(ch, m);
            if (!m.User.Equals(UserName, StringComparison.OrdinalIgnoreCase))
                IncomingMessage?.Invoke(ch, m);
        }

        static void OnPresence(string channel, List<ChatUser> users)
        {
            var ch = Channels.FirstOrDefault(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
            if (ch == null) return;
            ch.Online = users;
            PresenceChanged?.Invoke(ch);
        }

        public static bool IsOwn(ChatMessage m) => m.User.Equals(UserName, StringComparison.OrdinalIgnoreCase);
    }
}
