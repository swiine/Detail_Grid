using System.Windows.Forms;
using CivDoom.Engine;
using CivDoom.Windows;

namespace CivDoom.Sandbox;

internal static class Program
{
    /// <summary>
    /// CivDoom.Sandbox.exe              play the demo level
    /// CivDoom.Sandbox.exe host         host the demo level for co-op (TTW Chat folder/server settings)
    /// CivDoom.Sandbox.exe join [id]    join a co-op game (no id = the newest game in the shared folder)
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        string root = AppContext.BaseDirectory;
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        try
        {
            if (mode == "host")
            {
                NetConfig cfg = NetConfig.Load();
                int seed = Environment.TickCount;
                using NetSession session = NetLobby.Host(cfg, new Game(BuiltInLevels.DetailGrid(), seed, GameContent.Load(root)), seed);
                try { NetLobby.PostInvite(cfg, session, null); } catch (Exception) { /* playing matters more than the invite */ }
                Application.Run(new GameForm(session, root));
                return;
            }
            if (mode == "join")
            {
                NetConfig cfg = NetConfig.Load();
                string? id = args.Length > 1 ? args[1] : NetLobby.ListGames(cfg).FirstOrDefault()?.Id;
                if (id == null)
                {
                    MessageBox.Show("Nobody is hosting a game in " + cfg.Describe() + ".", "CivDOOM");
                    return;
                }
                using NetSession session = NetLobby.Join(cfg, id, GameContent.Load(root));
                Application.Run(new GameForm(session, root));
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            MessageBox.Show(ex.Message, "CivDOOM multiplayer");
            return;
        }
        Application.Run(new GameForm(BuiltInLevels.DetailGrid, root));
    }
}
