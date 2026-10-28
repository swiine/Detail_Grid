using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(DoomChat.ChatApp))]
[assembly: CommandClass(typeof(DoomChat.ChatCommands))]

namespace DoomChat
{
    /// <summary>
    /// DoomChat: the CivDOOM lobby chat, docked in Civil 3D. Built from TTW Chat, with its own commands,
    /// settings, palette and folder so it never mixes with the office chat.
    /// </summary>
    public sealed class ChatApp : IExtensionApplication
    {
        public void Initialize() => AcApp.Idle += OnFirstIdle;

        static void OnFirstIdle(object? sender, EventArgs e)
        {
            AcApp.Idle -= OnFirstIdle;
            try
            {
                ChatService.Initialize();
                ChatService.IncomingMessage += ChatUi.Notify;
                ChatUi.InitTray();
                Write("\nDoomChat loaded - type DOOMCHAT for the CivDOOM lobby, CIVDOOMHOST to start a game.");
            }
            catch (System.Exception ex)
            {
                Write("\nDoomChat failed to start: " + ex.Message);
            }
        }

        public void Terminate()
        {
            try { ChatService.Shutdown(); } catch { }
        }

        internal static void Write(string msg)
        {
            try { AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(msg); } catch { }
        }
    }

    public sealed class ChatCommands
    {
        [CommandMethod("DOOMCHAT", "DOOMCHAT", CommandFlags.Session)]
        public void Chat() => ChatUi.Show();

        [CommandMethod("DOOMCHAT", "DOOMCHATSERVER", CommandFlags.Modal)]
        public void ChatServer()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            var pso = new PromptStringOptions($"\nChat server address (a TTW Chat server) <{ChatService.Settings.ServerUrl}>: ") { AllowSpaces = false };
            var r = ed.GetString(pso);
            if (r.Status != PromptStatus.OK) return;
            var url = r.StringResult.Trim();
            if (url.Length == 0) return;
            url = NormalizeUrl(url);
            var s = ChatService.Settings;
            s.ServerUrl = url;
            s.ChatFolder = ""; // switch this user to server mode
            s.Save();
            ChatService.Reload();
            ed.WriteMessage($"\nDoomChat server set to {url}");
        }

        static string NormalizeUrl(string url)
        {
            if (!url.Contains("://")) url = "ws://" + url;
            bool hasPort = System.Text.RegularExpressions.Regex.IsMatch(url, @"^[a-z]+://[^/]+:\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var b = new UriBuilder(url);
            if (!hasPort) b.Port = 5280;
            if (b.Path == "/" || b.Path.Length == 0) b.Path = "/ws";
            return b.Uri.ToString();
        }

        [CommandMethod("DOOMCHAT", "DOOMCHATFOLDER", CommandFlags.Modal)]
        public void ChatFolder()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            var cur = ChatService.UsesFolder ? ChatService.Settings.ChatFolder : "(none - using server)";
            var pso = new PromptStringOptions($"\nShared DoomChat folder <{cur}>: ") { AllowSpaces = true };
            var r = ed.GetString(pso);
            if (r.Status != PromptStatus.OK) return;
            var path = r.StringResult.Trim().Trim('"');
            if (path.Length == 0) return;
            try { System.IO.Directory.CreateDirectory(path); }
            catch (System.Exception ex) { ed.WriteMessage($"\nCan't use that folder: {ex.Message}"); return; }
            var s = ChatService.Settings;
            s.ChatFolder = path;
            s.Save();
            ChatService.Reload();
            ed.WriteMessage($"\nDoomChat now uses the shared folder {path}");
        }

        [CommandMethod("DOOMCHAT", "DOOMCHATNAME", CommandFlags.Modal)]
        public void ChatName()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            var pso = new PromptStringOptions($"\nYour DoomChat / player name <{ChatService.DisplayName}>: ") { AllowSpaces = true };
            var r = ed.GetString(pso);
            if (r.Status != PromptStatus.OK || r.StringResult.Trim().Length == 0) return;
            var s = ChatService.Settings;
            s.DisplayName = r.StringResult.Trim();
            s.Save();
            ChatService.Reload();
            ed.WriteMessage($"\nName set to {s.DisplayName}");
        }

        [CommandMethod("DOOMCHAT", "DOOMCHATNOTIFY", CommandFlags.Modal)]
        public void ChatNotify()
        {
            var s = ChatService.Settings;
            s.PopupNotifications = !s.PopupNotifications;
            s.Save();
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                $"\nDoomChat pop-up notifications {(s.PopupNotifications ? "ON" : "OFF")}.");
        }
    }

    internal static class ChatUi
    {
        static readonly Guid PaletteId = new("B7E3A1D4-5C2F-4E8B-9A61-3D0F7C2E8B95");
        static PaletteSet? _ps;
        static ChatPanel? _panel;
        static TrayItem? _tray;

        public static bool IsVisible => _ps != null && _ps.Visible;

        public static void Show()
        {
            if (_ps == null)
            {
                _panel = new ChatPanel { IsShown = () => IsVisible };
                _ps = new PaletteSet("DoomChat", "DOOMCHAT", PaletteId)
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new Size(260, 320),
                    DockEnabled = DockSides.Left | DockSides.Right
                };
                _ps.AddVisual("Lobby", _panel);
                if (_ps.Size.Width < 330 || _ps.Size.Height < 500) _ps.Size = new Size(340, 560);
                _ps.StateChanged += (s, e) =>
                {
                    if (e.NewState == StateEventIndex.Show) _panel?.OnShown();
                };
            }
            _ps.Visible = true;
            _ps.KeepFocus = false;
            _panel!.OnShown();
            _panel.FocusInput();
        }

        /// <summary>The panel's Host game button.</summary>
        public static void HostClicked()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { System.Windows.MessageBox.Show("Open a drawing first.", "DoomChat"); return; }
            doc.SendStringToExecute("_CIVDOOMHOST ", true, false, true);
        }

        /// <summary>A Join game button on an invite.</summary>
        public static void JoinGame(string gameId)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { System.Windows.MessageBox.Show("Open a drawing first, then click Join game.", "DoomChat"); return; }
            doc.SendStringToExecute($"_CIVDOOMJOIN {gameId} ", true, false, true);
        }

        public static void UpdateTitle()
        {
            if (_ps == null) return;
            int unread = 0;
            foreach (var c in ChatService.Channels) unread += c.Unread;
            var name = unread > 0 ? $"DoomChat ({unread})" : "DoomChat";
            try { if (_ps.Name != name) _ps.Name = name; } catch { }
            if (_tray != null) _tray.ToolTipText = unread > 0 ? $"DoomChat - {unread} unread" : "DoomChat";
        }

        public static void Notify(ChannelState ch, ChatMessage m)
        {
            bool seen = IsVisible && _panel?.Selected == ch;
            if (!seen)
            {
                ch.Unread++;
                _panel?.RefreshTabs();
                UpdateTitle();
            }
            if (IsVisible) return;

            var invite = ChatService.DoomGameId(m) != null;
            var preview = m.Text.Length > 160 ? m.Text.Substring(0, 160) + "…" : m.Text;
            ChatApp.Write($"\n[DoomChat] {m.Display}: {preview.Replace('\n', ' ')}  (type DOOMCHAT{(invite ? " to join" : " to reply")})\n");

            if (!ChatService.Settings.PopupNotifications || _tray == null) return;
            try
            {
                var bw = new TrayItemBubbleWindow
                {
                    Title = invite ? $"{m.Display} started a CivDOOM game" : $"{m.Display} · DoomChat",
                    Text = preview,
                    HyperText = invite ? "Open DoomChat to join" : "Open DoomChat",
                    IconType = IconType.Information
                };
                bw.Closed += (s, e) =>
                {
                    if (e.CloseReason == TrayItemBubbleWindowCloseReason.HyperlinkClicked) Show();
                };
                _tray.ShowBubbleWindow(bw);
            }
            catch { }
        }

        public static void InitTray()
        {
            if (_tray != null) return;
            _tray = new TrayItem { ToolTipText = "DoomChat", Icon = MakeIcon(), Visible = true };
            _tray.MouseDown += (s, e) => Show();
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.TrayItems.Add(_tray);
            Autodesk.AutoCAD.ApplicationServices.Application.StatusBar.Update();
        }

        /// <summary>A red speech bubble (TTW Chat's is blue), so the two are easy to tell apart.</summary>
        static Icon MakeIcon()
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(Color.FromArgb(214, 52, 40));
                using var path = new GraphicsPath();
                path.AddArc(1, 1, 6, 6, 180, 90);
                path.AddArc(9, 1, 6, 6, 270, 90);
                path.AddArc(9, 6, 6, 6, 0, 90);
                path.AddLine(9, 12, 6, 12);
                path.AddLine(6, 12, 3, 15);
                path.AddLine(3, 15, 4, 12);
                path.AddArc(1, 6, 6, 6, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
                using var dot = new SolidBrush(Color.White);
                g.FillEllipse(dot, 4, 6, 2, 2);
                g.FillEllipse(dot, 7, 6, 2, 2);
                g.FillEllipse(dot, 10, 6, 2, 2);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
