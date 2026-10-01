using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class ObjectiveTests
{
    // A corridor: player, two bosses, a gate, then the finish line.
    private static readonly string[] Map =
    {
        "##############",
        "#P..Z...Z.DF.#",
        "##############",
    };

    private static void Kill(Game g, Enemy e)
    {
        e.Health = 1;
        g.Player.Position = e.Position - new Vec2(0.6, 0);
        g.Player.Angle = 0;
        var fire = new GameInput { Fire = true };
        for (int i = 0; i < 40 && e.IsAlive; i++) g.Update(0.05, fire);
        Assert.False(e.IsAlive);
    }

    [Fact]
    public void GateBlocksUntilAllBossesAreDead()
    {
        var game = new Game(AsciiMap.Parse("t", Map), 1);
        List<Enemy> bosses = game.Enemies.Where(e => e.Design.Boss).ToList();
        Assert.Equal(2, bosses.Count);
        Assert.NotEqual(bosses[0].Design, bosses[1].Design); // different fights
        Assert.False(game.ExitOpen);
        Assert.Contains("all bosses", game.Objective);

        // The gate is solid: you can't walk through it.
        Vec2 end = game.Move(new Vec2(9.5, 1.5), new Vec2(3, 0), Game.PlayerRadius);
        Assert.True(end.X < 10);

        Kill(game, bosses[0]);
        Assert.False(game.ExitOpen);
        Kill(game, bosses[1]);
        Assert.True(game.ExitOpen);
        Assert.Equal("Reach the finish line!", game.Objective);
        Assert.All(game.Level.Gates, g => Assert.True(g.IsOpen));
        end = game.Move(new Vec2(9.5, 1.5), new Vec2(3, 0), Game.PlayerRadius);
        Assert.True(end.X > 11);
    }

    [Fact]
    public void FinalBossRuleOnlyNeedsTheBossNearestTheFinish()
    {
        Level parsed = AsciiMap.Parse("t", Map);
        var level = new Level("t", parsed.Walls, parsed.PlayerStart, 0, parsed.Enemies, parsed.Pickups)
        {
            Exit = parsed.Exit,
            GateRule = GateRule.FinalBoss,
        };
        var game = new Game(level, 1);
        Assert.Same(game.Enemies.OrderBy(e => Vec2.Distance(e.Position, level.Exit!.Value)).First(), game.FinalBoss);
        Kill(game, game.FinalBoss!);
        Assert.True(game.ExitOpen);
        Assert.Contains(game.Enemies, e => e.Design.Boss && e.IsAlive);
    }

    [Fact]
    public void ReachingTheOpenFinishLineWins()
    {
        var game = new Game(AsciiMap.Parse("t", Map), 1);
        foreach (Enemy b in game.Enemies.Where(e => e.Design.Boss).ToList()) Kill(game, b);
        game.Player.Position = new Vec2(9.5, 1.5);
        game.Player.Angle = 0;
        var walk = new GameInput { Forward = true };
        for (int i = 0; i < 60 && game.State == GameState.Playing; i++) game.Update(0.05, walk);
        Assert.Equal(GameState.Won, game.State);
    }

    [Fact]
    public void KillingEverythingIsNotEnoughOnAFinishLineLevel()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "#######", "#P.E.F#", "#######" }), 1);
        Kill(game, game.Enemies[0]);
        Assert.Equal(GameState.Playing, game.State);
        Assert.True(game.ExitOpen); // no bosses: the finish line is open from the start
    }

    [Theory]
    [InlineData(LevelSize.Small, 1, 2)]
    [InlineData(LevelSize.Medium, 2, 3)]
    [InlineData(LevelSize.Large, 4, 5)]
    public void BossCountsMatchTheLevelSize(LevelSize size, int min, int max)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            GeneratedLevel gen = LevelGenerator.Generate(seed, size);
            Assert.InRange(gen.Bosses.Count, min, max);
            Assert.NotNull(gen.Exit);
            Assert.NotEmpty(gen.Gates);

            Level level = LevelBuilder.FromDrawing(gen.ToGeometry(Vec2.Zero, 10), 10, seed);
            var game = new Game(level, seed);
            List<Enemy> bosses = game.Enemies.Where(e => e.Design.Boss).ToList();
            Assert.Equal(gen.Bosses.Count, bosses.Count);
            Assert.Equal(bosses.Count, bosses.Select(b => b.Design.Id).Distinct().Count());

            // The finish is sealed until the gate opens.
            var closed = new Reachability(level.Index, level.PlayerStart, Game.PlayerRadius);
            Assert.False(closed.IsReachable(level.Exit!.Value), $"seed {seed}: finish reachable with the gate shut");
            foreach (Wall g in level.Gates) g.IsOpen = true;
            var open = new Reachability(level.Index, level.PlayerStart, Game.PlayerRadius);
            Assert.True(open.IsReachable(level.Exit!.Value), $"seed {seed}: finish unreachable with the gate open");
        }
    }

    [Fact]
    public void GeneratedWallsHaveConstantThickness()
    {
        GeneratedLevel gen = LevelGenerator.Generate(5, LevelSize.Medium, wallThickness: 0.25);
        Assert.Equal(gen.Walls.Count, gen.OuterWalls.Count);
        for (int i = 0; i < gen.Walls.Count; i++)
            for (int j = 0; j < gen.Walls[i].Count; j++)
            {
                Vec2 d = gen.OuterWalls[i][j] - gen.Walls[i][j];
                Assert.Equal(0.25, Math.Abs(d.X), 9);
                Assert.Equal(0.25, Math.Abs(d.Y), 9);
            }

        GeneratedLevel thin = LevelGenerator.Generate(5, LevelSize.Medium, wallThickness: 0);
        Assert.Empty(thin.OuterWalls);
    }

    [Fact]
    public void DemoLevelHasABossGuardingTheFinish()
    {
        Level level = BuiltInLevels.DetailGrid();
        Assert.NotNull(level.Exit);
        Assert.NotEmpty(level.Gates);
        var game = new Game(level, 1);
        Assert.Contains(game.Enemies, e => e.Design.Boss);
        Assert.False(game.ExitOpen);
    }
}
