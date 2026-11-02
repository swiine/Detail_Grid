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
        private static bool _applying;

        // Shared ids <-> objects in this drawing.
        private static readonly Dictionary<string, ObjectId> _byId = new();
        private static readonly Dictionary<ObjectId, string> _idOf = new();

        // What changed here since the last batch.
        private static readonly HashSet<ObjectId> _changed = new();
        private static readonly Dictionary<ObjectId, string> _erased = new();

        // Others' batches waiting to be applied, and how far we've got with each person.
        private static readonly ConcurrentQueue<(string Dir, LiveBatch Batch)> _incoming = new();
        private static readonly ConcurrentDictionary<string, long> _queuedUpTo = new();

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

        public static event Action? Changed;

        public static bool IsActive => _doc != null;
        public static bool IsHost => _host;
        public static string? DrawingPath => _session?.Drawing;
        public static string? SessionId => _session?.Id;
        public static IReadOnlyList<LivePerson> People => _people;

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
            var s = new LiveSession(LiveFolder.NewSessionId(), Unc(doc.Name), me, ChatHub.Settings.Name, DateTimeOffset.UtcNow);
            LiveFolder.WriteSession(ChatHub.DataFolder, s);
            Start(doc, s, me, host: true);
            return s;
        }

        /// <summary>Joins a session in a drawing you already have open (read-only).</summary>
        public static void Join(Document doc, LiveSession s)
        {
            Start(doc, s, LiveFolder.NewPersonId(Environment.UserName), host: false);
            Commands.Write($"\nJoined {s.HostName}'s CoWork session in {Path.GetFileName(s.Drawing)} - changes now sync both ways. Only {s.HostName} can save.");
        }

        /// <summary>Opens the session's drawing read-only (or switches to it) and joins.</summary>
        public static void OpenAndJoin(LiveSession s)
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
                Join(doc, s);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Couldn't join: " + ex.Message, "CoWork");
            }
        }

        private static void Start(Document doc, LiveSession s, string me, bool host)
        {
            if (IsActive) Leave(notify: false);
            _doc = doc;
            _db = doc.Database;
            _session = s;
            _me = me;
            _host = host;
            _ended = false;
            _seq = 0;
            _sessionDir = LiveFolder.SessionDir(ChatHub.DataFolder, s.Id);
            _myDir = LiveFolder.PersonDir(ChatHub.DataFolder, s.Id, me);
            _ms = SymbolUtilityServices.GetBlockModelSpaceId(_db);
            _seed = _db.Handseed.Value;
            _byId.Clear(); _idOf.Clear(); _changed.Clear(); _erased.Clear(); _queuedUpTo.Clear(); _people = new();
            _pickClaims.Clear(); _cmdClaims.Clear(); _claims = new Dictionary<string, DateTimeOffset>(); _held = new();
            while (_incoming.TryDequeue(out _)) { }
            Directory.CreateDirectory(_myDir);
            WriteHere();

            // Objects added in earlier sessions already carry their ids.
            using (doc.LockDocument())
            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                EnsureApp(_db, tr);
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(_ms, OpenMode.ForRead))
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && ReadId(e) is { } sid) Map(sid, id);
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
            _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _uiTimer.Tick += UiTick;
            _uiTimer.Start();
            _bgTimer = new Timer(_ => BackgroundTick(), null, 100, 500);
            Changed?.Invoke();
        }

        /// <summary>Leaves the session; if you're the host, it ends for everyone.</summary>
        public static void Leave(bool notify)
        {
            Document? doc = _doc;
            if (doc == null) return;
            try { if (doc == AcApp.DocumentManager.MdiActiveDocument && string.IsNullOrEmpty(doc.CommandInProgress)) Flush(); } catch { }
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
            catch { }
            ClearMarks();
            try { File.Delete(Path.Combine(_myDir, "here.json")); } catch { }
            if (_host && _session != null)
                try { LiveFolder.WriteSession(ChatHub.DataFolder, _session with { Ended = true }); } catch { }
            _doc = null;
            _db = null;
            _session = null;
            _host = false;
            _people = new();
            _pickClaims.Clear(); _cmdClaims.Clear(); _claims = new Dictionary<string, DateTimeOffset>(); _held = new();
            if (notify) Changed?.Invoke();
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
            if (sid.StartsWith("h:") && long.TryParse(sid[2..], System.Globalization.NumberStyles.HexNumber, null, out long h)
                && _db!.TryGetObjectId(new Handle(h), out ObjectId byHandle) && !byHandle.IsErased)
                return byHandle;
            return ObjectId.Null;
        }

        // ------------------------------------------------------------------ noticing your changes

        private static void OnChanged(object? sender, ObjectEventArgs e)
        {
            if (_applying || e.DBObject is not Entity ent) return;
            if (ent.OwnerId == _ms) _changed.Add(ent.ObjectId);
        }

        private static void OnErased(object? sender, ObjectErasedEventArgs e)
        {
            if (_applying || e.DBObject is not Entity ent || ent.OwnerId != _ms) return;
            ObjectId id = ent.ObjectId;
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
            catch { }
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
            catch { }
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
            catch { }
        }

        // ------------------------------------------------------------------ the loops

        /// <summary>Every 200 ms on the UI thread: between commands, bring in others' changes and send yours.</summary>
        private static void UiTick(object? sender, EventArgs e)
        {
            Document? doc = _doc;
            if (doc == null) return;
            if (_ended)
            {
                string host = _session?.HostName ?? "The host";
                Leave(notify: true);
                Commands.Write($"\n{host} ended the CoWork session. Your copy is read-only - use SAVEAS to keep anything.");
                return;
            }
            if (_peopleChanged) { _peopleChanged = false; DrawMarks(); Changed?.Invoke(); }
            if (_cursorMoved && DateTime.UtcNow - _lastHere > TimeSpan.FromMilliseconds(150)) WriteHereSoon();
            if (doc != AcApp.DocumentManager.MdiActiveDocument || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            if (_fixPickfirst) FixPickfirst(doc);
            try
            {
                ApplyIncoming();
                Flush();
            }
            catch (System.Exception ex)
            {
                Commands.Write($"\n[CoWork] Live drafting problem: {ex.Message}");
            }
        }

        /// <summary>Every 500 ms in the background: who's here, others' new batches, whether the session ended.</summary>
        private static void BackgroundTick()
        {
            if (Interlocked.Exchange(ref _bgBusy, 1) == 1) return;
            try
            {
                LiveSession? s = _session;
                if (s == null) return;
                if (!_host && LiveFolder.ReadSession(ChatHub.DataFolder, s.Id) is { Ended: true }) { _ended = true; return; }

                List<LivePerson> people = LiveFolder.Others(_sessionDir, _me);
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

                var fresh = new List<(string, LiveBatch)>();
                foreach (string dir in LiveFolder.OtherPeopleDirs(_sessionDir, _me))
                {
                    long after = _queuedUpTo.TryGetValue(dir, out long q) ? q : 0;
                    foreach (LiveBatch b in LiveFolder.BatchesAfter(dir, after))
                    {
                        fresh.Add((dir, b));
                        _queuedUpTo[dir] = b.Seq;
                    }
                }
                foreach (var item in fresh.OrderBy(f => f.Item2.Time)) _incoming.Enqueue(item);

                if (DateTime.UtcNow - _lastHere > TimeSpan.FromSeconds(5)) WriteHere();
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally
            {
                Interlocked.Exchange(ref _bgBusy, 0);
            }
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
                LiveFolder.WriteHere(_myDir, new LivePerson(_me, ChatHub.Settings.Name, Environment.UserName, Environment.MachineName,
                    _host, _cursor.X, _cursor.Y, DateTimeOffset.UtcNow, MyColour, new Dictionary<string, DateTimeOffset>(_claims)));
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
            if (_changed.Count == 0 && _erased.Count == 0) return;
            Database db = _db!;
            var ids = _changed.Where(i => i.IsValid && !i.IsErased).ToList();
            var erased = _erased.Values.Distinct().ToList();
            _changed.Clear();
            _erased.Clear();

            long seq = ++_seq;
            string name = LiveFolder.BatchName(seq);
            var sharedIds = new List<string>();
            using (_doc!.LockDocument())
            {
                if (ids.Count > 0)
                {
                    var wanted = new Dictionary<ObjectId, string>();
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in ids)
                        {
                            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.OwnerId != _ms) continue;
                            string sid = KnownId(id) ?? ReadId(e) ?? "g:" + Guid.NewGuid().ToString("N");
                            Map(sid, id);
                            wanted[id] = sid;
                        }
                        tr.Commit();
                    }
                    if (wanted.Count > 0)
                    {
                        using var side = new Database(true, true);
                        ObjectId sideMs = SymbolUtilityServices.GetBlockModelSpaceId(side);
                        var map = new IdMapping();
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
                        side.SaveAs(Path.Combine(_myDir, name + ".dwg"), DwgVersion.Current);
                    }
                }
            }
            LiveFolder.WriteBatch(_myDir, new LiveBatch(seq, _me, DateTimeOffset.UtcNow, sharedIds, erased, new List<string>()));
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
            _doc.Editor.UpdateScreen();
        }

        private static void Apply(Database db, string dir, LiveBatch b)
        {
            string dwg = Path.Combine(dir, LiveFolder.BatchName(b.Seq) + ".dwg");
            Database? side = null;
            try
            {
                var incoming = new Dictionary<ObjectId, string>(); // side object -> shared id
                if (b.Changed.Count > 0 && File.Exists(dwg))
                {
                    side = new Database(false, true);
                    side.ReadDwgFile(dwg, FileOpenMode.OpenForReadAndAllShare, true, "");
                    side.CloseInput(true);
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

                // In with the new.
                if (side != null && incoming.Count > 0)
                {
                    var map = new IdMapping();
                    side.WblockCloneObjects(new ObjectIdCollection(incoming.Keys.ToArray()), _ms, map, DuplicateRecordCloning.Ignore, false);
                    foreach (IdPair pair in map)
                        if (pair.IsPrimary && pair.IsCloned && incoming.TryGetValue(pair.Key, out string? sid)) Map(sid, pair.Value);
                }
            }
            finally
            {
                side?.Dispose();
            }
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
                        catch { }
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
            catch { }
        }

        private static void ClearMarks()
        {
            if (_marks.Count == 0) return;
            var tm = TransientManager.CurrentTransientManager;
            foreach (Entity e in _marks)
            {
                try { tm.EraseTransient(e, new IntegerCollection()); } catch { }
                e.Dispose();
            }
            _marks.Clear();
        }
    }
}
