namespace PavementBuildup.Core.Tests;

[Collection("usage log")]
public class UsageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ttw-usage-" + Guid.NewGuid().ToString("N"));

    public UsageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        UsageLog.Override = null;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Uses_are_logged_and_summed_into_hours_saved()
    {
        var log = Path.Combine(_dir, "usage.jsonl");
        UsageLog.Override = log;
        UsageLog.Record(UsageTools.DetailPlaced, "Civil 3D");
        UsageLog.Record(UsageTools.DetailPlaced, "Civil 3D");
        UsageLog.Record(UsageTools.BatchDetail, "Standalone", count: 6);
        UsageLog.Record(UsageTools.JointsDrawn, "Civil 3D", detail: "24 joints");
        UsageLog.Record(UsageTools.DxfSaved, "Standalone", count: 0);

        var events = UsageLog.Read(new[] { log, log });
        Assert.Equal(4, events.Count);
        var s = UsageReport.Summarise(events, TimeSaved.CreateDefault());
        Assert.Equal(9, s.Uses);
        Assert.Equal((2 * 20 + 6 * 15 + 45) / 60.0, s.Hours, 6);
        Assert.Equal(UsageTools.BatchDetail, s.ByTool[0].Name);
        Assert.Equal(1, s.People);
        Assert.Contains("hours saved", UsageReport.Html(s));
    }

    [Fact]
    public void Other_peoples_logs_combine_and_a_period_filters()
    {
        var a = Path.Combine(_dir, "a.jsonl");
        var b = Path.Combine(_dir, "b.jsonl");
        File.WriteAllLines(a, new[]
        {
            """{"Id":"1","Time":"2026-09-10T09:00:00","Tool":"Pavement detail placed","App":"Civil 3D","User":"sam","Count":1}""",
            "not json",
            """{"Id":"2","Time":"2026-10-02T09:00:00","Tool":"Pavement detail placed","App":"Civil 3D","User":"sam","Count":1}""",
        });
        File.WriteAllLines(b, new[] { """{"Id":"3","Time":"2026-10-05T09:00:00","Tool":"Design calculation report","App":"Standalone","User":"alex","Count":1}""" });
        var events = UsageLog.Read(new[] { a, b });
        Assert.Equal(3, events.Count);
        var october = UsageReport.Summarise(events, TimeSaved.CreateDefault(), new DateTime(2026, 10, 1), new DateTime(2026, 11, 1));
        Assert.Equal(2, october.Uses);
        Assert.Equal(2, october.People);
        Assert.Equal((20 + 60) / 60.0, october.Hours, 6);
        Assert.Single(october.ByMonth);
    }

    [Fact]
    public void Team_minutes_override_the_defaults()
    {
        var path = Path.Combine(_dir, TimeSaved.FileName);
        var t = TimeSaved.CreateDefault();
        t.Minutes[UsageTools.DetailPlaced] = 35;
        t.Save(path);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<TimeSaved>(File.ReadAllText(path))!;
        Assert.Equal(35, loaded.Minutes[UsageTools.DetailPlaced]);
    }

    [Fact]
    public void Usage_excel_has_a_summary_and_the_log()
    {
        var log = Path.Combine(_dir, "usage.jsonl");
        UsageLog.Override = log;
        UsageLog.Record(UsageTools.PdfSaved, "Standalone");
        var events = UsageLog.Read(new[] { log });
        var xlsx = Path.Combine(_dir, "usage.xlsx");
        PavementBuildup.Reports.UsageExcel.Save(UsageReport.Summarise(events, TimeSaved.CreateDefault()), events, xlsx);
        using var wb = new ClosedXML.Excel.XLWorkbook(xlsx);
        Assert.Equal(UsageTools.PdfSaved, wb.Worksheet("Log").Cell(2, 2).GetString());
        Assert.Equal(0.3, wb.Worksheet("Summary").Cell(4, 2).GetDouble(), 6);
    }
}

[Collection("usage log")]
public class ErrorReportTests
{
    [Fact]
    public void A_problem_report_zips_the_description_screenshot_and_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ttw-err-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var extra = Path.Combine(dir, "standalone.json");
            File.WriteAllText(extra, "{}");
            var zipPath = ErrorReport.Create(dir, "Standalone", "1.4.0", "It crashed when I saved", new byte[] { 1, 2, 3 }, new[] { extra, Path.Combine(dir, "missing.json") });
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.Name).ToList();
            Assert.Contains("about.txt", names);
            Assert.Contains("screenshot.png", names);
            Assert.Contains("standalone.json", names);
            using var reader = new StreamReader(zip.GetEntry("about.txt")!.Open());
            Assert.Contains("It crashed when I saved", reader.ReadToEnd());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Logging_can_be_switched_off_for_training()
    {
        var log = Path.Combine(Path.GetTempPath(), "ttw-off-" + Guid.NewGuid().ToString("N") + ".jsonl");
        UsageLog.Override = log;
        UsageLog.Enabled = false;
        try
        {
            UsageLog.Record(UsageTools.DxfSaved, "Standalone");
            Assert.False(File.Exists(log));
        }
        finally
        {
            UsageLog.Enabled = true;
            UsageLog.Override = null;
        }
    }
}
