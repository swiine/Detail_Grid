using CoWork.Core;
using Xunit;

namespace CivDoom.Engine.Tests;

/// <summary>Reactions, read receipts and tidying old "last seen" notes.</summary>
public class ChatExtrasTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cw-extras-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    [Fact]
    public void AnyoneCanReactAndReactingAgainTakesItBack()
    {
        var corey = new ChatStore(_folder, "coreyy", "Corey", "PC1");
        var daniel = new ChatStore(_folder, "danielv", "Daniel", "PC2");
        ChatMessage m = corey.Post("lobby", "Levels are in");
        daniel.React("lobby", m.Id, "👍");
        corey.React("lobby", m.Id, "👍");
        daniel.React("lobby", m.Id, "✅");
        daniel.React("lobby", m.Id, "✅");   // takes it back
        daniel.React("lobby", m.Id, "🤖");   // not one of the choices: ignored

        ChatMessage seen = corey.History("lobby").Single();
        Assert.Equal("Levels are in", seen.Text);
        Assert.Equal(new[] { "👍" }, seen.Reactions!.Keys);
        Assert.Equal(new[] { "danielv", "coreyy" }, seen.Reactions["👍"]);
    }

    [Fact]
    public void ReadMarksOnlyMoveForward()
    {
        var corey = new ChatStore(_folder, "coreyy", "Corey", "PC1");
        var t = new DateTimeOffset(2026, 11, 9, 10, 42, 0, TimeSpan.Zero);
        corey.MarkRead("lobby", t);
        corey.MarkRead("lobby", t.AddMinutes(-5));
        Assert.Equal(t, corey.ReadMarks("lobby")["coreyy"]);
        corey.MarkRead("lobby", t.AddMinutes(3));
        Assert.Equal(t.AddMinutes(3), corey.ReadMarks("lobby")["coreyy"]);
    }

    [Fact]
    public void PeopleNotSeenForNinetyDaysAreTidiedAway()
    {
        var corey = new ChatStore(_folder, "coreyy", "Corey", "PC1");
        var old = new ChatStore(_folder, "oldtimer", "Old Timer", "PC9");
        corey.SayOnline(new[] { "lobby" });
        old.SayOnline(new[] { "lobby" });
        string file = Path.Combine(_folder, "online", "oldtimer.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace(DateTimeOffset.UtcNow.Year.ToString(), (DateTimeOffset.UtcNow.Year - 1).ToString()));
        Assert.Equal(1, corey.TidyOnline());
        Assert.Equal(new[] { "coreyy" }, corey.LastSeen().Select(p => p.User));
    }
}

public class HealthTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cw-health-" + Guid.NewGuid().ToString("N"));

    public HealthTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    [Fact]
    public void AGoodFolderAndClockPass()
    {
        var (folder, clock) = Health.CheckFolder(_folder);
        Assert.Equal(Health.Level.Ok, folder.Level);
        Assert.Equal(Health.Level.Ok, clock!.Level);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "logs", "check"))); // tidies up after itself
    }

    [Fact]
    public void AClockThatIsOutIsCaught()
    {
        var (_, clock) = Health.CheckFolder(_folder, () => DateTime.UtcNow.AddSeconds(30)); // this PC runs 30 s fast
        Assert.Equal(Health.Level.Fail, clock!.Level);
        Assert.Contains("ahead", clock.Detail);
    }

    [Fact]
    public void AMissingFolderFails()
    {
        Assert.Equal(Health.Level.Fail, Health.CheckFolder(Path.Combine(_folder, "nope")).Folder.Level);
        Assert.Equal(Health.Level.Fail, Health.CheckFolder("").Folder.Level);
        Assert.StartsWith("✘ Shared folder:", Health.Format(new[] { Health.CheckFolder("").Folder }));
    }
}

public class LiveBatchTests
{
    [Fact]
    public void BatchesCarryStylesLayoutsPipesAndSampleLines()
    {
        var empty = new LiveBatch(1, "p", DateTimeOffset.UtcNow, new(), new(), new());
        Assert.True(empty.IsEmpty);
        Assert.False((empty with { Records = new() { LiveBatch.Record("dim", "Civil - 2.5") } }).IsEmpty);
        Assert.False((empty with { Pipes = new() { new LivePipePart("Storm", "Pipe - (1)", true, 0, 0, 10, 20, 0, 9.8) } }).IsEmpty);
        Assert.False((empty with { SampleLines = new() { new LiveSampleLine("SLG-1", "0+20.00", 20, null) } }).IsEmpty);
        Assert.Equal(("block", "North Arrow:2"), LiveBatch.SplitRecord("block:North Arrow:2")); // names may contain ':'

        var full = empty with
        {
            Changed = new() { "g:1" }, Layouts = new() { ["g:1"] = "Sheet 01" },
            Records = new() { "text:Notes" },
            Pipes = new() { new LivePipePart("Storm", "MH-01", false, 5, 6, 10, 0, 0, 0, 10.2, 7.9) },
            SampleLines = new() { new LiveSampleLine("SLG-1", "0+20.00", 20, new() { new[] { 1.0, 2, 3 }, new[] { 4.0, 5, 6 } }) },
        };
        LiveBatch back = LiveFolder.BatchFromJson(LiveFolder.BatchJson(full))!;
        Assert.Equal("Sheet 01", back.Layouts!["g:1"]);
        Assert.Equal("text:Notes", back.Records!.Single());
        Assert.Equal(10.2, back.Pipes!.Single().Rim);
        Assert.Equal(new[] { 4.0, 5, 6 }, back.SampleLines!.Single().Vertices![1]);
    }
}

public class TimeLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cw-time-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    [Fact]
    public void MinutesAddUpPerJobPerDayAcrossMonths()
    {
        var log = new TimeLog(_folder);
        var d1 = new DateOnly(2026, 10, 31);
        var d2 = new DateOnly(2026, 11, 2);
        for (int i = 0; i < 95; i++) log.Add(d1, "241796");
        log.Add(d1, "250012", 20);
        log.Add(d2, "241796", 7);
        log.Add(d2, "", 30); // no job: not counted

        List<TimeLog.Entry> all = new TimeLog(_folder).Between(d1, d2); // read back from disk
        Assert.Equal(3, all.Count);
        Assert.Equal(95, all.Single(e => e.Day == d1 && e.Job == "241796").Minutes);
        Assert.Equal("241796 1.75 h, 250012 0.25 h", TimeLog.Summary(all)); // 102 min -> 1.75 h; 20 min -> 0.25 h
        Assert.Equal(1.5, TimeLog.Hours(95));
        Assert.Equal(0.25, TimeLog.Hours(3)); // a few minutes still shows
        string csv = TimeLog.Csv(all);
        Assert.StartsWith("Date,Job,Hours,Minutes\n2026-10-31,241796,1.50,95\n", csv);
        Assert.Contains("2026-11-02,241796,0.00", TimeLog.Csv(new[] { new TimeLog.Entry(d2, "241796", 0) }));
    }
}

public class ProbeTests
{
    [Fact]
    public void TheConnectionTestSaysWhatsWrong()
    {
        using var server = new CoWorkServer(0, address: System.Net.IPAddress.Loopback);
        server.Start();
        Assert.Equal(Health.Level.Ok, Health.Probe("Session server", $"127.0.0.1:{server.Port}").Level);

        int closed = server.Port;
        server.Dispose();
        Health.Result refused = Health.Probe("Session server", $"127.0.0.1:{closed}");
        Assert.Equal(Health.Level.Fail, refused.Level);
        Assert.Contains("nothing is listening", refused.Detail);
    }

    [Fact]
    public void ListeningOnAnyAcceptsIPv4AndIPv6()
    {
        using var server = new CoWorkServer(0);
        server.Start();
        Assert.Equal(Health.Level.Ok, Health.Probe("v4", $"127.0.0.1:{server.Port}").Level);
        if (System.Net.Sockets.Socket.OSSupportsIPv6)
            Assert.Equal(Health.Level.Ok, Health.Probe("v6", $"[::1]:{server.Port}").Level);
    }
}

public class JobAlertTests
{
    private static OnlinePerson Person(string user, params string[] open) =>
        new(user, user, "PC", Array.Empty<string>(), DateTimeOffset.UtcNow, OpenDrawings: open);

    private const string Jobs = @"\\ttw\dfs\ttwcad\jobs\25\";

    [Fact]
    public void OnlyPeopleInTheSameJobAreAlerted()
    {
        string mine = Jobs + @"252525\civil\sheets\0\0\C-ROAD.dwg";
        var matt = Person("matt", Jobs + @"251920\civil\sheets\9\C-GRADING.dwg");          // another job: nothing
        var sam = Person("sam", Jobs + @"252525\civil\sheets\1\C-DRAINAGE.dwg");           // same job, other drawing
        var jo = Person("jo", (Jobs + @"252525\civil\sheets\0\0\c-road.dwg").Replace('\\', '/')); // the same drawing
        var none = Person("lee", @"C:\Temp\sketch.dwg");                                    // not in a job

        List<JobAlerts.Alert> alerts = JobAlerts.Find(new[] { mine }, new[] { matt, sam, jo, none, Person("coreyy", mine) }, "coreyy");
        Assert.Equal(new[] { "sam", "jo" }, alerts.Select(a => a.Person.User));
        Assert.False(alerts[0].SameDrawing);
        Assert.True(alerts[1].SameDrawing);
        Assert.Equal("252525", alerts[0].Job);

        // Matt's side: he's in 251920, you're in 252525 - he isn't told either.
        Assert.Empty(JobAlerts.Find(new[] { Jobs + @"251920\civil\sheets\9\C-GRADING.dwg" }, new[] { Person("coreyy", mine) }, "matt"));
        // Nothing of yours in a job folder: no alerts at all.
        Assert.Empty(JobAlerts.Find(new[] { @"C:\Temp\mine.dwg" }, new[] { sam }, "coreyy"));
    }
}
