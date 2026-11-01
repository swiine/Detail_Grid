using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using CivilInteractive.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CivilInteractive.Plugin))]
[assembly: CommandClass(typeof(CivilInteractive.Commands))]

namespace CivilInteractive
{
    /// <summary>Starts CivilInteractive when Civil 3D has finished loading.</summary>
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
                CiUi.AddTrayIcon();
                Commands.Write("\nCivilInteractive loaded - type CICHAT (or click the CI icon) to chat, CILIVE to draft together.");
            }
            catch (System.Exception ex)
            {
                Commands.Write("\nCivilInteractive couldn't start: " + ex.Message);
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
            if (doc == null) { System.Windows.MessageBox.Show("Open a drawing first.", "CivilInteractive"); return; }
            doc.SendStringToExecute("_" + command + " ", true, false, true);
        }

        private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

        [CommandMethod("CIVILINTERACTIVE", "CICHAT", CommandFlags.Session)]
        public void Chat() => CiUi.Show();

        [CommandMethod("CIVILINTERACTIVE", "CIVILINTERACTIVE", CommandFlags.Session)]
        public void Open() => CiUi.Show();

        [CommandMethod("CIVILINTERACTIVE", "CINAME", CommandFlags.Modal)]
        public void Name()
        {
            PromptResult r = Ed.GetString(new PromptStringOptions($"\nYour name in CivilInteractive <{ChatHub.Settings.Name}>: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK || r.StringResult.Trim().Length == 0) return;
            CiSettings mine = UserSettings();
            mine.DisplayName = r.StringResult.Trim();
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nName set to {mine.DisplayName}.");
        }

        [CommandMethod("CIVILINTERACTIVE", "CIFOLDER", CommandFlags.Modal)]
        public void Folder()
        {
            PromptResult r = Ed.GetString(new PromptStringOptions($"\nShared CivilInteractive folder <{ChatHub.DataFolder}>: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) return;
            string path = r.StringResult.Trim().Trim('"');
            if (path.Length == 0) return;
            if (!CiSettings.IsAllowedFolder(path)) { Ed.WriteMessage("\nThat's another chat tool's folder - CivilInteractive keeps to its own."); return; }
            try { Directory.CreateDirectory(path); }
            catch (System.Exception ex) { Ed.WriteMessage($"\nCan't use that folder: {ex.Message}"); return; }
            CiSettings mine = UserSettings();
            mine.Folder = path;
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nCivilInteractive now uses {path}.");
        }

        [CommandMethod("CIVILINTERACTIVE", "CIPOPUPS", CommandFlags.Modal)]
        public void Popups()
        {
            CiSettings mine = UserSettings();
            mine.Popups = !ChatHub.Settings.Popups;
            mine.Save();
            ChatHub.Reload();
            Ed.WriteMessage($"\nCivilInteractive pop-ups {(mine.Popups ? "ON" : "OFF")}.");
        }

        /// <summary>This person's own settings only (what CINAME / CIFOLDER / CIPOPUPS change).</summary>
        private static CiSettings UserSettings() => CiSettings.LoadUserOnly();

        // ------------------------------------------------------------------ multiplayer drafting

        /// <summary>CILIVE: start drafting this drawing together (or leave, if already in a session).</summary>
        [CommandMethod("CIVILINTERACTIVE", "CILIVE", CommandFlags.Modal)]
        public void Live()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (LiveDrafting.IsActive)
            {
                Ed.WriteMessage($"\nYou're already drafting live in {Path.GetFileName(LiveDrafting.DrawingPath)}. CILIVELEAVE leaves.");
                return;
            }
            if (!ChatHub.Connected) { Ed.WriteMessage("\nCivilInteractive isn't connected to its folder yet (" + ChatHub.Problem + ")."); return; }
            if (!Path.IsPathRooted(doc.Name)) { Ed.WriteMessage("\nSave the drawing first - live drafting needs a saved file others can open."); return; }
            if (doc.IsReadOnly)
            {
                LiveSession? existing = LiveDrafting.SessionFor(doc.Name);
                if (existing == null) { Ed.WriteMessage("\nThis drawing is open read-only and nobody is drafting in it. Ask whoever has it open to run CILIVE."); return; }
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
            ChatHub.Send(ChatHub.AddRoom(room), $"{ChatHub.Settings.Name} is drafting live in {Path.GetFileName(doc.Name)} - click to join.",
                new MessageButton("Join drafting", "CILIVEJOIN " + s.Id));
            Ed.WriteMessage($"\nLive drafting started in {Path.GetFileName(doc.Name)}. A Join button is in the {Rooms.Title(room)} room." +
                            "\nYou're the host: only you can save, and your save includes everyone's changes. CILIVELEAVE ends it.");
            CiUi.Show();
        }

        /// <summary>CILIVEJOIN [id]: join a drafting session (the Join button does this).</summary>
        [CommandMethod("CIVILINTERACTIVE", "CILIVEJOIN", CommandFlags.Session)]
        public void LiveJoin()
        {
            var ed = Ed;
            if (!ChatHub.Connected) { ed.WriteMessage("\nCivilInteractive isn't connected to its folder yet."); return; }
            PromptResult r = ed.GetString(new PromptStringOptions("\nDrafting session <list>: ") { AllowSpaces = false });
            if (r.Status is not (PromptStatus.OK or PromptStatus.None)) return;
            string id = (r.StringResult ?? "").Trim().ToLowerInvariant();
            var sessions = LiveFolder.ActiveSessions(ChatHub.DataFolder);
            LiveSession? s = sessions.FirstOrDefault(x => x.Id == id);
            if (s == null && id.Length > 0) { ed.WriteMessage("\nThat drafting session has ended."); return; }
            if (s == null)
            {
                if (sessions.Count == 0) { ed.WriteMessage("\nNobody is drafting live right now."); return; }
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

        [CommandMethod("CIVILINTERACTIVE", "CILIVELEAVE", CommandFlags.Session)]
        public void LiveLeave()
        {
            if (!LiveDrafting.IsActive) { Ed.WriteMessage("\nYou're not drafting live."); return; }
            bool host = LiveDrafting.IsHost;
            LiveDrafting.Leave(notify: true);
            Ed.WriteMessage(host ? "\nDrafting session ended for everyone. Remember to save." : "\nYou left the drafting session.");
        }
    }

    /// <summary>The palette window and the status-bar icon.</summary>
    internal static class CiUi
    {
        private static readonly Guid PaletteId = new("5E2B9C41-7A3D-4F0E-8B6C-1D9E4A7F2C30");
        private static PaletteSet? _set;
        private static ChatPalette? _palette;
        private static TrayItem? _tray;

        public static bool IsVisible => _set != null && _set.Visible;

        public static void Show()
        {
            if (_set == null)
            {
                _palette = new ChatPalette { IsShown = () => IsVisible };
                _set = new PaletteSet("CivilInteractive", "CICHAT", PaletteId)
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size(270, 340),
                    DockEnabled = DockSides.Left | DockSides.Right,
                };
                _set.AddVisual("Chat", _palette);
                if (_set.Size.Width < 330 || _set.Size.Height < 500) _set.Size = new Size(350, 580);
                _set.StateChanged += (_, e) => { if (e.NewState == StateEventIndex.Show) _palette?.OnShown(); };
            }
            _set.Visible = true;
            _set.KeepFocus = false;
            _palette!.OnShown();
            _palette.FocusInput();
        }

        public static void UpdateTitle()
        {
            int unread = ChatHub.Rooms.Sum(r => r.Unread);
            string title = unread > 0 ? $"CivilInteractive ({unread})" : "CivilInteractive";
            try { if (_set != null && _set.Name != title) _set.Name = title; } catch { }
            if (_tray != null) _tray.ToolTipText = unread > 0 ? $"CivilInteractive - {unread} unread" : "CivilInteractive";
        }

        /// <summary>A message from someone else: count it, and pop up if the palette isn't open.</summary>
        public static void Notify(Room room, ChatMessage m)
        {
            bool seen = IsVisible && _palette?.Current == room;
            if (!seen)
            {
                room.Unread++;
                _palette?.RefreshTabs();
                UpdateTitle();
            }
            if (IsVisible) return;
            string preview = m.Text.Length > 150 ? m.Text[..150] + "..." : m.Text;
            Commands.Write($"\n[CivilInteractive - {room.Title}] {m.Name}: {preview.Replace('\n', ' ')}");
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
                    else Show();
                };
                _tray.ShowBubbleWindow(bubble);
            }
            catch { }
        }

        public static void AddTrayIcon()
        {
            if (_tray != null) return;
            _tray = new TrayItem { ToolTipText = "CivilInteractive", Icon = MakeIcon(), Visible = true };
            _tray.MouseDown += (_, _) => Show();
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.TrayItems.Add(_tray);
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.Update();
        }

        /// <summary>A teal rounded square with "CI" in white pixel letters.</summary>
        private static Icon MakeIcon()
        {
            string[] art =
            {
                "................",
                ".tttttttttttttt.",
                "tttttttttttttttt",
                "tttttttttttttttt",
                "ttwwwwwttwwwwwtt",
                "ttwwwwwttwwwwwtt",
                "ttwwttttttwwtttt",
                "ttwwttttttwwtttt",
                "ttwwttttttwwtttt",
                "ttwwttttttwwtttt",
                "ttwwwwwttwwwwwtt",
                "ttwwwwwttwwwwwtt",
                "tttttttttttttttt",
                "tttttttttttttttt",
                ".tttttttttttttt.",
                "................",
            };
            using var bmp = new Bitmap(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    bmp.SetPixel(x, y, art[y][x] switch
                    {
                        't' => Color.FromArgb(34, 179, 160),
                        'w' => Color.White,
                        _ => Color.Transparent,
                    });
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
