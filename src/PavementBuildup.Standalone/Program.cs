namespace PavementBuildup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Default standard: _support\_Standards\TTW.json next to the Standalone Tool folder.
        PavementBuildup.Core.StandardStore.SearchFrom.Add(AppContext.BaseDirectory);
        // Anything dropped straight into _Exports goes into its date folder (_Exports\yyyy-MM-dd).
        AppFolders.FileLooseExports();
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new StandaloneForm());
    }
}
