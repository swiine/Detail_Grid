using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using CivilInteractive.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilInteractive
{
    /// <summary>A chat room as shown in the palette.</summary>
    public sealed class Room
    {
        public Room(string id) { Id = id; }
        public string Id { get; }
        public string Title => Rooms.Title(Id);
        public List<ChatMessage> Messages { get; } = new();
        public List<OnlinePerson> Online { get; set; } = new();
        public int Unread { get; set; }
        internal bool Loaded;
    }

    /// <summary>
    /// The chat for this Civil 3D session: reads the shared folder in the background and raises events on the
    /// UI thread. Always in the Lobby; joins a job's room when a drawing from that job is opened.
    /// </summary>
    public static class ChatHub
    {
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(70);

        private static Dispatcher? _ui;
        private static Timer? _timer;
        private static ChatStore? _store;
        private static int _busy;
        private static DateTime _lastHeartbeat = DateTime.MinValue;

        public static CiSettings Settings { get; private set; } = new();
        public static List<Room> Rooms { get; } = new();
        public static string? Problem { get; private set; } = "Starting...";
        public static string? CurrentJobRoom { get; private set; }

        public static event Action<Room>? RoomAdded;
        public static event Action<Room>? RoomLoaded;
        public static event Action<Room, ChatMessage>? MessageAdded;
        public static event Action<Room>? OnlineChanged;
        public static event Action? StatusChanged;
        /// <summary>A message from someone else (for pop-ups).</summary>
        public static event Action<Room, ChatMessage>? Incoming;
        /// <summary>A job room became current (the palette switches to it).</summary>
        public static event Action<Room>? JobRoomActivated;

        public static void Start()
        {
            _ui = Dispatcher.CurrentDispatcher;
            AddRoom(Core.Rooms.Lobby);
            AcApp.DocumentManager.DocumentActivated += (_, e) => JoinJobRoom(e.Document?.Name);
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
            foreach (Room r in Rooms) { r.Loaded = false; r.Messages.Clear(); }
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
            ChatStore? store = _store;
            if (store == null || text.Trim().Length == 0) return false;
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
                OnMessages(room.Id, new List<ChatMessage> { m });
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetProblem("Can't write to the CivilInteractive folder: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ background

        private static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            ChatStore? store = _store;
            try
            {
                if (store == null) return;
                if (!Directory.Exists(Settings.Folder)) throw new DirectoryNotFoundException("CivilInteractive folder not found: " + Settings.Folder);
                string[] ids;
                lock (Rooms) ids = Rooms.Select(r => r.Id).ToArray();
                foreach (string id in ids)
                {
                    Room? r = Rooms.FirstOrDefault(x => x.Id == id);
                    if (r == null) continue;
                    if (!r.Loaded)
                    {
                        List<ChatMessage> history = store.History(id);
                        r.Loaded = true; // so the next tick reads only what's new
                        Ui(() => OnHistory(r, history));
                    }
                    else
                    {
                        List<ChatMessage> fresh = store.ReadNew(id);
                        if (fresh.Count > 0) Ui(() => OnMessages(id, fresh));
                    }
                }
                if (DateTime.UtcNow - _lastHeartbeat > Heartbeat)
                {
                    _lastHeartbeat = DateTime.UtcNow;
                    store.SayOnline(ids);
                }
                List<OnlinePerson> online = store.Online(OnlineWindow);
                Ui(() => OnOnline(online));
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

        private static void OnMessages(string roomId, List<ChatMessage> list)
        {
            Room? r = Rooms.FirstOrDefault(x => x.Id == roomId);
            if (r == null) return;
            foreach (ChatMessage m in list)
            {
                if (r.Messages.Any(x => x.Id == m.Id)) continue;
                r.Messages.Add(m);
                if (r.Messages.Count > 500) r.Messages.RemoveAt(0);
                MessageAdded?.Invoke(r, m);
                if (!IsMine(m)) Incoming?.Invoke(r, m);
            }
        }

        private static string _onlineSig = "";

        private static void OnOnline(List<OnlinePerson> people)
        {
            string sig = string.Join("|", people.Select(p => p.User + "/" + p.Name + "/" + string.Join(",", p.Rooms)));
            if (sig == _onlineSig) return;
            _onlineSig = sig;
            foreach (Room r in Rooms)
            {
                r.Online = people.Where(p => p.Rooms.Contains(r.Id)).ToList();
                OnlineChanged?.Invoke(r);
            }
        }

        public static bool IsMine(ChatMessage m) => string.Equals(m.User, Environment.UserName, StringComparison.OrdinalIgnoreCase);
    }
}
