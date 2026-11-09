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
