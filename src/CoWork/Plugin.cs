using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CoWork.Plugin))]
[assembly: CommandClass(typeof(CoWork.Commands))]

namespace CoWork
{
    /// <summary>Starts CoWork when Civil 3D has finished loading.</summary>
    public sealed class Plugin : IExtensionApplication
    {
        public void Initialize() => AcApp.Idle += OnFirstIdle;

        private static void OnFirstIdle(object? sender, EventArgs e)
        {
            AcApp.Idle -= OnFirstIdle;
            try
            {
                StartLog();
                // Only people you've approved get CoWork: without a licence, just the licence window shows.
                LicenceGate.StartOrWait(StartCoWork);
            }
            catch (System.Exception ex)
            {
                Commands.Write("\nCoWork couldn't start: " + ex.Message);
            }
        }

        private static bool _hooked;

        public static string Version => typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "?";

        /// <summary>Starts the log, and records anything that goes wrong that nothing else caught.</summary>
        private static void StartLog()
        {
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork");
            CoWork.Core.Log.Init(local, CiSettings.Load().Folder, Environment.UserName, Environment.MachineName, Version);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is System.Exception ex && ex.StackTrace?.Contains("CoWork") == true) CoWork.Core.Log.Error("Crash", ex);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => CoWork.Core.Log.Error("Background task", e.Exception);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
            {
                if (e.Exception.StackTrace?.Contains("CoWork") == true) CoWork.Core.Log.Error("Panel", e.Exception);
            };
        }

        private static void StartCoWork()
        {
            LicenceGate.MarkStarted();
            ChatHub.Start();
            if (!_hooked)
            {
                _hooked = true;
                ChatHub.Incoming += CiUi.Notify;
                ChatHub.FriendCameOnline += CiUi.FriendOnline;
                ChatHub.SharedDrawing += CiUi.SomeoneHasIt;
                TeamsLink.Start();
                CiUi.AddTrayIcon();
            }
            CiUi.ShowStrip();
            Commands.Write("\nCoWork loaded - the CoWork strip is docked at the edge of the screen (CW brings it back if closed).");
            WhatsNew.ShowIfUpdated();
            UpdateCheck.Start(); // in case the network wasn't ready when Civil 3D started
            TimeTracker.Start();
        }

        public void Terminate()
        {
            try { LiveDrafting.Leave(notify: false); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            try { ChatHub.Stop(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }
    }

    public sealed class Commands
    {
        internal static void Write(string msg)
        {
            try { AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(msg); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        /// <summary>Runs a command in the current drawing as if typed (from buttons and menus).</summary>
        internal static void Run(string command)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { System.Windows.MessageBox.Show("Open a drawing first.", "CoWork"); return; }
            doc.SendStringToExecute("_" + command + "\n", true, false, true);
        }

        private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

        [CommandMethod("COWORK", "CWCHAT", CommandFlags.Session)]
        public void Chat() { if (LicenceGate.Check()) CiUi.Show(); }

        [CommandMethod("COWORK", "CW", CommandFlags.Session)]
        public void Open() { if (LicenceGate.Check()) CiUi.Show(); }

        [CommandMethod("COWORK", "CWNAME", CommandFlags.Modal)]
        public void Name()
        {
            if (!LicenceGate.Check()) return;
            PromptResult r = Ed.GetString(new PromptStringOptions($"\nYour name in CoWork <{ChatHub.Settings.Name}>: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK || r.StringResult.Trim().Length == 0) return;
            CiSettings mine = UserSettings();
            mine.DisplayName = r.StringResult.Trim();
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nName set to {mine.DisplayName}.");
        }

        [CommandMethod("COWORK", "CWFOLDER", CommandFlags.Modal)]
        public void Folder()
        {
            if (!LicenceGate.Check()) return;
            PromptResult r = Ed.GetString(new PromptStringOptions($"\nShared CoWork folder <{ChatHub.DataFolder}>: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) return;
            string path = r.StringResult.Trim().Trim('"');
            if (path.Length == 0) return;
            if (!CiSettings.IsAllowedFolder(path)) { Ed.WriteMessage("\nThat's another chat tool's folder - CoWork keeps to its own."); return; }
            try { Directory.CreateDirectory(path); }
            catch (System.Exception ex) { Ed.WriteMessage($"\nCan't use that folder: {ex.Message}"); return; }
            CiSettings mine = UserSettings();
            mine.Folder = path;
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nCoWork now uses {path}.");
        }

        [CommandMethod("COWORK", "CWPOPUPS", CommandFlags.Modal)]
        public void Popups()
        {
            if (!LicenceGate.Check()) return;
            CiSettings mine = UserSettings();
            mine.Popups = !ChatHub.Settings.Popups;
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nCoWork pop-ups {(mine.Popups ? "ON" : "OFF")}.");
        }

        // ------------------------------------------------------------------ friends and Teams

        /// <summary>CWFRIENDS: shows your friends list.</summary>
        [CommandMethod("COWORK", "CWFRIENDS", CommandFlags.Session)]
        public void Friends() { if (LicenceGate.Check()) CiUi.Show(Page.Friends); }

        /// <summary>CWFRIEND: adds a friend by login (or removes one with "-login").</summary>
        [CommandMethod("COWORK", "CWFRIEND", CommandFlags.Modal)]
        public void Friend()
        {
            if (!LicenceGate.Check()) return;
            PromptResult r = Ed.GetString(new PromptStringOptions("\nFriend's login or work email to add (-login removes): ") { AllowSpaces = false });
            if (r.Status != PromptStatus.OK) return;
            string text = r.StringResult.Trim();
            if (text.StartsWith('-'))
            {
                ChatHub.RemoveFriend(text[1..]);
                Ed.WriteMessage($"\nRemoved {text[1..]} from your friends.");
                return;
            }
            string user = text.Contains('@') ? text[..text.IndexOf('@')] : text;
            if (user.Length == 0) return;
            OnlinePerson? seen = ChatHub.LastSeenOf(user);
            ChatHub.AddFriend(user, seen?.Name ?? user, text.Contains('@') ? text : seen?.Email);
            Ed.WriteMessage($"\nAdded {seen?.Name ?? user} to your friends.");
        }

        /// <summary>CWTEAMS: sign in to / out of Teams, or show the Teams link's status.</summary>
        [CommandMethod("COWORK", "CWTEAMS", CommandFlags.Session)]
        public void Teams()
        {
            if (!LicenceGate.Check()) return;
            var ed = Ed;
            if (!TeamsLink.IsSetUp)
            {
                ed.WriteMessage("\nThe Teams link is off until IT approves it and adds the tenant and app ids (see TEAMS-IT-SETUP.md).");
                return;
            }
            var opts = new PromptKeywordOptions(TeamsLink.SignedIn
                ? $"\nSigned in to Teams as {TeamsLink.Me!.Name}. [Out/Status] <Status>: "
                : "\nNot signed in to Teams. [In/Status] <In>: ") { AllowNone = true };
            opts.Keywords.Add("In");
            opts.Keywords.Add("Out");
            opts.Keywords.Add("Status");
            PromptResult r = ed.GetKeywords(opts);
            if (r.Status is not (PromptStatus.OK or PromptStatus.None)) return;
            string k = r.Status == PromptStatus.OK && !string.IsNullOrEmpty(r.StringResult) ? r.StringResult : TeamsLink.SignedIn ? "Status" : "In";
            switch (k)
            {
                case "In":
                    ed.WriteMessage("\nOpening Microsoft's sign-in page in your browser...");
                    _ = TeamsLink.SignInAsync();
                    break;
                case "Out":
                    _ = TeamsLink.SignOutAsync();
                    ed.WriteMessage("\nSigned out of Teams.");
                    break;
                default:
                    ed.WriteMessage(TeamsLink.SignedIn ? $"\nSigned in to Teams as {TeamsLink.Me!.Name} ({TeamsLink.Me.Email})." : "\nNot signed in to Teams.");
                    if (TeamsLink.Problem != null) ed.WriteMessage("\n" + TeamsLink.Problem);
                    break;
            }
            CiUi.Show(Page.Teams);
        }

        /// <summary>CWBUSY: do not disturb for an hour (or off, if it's on).</summary>
        [CommandMethod("COWORK", "CWBUSY", CommandFlags.Session)]
        public void Busy()
        {
            if (!LicenceGate.Check()) return;
            if (ChatHub.Settings.IsBusy) { ChatHub.SetBusy(null); Write("\nDo not disturb is off."); return; }
            ChatHub.SetBusy(DateTimeOffset.UtcNow.AddHours(1));
            Write($"\nDo not disturb until {DateTime.Now.AddHours(1):HH:mm} - pop-ups paused. CWBUSY again turns it off.");
        }

        /// <summary>CWFOLLOW: follow someone's view in the CoWork session (or stop following).</summary>
        [CommandMethod("COWORK", "CWFOLLOW", CommandFlags.Session)]
        public void FollowView()
        {
            if (!LicenceGate.Check()) return;
            if (!LiveDrafting.IsActive) { Write("\nYou're not in a CoWork session."); return; }
            if (LiveDrafting.Following != null) { LiveDrafting.Follow(null); return; }
            var people = LiveDrafting.People;
            if (people.Count == 0) { Write("\nNobody else is in the session yet."); return; }
            if (people.Count == 1) { LiveDrafting.Follow(people[0].Id); return; }
            for (int i = 0; i < people.Count; i++) Write($"\n  {i + 1}. {people[i].Name}");
            PromptIntegerResult r = Ed.GetInteger(new PromptIntegerOptions("\nFollow who (number)? ") { LowerLimit = 1, UpperLimit = people.Count });
            if (r.Status == PromptStatus.OK) LiveDrafting.Follow(people[r.Value - 1].Id);
        }

        /// <summary>CWPRESENT: host - present to everyone (or stop); guest - rejoin the presentation.</summary>
        [CommandMethod("COWORK", "CWPRESENT", CommandFlags.Session)]
        public void Present()
        {
            if (!LicenceGate.Check()) return;
            if (!LiveDrafting.IsActive) { Write("\nYou're not in a CoWork session."); return; }
            if (LiveDrafting.IsHost) LiveDrafting.Present(!LiveDrafting.IsPresenting);
            else if (LiveDrafting.Presenter != null) LiveDrafting.RejoinPresentation();
            else Write("\nOnly the host can present. Use CWFOLLOW to follow someone's view.");
        }

        /// <summary>CWTIME: your time on jobs today and this week (if time on job is on, in Settings).</summary>
        [CommandMethod("COWORK", "CWTIME", CommandFlags.Session)]
        public void Time()
        {
            if (!ChatHub.Settings.TimeTracking) { Write("\nTime on job is off - turn it on in CoWork Settings (it stays on your PC only)."); return; }
            Write("\n" + TimeTracker.Report());
        }

        /// <summary>CWCHECK: checks the shared folder, clock, server, licence and more, and says what's wrong.</summary>
        [CommandMethod("COWORK", "CWCHECK", CommandFlags.Session)]
        public void Check()
        {
            Write("\nCoWork check - one moment...");
            string drawing = HealthCheck.ActiveDrawing();
            System.Threading.Tasks.Task.Run(() => HealthCheck.Run(drawing)).ContinueWith(t =>
            {
                string report = t.Exception == null ? Health.Format(t.Result) : "Couldn't finish the check: " + t.Exception.InnerException?.Message;
                Write("\n" + report + "\n");
                Log.Info("CWCHECK\n" + report);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>CWSERVER: shows the server connection; set your own server address (to try it before everyone), or "." for the office default.</summary>
        [CommandMethod("COWORK", "CWSERVER", CommandFlags.Session)]
        public void Server()
        {
            if (!LicenceGate.Check()) return;
            Write("\n" + ServerLink.Status);
            var opts = new PromptStringOptions("\nServer for this PC (pc-name or pc-name:5391; \".\" = office default, Enter = keep): ") { AllowSpaces = false };
            PromptResult r = Ed.GetString(opts);
            if (r.Status != PromptStatus.OK || r.StringResult.Trim().Length == 0) return;
            CiSettings mine = UserSettings();
            mine.Server = r.StringResult.Trim() == "." ? null : r.StringResult.Trim();
            mine.Save();
            ChatHub.Reload();
            Write("\n" + (mine.Server == null ? "Using the office default." : $"Connecting to {mine.Server} - CWSERVER again to see if it worked."));
        }

        /// <summary>CWLICENSE: who CoWork is licensed to, or ask for access / paste a key.</summary>
        [CommandMethod("COWORK", "CWLICENSE", CommandFlags.Session)]
        public void License()
        {
            LicenceGate.Refresh();
            Write("\n" + LicenceGate.Describe(LicenceGate.State, LicenceGate.Info));
            LicenceGate.ShowWindow();
        }

        /// <summary>This person's own settings only (what CWNAME / CWFOLDER / CWPOPUPS change).</summary>
        private static CiSettings UserSettings() => CiSettings.LoadUserOnly();

        // ------------------------------------------------------------------ multiplayer drafting

        /// <summary>COWORK: start drafting this drawing together (or leave, if already in a session).</summary>
        [CommandMethod("COWORK", "COWORK", CommandFlags.Modal)]
        public void Live()
        {
            if (!LicenceGate.Check()) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (LiveDrafting.IsActive)
            {
                Ed.WriteMessage($"\nYou're already CoWorking in {Path.GetFileName(LiveDrafting.DrawingPath)}. CWLEAVE leaves.");
                return;
            }
            if (!ChatHub.Connected) { Ed.WriteMessage("\nCoWork isn't connected to its folder yet (" + ChatHub.Problem + ")."); return; }
            if (!Path.IsPathRooted(doc.Name)) { Ed.WriteMessage("\nSave the drawing first - live drafting needs a saved file others can open."); return; }
            if (doc.IsReadOnly)
            {
                LiveSession? existing = LiveDrafting.SessionFor(doc.Name);
                if (existing == null) { Ed.WriteMessage("\nThis drawing is open read-only and nobody is drafting in it. Ask whoever has it open to run COWORK."); return; }
                LiveDrafting.Join(doc, existing);
                return;
            }
            if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0)
            {
                Ed.WriteMessage("\nSaving first so everyone starts from the same drawing...");
                Ed.Command("_.QSAVE");
            }
            LiveSession s = LiveDrafting.Host(doc);
            string room = Rooms.ForDrawing(doc.Name) ?? Rooms.Lobby;
            ChatHub.Send(ChatHub.AddRoom(room), $"{ChatHub.Settings.Name} is CoWorking in {Path.GetFileName(doc.Name)} - click to join.",
                new MessageButton("Join CoWork", "CWJOIN " + s.Id));
            if (CiUi.PendingInvite is { } invite)
            {
                CiUi.PendingInvite = null;
                ChatHub.Send(ChatHub.OpenPrivate(invite), $"I've started CoWork in {Path.GetFileName(doc.Name)} - come and join.",
                    new MessageButton("Join CoWork", "CWJOIN " + s.Id));
            }
            Ed.WriteMessage($"\nLive drafting started in {Path.GetFileName(doc.Name)}. A Join button is in the {Rooms.Title(room)} room." +
                            "\nYou're the host: only you can save, and your save includes everyone's changes. CWLEAVE ends it.");
            CiUi.Show(Page.Session);
        }

        // ------------------------------------------------------------------ share a view, who has it open

        /// <summary>CWSHARE: posts "look here" - this drawing, this zoom, and any selected objects - in the chat.</summary>
        [CommandMethod("COWORK", "CWSHARE", CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void Share()
        {
            if (!LicenceGate.Check()) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            if (!Path.IsPathRooted(doc.Name)) { ed.WriteMessage("\nSave the drawing first so others can open it."); return; }
            string? note = CiUi.PendingShareNote;
            Room? room = CiUi.PendingShareRoom;
            CiUi.PendingShareNote = null;
            CiUi.PendingShareRoom = null;
            if (note == null)
            {
                PromptResult r = ed.GetString(new PromptStringOptions("\nNote to go with it (Enter for none): ") { AllowSpaces = true });
                if (r.Status is not (PromptStatus.OK or PromptStatus.None)) return;
                note = r.StringResult?.Trim() ?? "";
            }
            var handles = new System.Collections.Generic.List<string>();
            PromptSelectionResult sel = ed.SelectImplied();
            if (sel.Status == PromptStatus.OK && sel.Value != null)
                foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId id in sel.Value.GetObjectIds()) handles.Add(id.Handle.ToString());
            using Autodesk.AutoCAD.DatabaseServices.ViewTableRecord v = ed.GetCurrentView();
            var view = new SharedView(SharedView.NewId(), LiveDrafting.Unc(doc.Name), v.CenterPoint.X, v.CenterPoint.Y, v.Width, v.Height,
                handles.ToArray(), ChatHub.Settings.Name, DateTimeOffset.UtcNow);
            view.Save(ChatHub.DataFolder);
            room ??= ChatHub.AddRoom(Rooms.ForDrawing(doc.Name) ?? Rooms.Lobby);
            string text = $"Look here: {Path.GetFileName(doc.Name)}" + (handles.Count > 0 ? $" ({handles.Count} object{(handles.Count == 1 ? "" : "s")})" : "")
                          + (note.Length > 0 ? $" - {note}" : "");
            ChatHub.Send(room, text, new MessageButton("Go to view", "CWGOTO " + view.Id));
            ed.WriteMessage($"\nShared your view in {room.Title}.");
        }

        /// <summary>CWGOTO id: opens (or switches to) a shared view's drawing, then shows the view (the Go to view button).</summary>
        [CommandMethod("COWORK", "CWGOTO", CommandFlags.Session)]
        public void GoTo()
        {
            if (!LicenceGate.Check()) return;
            PromptResult r = Ed.GetString(new PromptStringOptions("\nShared view: ") { AllowSpaces = false });
            if (r.Status != PromptStatus.OK) return;
            SharedView? view = SharedView.Load(ChatHub.DataFolder, r.StringResult.Trim());
            if (view == null) { Write("\nThat shared view isn't there any more."); return; }
            var docs = AcApp.DocumentManager;
            Autodesk.AutoCAD.ApplicationServices.Document? target = null;
            foreach (Autodesk.AutoCAD.ApplicationServices.Document d in docs)
                if (Path.IsPathRooted(d.Name) && Paths.Same(LiveDrafting.Unc(d.Name), view.Drawing)) target = d;
            try
            {
                if (target == null)
                {
                    if (!File.Exists(view.Drawing)) { Write("\nCan't reach the drawing: " + view.Drawing); return; }
                    target = Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension.Open(docs, view.Drawing, false);
                }
                docs.MdiActiveDocument = target;
                target.SendStringToExecute("_CWGOTOVIEW " + view.Id + "\n", true, false, true);
            }
            catch (System.Exception ex)
            {
                CoWork.Core.Log.Error("Go to view", ex);
                Write("\nCouldn't open the drawing: " + ex.Message);
            }
        }

        /// <summary>CWGOTOVIEW id: zooms to a shared view in this drawing and selects its objects.</summary>
        [CommandMethod("COWORK", "CWGOTOVIEW", CommandFlags.Modal | CommandFlags.Redraw)]
        public void GoToView()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            PromptResult r = ed.GetString(new PromptStringOptions("\nShared view: ") { AllowSpaces = false });
            if (r.Status != PromptStatus.OK || SharedView.Load(ChatHub.DataFolder, r.StringResult.Trim()) is not { } view) return;
            using (Autodesk.AutoCAD.DatabaseServices.ViewTableRecord v = ed.GetCurrentView())
            {
                v.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d(view.CenterX, view.CenterY);
                v.Width = view.Width;
                v.Height = view.Height;
                ed.SetCurrentView(v);
            }
            var ids = new System.Collections.Generic.List<Autodesk.AutoCAD.DatabaseServices.ObjectId>();
            foreach (string h in view.Handles)
                if (long.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out long n)
                    && doc.Database.TryGetObjectId(new Autodesk.AutoCAD.DatabaseServices.Handle(n), out var id) && !id.IsErased)
                    ids.Add(id);
            if (ids.Count > 0) ed.SetImpliedSelection(ids.ToArray());
            ed.WriteMessage($"\nShowing {view.By}'s view" + (view.Handles.Length > 0 ? $" ({ids.Count} of {view.Handles.Length} objects found and selected)." : "."));
        }

        /// <summary>CWHOST path: switches to that drawing and starts CoWork in it ("Ask to CoWork" sends this).</summary>
        [CommandMethod("COWORK", "CWHOST", CommandFlags.Session)]
        public void HostThis()
        {
            if (!LicenceGate.Check()) return;
            PromptResult r = Ed.GetString(new PromptStringOptions("\nDrawing: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) return;
            foreach (Autodesk.AutoCAD.ApplicationServices.Document d in AcApp.DocumentManager)
                if (Path.IsPathRooted(d.Name) && Paths.Same(LiveDrafting.Unc(d.Name), r.StringResult.Trim()))
                {
                    AcApp.DocumentManager.MdiActiveDocument = d;
                    d.SendStringToExecute("_COWORK\n", true, false, true);
                    return;
                }
            Write("\nYou don't have that drawing open any more.");
        }

        /// <summary>CWWATCH [id]: watch a CoWork session, view only (the Watch button does this).</summary>
        [CommandMethod("COWORK", "CWWATCH", CommandFlags.Session)]
        public void LiveWatch() => JoinSession(watch: true);

        /// <summary>CWHANDOVER person: host hands the session to a guest (saves first).</summary>
        [CommandMethod("COWORK", "CWHANDOVER", CommandFlags.Modal)]
        public void HandOver()
        {
            if (!LicenceGate.Check()) return;
            if (!LiveDrafting.IsHost) { Ed.WriteMessage("\nOnly the host can hand CoWork over."); return; }
            PromptResult r = Ed.GetString(new PromptStringOptions("\nHand over to (person id): ") { AllowSpaces = false });
            if (r.Status != PromptStatus.OK) return;
            Ed.WriteMessage("\nSaving so the new host starts from everything so far...");
            Ed.Command("_.QSAVE");
            LiveDrafting.HandOver(r.StringResult.Trim(), AcApp.DocumentManager.MdiActiveDocument);
        }

        /// <summary>CWJOIN [id]: join a CoWork session (the Join button does this).</summary>
        [CommandMethod("COWORK", "CWJOIN", CommandFlags.Session)]
        public void LiveJoin() => JoinSession(watch: false);

        private void JoinSession(bool watch)
        {
            if (!LicenceGate.Check()) return;
            var ed = Ed;
            if (!ChatHub.Connected) { ed.WriteMessage("\nCoWork isn't connected to its folder yet."); return; }
            PromptResult r = ed.GetString(new PromptStringOptions("\nCoWork session <list>: ") { AllowSpaces = false });
            if (r.Status is not (PromptStatus.OK or PromptStatus.None)) return;
            string id = (r.StringResult ?? "").Trim().ToLowerInvariant();
            var sessions = LiveFolder.ActiveSessions(ChatHub.DataFolder);
            LiveSession? s = sessions.FirstOrDefault(x => x.Id == id);
            if (s == null && id.Length > 0) { ed.WriteMessage("\nThat CoWork session has ended."); return; }
            if (s == null)
            {
                if (sessions.Count == 0) { ed.WriteMessage("\nNobody is CoWorking right now."); return; }
                for (int i = 0; i < sessions.Count; i++)
                    ed.WriteMessage($"\n  {i + 1}. {sessions[i].HostName}: {Path.GetFileName(sessions[i].Drawing)}");
                PromptIntegerResult pick = ed.GetInteger(new PromptIntegerOptions($"\nJoin which [1-{sessions.Count}] <1>: ")
                {
                    LowerLimit = 1, UpperLimit = sessions.Count, DefaultValue = 1, UseDefaultValue = true, AllowNone = true,
                });
                if (pick.Status is not (PromptStatus.OK or PromptStatus.None)) return;
                s = sessions[pick.Status == PromptStatus.OK ? pick.Value - 1 : 0];
            }
            LiveDrafting.OpenAndJoin(s, watch);
        }

        [CommandMethod("COWORK", "CWLEAVE", CommandFlags.Session)]
        public void LiveLeave()
        {
            if (!LicenceGate.Check()) return;
            if (!LiveDrafting.IsActive) { Ed.WriteMessage("\nYou're not in a CoWork session."); return; }
            bool host = LiveDrafting.IsHost;
            LiveDrafting.Leave(notify: true);
            Ed.WriteMessage(host ? "\nCoWork session ended for everyone. Remember to save." : "\nYou left the CoWork session.");
        }
    }

    /// <summary>
    /// "What's new": each release carries whatsnew.txt next to CoWork.dll. The first time a PC starts a new version,
    /// a pop-up says so and Settings shows the notes.
    /// </summary>
    internal static class WhatsNew
    {
        private static string Seen => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork", "last-version.txt");

        /// <summary>The notes for this version (blank if none shipped).</summary>
        public static string Notes
        {
            get
            {
                try
                {
                    string f = Path.Combine(Path.GetDirectoryName(typeof(WhatsNew).Assembly.Location) ?? "", "whatsnew.txt");
                    return File.Exists(f) ? File.ReadAllText(f).Trim() : "";
                }
                catch (System.Exception ex) { CoWork.Core.Log.Swallowed(ex); return ""; }
            }
        }

        public static void ShowIfUpdated()
        {
            try
            {
                string before = File.Exists(Seen) ? File.ReadAllText(Seen).Trim() : "";
                if (before == Plugin.Version) return;
                Directory.CreateDirectory(Path.GetDirectoryName(Seen)!);
                File.WriteAllText(Seen, Plugin.Version);
                if (before.Length == 0) return; // first install: nothing to announce
                CoWork.Core.Log.Info($"Updated from {before} to {Plugin.Version}");
                Commands.Write($"\n[CoWork] Updated to {Plugin.Version} - see what's new in Settings.");
                CiUi.Announce($"CoWork updated to {Plugin.Version}", FirstLines(Notes, 3), "What's new", () => CiUi.Show(Page.Settings));
            }
            catch (System.Exception ex) { CoWork.Core.Log.Swallowed(ex); }
        }

        private static string FirstLines(string text, int n) =>
            string.Join("\n", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !char.IsDigit(l[0])).Take(n));
    }

    /// <summary>The palette window (the CoWork strip and its pages) and the status-bar icon.</summary>
    internal static class CiUi
    {
        private static readonly Guid PaletteId = new("5E2B9C41-7A3D-4F0E-8B6C-1D9E4A7F2C30");
        private const double ExpandedDefault = 360;
        private static PaletteSet? _set;
        private static CoWorkPanel? _panel;
        private static TrayItem? _tray;
        private static double _expandedWidth = ExpandedDefault;
        private static bool _resizing;

        public static bool IsVisible => _set != null && _set.Visible;

        // Handed from the panel to CWSHARE / COWORK (which run in the drawing).
        public static string? PendingShareNote;
        public static Room? PendingShareRoom;
        public static string? PendingInvite;

        /// <summary>Someone else has a drawing you have open.</summary>
        public static void SomeoneHasIt(JobAlerts.Alert a)
        {
            OnlinePerson p = a.Person;
            string file = Path.GetFileName(a.Drawing);
            if (a.SameDrawing)
            {
                Commands.Write($"\n[CoWork] {p.Name} also has {file} open" + (p.LiveSession != null ? " (they're CoWorking in it)." : "."));
                Announce($"{p.Name} also has {file} open", "Message them, or CoWork in it together (CoWork page).", "Message " + p.Name,
                    () => { Show(); _panel?.Select(ChatHub.OpenPrivate(p.User)); });
            }
            else
            {
                Commands.Write($"\n[CoWork] {p.Name} opened {file} (job {a.Job}).");
                if (!ChatHub.Settings.IsBusy)
                    Announce($"{p.Name} is in job {a.Job}", $"They opened {file}.", "Message " + p.Name,
                        () => { Show(); _panel?.Select(ChatHub.OpenPrivate(p.User)); });
            }
            _panel?.RefreshSessionIfShown();
        }

        /// <summary>Shows the CoWork strip (docked on the right the first time) with a page slid out.</summary>
        public static void Show(Page page = Page.Chat) => Show(page, expand: true);

        private static void Show(Page page, bool expand)
        {
            if (_set == null)
            {
                _panel = new CoWorkPanel { IsShown = () => IsVisible };
                _set = new PaletteSet("CoWork", "CWCHAT", PaletteId)
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size((int)CoWorkPanel.RailWidth + 4, 300),
                    DockEnabled = DockSides.Left | DockSides.Right,
                };
                _set.AddVisual("CoWork", _panel);
                _set.StateChanged += (_, e) => { if (e.NewState == StateEventIndex.Show) _panel?.OnShown(); };
                _set.SizeChanged += (_, _) =>
                {
                    // Remember the width people drag it to (while it's open), and keep the strip on the outside edge.
                    if (!_resizing && _panel!.IsExpanded && _set.Size.Width > CoWorkPanel.RailWidth + 60) _expandedWidth = _set.Size.Width;
                    _panel!.SetRailSide(_set.Dock == DockSides.Left);
                };
                _panel.ExpandedChanged += Resize;
                _set.Visible = true;
                if (_set.Dock == DockSides.None && _set.Size.Width < 200) _set.Dock = DockSides.Right;
                _panel.SetRailSide(_set.Dock == DockSides.Left);
            }
            _set.Visible = true;
            _set.KeepFocus = false;
            if (expand) _panel!.ShowPage(page);
            else Resize(_panel!.IsExpanded);
            _panel!.OnShown();
        }

        /// <summary>Hides the strip (when the licence stops working).</summary>
        public static void Hide()
        {
            try { if (_set != null) _set.Visible = false; } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        /// <summary>Starts with just the strip showing (called at startup if CoWork was open last time).</summary>
        public static void ShowStrip() => Show(Page.Chat, expand: false);

        /// <summary>Widens the palette when a page slides out and narrows it to the strip when it tucks away.</summary>
        private static void Resize(bool expanded)
        {
            if (_set == null) return;
            try
            {
                _resizing = true;
                int h = Math.Max(_set.Size.Height, 300);
                int w = expanded ? (int)Math.Max(_expandedWidth, 280) : (int)CoWorkPanel.RailWidth + 4;
                _set.Size = new Size(w, h);
                try { _set.RecalculateDockSiteLayout(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            finally { _resizing = false; }
        }

        public static void UpdateTitle()
        {
            int unread = ChatHub.Rooms.Sum(r => r.Unread);
            string title = unread > 0 ? $"CoWork ({unread})" : "CoWork";
            try { if (_set != null && _set.Name != title) _set.Name = title; } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            if (_tray != null) _tray.ToolTipText = unread > 0 ? $"CoWork - {unread} unread" : "CoWork";
            _panel?.RefreshBadges();
        }

        /// <summary>A message from someone else: count it, and pop up if you're not looking at that chat.</summary>
        public static void Notify(Room room, ChatMessage m)
        {
            if (_panel != null && _panel.IsWatching(room)) return;
            bool mentioned = ChatHub.MentionsMe(m);
            room.Unread++;
            _panel?.RefreshTabs();
            UpdateTitle();
            // Already looking at the chat: no pop-up, unless you're @mentioned somewhere else.
            if (!mentioned && _panel != null && IsVisible && _panel.IsExpanded && _panel.Page == Page.Chat) return;
            string preview = m.Text.Length > 150 ? m.Text[..150] + "..." : m.Text;
            Commands.Write($"\n[CoWork - {room.Title}] {(mentioned ? "@ " : "")}{m.Name}: {preview.Replace('\n', ' ')}");
            // Do not disturb pauses every pop-up; otherwise @mentions always pop up, the rest only if pop-ups are on.
            if (ChatHub.Settings.IsBusy || (!ChatHub.Settings.Popups && !mentioned) || _tray == null) return;
            try
            {
                var bubble = new TrayItemBubbleWindow
                {
                    Title = mentioned ? $"{m.Name} mentioned you - {room.Title}" : $"{m.Name} - {room.Title}", Text = preview, HyperText = m.Button?.Label ?? "Open chat", IconType = IconType.Information,
                };
                string? command = m.Button?.Command;
                bubble.Closed += (_, e) =>
                {
                    if (e.CloseReason != TrayItemBubbleWindowCloseReason.HyperlinkClicked) return;
                    if (command != null) Commands.Run(command);
                    else { Show(); _panel?.Select(room); }
                };
                _tray.ShowBubbleWindow(bubble);
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        /// <summary>A pop-up from the status-bar icon with a link (doesn't block Civil 3D).</summary>
        public static void Announce(string title, string text, string link, Action onLink)
        {
            if (_tray == null || ChatHub.Settings.IsBusy) return;
            try
            {
                var bubble = new TrayItemBubbleWindow { Title = title, Text = text, HyperText = link, IconType = IconType.Information };
                bubble.Closed += (_, e) => { if (e.CloseReason == TrayItemBubbleWindowCloseReason.HyperlinkClicked) onLink(); };
                _tray.ShowBubbleWindow(bubble);
            }
            catch (System.Exception ex) { CoWork.Core.Log.Swallowed(ex); }
        }

        /// <summary>A guest was taken out of a CoWork session because the host's CoWork went away.</summary>
        public static void Disconnected(string host, string file, string reason)
        {
            string why = reason switch
            {
                "lost" => $"Lost the connection to {host} - they may have closed Civil 3D or dropped off the network.",
                "kicked" => $"{host} removed you from the CoWork session in {file}.",
                _ => $"{host} turned CoWork off for {file}.",
            };
            try
            {
                AcApp.ShowAlertDialog($"You have been disconnected.\n\n{why}\n\nYour copy of {file} has been closed so you don't keep " +
                    $"working in a drawing that's no longer shared. Everything that reached {host} before this is in their drawing.");
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            _panel?.RefreshLive();
        }

        /// <summary>A friend just came online: a pop-up from the status-bar icon.</summary>
        public static void FriendOnline(Friend f)
        {
            Commands.Write($"\n[CoWork] {f.Name} is online.");
            if (ChatHub.Settings.IsBusy || !ChatHub.Settings.Popups || _tray == null) return;
            try
            {
                OnlinePerson? p = ChatHub.LastSeenOf(f.User);
                var bubble = new TrayItemBubbleWindow
                {
                    Title = $"{f.Name} is online", Text = p?.Drawing != null ? "Working in " + p.Drawing : "Your friend just opened Civil 3D.",
                    HyperText = "Message them", IconType = IconType.Information,
                };
                bubble.Closed += (_, e) =>
                {
                    if (e.CloseReason != TrayItemBubbleWindowCloseReason.HyperlinkClicked) return;
                    Show();
                    _panel?.Select(ChatHub.OpenPrivate(f.User));
                };
                _tray.ShowBubbleWindow(bubble);
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        public static void AddTrayIcon()
        {
            if (_tray != null) return;
            _tray = new TrayItem { ToolTipText = "CoWork", Icon = MakeIcon(), Visible = true };
            _tray.MouseDown += (_, _) => { if (LicenceGate.Ok) Show(); else LicenceGate.ShowWindow(); };
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.TrayItems.Add(_tray);
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.Update();
        }

        /// <summary>
        /// The status-bar icon: your own cowork-icon.ico / cowork-icon.png if there is one next to the DLLs,
        /// otherwise the Interlock logo in your colour scheme.
        /// </summary>
        private static Icon MakeIcon()
        {
            // Your own icon, if there's one next to the DLLs: cowork-icon.ico (best: 16, 24 and 32 px inside)
            // or cowork-icon.png (16 x 16, transparent background).
            string dir = Path.GetDirectoryName(typeof(CiUi).Assembly.Location) ?? "";
            try
            {
                string ico = Path.Combine(dir, "cowork-icon.ico"), png = Path.Combine(dir, "cowork-icon.png");
                if (File.Exists(ico)) return new Icon(ico, 16, 16);
                if (File.Exists(png))
                {
                    using var src = new Bitmap(png);
                    using var small = new Bitmap(src, new Size(16, 16));
                    return Icon.FromHandle(small.GetHicon());
                }
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); } // a bad file: fall back to the built-in one
            try { return Logos.Icon(Brand.PaletteFor(ChatHub.Settings.ThemeName)); }
            catch { return SystemIcons.Information; }
        }

        /// <summary>Redraws the status-bar icon (after picking a colour scheme).</summary>
        public static void RefreshIcon()
        {
            if (_tray == null) return;
            try
            {
                _tray.Icon = MakeIcon();
                Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.Update();
            }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }
    }
}
