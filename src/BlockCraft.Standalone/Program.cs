using System.Windows.Forms;
using BlockCraft.Core;
using BlockCraft.UI;

namespace BlockCraft.Standalone;

/// <summary>Runs the game outside Civil 3D, handy for trying it out or developing the engine.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int seed = args.Length > 0 && int.TryParse(args[0], out int s) ? s : Environment.TickCount;

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var game = new Game(TerrainGenerator.Procedural(seed: seed));
        Application.Run(new GameForm(game, $"BlockCraft (seed {seed})"));
    }
}
