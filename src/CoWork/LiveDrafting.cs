using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace CoWork
{
    /// <summary>
    /// Multiplayer drafting: several people edit one drawing at the same time. The host has it open normally;
    /// everyone else opens it read-only. After each command, the objects you added or changed in model space
    /// are copied into a small side drawing in your folder of the session (see <see cref="LiveFolder"/>), with
    /// the ids of anything you erased. Everyone else brings those in between their own commands, without
    /// touching their undo history. Objects are matched across computers by an id kept in their XData
    /// (objects that were in the saved drawing use their handle). Crosshairs are shown with names.
    /// Lock while editing: whatever you have selected (or are changing in a command) is yours until you let go;
    /// everyone else sees it in your crosshair colour, labelled "Sam is editing this", and can't select it.
    /// Only the host saves; their file includes everyone's work.
    /// </summary>
    public static class LiveDrafting
    {
        private const string App = "COWORK";
        private const int MaxHighlighted = 300;

        private static Document? _doc;
        private static Database? _db;
        private static ObjectId _ms;
        private static LiveSession? _session;
        private static string _me = "", _myDir = "", _sessionDir = "";
        private static bool _host;
        private static long _seq, _seed;
        private static volatile bool _ended;
        private static volatile bool _hostLost;      // the host vanished (crash, network) rather than ending it
        private static volatile bool _kicked;        // the host removed this guest
        private static DateTimeOffset _hostSeen;      // last sign of the host (starts at join; pushed on during a hand-over)

        // Watching (view-only), handing over hosting, autosave.
        private static bool _watchRequested;          // joined with Watch
        private static volatile bool _viewer;          // view-only now (asked to watch, or the host said so)
        private static bool _viewerWarned;
        private static volatile LiveRoles _roles = LiveRoles.None;
        private static volatile bool _becomeHost;      // the host handed the session to this guest
        private static DateTime _takeoverStarted, _takeoverNextTry;
        private static bool _replayOwn;                // after taking over: re-apply your own earlier changes too
        private static DateTime _lastAutosave;
        private static bool _applying;

        // Shared ids <-> objects in this drawing.
        private static readonly Dictionary<string, ObjectId> _byId = new();
        private static readonly Dictionary<ObjectId, string> _idOf = new();

        // What changed here since the last batch.
        private static readonly HashSet<ObjectId> _changed = new();
        private static readonly Dictionary<ObjectId, string> _erased = new();

        // Layers, Civil 3D points and point groups changed here since the last batch.
        private static readonly HashSet<ObjectId> _layersDirty = new(), _pointsDirty = new(), _groupsDirty = new();
        // Styles, linetypes and block definitions changed (sent whole, replacing the others' copies); pipes and sample lines changed.
        private static readonly HashSet<ObjectId> _recordsDirty = new(), _pipesDirty = new(), _samplesDirty = new();
        private static DateTime _civilAppliedAt;
        private static readonly HashSet<ObjectId> _ownersTouched = new(); // owners of erased objects we didn't share (maybe inside a block)
        private static readonly HashSet<uint> _pointsErased = new();
        private static readonly HashSet<string> _groupsErased = new(StringComparer.OrdinalIgnoreCase);
        private static bool _civil, _civilSkipped, _civilWarned, _needRegen;

        // Others' batches waiting to be applied, and how far we've got with each person.
        private static readonly ConcurrentQueue<(string Dir, LiveBatch Batch)> _incoming = new();
        private static readonly ConcurrentDictionary<string, long> _queuedUpTo = new();
        private static readonly object _queueLock = new();
        private static System.Threading.Tasks.Task _folderWrites = System.Threading.Tasks.Task.CompletedTask;
        private static string LocalDir(string kind) => Path.Combine(Path.GetTempPath(), "CoWork", kind, _session?.Id ?? "none");

        // Others' crosshairs.
        private static volatile List<LivePerson> _people = new();
        private static volatile bool _peopleChanged;
        private static readonly List<Entity> _marks = new();
        private static Point3d _cursor;
        private static volatile bool _cursorMoved;
        private static DateTime _lastHere = DateTime.MinValue;

        // Lock while editing: what you're holding (shared id -> when you grabbed it), from your selection
        // (pickfirst/grips) and from what the current command selected; and what others are holding.
        private static readonly Dictionary<string, DateTimeOffset> _pickClaims = new(), _cmdClaims = new();
        private static volatile IReadOnlyDictionary<string, DateTimeOffset> _claims = new Dictionary<string, DateTimeOffset>();
        private static volatile Dictionary<string, LivePerson> _held = new();
        private static volatile bool _fixPickfirst;
        private static string? _lastBlockedBy;
        private static DateTime _lastBlockedAt = DateTime.MinValue;

        private static DispatcherTimer? _uiTimer;
        private static Timer? _bgTimer;
        private static int _bgBusy;
        private static volatile bool _bgAgain;
        // Server mode: the newest crosshair/locks of each person, straight from the server.
        private static readonly ConcurrentDictionary<string, LivePerson> _live = new();
        private static DateTime _lastHereFile;
        private static DateTime _lastBackup;
        // Follow mode: your view (sent with your crosshair) and whose view you're following.
        private static (double X, double Y, double H) _view, _applied;
        private static DateTime _lastViewRead;
        private static string? _follow;

        /// <summary>The person whose view you're following (null = nobody).</summary>
        public static string? Following => _follow;

        /// <summary>Follow someone's zoom and pan (null stops).</summary>
        public static void Follow(string? personId)
        {
            _follow = personId;
            _applied = default;
            string? name = personId == null ? null : _people.FirstOrDefault(p => p.Id == personId)?.Name;
            Commands.Write(personId == null ? "\n[CoWork] Stopped following." : $"\n[CoWork] Following {name ?? "them"} - your view moves with theirs. Zoom or pan yourself (or CWFOLLOW) to stop.");
            Changed?.Invoke();
        }

        /// <summary>Reads your view; follows someone else's if you're following them.</summary>
        private static void TrackView(Document doc)
        {
            if (DateTime.UtcNow - _lastViewRead < TimeSpan.FromMilliseconds(120)) return;
            _lastViewRead = DateTime.UtcNow;
            using ViewTableRecord v = doc.Editor.GetCurrentView();
            var now = (v.CenterPoint.X, v.CenterPoint.Y, v.Height);
            static bool Same((double X, double Y, double H) a, (double X, double Y, double H) b) =>
                Math.Abs(a.X - b.X) <= b.H * 0.002 && Math.Abs(a.Y - b.Y) <= b.H * 0.002 && Math.Abs(a.H - b.H) <= b.H * 0.002;
            if (_follow != null)
            {
                LivePerson? leader = _people.FirstOrDefault(p => p.Id == _follow);
                if (leader == null) { Follow(null); return; }
                // You moved the view yourself: stop following.
                if (_applied != default && !Same(now, _applied)) { _follow = null; _applied = default; Commands.Write("\n[CoWork] Stopped following (you moved the view)."); Changed?.Invoke(); }
                else if (leader.ViewH > 0 && !Same((leader.ViewX, leader.ViewY, leader.ViewH), now))
                {
                    double ratio = v.Height > 0 ? v.Width / v.Height : 1.6;
                    v.CenterPoint = new Point2d(leader.ViewX, leader.ViewY);
                    v.Height = leader.ViewH;
                    v.Width = leader.ViewH * ratio;
                    doc.Editor.SetCurrentView(v);
                    now = (leader.ViewX, leader.ViewY, leader.ViewH);
                    _applied = now;
                }
            }
            if (!Same(now, _view)) { _view = now; _cursorMoved = true; } // send it with your crosshair
        }

        public static event Action? Changed;

        public static bool IsActive => _doc != null;
        public static bool IsHost => _host;
        public static string? DrawingPath => _session?.Drawing;
        public static string? SessionId => _session?.Id;
        public static IReadOnlyList<LivePerson> People => _people;
        public static bool IsViewer => _viewer && !_host;
        public static LiveRoles Roles => _roles;

        // ------------------------------------------------------------------ paths

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        /// <summary>The drawing's network path (Z:\... becomes \\server\share\...), so every PC names it the same way.</summary>
        public static string Unc(string path)
        {
            string full = Path.GetFullPath(path);
            if (full.Length >= 2 && full[1] == ':')
            {
                var sb = new StringBuilder(1024);
                int len = sb.Capacity;
                if (WNetGetConnection(full[..2], sb, ref len) == 0) return sb.ToString().TrimEnd('\\') + full[2..];
            }
            return full;
        }

        private static bool SamePath(string a, string b) => string.Equals(Unc(a), Unc(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>A running session for this drawing, if anyone is hosting one.</summary>
        public static LiveSession? SessionFor(string drawing) =>
            LiveFolder.ActiveSessions(ChatHub.DataFolder).FirstOrDefault(s => SamePath(s.Drawing, drawing));

        // ------------------------------------------------------------------ start / join / leave

        /// <summary>Starts a session in this (saved, writable) drawing, with you as host.</summary>
        public static LiveSession Host(Document doc)
        {
            string me = LiveFolder.NewPersonId(Environment.UserName);
            var s = new LiveSession(LiveFolder.NewSessionId(), Unc(doc.Name), me, ChatHub.Settings.Name, DateTimeOffset.UtcNow,
                Server: SessionChannel.StartHosting());
            LiveFolder.WriteSession(ChatHub.DataFolder, s);
            Start(doc, s, me, host: true);
            BackupUi.MakeSoon(s.Drawing, "session start");
            return s;
        }

        /// <summary>Joins a session in a drawing you already have open (read-only).</summary>
        public static void Join(Document doc, LiveSession s, bool watch = false)
        {
            Start(doc, s, LiveFolder.NewPersonId(Environment.UserName), host: false, watch: watch);
            Commands.Write(watch
                ? $"\nWatching {s.HostName}'s CoWork session in {Path.GetFileName(s.Drawing)} - you see everyone's changes and crosshairs, view only."
                : $"\nJoined {s.HostName}'s CoWork session in {Path.GetFileName(s.Drawing)} - changes now sync both ways. Only {s.HostName} can save.");
        }

        /// <summary>Opens the session's drawing read-only (or switches to it) and joins.</summary>
        public static void OpenAndJoin(LiveSession s, bool watch = false)
        {
            if (IsActive)
            {
                if (_session?.Id == s.Id) { Commands.Write("\nYou're already in this CoWork session."); return; }
                Leave(notify: true);
            }
            DocumentCollection docs = AcApp.DocumentManager;
            Document? doc = null;
            foreach (Document d in docs)
                if (Path.IsPathRooted(d.Name) && SamePath(d.Name, s.Drawing)) doc = d;
            if (doc != null && !doc.IsReadOnly)
            {
                System.Windows.MessageBox.Show("You have this drawing open for editing. Close it first, then join - " +
                    $"{s.HostName} is the one editing the file itself.", "CoWork");
                return;
            }
            try
            {
                if (doc == null)
                {
                    if (!File.Exists(s.Drawing)) { System.Windows.MessageBox.Show("Can't reach the drawing:\n" + s.Drawing, "CoWork"); return; }
                    doc = DocumentCollectionExtension.Open(docs, s.Drawing, true);
                }
                docs.MdiActiveDocument = doc;
                Join(doc, s, watch);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Couldn't join: " + ex.Message, "CoWork");
            }
        }

        private static void Start(Document doc, LiveSession s, string me, bool host, bool watch = false, long keepSeq = 0, bool replayAll = false)
        {
            if (IsActive) Leave(notify: false);
            _watchRequested = watch && !host;
            _viewer = _watchRequested;
            _viewerWarned = false;
            _kicked = false;
            _becomeHost = false;
            _replayOwn = replayAll;
            _roles = LiveRoles.None;
            _lastAutosave = DateTime.UtcNow;
            _lastBackup = DateTime.UtcNow;
            _doc = doc;
            _db = doc.Database;
            _session = s;
            _me = me;
            _host = host;
            _ended = false;
            _hostLost = false;
            _hostSeen = DateTimeOffset.UtcNow;
            _seq = keepSeq; // a new host carries on its own numbering, so its earlier changes aren't overwritten
            _sessionDir = LiveFolder.SessionDir(ChatHub.DataFolder, s.Id);
            _myDir = LiveFolder.PersonDir(ChatHub.DataFolder, s.Id, me);
            _ms = SymbolUtilityServices.GetBlockModelSpaceId(_db);
            _seed = _db.Handseed.Value;
            _byId.Clear(); _idOf.Clear(); _changed.Clear(); _erased.Clear(); _queuedUpTo.Clear(); _people = new();
            _pickClaims.Clear(); _cmdClaims.Clear(); _claims = new Dictionary<string, DateTimeOffset>(); _held = new();
            ClearDirty();
            _civil = CivilSync.Available;
            _civilSkipped = _civilWarned = false;
            while (_incoming.TryDequeue(out _)) { }
            Directory.CreateDirectory(_myDir);
            WriteHere();

            // Objects added in earlier sessions already carry their ids.
            using (doc.LockDocument())
            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                EnsureApp(_db, tr);
                foreach (ObjectId bid in (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead))
                {
                    if (tr.GetObject(bid, OpenMode.ForRead) is not BlockTableRecord space || !space.IsLayout) continue; // model space and every layout
                    foreach (ObjectId id in space)
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                        if (ReadId(e) is { } sid) Map(sid, id);
                        else if (_civil && bid == _ms && CivilSync.IsPoint(e)) Map(PointId(CivilSync.PointNumber(e)), id); // points go by number
                    }
                }
                tr.Commit();
            }

            _db.ObjectAppended += OnChanged;
            _db.ObjectModified += OnChanged;
            _db.ObjectErased += OnErased;
            doc.Editor.PointMonitor += OnPointMonitor;
            doc.Editor.SelectionAdded += OnSelectionAdded;
            doc.ImpliedSelectionChanged += OnPickfirstChanged;
            doc.CommandEnded += OnCommandDone;
            doc.CommandCancelled += OnCommandDone;
            doc.CommandFailed += OnCommandDone;
            doc.BeginDocumentClose += OnClosing;
            _live.Clear();
            _lastHereFile = DateTime.MinValue;
            _follow = null; _applied = default; _view = default;
            _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = UiInterval };
            _uiTimer.Tick += UiTick;
            _uiTimer.Start();
            _bgTimer = new Timer(_ => BackgroundTick(), null, TimeSpan.FromMilliseconds(100), BgPeriod);
            SessionChannel.Enter(s);
            SessionChannel.Poke("live:" + s.Id);
            Changed?.Invoke();
        }

        /// <summary>Leaves the session; if you're the host, it ends for everyone.</summary>
        public static void Leave(bool notify, bool end = true)
        {
            Document? doc = _doc;
            if (doc == null) return;
            try { if (doc == AcApp.DocumentManager.MdiActiveDocument && string.IsNullOrEmpty(doc.CommandInProgress)) Flush(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            _bgTimer?.Dispose();
            _bgTimer = null;
            if (_uiTimer != null) { _uiTimer.Stop(); _uiTimer.Tick -= UiTick; _uiTimer = null; }
            try
            {
                _db!.ObjectAppended -= OnChanged;
                _db.ObjectModified -= OnChanged;
                _db.ObjectErased -= OnErased;
                doc.Editor.PointMonitor -= OnPointMonitor;
                doc.Editor.SelectionAdded -= OnSelectionAdded;
                doc.ImpliedSelectionChanged -= OnPickfirstChanged;
                doc.CommandEnded -= OnCommandDone;
                doc.CommandCancelled -= OnCommandDone;
                doc.CommandFailed -= OnCommandDone;
                doc.BeginDocumentClose -= OnClosing;
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            ClearMarks();
            try { File.Delete(Path.Combine(_myDir, "here.json")); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            if (_host && _session != null && end)
                try { LiveFolder.WriteSession(ChatHub.DataFolder, _session with { Ended = true }); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            PokeSession(); // others see you've gone (or that it's over) straight away
            SessionChannel.Exit();
            foreach (string kind in new[] { "in", "out" })
                try { if (Directory.Exists(LocalDir(kind))) Directory.Delete(LocalDir(kind), true); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            if (_host) SessionChannel.StopHosting(end ? null : TimeSpan.FromSeconds(90)); // after a hand-over, give everyone time to move across
            _doc = null;
            _db = null;
            _session = null;
            _host = false;
            _people = new();
            _pickClaims.Clear(); _cmdClaims.Clear(); _claims = new Dictionary<string, DateTimeOffset>(); _held = new();
            ClearDirty();
            if (notify) Changed?.Invoke();
        }

        private static void ClearDirty()
        {
            _layersDirty.Clear(); _pointsDirty.Clear(); _groupsDirty.Clear(); _pointsErased.Clear(); _groupsErased.Clear();
            _recordsDirty.Clear(); _pipesDirty.Clear(); _samplesDirty.Clear(); _ownersTouched.Clear();
        }

        private static string PointId(uint number) => "p:" + number;

        // ------------------------------------------------------------------ host controls

        /// <summary>Host: make a guest view-only, or let them edit again.</summary>
        public static void SetViewer(string personId, bool viewOnly)
        {
            if (!_host) return;
            LiveRoles r = LiveFolder.ReadRoles(_sessionDir);
            var viewers = r.Viewers.Where(v => v != personId).ToList();
            if (viewOnly) viewers.Add(personId);
            _roles = r with { Viewers = viewers.ToArray() };
            LiveFolder.WriteRoles(_sessionDir, _roles);
            PokeSession();
            Changed?.Invoke();
        }

        /// <summary>Host: remove a guest from the session (their copy closes, with a message).</summary>
        public static void Kick(string personId)
        {
            if (!_host) return;
            LiveRoles r = LiveFolder.ReadRoles(_sessionDir);
            _roles = new LiveRoles(r.Viewers.Where(v => v != personId).ToArray(), r.Kicked.Append(personId).Distinct().ToArray());
            LiveFolder.WriteRoles(_sessionDir, _roles);
            PokeSession();
            CoWork.Core.Log.Info($"Removed {personId} from session {_session?.Id}");
            Changed?.Invoke();
        }

        /// <summary>
        /// Host: hand the session to a guest. Call after saving. Everyone is told who the host is now first, then this
        /// PC stops hosting and closes the drawing (so the new host can open it for editing), and once the new host is
        /// in, this PC joins back as a guest - nobody is dropped.
        /// </summary>
        public static void HandOver(string personId, Document doc)
        {
            if (!_host || _session == null) return;
            LivePerson? to = _people.FirstOrDefault(p => p.Id == personId);
            if (to == null) { Commands.Write("\n[CoWork] They're not in the session any more."); return; }
            LiveSession s = _session;
            Flush();
            BackupUi.MakeSoon(s.Drawing, "before hand-over"); // it's just been saved
            // Say who the host is BEFORE going quiet, so guests wait for the new host instead of thinking it's over.
            LiveSession next = s with { HostId = to.Id, HostName = to.Name, HostSince = DateTimeOffset.UtcNow };
            LiveFolder.WriteSession(ChatHub.DataFolder, next);
            SessionChannel.Poke("live:" + s.Id);
            Leave(notify: true, end: false);
            CoWork.Core.Log.Info($"Handed session {s.Id} to {to.Name}");
            void CloseThenRejoin(object? sender, EventArgs e)
            {
                AcApp.Idle -= CloseThenRejoin;
                try { doc.CloseAndDiscard(); } catch (System.Exception ex) { CoWork.Core.Log.Swallowed(ex); }
                Commands.Write($"\n[CoWork] You handed CoWork in {Path.GetFileName(s.Drawing)} to {to.Name}. You'll join back as a guest once they've opened it.");
                RejoinAfterHandOver(next);
            }
            AcApp.Idle += CloseThenRejoin;
        }

        /// <summary>The old host: wait (up to 2 minutes) for the new host to be in, then join their session read-only.</summary>
        private static void RejoinAfterHandOver(LiveSession s)
        {
            DateTime until = DateTime.UtcNow.AddMinutes(2);
            string dir = LiveFolder.SessionDir(ChatHub.DataFolder, s.Id);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    LiveSession? now = LiveFolder.ReadSession(ChatHub.DataFolder, s.Id);
                    if (now == null || now.Ended || IsActive) { timer.Stop(); return; }
                    if (DateTime.UtcNow > until)
                    {
                        timer.Stop();
                        Commands.Write($"\n[CoWork] {now.HostName} hasn't picked up hosting yet - join again from the CoWork page when they have.");
                        return;
                    }
                    bool ready = LiveFolder.Others(dir, "").Any(p => p.Host && p.Id == now.HostId && DateTimeOffset.UtcNow - p.Seen < LiveFolder.HostTimeout);
                    if (!ready) return;
                    timer.Stop();
                    void Join(object? sender, EventArgs e)
                    {
                        AcApp.Idle -= Join;
                        OpenAndJoin(now);
                    }
                    AcApp.Idle += Join;
                }
                catch (System.Exception ex) { timer.Stop(); CoWork.Core.Log.Error("Rejoin after hand-over", ex); }
            };
            timer.Start();
        }

        /// <summary>
        /// This guest has been made the host: reopen the drawing for editing (once the old host has let go of the
        /// file), and carry on - everyone's changes are brought back in so nothing is lost.
        /// </summary>
        private static void TakeOver(Document doc)
        {
            if (DateTime.UtcNow < _takeoverNextTry) return;
            _takeoverNextTry = DateTime.UtcNow.AddSeconds(2);
            LiveSession s = _session!;
            // Wait for the old host to let go of the file (their lock file goes) - up to 30 s, in case it's a stale one.
            try
            {
                if (File.Exists(Path.ChangeExtension(s.Drawing, ".dwl")) && DateTime.UtcNow - _takeoverStarted < TimeSpan.FromSeconds(30)) return;
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            _takeoverNextTry = DateTime.UtcNow.AddSeconds(5);
            string me = _me;
            long seq = _seq;
            try
            {
                Leave(notify: false, end: false);
                try { doc.CloseAndDiscard(); } catch (System.Exception ex) { CoWork.Core.Log.Swallowed(ex); }
                Document mine = DocumentCollectionExtension.Open(AcApp.DocumentManager, s.Drawing, false);
                if (mine.IsReadOnly)
                {
                    // The old host still has it: go back to watching their copy and try again shortly.
                    if (DateTime.UtcNow - _takeoverStarted > TimeSpan.FromSeconds(90))
                    {
                        Commands.Write("\n[CoWork] Couldn't take over hosting - the drawing is still open for editing somewhere.");
                        Start(mine, s, me, host: false, keepSeq: seq);
                        return;
                    }
                    Start(mine, s, me, host: false, keepSeq: seq);
                    _becomeHost = true;
                    return;
                }
                AcApp.DocumentManager.MdiActiveDocument = mine;
                if (SessionChannel.StartHosting() is { } server)
                {
                    s = s with { Server = server };
                    LiveFolder.WriteSession(ChatHub.DataFolder, s);
                }
                Start(mine, s, me, host: true, keepSeq: seq, replayAll: true);
                Commands.Write($"\n[CoWork] You're now the host of {Path.GetFileName(s.Drawing)}. Only you can save; autosave is on.");
                CiUi.Announce("You're now the host", $"CoWork in {Path.GetFileName(s.Drawing)} is yours to save.", "CoWork page", () => CiUi.Show(Page.Session));
            }
            catch (System.Exception ex)
            {
                CoWork.Core.Log.Error("Take over hosting", ex);
                Commands.Write("\n[CoWork] Couldn't take over hosting: " + ex.Message);
            }
        }

        /// <summary>While hosting: a safety copy every 30 minutes (Settings), if the drawing has been saved since the last one.</summary>
        private static void TimedBackup()
        {
            int minutes = BackupUi.Minutes;
            if (minutes <= 0 || _session == null || DateTime.UtcNow - _lastBackup < TimeSpan.FromMinutes(minutes)) return;
            _lastBackup = DateTime.UtcNow;
            BackupUi.MakeSoon(_session.Drawing, $"every {minutes} minutes", onlyIfChanged: true, say: false);
        }

        /// <summary>While hosting: save every few minutes (Settings), between commands, if anything changed.</summary>
        private static void Autosave(Document doc)
        {
            int minutes = ChatHub.Settings.AutosaveMinutes ?? 5;
            if (minutes <= 0 || DateTime.UtcNow - _lastAutosave < TimeSpan.FromMinutes(minutes)) return;
            if (doc != AcApp.DocumentManager.MdiActiveDocument || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            _lastAutosave = DateTime.UtcNow;
            try
            {
                if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) == 0) return;
                doc.SendStringToExecute("_.QSAVE\n", true, false, false);
                Commands.Write($"\n[CoWork] Autosaved {Path.GetFileName(doc.Name)} ({DateTime.Now:HH:mm}).");
            }
            catch (System.Exception ex) { CoWork.Core.Log.Error("Autosave", ex); }
        }

        /// <summary>
        /// The host's CoWork has gone (they ended the session, turned CoWork off, closed the drawing or Civil 3D, or lost
        /// the connection): close this guest's copy so they don't keep drafting into nothing, and say so.
        /// </summary>
        private static void Disconnect(Document doc)
        {
            string host = _session?.HostName ?? "The host";
            string file = Path.GetFileName(_session?.Drawing ?? doc.Name);
            string reason = _kicked ? "kicked" : _hostLost ? "lost" : "ended";
            Leave(notify: true);
            try { doc.CloseAndDiscard(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); } // a guest's copy is read-only; their work is already in the host's drawing
            Commands.Write($"\n[CoWork] You have been disconnected from {file}.");
            CiUi.Disconnected(host, file, reason);
        }

        private static void OnClosing(object? sender, DocumentBeginCloseEventArgs e) => Leave(notify: true);

        // ------------------------------------------------------------------ ids

        private static void EnsureApp(Database db, Transaction tr)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (apps.Has(App)) return;
            apps.UpgradeOpen();
            var r = new RegAppTableRecord { Name = App };
            apps.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
        }

        private static string? ReadId(DBObject o)
        {
            using ResultBuffer? rb = o.GetXDataForApplication(App);
            if (rb == null) return null;
            foreach (TypedValue v in rb)
                if (v.TypeCode == (int)DxfCode.ExtendedDataAsciiString) return v.Value as string;
            return null;
        }

        private static void WriteId(DBObject o, string id) =>
            o.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, id));

        private static void Map(string sid, ObjectId id)
        {
            if (_byId.TryGetValue(sid, out ObjectId old)) _idOf.Remove(old);
            _byId[sid] = id;
            _idOf[id] = sid;
        }

        /// <summary>The shared id of one of our objects, if it has one yet.</summary>
        private static string? KnownId(ObjectId id) =>
            _idOf.TryGetValue(id, out string? sid) ? sid : id.Handle.Value < _seed ? "h:" + id.Handle.Value.ToString("X") : null;

        private static ObjectId Resolve(string sid)
        {
            if (_byId.TryGetValue(sid, out ObjectId id) && id.IsValid && !id.IsErased) return id;
            if (_civil && sid.StartsWith("p:") && uint.TryParse(sid[2..], out uint number))
                try { ObjectId p = CivilSync.FindPoint(_db!, number); if (!p.IsNull && !p.IsErased) return p; } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            if (sid.StartsWith("h:") && long.TryParse(sid[2..], System.Globalization.NumberStyles.HexNumber, null, out long h)
                && _db!.TryGetObjectId(new Handle(h), out ObjectId byHandle) && !byHandle.IsErased)
                return byHandle;
            return ObjectId.Null;
        }

        // ------------------------------------------------------------------ noticing your changes

        private static void OnChanged(object? sender, ObjectEventArgs e)
        {
            if (_applying) return;
            DBObject o = e.DBObject;
            if (o is LayerTableRecord layer)
            {
                if (!layer.IsDependent) _layersDirty.Add(layer.ObjectId);
                return;
            }
            if (o is TextStyleTableRecord or DimStyleTableRecord or LinetypeTableRecord)
            {
                if (!((SymbolTableRecord)o).IsDependent) _recordsDirty.Add(o.ObjectId);
                return;
            }
            if (o is BlockTableRecord) return; // block definitions are sent when something inside them changes (inserting one touches it too)
            if (_civil && CivilSync.IsGroup(o)) { _groupsDirty.Add(o.ObjectId); return; }
            if (o is not Entity ent) return;
            if (CivilSync.IsCivil(ent))
            {
                if (!_civil) return;
                if (CivilSync.IsPoint(ent)) { if (ent.OwnerId == _ms) _pointsDirty.Add(ent.ObjectId); }
                else if (DateTime.UtcNow - _civilAppliedAt < TimeSpan.FromSeconds(1)) return; // Civil 3D catching up after applying someone's change
                else if (CivilSync.IsPipePart(ent)) _pipesDirty.Add(ent.ObjectId);
                else if (CivilSync.IsSampleLine(ent)) _samplesDirty.Add(ent.ObjectId);
                else if (ent.OwnerId == _ms) _civilSkipped = true; // alignments, surfaces... not yet
                return;
            }
            _changed.Add(ent.ObjectId); // model space, a layout, or inside a block definition: sorted out when sending
        }

        private static void OnErased(object? sender, ObjectErasedEventArgs e)
        {
            if (_applying) return;
            DBObject o = e.DBObject;
            try
            {
                if (_civil && CivilSync.IsGroup(o))
                {
                    string name = CivilSync.GroupName(o);
                    if (e.Erased) { _groupsDirty.Remove(o.ObjectId); _groupsErased.Add(name); }
                    else { _groupsErased.Remove(name); _groupsDirty.Add(o.ObjectId); }
                    return;
                }
                if (o is Entity c && c.OwnerId == _ms && CivilSync.IsCivil(c))
                {
                    if (!_civil || !CivilSync.IsPoint(c)) return;
                    uint n = CivilSync.PointNumber(c);
                    if (e.Erased) { _pointsDirty.Remove(c.ObjectId); _pointsErased.Add(n); }
                    else { _pointsErased.Remove(n); _pointsDirty.Add(c.ObjectId); }
                    return;
                }
            }
            catch { return; }
            if (o is not Entity ent || CivilSync.IsCivil(ent)) return;
            ObjectId id = ent.ObjectId;
            if (ent.OwnerId != _ms && KnownId(id) == null) { _ownersTouched.Add(ent.OwnerId); return; }
            if (e.Erased)
            {
                _changed.Remove(id);
                if (KnownId(id) is { } sid) _erased[id] = sid;
            }
            else
            {
                _erased.Remove(id);
                _changed.Add(id); // undone
            }
        }

        private static void OnPointMonitor(object? sender, PointMonitorEventArgs e)
        {
            Point3d p = e.Context.RawPoint;
            if (p.DistanceTo(_cursor) < 1e-6) return;
            _cursor = p;
            _cursorMoved = true;
        }

        // ------------------------------------------------------------------ lock while editing

        /// <summary>Your crosshair colour (others see your cursor and what you're editing in it).</summary>
        public static string MyColour => CrosshairColours.For(Environment.UserName, ChatHub.Settings.CrosshairColour);

        /// <summary>Tells the others straight away (after a colour change, say).</summary>
        public static void Touch() { if (IsActive) WriteHereSoon(); }

        /// <summary>
        /// Something was selected (in a command or by clicking/grips): anything someone else is editing is taken
        /// back out of the selection; the rest becomes yours while the command runs.
        /// </summary>
        private static void OnSelectionAdded(object? sender, SelectionAddedEventArgs e)
        {
            if (_applying || _doc == null) return;
            try
            {
                if (IsViewer)
                {
                    // Watching: nothing can be picked up to change.
                    for (int i = e.AddedObjects.Count - 1; i >= 0; i--) e.Remove(i);
                    if (!_viewerWarned) { _viewerWarned = true; Commands.Write("\n[CoWork] You're watching this session (view only) - you can't change anything."); }
                    return;
                }
                Dictionary<string, LivePerson> held = _held;
                SelectionSet added = e.AddedObjects;
                string? blockedBy = null;
                var grabbed = new List<string>();
                for (int i = added.Count - 1; i >= 0; i--)
                {
                    ObjectId id = added[i].ObjectId;
                    string? sid = KnownId(id);
                    if (sid == null) continue; // brand new here: nobody else has it yet
                    if (held.TryGetValue(sid, out LivePerson? p)) { e.Remove(i); blockedBy = p.Name; }
                    else grabbed.Add(sid);
                }
                if (blockedBy != null) Blocked(blockedBy);
                if (!string.IsNullOrEmpty(_doc.CommandInProgress) && grabbed.Count > 0)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    foreach (string sid in grabbed) _cmdClaims.TryAdd(sid, now);
                    ClaimsChanged();
                }
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        /// <summary>Your selection (clicked objects, grips) changed: hold what's selected, let go of the rest.</summary>
        private static void OnPickfirstChanged(object? sender, EventArgs e)
        {
            if (_applying || _doc == null) return;
            try
            {
                PromptSelectionResult r = _doc.Editor.SelectImplied();
                var now = DateTimeOffset.UtcNow;
                var keep = new Dictionary<string, DateTimeOffset>();
                if (r.Status == PromptStatus.OK && r.Value != null)
                    foreach (ObjectId id in r.Value.GetObjectIds())
                    {
                        if (KnownId(id) is not { } sid) continue;
                        if (_held.ContainsKey(sid)) { _fixPickfirst = true; continue; }
                        keep[sid] = _pickClaims.TryGetValue(sid, out DateTimeOffset t) ? t : now;
                    }
                if (keep.Count == _pickClaims.Count && keep.Keys.All(_pickClaims.ContainsKey)) return;
                _pickClaims.Clear();
                foreach (var kv in keep) _pickClaims[kv.Key] = kv.Value;
                ClaimsChanged();
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        private static void OnCommandDone(object? sender, CommandEventArgs e)
        {
            if (_cmdClaims.Count == 0) return;
            _cmdClaims.Clear();
            ClaimsChanged();
        }

        private static void ClaimsChanged()
        {
            var all = new Dictionary<string, DateTimeOffset>(_pickClaims);
            foreach (var kv in _cmdClaims)
                if (!all.TryGetValue(kv.Key, out DateTimeOffset t) || kv.Value < t) all[kv.Key] = kv.Value;
            _claims = all;
            WriteHereSoon();
        }

        private static void Blocked(string who)
        {
            if (who == _lastBlockedBy && DateTime.UtcNow - _lastBlockedAt < TimeSpan.FromSeconds(3)) return;
            _lastBlockedBy = who;
            _lastBlockedAt = DateTime.UtcNow;
            Commands.Write($"\n[CoWork] {who} is editing this - it's locked until they're done.");
        }

        /// <summary>Between commands: take out of your selection anything someone else holds (they got there first).</summary>
        private static void FixPickfirst(Document doc)
        {
            _fixPickfirst = false;
            try
            {
                PromptSelectionResult r = doc.Editor.SelectImplied();
                if (r.Status != PromptStatus.OK || r.Value == null) return;
                Dictionary<string, LivePerson> held = _held;
                string? who = null;
                var keep = new List<ObjectId>();
                foreach (ObjectId id in r.Value.GetObjectIds())
                {
                    if (KnownId(id) is { } sid && held.TryGetValue(sid, out LivePerson? p)) who = p.Name;
                    else keep.Add(id);
                }
                if (who == null) return;
                doc.Editor.SetImpliedSelection(keep.ToArray());
                Blocked(who);
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        // ------------------------------------------------------------------ the loops

        /// <summary>Every 200 ms on the UI thread: between commands, bring in others' changes and send yours.</summary>
        private static void UiTick(object? sender, EventArgs e)
        {
            Document? doc = _doc;
            if (doc == null) return;
            if (_ended)
            {
                // Wait for any command to finish, then take the guest out of the drawing and tell them why.
                if (!string.IsNullOrEmpty(doc.CommandInProgress)) return;
                Disconnect(doc);
                return;
            }
            if (_becomeHost && string.IsNullOrEmpty(doc.CommandInProgress)) { TakeOver(doc); return; }
            if (_host) { Autosave(doc); TimedBackup(); }
            if (_peopleChanged) { _peopleChanged = false; DrawMarks(); Changed?.Invoke(); }
            if (_cursorMoved && DateTime.UtcNow - _lastHere > TimeSpan.FromMilliseconds(SessionChannel.Fast ? 30 : 80)) WriteHereSoon();
            if (doc != AcApp.DocumentManager.MdiActiveDocument || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            try { TrackView(doc); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            if (_fixPickfirst) FixPickfirst(doc);
            try
            {
                ApplyIncoming();
                Flush();
            }
            catch (System.Exception ex)
            {
                CoWork.Core.Log.Error("CoWork session", ex);
                Commands.Write($"\n[CoWork] Live drafting problem: {ex.Message}");
            }
        }

        /// <summary>Every 500 ms in the background: who's here, others' new batches, whether the session ended.</summary>
        private static void BackgroundTick()
        {
            if (Interlocked.Exchange(ref _bgBusy, 1) == 1) { _bgAgain = true; return; }
            try
            {
                LiveSession? s = _session;
                if (s == null) return;
                _roles = LiveFolder.ReadRoles(_sessionDir);
                if (!_host)
                {
                    LiveSession? now = LiveFolder.ReadSession(ChatHub.DataFolder, s.Id);
                    if (now is { Ended: true }) { _ended = true; return; }
                    if (now != null && now.Server != s.Server && now.HostId == s.HostId) { _session = now; SessionChannel.Enter(now); }
                    if (now != null && now.HostId != s.HostId)
                    {
                        // Hosting was handed over: give the new host time to open the drawing.
                        _session = now;
                        SessionChannel.Enter(now);
                        _hostSeen = DateTimeOffset.UtcNow.AddSeconds(40);
                        if (now.HostId == _me) { _becomeHost = true; _takeoverStarted = DateTime.UtcNow; _takeoverNextTry = DateTime.MinValue; }
                        _peopleChanged = true;
                    }
                    if (_roles.Kicked.Contains(_me)) { _kicked = true; _ended = true; return; }
                    bool viewer = _watchRequested || _roles.Viewers.Contains(_me);
                    if (viewer != _viewer) { _viewer = viewer; _viewerWarned = false; _peopleChanged = true; WriteHere(); }
                }

                List<LivePerson> people = LiveFolder.Merge(LiveFolder.Others(_sessionDir, _me), _live.Values, _me, DateTimeOffset.UtcNow);
                // The host closed Civil 3D, crashed or dropped off the network without ending the session.
                // Only real silence counts: a missing here.json for a moment (rewriting it, handing over) doesn't.
                _hostSeen = LiveFolder.HostLastSeen(people, _hostSeen);
                if (!_host && !_becomeHost && LiveFolder.HostGone(_hostSeen, DateTimeOffset.UtcNow)) { _hostLost = true; _ended = true; return; }
                Dictionary<string, LivePerson> held = LiveLocks.HeldByOthers(people, _me, _claims, DateTimeOffset.UtcNow);
                string sig = Sig(people, held);
                if (sig != Sig(_people, _held))
                {
                    // Something you hold that someone else grabbed first: let go of it.
                    if (held.Keys.Any(_claims.ContainsKey)) _fixPickfirst = true;
                    _people = people;
                    _held = held;
                    _peopleChanged = true;
                }

                var dirs = LiveFolder.OtherPeopleDirs(_sessionDir, _me).ToList();
                if (_replayOwn) { dirs.Add(_myDir); _replayOwn = false; } // new host: bring your own earlier changes back too
                foreach (string dir in dirs)
                    lock (_queueLock) // the same person's changes may also be arriving through the server
                    {
                        long after = _queuedUpTo.TryGetValue(dir, out long q) ? q : 0;
                        foreach (LiveBatch b in LiveFolder.BatchesAfter(dir, after))
                        {
                            _incoming.Enqueue((dir, b));
                            _queuedUpTo[dir] = b.Seq;
                        }
                    }

                if (DateTime.UtcNow - _lastHere > TimeSpan.FromSeconds(5)) WriteHere();
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally
            {
                Interlocked.Exchange(ref _bgBusy, 0);
                if (_bgAgain) { _bgAgain = false; NudgeNow(); }
            }
        }

        // ------------------------------------------------------------------ server mode

        /// <summary>Crosshairs are redrawn this often (faster when they come live through the server).</summary>
        private static TimeSpan UiInterval => SessionChannel.Fast
            ? TimeSpan.FromMilliseconds(30)
            : TimeSpan.FromMilliseconds(Math.Max(80, ChatHub.Settings.LiveRefresh.TotalMilliseconds / 2));

        /// <summary>The folder is read this often: the usual rate, or every second as a backstop while the server tells us of changes.</summary>
        private static TimeSpan BgPeriod => SessionChannel.Fast && ChatHub.Settings.LiveRefresh < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1) : ChatHub.Settings.LiveRefresh;

        /// <summary>The server connected or dropped: switch rates.</summary>
        public static void Retime()
        {
            NudgeNow();
            DispatcherTimer? ui = _uiTimer;
            ui?.Dispatcher.BeginInvoke(new Action(() => { if (_uiTimer == ui) ui.Interval = UiInterval; }));
        }

        /// <summary>Someone changed something in a session (new batch, joined, left, roles, ended): read it now if it's ours.</summary>
        public static void Nudge(string sessionId)
        {
            if (_session?.Id == sessionId) NudgeNow();
        }

        private static void NudgeNow()
        {
            try { _bgTimer?.Change(TimeSpan.Zero, BgPeriod); } catch (ObjectDisposedException) { }
        }

        /// <summary>Someone's crosshair/locks, live from the server (any thread).</summary>
        public static void FromServer(string sessionId, string json)
        {
            if (_session?.Id != sessionId || LiveFolder.PersonFromJson(json) is not { } p || p.Id == _me) return;
            _live[p.Id] = p;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<LivePerson> people = LiveFolder.Merge(_people, new[] { p }, _me, now);
            Dictionary<string, LivePerson> held = LiveLocks.HeldByOthers(people, _me, _claims, now);
            if (held.Keys.Any(_claims.ContainsKey)) _fixPickfirst = true;
            _people = people;
            _held = held;
            _peopleChanged = true;
        }

        private static void PokeSession()
        {
            if (_session != null) SessionChannel.Poke("live:" + _session.Id);
        }

        private static string Sig(List<LivePerson> people, Dictionary<string, LivePerson> held) =>
            string.Join("|", people.Select(p => $"{p.Id}:{p.X:0.###},{p.Y:0.###},{p.Colour}")) + "#" +
            string.Join(",", held.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => h.Key + "=" + h.Value.Id));

        private static void WriteHere()
        {
            if (_session == null) return;
            _lastHere = DateTime.UtcNow;
            _cursorMoved = false;
            try
            {
                var me = new LivePerson(_me, ChatHub.Settings.Name, Environment.UserName, Environment.MachineName,
                    _host, _cursor.X, _cursor.Y, DateTimeOffset.UtcNow, MyColour, new Dictionary<string, DateTimeOffset>(_claims), IsViewer,
                    _view.X, _view.Y, _view.H);
                if (_session is { } s) SessionChannel.SendHere(s.Id, me);
                // With the server passing it on live, the shared folder only needs it every couple of seconds.
                if (SessionChannel.Fast && DateTime.UtcNow - _lastHereFile < TimeSpan.FromSeconds(2)) return;
                _lastHereFile = DateTime.UtcNow;
                LiveFolder.WriteHere(_myDir, me);
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        private static void WriteHereSoon()
        {
            _lastHere = DateTime.UtcNow;
            _cursorMoved = false;
            System.Threading.Tasks.Task.Run(WriteHere);
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Copies what you've changed since last time into a numbered side drawing for everyone else.</summary>
        private static void Flush()
        {
            if (IsViewer)
            {
                // Anything drawn while watching stays on this PC only.
                if (_changed.Count + _erased.Count + _layersDirty.Count + _pointsDirty.Count > 0 && !_viewerWarned)
                {
                    _viewerWarned = true;
                    Commands.Write("\n[CoWork] You're watching (view only): changes you make here aren't shared.");
                }
                _changed.Clear(); _erased.Clear(); ClearDirty(); _pointsErased.Clear(); _groupsErased.Clear();
                return;
            }
            if (_civilSkipped && !_civilWarned)
            {
                _civilWarned = true;
                Commands.Write("\n[CoWork] Heads up: alignments, profiles, surfaces and corridors don't sync yet - points, point groups, pipe networks, sample lines, layers, styles, blocks and normal CAD objects do.");
            }
            if (_changed.Count == 0 && _erased.Count == 0 && _layersDirty.Count == 0 && _pointsDirty.Count == 0
                && _pointsErased.Count == 0 && _groupsDirty.Count == 0 && _groupsErased.Count == 0
                && _recordsDirty.Count == 0 && _pipesDirty.Count == 0 && _samplesDirty.Count == 0 && _ownersTouched.Count == 0) return;
            Database db = _db!;
            if (_ownersTouched.Count > 0)
            {
                using (_doc!.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId owner in _ownersTouched)
                        if (owner.IsValid && !owner.IsErased && tr.GetObject(owner, OpenMode.ForRead) is BlockTableRecord b
                            && !b.IsLayout && !b.IsAnonymous && !b.IsFromExternalReference && !b.IsDependent)
                            _recordsDirty.Add(owner);
                    tr.Commit();
                }
                _ownersTouched.Clear();
            }
            var (layers, points, groups) = ReadExtras(db);
            var (pipes, samples) = ReadCivilExtras(db);
            var erasedPoints = _pointsErased.ToList();
            var erasedGroups = _groupsErased.ToList();
            ClearDirty();
            var ids = _changed.Where(i => i.IsValid && !i.IsErased).ToList();
            var erased = _erased.Values.Distinct().ToList();
            _changed.Clear();
            _erased.Clear();

            long seq = ++_seq;
            string name = LiveFolder.BatchName(seq);
            var sharedIds = new List<string>();
            var layoutOf = new Dictionary<string, string>();
            var records = new List<string>();
            using (_doc!.LockDocument())
            {
                if (ids.Count > 0 || _recordsDirty.Count > 0)
                {
                    var wanted = new Dictionary<ObjectId, string>();
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in ids)
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                            if (e.OwnerId != _ms)
                            {
                                if (tr.GetObject(e.OwnerId, OpenMode.ForRead) is not BlockTableRecord owner) continue;
                                if (!owner.IsLayout)
                                {
                                    // Edited inside a block definition: send the whole definition instead.
                                    if (!owner.IsAnonymous && !owner.IsFromExternalReference && !owner.IsDependent) _recordsDirty.Add(owner.ObjectId);
                                    continue;
                                }
                                if (e is Autodesk.AutoCAD.DatabaseServices.Viewport vp && IsMainViewport(tr, owner, vp)) continue; // the layout's own paper-space viewport
                                string sidL = KnownId(id) ?? ReadId(e) ?? "g:" + Guid.NewGuid().ToString("N");
                                layoutOf[sidL] = ((Layout)tr.GetObject(owner.LayoutId, OpenMode.ForRead)).LayoutName;
                                Map(sidL, id);
                                wanted[id] = sidL;
                                continue;
                            }
                            string sid = KnownId(id) ?? ReadId(e) ?? "g:" + Guid.NewGuid().ToString("N");
                            Map(sid, id);
                            wanted[id] = sid;
                        }
                        tr.Commit();
                    }
                    if (wanted.Count > 0 || _recordsDirty.Count > 0)
                    {
                        using var side = new Database(true, true);
                        ObjectId sideMs = SymbolUtilityServices.GetBlockModelSpaceId(side);
                        // Styles, linetypes and block definitions first, as they are now (they'll replace the others' copies).
                        records.AddRange(CloneRecords(db, side));
                        var map = new IdMapping();
                        if (wanted.Count > 0)
                            db.WblockCloneObjects(new ObjectIdCollection(wanted.Keys.ToArray()), sideMs, map, DuplicateRecordCloning.Ignore, false);
                        using (Transaction tr = side.TransactionManager.StartTransaction())
                        {
                            EnsureApp(side, tr);
                            foreach (IdPair pair in map)
                            {
                                if (!pair.IsPrimary || !pair.IsCloned || !wanted.TryGetValue(pair.Key, out string? sid)) continue;
                                WriteId(tr.GetObject(pair.Value, OpenMode.ForWrite), sid);
                                sharedIds.Add(sid);
                            }
                            tr.Commit();
                        }
                        Directory.CreateDirectory(LocalDir("out"));
                        side.SaveAs(Path.Combine(LocalDir("out"), name + ".dwg"), DwgVersion.Current); // local disk: fast
                    }
                }
            }
            var batch = new LiveBatch(seq, _me, DateTimeOffset.UtcNow, sharedIds, erased, new List<string>(),
                layers.Count > 0 ? layers : null, points.Count > 0 ? points : null, erasedPoints.Count > 0 ? erasedPoints : null,
                groups.Count > 0 ? groups : null, erasedGroups.Count > 0 ? erasedGroups : null,
                records.Count > 0 ? records : null, layoutOf.Count > 0 ? layoutOf.Where(kv => sharedIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value) : null,
                pipes.Count > 0 ? pipes : null, samples.Count > 0 ? samples : null);
            if (batch.IsEmpty) { _seq--; return; }
            string local = Path.Combine(LocalDir("out"), name + ".dwg");
            byte[]? dwg = File.Exists(local) ? File.ReadAllBytes(local) : null;
            string session = _session!.Id, me = _me, myDir = _myDir;
            // Straight to the others (instant), then into the shared folder as the record - one at a time, in order.
            SessionChannel.SendBatch(session, me, batch, dwg);
            _folderWrites = _folderWrites.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(myDir);
                    if (dwg != null) File.Copy(local, Path.Combine(myDir, name + ".dwg"), overwrite: true);
                    LiveFolder.WriteBatch(myDir, batch);
                    SessionChannel.Poke("live:" + session);
                    try { File.Delete(local); } catch (IOException) { }
                }
                catch (System.Exception ex) { CoWork.Core.Log.Error("Writing CoWork changes to the shared folder", ex); }
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>Someone's changes, straight from the server (any thread). Used only if it's their next one in order.</summary>
        public static void FromServerBatch(string sessionId, string person, string json, byte[]? dwg)
        {
            try
            {
                if (_session?.Id != sessionId || person == _me || LiveFolder.BatchFromJson(json) is not { } b) return;
                string dir = Path.Combine(_sessionDir, person);
                lock (_queueLock)
                {
                    long q = _queuedUpTo.TryGetValue(dir, out long v) ? v : 0;
                    if (b.Seq != q + 1) return; // missed one: the shared folder fills the gap
                    string local = Path.Combine(LocalDir("in"), person);
                    Directory.CreateDirectory(local);
                    if (dwg != null) File.WriteAllBytes(Path.Combine(local, LiveFolder.BatchName(b.Seq) + ".dwg"), dwg);
                    _queuedUpTo[dir] = b.Seq;
                    _incoming.Enqueue((local, b));
                }
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { CoWork.Core.Log.Swallowed(ex); }
        }

        /// <summary>The layers, points and point groups you changed, as they are now.</summary>
        private static (List<LiveLayer>, List<LivePoint>, List<LivePointGroup>) ReadExtras(Database db)
        {
            var layers = new List<LiveLayer>();
            var points = new List<LivePoint>();
            var groups = new List<LivePointGroup>();
            if (_layersDirty.Count + _pointsDirty.Count + _groupsDirty.Count == 0) return (layers, points, groups);
            using (_doc!.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in _layersDirty)
                    if (id.IsValid && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is LayerTableRecord l && !l.IsDependent)
                    {
                        AcColor c = l.Color;
                        string colour = c.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByColor
                            ? $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}" : c.ColorIndex.ToString();
                        string lt = tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord ltr ? ltr.Name : "Continuous";
                        layers.Add(new LiveLayer(l.Name, colour, l.IsOff, l.IsFrozen, l.IsLocked, lt, (int)l.LineWeight, l.IsPlottable, l.Description));
                    }
                if (_civil)
                {
                    foreach (ObjectId id in _pointsDirty)
                        if (id.IsValid && !id.IsErased)
                            try
                            {
                                LivePoint p = CivilSync.ReadPoint(tr.GetObject(id, OpenMode.ForRead));
                                points.Add(p);
                                Map(PointId(p.Number), id);
                            }
                            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                    foreach (ObjectId id in _groupsDirty)
                        if (id.IsValid && !id.IsErased)
                            try { if (CivilSync.ReadGroup(tr.GetObject(id, OpenMode.ForRead)) is { } g) groups.Add(g); }
                            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                }
                tr.Commit();
            }
            return (layers, points, groups);
        }

        /// <summary>Changed pipes, structures and sample lines, as they are now.</summary>
        private static (List<LivePipePart>, List<LiveSampleLine>) ReadCivilExtras(Database db)
        {
            var pipes = new List<LivePipePart>();
            var samples = new List<LiveSampleLine>();
            if (!_civil || _pipesDirty.Count + _samplesDirty.Count == 0) { _pipesDirty.Clear(); _samplesDirty.Clear(); return (pipes, samples); }
            using (_doc!.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in _pipesDirty)
                    if (id.IsValid && !id.IsErased)
                        try { if (CivilSync.ReadPipePart(tr.GetObject(id, OpenMode.ForRead)) is { } p) pipes.Add(p); }
                        catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                foreach (ObjectId id in _samplesDirty)
                    if (id.IsValid && !id.IsErased)
                        try { if (CivilSync.ReadSampleLine(tr, tr.GetObject(id, OpenMode.ForRead)) is { } sl) samples.Add(sl); }
                        catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                tr.Commit();
            }
            _pipesDirty.Clear();
            _samplesDirty.Clear();
            return (pipes, samples);
        }

        /// <summary>Copies the changed styles, linetypes and block definitions into the side drawing. Returns them as "kind:name".</summary>
        private static List<string> CloneRecords(Database db, Database side)
        {
            var names = new List<string>();
            if (_recordsDirty.Count == 0) return names;
            var byTable = new Dictionary<string, List<ObjectId>>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in _recordsDirty)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    string? kind = tr.GetObject(id, OpenMode.ForRead) switch
                    {
                        TextStyleTableRecord => "text",
                        DimStyleTableRecord => "dim",
                        LinetypeTableRecord => "ltype",
                        BlockTableRecord => "block",
                        _ => null,
                    };
                    if (kind == null) continue;
                    (byTable.TryGetValue(kind, out var list) ? list : byTable[kind] = new List<ObjectId>()).Add(id);
                    names.Add(LiveBatch.Record(kind, ((SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name));
                }
                tr.Commit();
            }
            _recordsDirty.Clear();
            foreach (var (kind, ids) in byTable)
            {
                ObjectId table = kind switch { "text" => side.TextStyleTableId, "dim" => side.DimStyleTableId, "ltype" => side.LinetypeTableId, _ => side.BlockTableId };
                db.WblockCloneObjects(new ObjectIdCollection(ids.ToArray()), table, new IdMapping(), DuplicateRecordCloning.Replace, false);
            }
            return names;
        }

        /// <summary>A layout's own paper-space viewport (the first one): never sent.</summary>
        private static bool IsMainViewport(Transaction tr, BlockTableRecord layout, Autodesk.AutoCAD.DatabaseServices.Viewport vp)
        {
            foreach (ObjectId id in layout)
                if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Viewport) return id == vp.ObjectId;
            return false;
        }

        /// <summary>The paper space of a layout by name (made if it isn't there yet).</summary>
        private static ObjectId LayoutSpace(Database db, Transaction tr, string layoutName)
        {
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            if (layouts.Contains(layoutName)) return ((Layout)tr.GetObject(layouts.GetAt(layoutName), OpenMode.ForRead)).BlockTableRecordId;
            ObjectId made = LayoutManager.Current.CreateLayout(layoutName);
            return ((Layout)tr.GetObject(made, OpenMode.ForRead)).BlockTableRecordId;
        }

        // ------------------------------------------------------------------ receiving

        private static void ApplyIncoming()
        {
            if (_incoming.IsEmpty) return;
            Database db = _db!;
            using (_doc!.LockDocument())
            {
                _applying = true;
                db.DisableUndoRecording(true);
                try
                {
                    while (_incoming.TryDequeue(out var item)) Apply(db, item.Dir, item.Batch);
                }
                finally
                {
                    db.DisableUndoRecording(false);
                    _applying = false;
                }
            }
            if (_needRegen)
            {
                _needRegen = false;
                try { _doc.Editor.Regen(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            }
            _doc.Editor.UpdateScreen();
        }

        private static void Apply(Database db, string dir, LiveBatch b)
        {
            string dwg = Path.Combine(dir, LiveFolder.BatchName(b.Seq) + ".dwg");
            Database? side = null;
            try
            {
                var incoming = new Dictionary<ObjectId, string>(); // side object -> shared id
                if ((b.Changed.Count > 0 || (b.Records?.Count ?? 0) > 0) && File.Exists(dwg))
                {
                    side = new Database(false, true);
                    side.ReadDwgFile(dwg, FileOpenMode.OpenForReadAndAllShare, true, "");
                    side.CloseInput(true);
                    if (b.Records is { Count: > 0 } recs) ApplyRecords(db, side, recs);
                    using Transaction st = side.TransactionManager.StartTransaction();
                    foreach (ObjectId id in (BlockTableRecord)st.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(side), OpenMode.ForRead))
                        if (st.GetObject(id, OpenMode.ForRead) is Entity e && ReadId(e) is { } sid) incoming[id] = sid;
                    st.Commit();
                }

                // Out with the old: erased objects, and the old versions of changed ones.
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (string sid in b.Erased.Concat(incoming.Values))
                    {
                        ObjectId id = Resolve(sid);
                        if (id.IsNull) continue;
                        tr.GetObject(id, OpenMode.ForWrite).Erase();
                        _byId.Remove(sid);
                        _idOf.Remove(id);
                    }
                    tr.Commit();
                }

                // In with the new: model space, or the layout it came from.
                if (side != null && incoming.Count > 0)
                {
                    var bySpace = new Dictionary<ObjectId, List<ObjectId>>();
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var (sideId, sid) in incoming)
                        {
                            ObjectId space = _ms;
                            if (b.Layouts != null && b.Layouts.TryGetValue(sid, out string? layout))
                                try { space = LayoutSpace(db, tr, layout); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                            (bySpace.TryGetValue(space, out var list) ? list : bySpace[space] = new List<ObjectId>()).Add(sideId);
                        }
                        tr.Commit();
                    }
                    foreach (var (space, ids) in bySpace)
                    {
                        var map = new IdMapping();
                        side.WblockCloneObjects(new ObjectIdCollection(ids.ToArray()), space, map, DuplicateRecordCloning.Ignore, false);
                        foreach (IdPair pair in map)
                            if (pair.IsPrimary && pair.IsCloned && incoming.TryGetValue(pair.Key, out string? sid)) Map(sid, pair.Value);
                    }
                }
            }
            finally
            {
                side?.Dispose();
            }
            ApplyExtras(db, b);
        }

        /// <summary>Styles, linetypes and block definitions from someone else: theirs replaces yours (blocks then redraw).</summary>
        private static void ApplyRecords(Database db, Database side, List<string> records)
        {
            var wanted = new Dictionary<string, List<ObjectId>>();
            using (Transaction st = side.TransactionManager.StartTransaction())
            {
                foreach (string r in records)
                {
                    var (kind, name) = LiveBatch.SplitRecord(r);
                    ObjectId tableId = kind switch { "text" => side.TextStyleTableId, "dim" => side.DimStyleTableId, "ltype" => side.LinetypeTableId, "block" => side.BlockTableId, _ => ObjectId.Null };
                    if (tableId.IsNull || st.GetObject(tableId, OpenMode.ForRead) is not SymbolTable table || !table.Has(name)) continue;
                    (wanted.TryGetValue(kind, out var list) ? list : wanted[kind] = new List<ObjectId>()).Add(table[name]);
                }
                st.Commit();
            }
            foreach (var (kind, ids) in wanted)
            {
                ObjectId target = kind switch { "text" => db.TextStyleTableId, "dim" => db.DimStyleTableId, "ltype" => db.LinetypeTableId, _ => db.BlockTableId };
                try { side.WblockCloneObjects(new ObjectIdCollection(ids.ToArray()), target, new IdMapping(), DuplicateRecordCloning.Replace, false); }
                catch (System.Exception ex) { CoWork.Core.Log.Error($"Updating {kind} definitions", ex); }
            }
            _needRegen = true; // dimensions, text and blocks pick up the new definitions
        }

        /// <summary>Layers first (new objects may sit on them), then points, then point groups.</summary>
        private static void ApplyExtras(Database db, LiveBatch b)
        {
            if (b.LayerStates is { Count: > 0 } layers)
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (LiveLayer l in layers)
                        try { ApplyLayer(db, tr, l); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                    tr.Commit();
                }
            bool points = (b.Points?.Count ?? 0) + (b.ErasedPoints?.Count ?? 0) > 0;
            bool groups = (b.Groups?.Count ?? 0) + (b.ErasedGroups?.Count ?? 0) > 0;
            if (!_civil) return;
            if (points || groups)
            {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (uint n in b.ErasedPoints ?? new())
                    try { CivilSync.ErasePoint(db, tr, n); _byId.Remove(PointId(n)); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                foreach (LivePoint p in b.Points ?? new())
                    try { Map(PointId(p.Number), CivilSync.ApplyPoint(db, tr, p)); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                foreach (string g in b.ErasedGroups ?? new())
                    try { CivilSync.EraseGroup(db, tr, g); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                foreach (LivePointGroup g in b.Groups ?? new())
                    try { CivilSync.ApplyGroup(db, tr, g); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                tr.Commit();
            }
            CivilSync.UpdateGroups(db);
            }
            if ((b.Pipes?.Count ?? 0) + (b.SampleLines?.Count ?? 0) == 0) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (LivePipePart p in b.Pipes ?? new())
                    try { CivilSync.ApplyPipePart(db, tr, p); } catch (System.Exception ex) { CoWork.Core.Log.Error($"Pipe {p.Name}", ex); }
                foreach (LiveSampleLine sl in b.SampleLines ?? new())
                    try { CivilSync.ApplySampleLine(db, tr, sl); } catch (System.Exception ex) { CoWork.Core.Log.Error($"Sample line {sl.Name}", ex); }
                tr.Commit();
            }
            _civilAppliedAt = DateTime.UtcNow;
        }

        private static void ApplyLayer(Database db, Transaction tr, LiveLayer l)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord rec;
            if (table.Has(l.Name)) rec = (LayerTableRecord)tr.GetObject(table[l.Name], OpenMode.ForWrite);
            else
            {
                table.UpgradeOpen();
                rec = new LayerTableRecord { Name = l.Name };
                table.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            AcColor colour = l.Colour.StartsWith('#')
                ? AcColor.FromRgb(Convert.ToByte(l.Colour.Substring(1, 2), 16), Convert.ToByte(l.Colour.Substring(3, 2), 16), Convert.ToByte(l.Colour.Substring(5, 2), 16))
                : AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, short.Parse(l.Colour));
            if (!rec.Color.Equals(colour)) rec.Color = colour;
            var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (linetypes.Has(l.Linetype) && rec.LinetypeObjectId != linetypes[l.Linetype]) rec.LinetypeObjectId = linetypes[l.Linetype];
            if ((int)rec.LineWeight != l.LineWeight) rec.LineWeight = (LineWeight)l.LineWeight;
            if (rec.IsOff != l.Off) { rec.IsOff = l.Off; _needRegen = true; }
            if (rec.IsFrozen != l.Frozen && rec.ObjectId != db.Clayer) { rec.IsFrozen = l.Frozen; _needRegen = true; } // can't freeze your current layer
            if (rec.IsLocked != l.Locked) rec.IsLocked = l.Locked;
            if (rec.IsPlottable != l.Plottable) rec.IsPlottable = l.Plottable;
            if ((rec.Description ?? "") != (l.Description ?? "")) rec.Description = l.Description ?? "";
        }

        // ------------------------------------------------------------------ crosshairs

        private static AcColor ColourOf(LivePerson p)
        {
            string hex = CrosshairColours.For(p.User, p.Colour);
            int rgb = Convert.ToInt32(hex.TrimStart('#'), 16);
            return AcColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));
        }

        /// <summary>Others' crosshairs with their names, and what they're editing in their colour with a label.</summary>
        private static void DrawMarks()
        {
            ClearMarks();
            if (_doc == null) return;
            double size;
            try { size = Convert.ToDouble(AcApp.GetSystemVariable("VIEWSIZE")) * 0.012; } catch { size = 1; }
            var tm = TransientManager.CurrentTransientManager;
            void Show(Entity part, AcColor colour)
            {
                part.Color = colour;
                tm.AddTransient(part, TransientDrawingMode.DirectTopmost, 128, new IntegerCollection());
                _marks.Add(part);
            }

            foreach (LivePerson p in _people)
            {
                AcColor colour = ColourOf(p);
                var at = new Point3d(p.X, p.Y, 0);
                Show(new Circle(at, Vector3d.ZAxis, size * 0.6), colour);
                Show(new Line(at - new Vector3d(size, 0, 0), at + new Vector3d(size, 0, 0)), colour);
                Show(new Line(at - new Vector3d(0, size, 0), at + new Vector3d(0, size, 0)), colour);
                Show(new DBText { Position = at + new Vector3d(size * 0.8, size * 0.6, 0), Height = size * 0.9, TextString = p.Name + (p.Host ? " (host)" : "") }, colour);
            }

            Dictionary<string, LivePerson> held = _held;
            if (held.Count == 0 || _db == null) return;
            try
            {
                using Transaction tr = _db.TransactionManager.StartOpenCloseTransaction();
                foreach (var group in held.GroupBy(h => h.Value.Id))
                {
                    LivePerson p = group.First().Value;
                    AcColor colour = ColourOf(p);
                    Extents3d? box = null;
                    foreach (var (sid, _) in group.Take(MaxHighlighted))
                    {
                        ObjectId id = Resolve(sid);
                        if (id.IsNull || tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                        if (e.Clone() is Entity copy) Show(copy, colour);
                        try
                        {
                            Extents3d x = e.GeometricExtents;
                            if (box is { } b) { b.AddExtents(x); box = b; } else box = x;
                        }
                        catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                    }
                    if (box is { } ext)
                        Show(new DBText
                        {
                            Position = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y + size * 0.4, 0), Height = size * 0.8,
                            TextString = $"{p.Name} is editing this",
                        }, colour);
                }
                tr.Commit();
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        private static void ClearMarks()
        {
            if (_marks.Count == 0) return;
            var tm = TransientManager.CurrentTransientManager;
            foreach (Entity e in _marks)
            {
                try { tm.EraseTransient(e, new IntegerCollection()); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
                e.Dispose();
            }
            _marks.Clear();
        }
    }
}
