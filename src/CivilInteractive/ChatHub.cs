using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using CivilInteractive.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilInteractive
{
    /// <summary>A chat room as shown in the palette: shared (Lobby, a job, a private chat) or a Teams chat.</summary>
    public sealed class Room
    {
        public Room(string id) { Id = id; }
        public string Id { get; }
        public string Title => ChatHub.TitleOf(this);
        public List<ChatMessage> Messages { get; } = new();
        public List<OnlinePerson> Online { get; set; } = new();
        public int Unread { get; set; }

        /// <summary>For a Teams chat: the Teams chat id and the friend it's with.</summary>
        public string? TeamsChatId { get; internal set; }
        public Friend? TeamsFriend { get; internal set; }

        public bool IsTeams => TeamsChatId != null;
        internal bool Loaded;
    }

    /// <summary>
    /// The chat for this Civil 3D session: reads the shared folder in the background and raises events on the
    /// UI thread. Always in the Lobby; joins a job's room when a drawing from that job is opened, and any private
    /// chat someone starts with you. Also keeps your friends list.
    /// </summary>
    public static class ChatHub
    {
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(70);

        private static Dispatcher? _ui;
        private static Timer? _timer;
        private static ChatStore? _store;
        private static int _busy, _ticks;
        private static DateTime _lastHeartbeat = DateTime.MinValue;
        private static List<OnlinePerson> _everyone = new();
        private static HashSet<string> _onlineUsers = new(StringComparer.OrdinalIgnoreCase);
        private static bool _firstOnline = true;

        public static CiSettings Settings { get; private set; } = new();
        public static List<Room> Rooms { get; } = new();
        public static string? Problem { get; private set; } = "Starting...";
        public static string? CurrentJobRoom { get; private set; }

        /// <summary>Your work email (sign-in name), from Windows; shared so friends can find you in Teams.</summary>
        public static string? MyEmail { get; } = Upn();

        public static event Action<Room>? RoomAdded;
        public static event Action<Room>? RoomLoaded;
        public static event Action<Room, ChatMessage>? MessageAdded;
        public static event Action<Room>? OnlineChanged;
        public static event Action? StatusChanged;
        /// <summary>A message from someone else (for pop-ups).</summary>
        public static event Action<Room, ChatMessage>? Incoming;
        /// <summary>A job room became current (the palette switches to it).</summary>
        public static event Action<Room>? JobRoomActivated;
        /// <summary>Your friends list, or what your friends are up to, changed.</summary>
        public static event Action? FriendsChanged;
        /// <summary>A friend just came online.</summary>
        public static event Action<Friend>? FriendCameOnline;

        public static void Start()
        {
            _ui = Dispatcher.CurrentDispatcher;
            AddRoom(Core.Rooms.Lobby);
            AcApp.DocumentManager.DocumentActivated += (_, e) => { JoinJobRoom(e.Document?.Name); _lastHeartbeat = DateTime.MinValue; };
            AcApp.DocumentManager.DocumentCreated += (_, e) => JoinJobRoom(e.Document?.Name);
            Reload();
            JoinJobRoom(AcApp.DocumentManager.MdiActiveDocument?.Name);
        }

        /// <summary>(Re)reads the settings and reconnects to the folder.</summary>
        public static void Reload()
        {
            Settings = CiSettings.Load();
            _timer?.Dispose();
            _store = null;
            foreach (Room r in Rooms.Where(r => !r.IsTeams)) { r.Loaded = false; r.Messages.Clear(); }
            FriendsChanged?.Invoke();
            if (string.IsNullOrWhiteSpace(Settings.Folder))
            {
                SetProblem("No CivilInteractive folder set - run CIFOLDER");
                return;
            }
            _store = new ChatStore(Settings.Folder!, Environment.UserName, Settings.Name, Environment.MachineName);
            _lastHeartbeat = DateTime.MinValue;
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Poll);
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            try { _store?.SayOffline(); } catch { }
        }

        public static string DataFolder => Settings.Folder ?? "";
        public static bool Connected => Problem == null;

        public static Room AddRoom(string id)
        {
            Room? r = Rooms.FirstOrDefault(x => x.Id == id);
            if (r != null) return r;
            r = new Room(id);
            Rooms.Add(r);
            RoomAdded?.Invoke(r);
            _lastHeartbeat = DateTime.MinValue; // tell others we're in it
            return r;
        }

        /// <summary>A Teams chat with a friend, as a room (see <see cref="TeamsLink"/>).</summary>
        public static Room AddTeamsRoom(Friend f, string chatId)
        {
            string id = "teams-" + Core.Rooms.Safe(f.User);
            Room? r = Rooms.FirstOrDefault(x => x.Id == id);
            if (r != null) return r;
            r = new Room(id) { TeamsChatId = chatId, TeamsFriend = f };
            Rooms.Add(r);
            RoomAdded?.Invoke(r);
            return r;
        }

        /// <summary>Your private chat with someone (it appears for them as soon as you send something).</summary>
        public static Room OpenPrivate(string user) => AddRoom(Core.Rooms.Private(Environment.UserName, user));

        public static string TitleOf(Room r)
        {
            if (r.TeamsFriend is { } tf) return "Teams: " + tf.Name;
            if (Core.Rooms.OtherPerson(r.Id, Environment.UserName) is { } other) return "@" + NameOf(other);
            return Core.Rooms.Title(r.Id);
        }

        /// <summary>A person's display name from their login (friends list first, then what they last told us).</summary>
        public static string NameOf(string user)
        {
            string safe = Core.Rooms.Safe(user);
            Friend? f = Settings.Friends.FirstOrDefault(x => Core.Rooms.Safe(x.User) == safe);
            if (f != null) return f.Name;
            OnlinePerson? p = _everyone.FirstOrDefault(x => Core.Rooms.Safe(x.User) == safe);
            return p?.Name ?? user;
        }

        private static void JoinJobRoom(string? drawing)
        {
            string? job = Core.Rooms.ForDrawing(drawing);
            if (job == null) return;
            CurrentJobRoom = job;
            JobRoomActivated?.Invoke(AddRoom(job));
        }

        /// <summary>Sends a message in a room, noting the drawing it was sent from.</summary>
        public static bool Send(Room room, string text, MessageButton? button = null)
        {
            if (text.Trim().Length == 0) return false;
            if (room.IsTeams)
            {
                if (!TeamsLink.SignedIn) return false;
                TeamsLink.Send(room, text.Trim());
                return true;
            }
            ChatStore? store = _store;
            if (store == null) return false;
            string? drawing = null;
            try
            {
                string? name = AcApp.DocumentManager.MdiActiveDocument?.Name;
                if (name != null && Path.IsPathRooted(name)) drawing = name;
            }
            catch { }
            try
            {
                ChatMessage m = store.Post(room.Id, text, drawing, button);
                AddMessages(room, new List<ChatMessage> { m });
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetProblem("Can't write to the CivilInteractive folder: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ friends

        public static Friend? FriendOf(string user) =>
            Settings.Friends.FirstOrDefault(f => string.Equals(f.User, user, StringComparison.OrdinalIgnoreCase));

        public static bool AddFriend(string user, string name, string? email)
        {
            CiSettings mine = CiSettings.LoadUserOnly();
            bool added = mine.AddFriend(new Friend(user, name, email));
            mine.Save();
            Settings.Friends = mine.Friends;
            FriendsChanged?.Invoke();
            return added;
        }

        public static void RemoveFriend(string user)
        {
            CiSettings mine = CiSettings.LoadUserOnly();
            mine.RemoveFriend(user);
            mine.Save();
            Settings.Friends = mine.Friends;
            FriendsChanged?.Invoke();
        }

        public static void SetFriendAlerts(bool on)
        {
            CiSettings mine = CiSettings.LoadUserOnly();
            mine.FriendAlerts = on;
            mine.Save();
            Settings.FriendAlerts = on;
        }

        /// <summary>The latest note from someone (online or not), for "last seen", their drawing and email.</summary>
        public static OnlinePerson? LastSeenOf(string user) =>
            _everyone.FirstOrDefault(p => string.Equals(p.User, user, StringComparison.OrdinalIgnoreCase));

        public static bool IsOnline(string user) => _onlineUsers.Contains(user);

        /// <summary>Everyone who's online right now (for adding friends).</summary>
        public static IEnumerable<OnlinePerson> OnlineNow =>
            _everyone.Where(p => _onlineUsers.Contains(p.User) && !string.Equals(p.User, Environment.UserName, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ background

        private static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            ChatStore? store = _store;
            try
            {
                if (store == null) return;
                if (!Directory.Exists(Settings.Folder)) throw new DirectoryNotFoundException("CivilInteractive folder not found: " + Settings.Folder);
                _ticks++;
                Room[] rooms;
                lock (Rooms) rooms = Rooms.Where(r => !r.IsTeams).ToArray();
                foreach (Room r in rooms)
                {
                    if (!r.Loaded)
                    {
                        List<ChatMessage> history = store.History(r.Id);
                        r.Loaded = true; // so the next tick reads only what's new
                        Ui(() => OnHistory(r, history));
                    }
                    else
                    {
                        List<ChatMessage> fresh = store.ReadNew(r.Id);
                        if (fresh.Count > 0) Ui(() => AddMessages(r, fresh));
                    }
                }
                if (_ticks % 4 == 1)
                {
                    // Private chats someone started with you.
                    foreach (string id in store.PrivateRoomsForMe())
                        if (rooms.All(r => r.Id != id)) Ui(() => AddRoom(id));
                }
                if (DateTime.UtcNow - _lastHeartbeat > Heartbeat)
                {
                    _lastHeartbeat = DateTime.UtcNow;
                    string? drawing = null;
                    try { drawing = Path.GetFileName(AcApp.DocumentManager.MdiActiveDocument?.Name); } catch { }
                    store.SayOnline(rooms.Select(r => r.Id), drawing, LiveDrafting.SessionId, MyEmail, TeamsLink.Me?.Id);
                }
                List<OnlinePerson> everyone = store.LastSeen();
                Ui(() => OnPeople(everyone));
                if (Problem != null) Ui(() => SetProblem(null));
            }
            catch (Exception ex)
            {
                string msg = ex is UnauthorizedAccessException ? "No permission to use the CivilInteractive folder" : ex.Message;
                Ui(() => SetProblem(msg));
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static void Ui(Action a) => _ui?.BeginInvoke(new Action(() => { try { a(); } catch { } }));

        private static void SetProblem(string? p)
        {
            if (p == Problem) return;
            Problem = p;
            StatusChanged?.Invoke();
        }

        private static void OnHistory(Room r, List<ChatMessage> list)
        {
            r.Messages.Clear();
            r.Messages.AddRange(list);
            RoomLoaded?.Invoke(r);
        }

        /// <summary>Adds messages to a room (skipping ones it has), raising events for the palette and pop-ups.</summary>
        public static void AddMessages(Room r, List<ChatMessage> list)
        {
            bool first = r.IsTeams && !r.Loaded;
            foreach (ChatMessage m in list)
            {
                if (r.Messages.Any(x => x.Id == m.Id)) continue;
                r.Messages.Add(m);
                if (r.Messages.Count > 500) r.Messages.RemoveAt(0);
                MessageAdded?.Invoke(r, m);
                if (!first && !IsMine(m)) Incoming?.Invoke(r, m);
            }
            if (first) { r.Loaded = true; RoomLoaded?.Invoke(r); }
        }

        private static string _peopleSig = "";

        private static void OnPeople(List<OnlinePerson> everyone)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var online = everyone.Where(p => !p.Left && now - p.Seen < OnlineWindow).ToList();
            var onlineUsers = new HashSet<string>(online.Select(p => p.User), StringComparer.OrdinalIgnoreCase);

            // Pop up for friends who weren't online last time (not for everyone already on when you start).
            if (!_firstOnline && Settings.FriendAlerts)
                foreach (Friend f in Settings.Friends)
                    if (onlineUsers.Contains(f.User) && !_onlineUsers.Contains(f.User)) FriendCameOnline?.Invoke(f);
            _firstOnline = false;

            _everyone = everyone;
            _onlineUsers = onlineUsers;
            string sig = string.Join("|", online.Select(p => $"{p.User}/{p.Name}/{string.Join(",", p.Rooms)}/{p.Drawing}/{p.LiveSession}"));
            if (sig == _peopleSig) return;
            _peopleSig = sig;
            foreach (Room r in Rooms)
            {
                r.Online = online.Where(p => p.Rooms.Contains(r.Id)).ToList();
                OnlineChanged?.Invoke(r);
            }
            FriendsChanged?.Invoke();
        }

        public static bool IsMine(ChatMessage m) => string.Equals(m.User, Environment.UserName, StringComparison.OrdinalIgnoreCase);

        [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserNameEx(int nameFormat, StringBuilder name, ref uint size);

        /// <summary>The Windows sign-in name (user principal name), usually the work email on a company PC.</summary>
        private static string? Upn()
        {
            try
            {
                var sb = new StringBuilder(256);
                uint size = (uint)sb.Capacity;
                return GetUserNameEx(8, sb, ref size) && sb.ToString().Contains('@') ? sb.ToString() : null;
            }
            catch { return null; }
        }
    }
}
