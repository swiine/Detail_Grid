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
                ChatHub.Start();
                ChatHub.Incoming += CiUi.Notify;
                ChatHub.FriendCameOnline += CiUi.FriendOnline;
                TeamsLink.Start();
                CiUi.AddTrayIcon();
                CiUi.ShowStrip();
                Commands.Write("\nCoWork loaded - the CoWork strip is docked at the edge of the screen (CW brings it back if closed).");
            }
            catch (System.Exception ex)
            {
                Commands.Write("\nCoWork couldn't start: " + ex.Message);
            }
        }

        public void Terminate()
        {
            try { LiveDrafting.Leave(notify: false); } catch { }
            try { ChatHub.Stop(); } catch { }
        }
    }

    public sealed class Commands
    {
        internal static void Write(string msg)
        {
            try { AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(msg); } catch { }
        }

        /// <summary>Runs a command in the current drawing as if typed (from buttons and menus).</summary>
        internal static void Run(string command)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { System.Windows.MessageBox.Show("Open a drawing first.", "CoWork"); return; }
            doc.SendStringToExecute("_" + command + " ", true, false, true);
        }

        private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

        [CommandMethod("COWORK", "CWCHAT", CommandFlags.Session)]
        public void Chat() => CiUi.Show();

        [CommandMethod("COWORK", "CW", CommandFlags.Session)]
        public void Open() => CiUi.Show();

        [CommandMethod("COWORK", "CWNAME", CommandFlags.Modal)]
        public void Name()
        {
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
            CiSettings mine = UserSettings();
            mine.Popups = !ChatHub.Settings.Popups;
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nCoWork pop-ups {(mine.Popups ? "ON" : "OFF")}.");
        }

        // ------------------------------------------------------------------ friends and Teams

        /// <summary>CWFRIENDS: shows your friends list.</summary>
        [CommandMethod("COWORK", "CWFRIENDS", CommandFlags.Session)]
        public void Friends() => CiUi.Show(Page.Friends);

        /// <summary>CWFRIEND: adds a friend by login (or removes one with "-login").</summary>
        [CommandMethod("COWORK", "CWFRIEND", CommandFlags.Modal)]
        public void Friend()
        {
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

        /// <summary>This person's own settings only (what CWNAME / CWFOLDER / CWPOPUPS change).</summary>
        private static CiSettings UserSettings() => CiSettings.LoadUserOnly();

        // ------------------------------------------------------------------ multiplayer drafting

        /// <summary>COWORK: start drafting this drawing together (or leave, if already in a session).</summary>
        [CommandMethod("COWORK", "COWORK", CommandFlags.Modal)]
        public void Live()
        {
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
            Ed.WriteMessage($"\nLive drafting started in {Path.GetFileName(doc.Name)}. A Join button is in the {Rooms.Title(room)} room." +
                            "\nYou're the host: only you can save, and your save includes everyone's changes. CWLEAVE ends it.");
            CiUi.Show(Page.Session);
        }

        /// <summary>CWJOIN [id]: join a CoWork session (the Join button does this).</summary>
        [CommandMethod("COWORK", "CWJOIN", CommandFlags.Session)]
        public void LiveJoin()
        {
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
            LiveDrafting.OpenAndJoin(s);
        }

        [CommandMethod("COWORK", "CWLEAVE", CommandFlags.Session)]
        public void LiveLeave()
        {
            if (!LiveDrafting.IsActive) { Ed.WriteMessage("\nYou're not in a CoWork session."); return; }
            bool host = LiveDrafting.IsHost;
            LiveDrafting.Leave(notify: true);
            Ed.WriteMessage(host ? "\nCoWork session ended for everyone. Remember to save." : "\nYou left the CoWork session.");
        }
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
                try { _set.RecalculateDockSiteLayout(); } catch { }
            }
            catch { }
            finally { _resizing = false; }
        }

        public static void UpdateTitle()
        {
            int unread = ChatHub.Rooms.Sum(r => r.Unread);
            string title = unread > 0 ? $"CoWork ({unread})" : "CoWork";
            try { if (_set != null && _set.Name != title) _set.Name = title; } catch { }
            if (_tray != null) _tray.ToolTipText = unread > 0 ? $"CoWork - {unread} unread" : "CoWork";
            _panel?.RefreshBadges();
        }

        /// <summary>A message from someone else: count it, and pop up if you're not looking at that chat.</summary>
        public static void Notify(Room room, ChatMessage m)
        {
            if (_panel != null && _panel.IsWatching(room)) return;
            room.Unread++;
            _panel?.RefreshTabs();
            UpdateTitle();
            if (_panel != null && IsVisible && _panel.IsExpanded && _panel.Page == Page.Chat) return; // it's in front of them already
            string preview = m.Text.Length > 150 ? m.Text[..150] + "..." : m.Text;
            Commands.Write($"\n[CoWork - {room.Title}] {m.Name}: {preview.Replace('\n', ' ')}");
            if (!ChatHub.Settings.Popups || _tray == null) return;
            try
            {
                var bubble = new TrayItemBubbleWindow
                {
                    Title = $"{m.Name} - {room.Title}", Text = preview, HyperText = m.Button?.Label ?? "Open chat", IconType = IconType.Information,
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
            catch { }
        }

        /// <summary>A friend just came online: a pop-up from the status-bar icon.</summary>
        public static void FriendOnline(Friend f)
        {
            Commands.Write($"\n[CoWork] {f.Name} is online.");
            if (!ChatHub.Settings.Popups || _tray == null) return;
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
            catch { }
        }

        public static void AddTrayIcon()
        {
            if (_tray != null) return;
            _tray = new TrayItem { ToolTipText = "CoWork", Icon = MakeIcon(), Visible = true };
            _tray.MouseDown += (_, _) => Show();
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.TrayItems.Add(_tray);
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.Update();
        }

        /// <summary>
        /// TTW colours: TTW's mark (four dots round a square) in cream on navy.
        /// Civil 3D colours: "CW" in white on Autodesk blue.
        /// </summary>
        private static Icon MakeIcon()
        {
            bool ttw = ChatHub.Settings.ThemeName == "ttw";
            string[] art = ttw
                ? new[]
                {
                    "................",
                    ".bbbbbbbbbbbbbb.",
                    "bbbbbbbbbbbbbbbb",
                    "bbbwwbbbbbbwwbbb",
                    "bbwwwwbbbbwwwwbb",
                    "bbwwwwbbbbwwwwbb",
                    "bbbwwbbbbbbwwbbb",
                    "bbbbbbbwwbbbbbbb",
                    "bbbbbbwwwwbbbbbb",
                    "bbbbbbbwwbbbbbbb",
                    "bbbwwbbbbbbwwbbb",
                    "bbwwwwbbbbwwwwbb",
                    "bbwwwwbbbbwwwwbb",
                    "bbbwwbbbbbbwwbbb",
                    ".bbbbbbbbbbbbbb.",
                    "................",
                }
                : new[]
                {
                    "................",
                    ".bbbbbbbbbbbbbb.",
                    "bbbbbbbbbbbbbbbb",
                    "bbbbbbbbbbbbbbbb",
                    "bbwwwwbbwbbbbwbb",
                    "bwwwwwbbwbbbbwbb",
                    "bwwbbbbbwbbbbwbb",
                    "bwwbbbbbwbbbbwbb",
                    "bwwbbbbbwbwwbwbb",
                    "bwwbbbbbwbwwbwbb",
                    "bwwwwwbbwwbbwwbb",
                    "bbwwwwbbwbbbbwbb",
                    "bbbbbbbbbbbbbbbb",
                    "bbbbbbbbbbbbbbbb",
                    ".bbbbbbbbbbbbbb.",
                    "................",
                };
            Color back = ttw ? Color.FromArgb(2, 67, 96) : Color.FromArgb(6, 150, 215);
            Color front = ttw ? Color.FromArgb(247, 244, 231) : Color.White;
            using var bmp = new Bitmap(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    bmp.SetPixel(x, y, art[y][x] switch { 'b' => back, 'w' => front, _ => Color.Transparent });
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
