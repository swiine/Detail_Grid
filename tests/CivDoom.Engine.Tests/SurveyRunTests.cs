using CoWork.Core;
using Xunit;

namespace CivDoom.Engine.Tests;

/// <summary>The Easter egg game's rules.</summary>
public class SurveyRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cw-egg-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static void Run(SurveyRun g, double seconds)
    {
        for (double t = 0; t < seconds && !g.Over; t += 1 / 60.0) g.Step(1 / 60.0);
    }

    [Fact]
    public void JumpingClearsAConeAndStandingStillDoesnt()
    {
        var still = new SurveyRun(1);
        still.Add(SurveyRun.Kind.Cone, 120);
        Run(still, 0.5);
        Assert.True(still.Over);

        var hopper = new SurveyRun(1);
        hopper.Add(SurveyRun.Kind.Cone, 120);
        // Jump when the cone is about a quarter of a second away.
        while (hopper.Obstacles[0].X > SurveyRun.PlayerX + 70) hopper.Step(1 / 60.0);
        hopper.Jump();
        Assert.True(hopper.Hopped);
        Run(hopper, 0.6);
        Assert.False(hopper.Over);
        Assert.True(hopper.OnGround);
    }

    [Fact]
    public void DuckUnderTheLowDroneButNotOverItWhenStanding()
    {
        var standing = new SurveyRun(1);
        standing.Add(SurveyRun.Kind.DroneLow, 120);
        Run(standing, 0.5);
        Assert.True(standing.Over);

        var ducking = new SurveyRun(1);
        ducking.Add(SurveyRun.Kind.DroneLow, 120);
        ducking.Duck(true);
        Run(ducking, 0.5);
        Assert.False(ducking.Over);

        // The high drone only bothers you if you jump into it.
        var under = new SurveyRun(1);
        under.Add(SurveyRun.Kind.DroneHigh, 120);
        Run(under, 0.5);
        Assert.False(under.Over);
    }

    [Fact]
    public void ItSpeedsUpScoresAndKeepsObstaclesComing()
    {
        var g = new SurveyRun(7);
        double first = g.Speed;
        int milestones = 0;
        // An autopilot that never hits anything: jump or duck as needed.
        for (int i = 0; i < 60 * 40 && !g.Over; i++)
        {
            SurveyRun.Obstacle? next = g.Obstacles.Where(o => o.X + o.W > SurveyRun.PlayerX).OrderBy(o => o.X).FirstOrDefault();
            double lead = g.Speed * 0.17;
            g.Duck(next is { Kind: SurveyRun.Kind.DroneLow } && next.X - SurveyRun.PlayerX < lead + 40);
            if (next is { Kind: not (SurveyRun.Kind.DroneLow or SurveyRun.Kind.DroneHigh) } && next.X - SurveyRun.PlayerX < lead && g.OnGround) g.Jump();
            g.Step(1 / 60.0);
            if (g.Milestone) milestones++;
        }
        Assert.True(g.Score > 400, $"score {g.Score}, over={g.Over}");
        Assert.True(g.Speed > first);
        Assert.True(g.Speed <= SurveyRun.MaxSpeed);
        Assert.Equal(g.Score / 100, milestones);
    }

    [Fact]
    public void SpritesMatchTheirHitboxes()
    {
        int s = SurveyRun.Art.Scale;
        Assert.Equal(SurveyRun.StandW, SurveyRun.Art.RunA[0].Length * s);
        Assert.Equal(SurveyRun.StandH, SurveyRun.Art.RunA.Length * s);
        Assert.Equal(SurveyRun.DuckH, SurveyRun.Art.Duck.Length * s);
        Assert.Equal(SurveyRun.Make(SurveyRun.Kind.Cone, 0).H, SurveyRun.Art.Cone.Length * s);
        Assert.Equal(SurveyRun.Make(SurveyRun.Kind.Barrier, 0).W, SurveyRun.Art.Barrier[0].Length * s);
        Assert.Equal(SurveyRun.Make(SurveyRun.Kind.DroneLow, 0).W, SurveyRun.Art.DroneA[0].Length * s);
        Assert.All(new[] { SurveyRun.Art.RunA, SurveyRun.Art.RunB, SurveyRun.Art.Bonked }, a => Assert.Equal(12, a.Length));
    }

    [Fact]
    public void OfficeHighScoresKeepEachPersonsBest()
    {
        Assert.True(SurveyRun.SaveBest(_root, "coreyy", "Corey", 512));
        Assert.False(SurveyRun.SaveBest(_root, "coreyy", "Corey", 300)); // not a new best
        Assert.True(SurveyRun.SaveBest(_root, "danielv", "Daniel", 700));
        Assert.True(SurveyRun.SaveBest(_root, "coreyy", "Corey", 900));
        File.WriteAllText(Path.Combine(SurveyRun.ScoresFolder(_root), "cheat.txt"), "999999999|Cheater");
        List<SurveyRun.Best> top = SurveyRun.TopScores(_root);
        Assert.Equal(new[] { "Corey", "Daniel" }, top.Select(b => b.Name));
        Assert.Equal(900, top[0].Score);
    }
}
