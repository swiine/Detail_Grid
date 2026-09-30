using System.Windows.Forms;
using CivDoom.Engine;
using CivDoom.Windows;

namespace CivDoom.Sandbox;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new GameForm(BuiltInLevels.DetailGrid, Path.Combine(AppContext.BaseDirectory, "monsters")));
    }
}
