using PavementBuildup.Core;

namespace PavementBuildup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        StandardStore.SearchFrom.Add(AppContext.BaseDirectory);
        if (args.Any(a => a.Trim().TrimStart('-', '/').Equals("training", StringComparison.OrdinalIgnoreCase)))
        {
            StandaloneTraining.On = true;
            UsageLog.Enabled = false;
            AppFolders.TrainingRoot = Path.Combine(Path.GetTempPath(), "TTW Pavement Training");
        }
        else AppFolders.FileLooseExports();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crashed(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) ErrorLog.Write(ex, "Standalone (fatal)"); };
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new StandaloneForm());
    }

    private static void Crashed(Exception ex)
    {
        ErrorLog.Write(ex, "Standalone");
        try
        {
            using var form = new ProblemForm("TTW Pavement standalone", Screenshot(), error: ex.Message);
            form.ShowDialog();
        }
        catch (Exception inner) { ErrorLog.Write(inner, "Standalone (problem form)"); }
    }

    public static byte[]? Screenshot()
    {
        var form = Form.ActiveForm ?? Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (form is null || form.Width <= 0 || form.Height <= 0) return null;
        try
        {
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
}
