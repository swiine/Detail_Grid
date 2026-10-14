namespace PavementBuildup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        PavementBuildup.Core.StandardStore.SearchFrom.Add(AppContext.BaseDirectory);
        AppFolders.FileLooseExports();
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new StandaloneForm());
    }
}
