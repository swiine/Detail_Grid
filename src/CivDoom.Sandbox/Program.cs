using System.Windows.Forms;
using CivDoom.Engine;
using CivDoom.Windows;

namespace CivDoom.Sandbox;

internal static class Program
{
    /// <summary>
    /// CivDoom.Sandbox.exe                         play the demo level
    /// CivDoom.Sandbox.exe host [server]           host the demo level for co-op (no server = run one here)
    /// CivDoom.Sandbox.exe join server [game]      join a co-op game (no game = the newest one)
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        string root = AppContext.BaseDirectory;
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        string name = Environment.UserName;
        try
        {
            if (mode == "host")
            {
                GameServer? own = null;
                string server = args.Length > 1 ? args[1] : "";
                if (server.Length == 0)
                {
                    own = new GameServer(GameServer.DefaultPort);
                    own.Start();
                    server = "localhost";
                }
                int seed = Environment.TickCount;
                using NetSession session = NetLobby.Host(server, name, new Game(BuiltInLevels.DetailGrid(), seed, GameContent.Load(root)), seed);
                if (own != null) session.Ended += own.Dispose;
                Application.Run(new GameForm(session, root));
                return;
            }
            if (mode == "join" && args.Length > 1)
            {
                string? id = args.Length > 2 ? args[2] : NetLobby.ListGames(args[1]).FirstOrDefault()?.Id;
                if (id == null)
                {
                    MessageBox.Show("Nobody is hosting a game on " + args[1] + ".", "CivDOOM");
                    return;
                }
                using NetSession session = NetLobby.Join(args[1], name, id, GameContent.Load(root));
                Application.Run(new GameForm(session, root));
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.Net.Sockets.SocketException)
        {
            MessageBox.Show(ex.Message, "CivDOOM multiplayer");
            return;
        }
        Application.Run(new GameForm(BuiltInLevels.DetailGrid, root));
    }
}
