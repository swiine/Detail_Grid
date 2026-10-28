namespace ETWalls.Standalone;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--selftest") return SelfTest.Run(args[1]);
        // Anything dropped straight into _Exports goes into its date folder (_Exports\yyyy-MM-dd).
        AppFolders.FileLooseExports();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
