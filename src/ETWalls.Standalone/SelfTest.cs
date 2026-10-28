using System.Text;
using ETWalls.Core.Design;
using ETWalls.Core.Standard;
using ETWalls.Export;
using ETWalls.Reports;

namespace ETWalls.Standalone;

/// <summary>
/// "ET Walls.exe --selftest &lt;folder&gt;": opens the real window, walks the wizard through every wall type and writes
/// the report, workbook and DXF for each into the folder, then closes. Exit code 0 = all fine. Used by the Windows CI
/// build, where the app can actually run.
/// </summary>
internal static class SelfTest
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new StringBuilder();
        int exit = 1;
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => { log.AppendLine("UI error: " + e.Exception); exit = 1; Application.Exit(); };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        using var form = new MainForm();
        form.Shown += async (_, _) =>
        {
            try
            {
                foreach (var type in Enum.GetValues<WallType>())
                    foreach (var footing in type == WallType.Cantilever ? new[] { Footing.Heel, Footing.Toe } : new[] { Footing.Heel })
                    {
                        var input = new WallInput { Type = type, Footing = footing, JobNumber = "SELFTEST", WallName = $"{type} {footing}", Project = "Self test", H = type == WallType.Cantilever ? 2.4 : 1.6 };
                        form.LoadDesign(input);
                        for (int step = 0; step < 4; step++) { form.Step(step); await Task.Delay(150); Application.DoEvents(); }
                        var run = form.State.Run ?? throw new InvalidOperationException($"{type}: no result ({form.State.Problem})");
                        string name = $"{type}-{footing}";
                        File.WriteAllText(Path.Combine(folder, name + ".html"), HtmlReport.Build(run, name + ".xlsx"));
                        ExcelReport.Save(run, Path.Combine(folder, name + ".xlsx"), name + ".html");
                        using (var fs = File.Create(Path.Combine(folder, name + ".dxf")))
                            DxfExport.Write(form.State.Drawing(true)!, CadStandard.Default, fs);
                        using (var bmp = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height)); bmp.Save(Path.Combine(folder, name + ".png")); }
                        log.AppendLine($"{name}: {(run.Result.Pass ? "pass" : "fail")}, {ReportRows.Money(run.Cost.PerMetre)}/m, {DesignRun.SizeText(run.Input)}");
                    }
                exit = 0;
            }
            catch (Exception ex) { log.AppendLine("Error: " + ex); exit = 1; }
            form.Close();
        };
        Application.Run(form);
        File.WriteAllText(Path.Combine(folder, "selftest.log"), log.ToString());
        Console.WriteLine(log.ToString());
        return exit;
    }
}
