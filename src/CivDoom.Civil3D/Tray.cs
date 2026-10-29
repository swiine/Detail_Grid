using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using CivDoom.Engine;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CivDoom.Civil3D;

/// <summary>
/// CivDOOM's icon in the Civil 3D status bar: a pixel skull. Click it for a menu (play, draw a level, host or join a
/// co-op game, settings). It also checks the CivDOOM server every so often and pops up a bubble when someone
/// starts a co-op game, with a link to join.
/// </summary>
internal static class CivDoomTray
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(12);

    private static TrayItem? _tray;
    private static Control? _ui;                       // for getting back onto the UI thread
    private static System.Threading.Timer? _timer;
    private static List<GameListing> _games = new();
    private static readonly HashSet<string> _announced = new();
    private static bool _firstCheck = true;
    private static int _checking;

    public static void Init()
    {
        if (_tray != null) return;
        _ui = new Control();
        _ = _ui.Handle; // create it on this (the UI) thread
        _tray = new TrayItem { ToolTipText = "CivDOOM", Icon = MakeIcon(), Visible = true };
        _tray.MouseDown += (_, e) => ShowMenu();
        AcApp.StatusBar.TrayItems.Add(_tray);
        AcApp.StatusBar.Update();
        _timer = new System.Threading.Timer(_ => CheckServer(), null, TimeSpan.FromSeconds(3), CheckEvery);
    }

    public static void Shutdown()
    {
        _timer?.Dispose();
        _timer = null;
        if (_tray == null) return;
        try
        {
            AcApp.StatusBar.TrayItems.Remove(_tray);
            AcApp.StatusBar.Update();
        }
        catch (System.Exception) { /* Civil 3D is closing */ }
        _tray = null;
    }

    // ------------------------------------------------------------------ menu

    private static void ShowMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripLabel("CivDOOM") { Font = new Font(menu.Font, FontStyle.Bold) });
        Item(menu, "Play this drawing", "CIVDOOM");
        Item(menu, "Play in a window", "CIVDOOMWINDOW");
        Item(menu, "Draw a new level", "CIVDOOMGEN");
        Item(menu, "Change the area (theme)...", "CIVDOOMTHEME");
        menu.Items.Add(new ToolStripSeparator());
        Item(menu, "Host a co-op game", "CIVDOOMHOST");

        var join = new ToolStripMenuItem("Join a co-op game");
        NetConfig cfg = Config();
        if (!cfg.HasServer)
        {
            join.DropDownItems.Add(new ToolStripMenuItem("Set up a server first...", null, (_, _) => Run("CIVDOOMNET")));
        }
        else
        {
            foreach (GameListing g in _games)
            {
                string id = g.Id;
                join.DropDownItems.Add(new ToolStripMenuItem($"{g.HostName}: {g.LevelName}  ({g.Players} playing)", null, (_, _) => Run("CIVDOOMJOIN " + id)));
            }
            if (_games.Count == 0) join.DropDownItems.Add(new ToolStripMenuItem("No games running right now") { Enabled = false });
            join.DropDownItems.Add(new ToolStripSeparator());
            join.DropDownItems.Add(new ToolStripMenuItem("Enter a game id...", null, (_, _) => Run("CIVDOOMJOIN")));
        }
        menu.Items.Add(join);
        menu.Items.Add(new ToolStripSeparator());
        Item(menu, "Server and player name...", "CIVDOOMNET");
        var alerts = new ToolStripMenuItem("Pop up when someone starts a game") { Checked = cfg.GameAlerts, CheckOnClick = true };
        alerts.Click += (_, _) => SetAlerts(alerts.Checked);
        menu.Items.Add(alerts);
        menu.Closed += (_, _) => menu.BeginInvoke(new Action(menu.Dispose));
        menu.Show(Control.MousePosition, ToolStripDropDownDirection.AboveLeft);
    }

    private static void Item(ContextMenuStrip menu, string text, string command) =>
        menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => Run(command)));

    /// <summary>Runs a command in the current drawing, as if typed.</summary>
    private static void Run(string command)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null)
        {
            MessageBox.Show("Open a drawing first.", "CivDOOM");
            return;
        }
        doc.SendStringToExecute("_" + command + " ", true, false, true);
    }

    private static NetConfig Config() => Commands.LoadNetConfig();

    internal static void SetAlerts(bool on)
    {
        NetConfig mine = Commands.LoadUserNetConfig();
        mine.GameAlerts = on;
        mine.SaveForUser();
    }

    // ------------------------------------------------------------------ server watch

    /// <summary>Background: asks the server which games are running (never blocks Civil 3D).</summary>
    private static void CheckServer()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            NetConfig cfg = Config();
            List<GameListing> games = new();
            if (cfg.HasServer)
            {
                try { games = NetLobby.ListGames(cfg.Server!); }
                catch (System.Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { games = new(); }
            }
            _ui?.BeginInvoke(new Action(() => OnGames(cfg, games)));
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private static void OnGames(NetConfig cfg, List<GameListing> games)
    {
        _games = games;
        if (_tray == null) return;
        _tray.ToolTipText = !cfg.HasServer ? "CivDOOM - click for the menu"
            : games.Count == 0 ? "CivDOOM - no co-op games running"
            : $"CivDOOM - {games.Count} co-op game{(games.Count == 1 ? "" : "s")} running";

        // Pop up once for each game someone else starts (not for games already running when Civil 3D opened).
        foreach (GameListing g in games)
        {
            if (!_announced.Add(g.Id) || _firstCheck || !cfg.GameAlerts) continue;
            if (string.Equals(g.HostName, cfg.Name, StringComparison.OrdinalIgnoreCase)) continue;
            Bubble($"{g.HostName} started a CivDOOM game", $"{g.LevelName} - {g.Players} playing", "Join game", () => Run("CIVDOOMJOIN " + g.Id));
        }
        _firstCheck = false;
    }

    private static void Bubble(string title, string text, string link, Action onLink)
    {
        if (_tray == null) return;
        try
        {
            var bubble = new TrayItemBubbleWindow { Title = title, Text = text, HyperText = link, IconType = IconType.Information };
            bubble.Closed += (_, e) =>
            {
                if (e.CloseReason == TrayItemBubbleWindowCloseReason.HyperlinkClicked) onLink();
            };
            _tray.ShowBubbleWindow(bubble);
        }
        catch (System.Exception) { /* no bubble is no big deal */ }
    }

    // ------------------------------------------------------------------ icon

    /// <summary>A 16x16 pixel skull with glowing red eyes.</summary>
    private static Icon MakeIcon()
    {
        string[] art =
        {
            "................",
            ".....wwwwww.....",
            "...wwwwwwwwww...",
            "..wwwwwwwwwwww..",
            "..wwwwwwwwwwww..",
            ".wwwrrwwwwrrwww.",
            ".wwrRRrwwrRRrww.",
            ".wwrRRrwwrRRrww.",
            ".wwwrrwwwwrrwww.",
            "..wwwwwkkwwwww..",
            "..wwwwkkkkwwww..",
            "...wwwwwwwwww...",
            "....wkwkwkwk....",
            "....wwwwwwww....",
            ".....kwkwkw.....",
            "................",
        };
        using var bmp = new Bitmap(16, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                Color c = art[y][x] switch
                {
                    'w' => Color.FromArgb(232, 226, 210),
                    'r' => Color.FromArgb(200, 20, 20),
                    'R' => Color.FromArgb(255, 90, 60),
                    'k' => Color.FromArgb(40, 30, 30),
                    _ => Color.Transparent,
                };
                bmp.SetPixel(x, y, c);
            }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
