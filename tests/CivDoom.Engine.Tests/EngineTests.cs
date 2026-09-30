using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class EngineTests
{
    private static DrawingGeometry Room(double w, double h, double ox = 1000, double oy = 5000)
    {
        var g = new DrawingGeometry();
        Vec2[] c = { new(ox, oy), new(ox + w, oy), new(ox + w, oy + h), new(ox, oy + h) };
        for (int i = 0; i < 4; i++) g.Segments.Add(new DrawingSegment(c[i], c[(i + 1) % 4], 0xFFFFFF));
        return g;
    }

    [Fact]
    public void ArtParses()
    {
        Assert.NotNull(Art.FireballSprite);
    }

    [Fact]
    public void BuiltInMapIsRectangularAndEnclosed()
    {
        int width = BuiltInLevels.DetailGridMap[0].Length;
        Assert.All(BuiltInLevels.DetailGridMap, row => Assert.Equal(width, row.Length));

        Level level = BuiltInLevels.DetailGrid();
        var reach = new Reachability(level.Index, level.PlayerStart, Game.PlayerRadius);
        Assert.False(reach.IsReachable(new Vec2(-0.5, -0.5)), "player can escape the map");
        Assert.All(level.Enemies, e => Assert.True(reach.IsReachable(e.Position), $"enemy at {e.Position} unreachable"));
        Assert.All(level.Pickups, p => Assert.True(reach.IsReachable(p.Position), $"pickup at {p.Position} unreachable"));
        Assert.NotEmpty(level.Enemies);
    }

    [Fact]
    public void CastRayMatchesBruteForce()
    {
        Level level = BuiltInLevels.DetailGrid();
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var origin = new Vec2(rng.NextDouble() * 26, rng.NextDouble() * 20);
            Vec2 dir = Vec2.FromAngle(rng.NextDouble() * Math.PI * 2);
            double best = double.PositiveInfinity;
            foreach (Wall w in level.Walls)
                if (SpatialIndex.Intersect(origin, dir, w, out double t, out _) && t < best && t <= 64) best = t;

            double? got = level.Index.CastRay(origin, dir, 64)?.Distance;
            if (double.IsPositiveInfinity(best)) Assert.Null(got);
            else Assert.Equal(best, got!.Value, 9);
        }
    }

    [Fact]
    public void CastRayFromOutsideTheGridFindsWalls()
    {
        Level level = AsciiMap.Parse("t", new[] { "###", "#P#", "###" });
        RayHit? hit = level.Index.CastRay(new Vec2(-10, 1.5), new Vec2(1, 0), 100);
        Assert.NotNull(hit);
        Assert.Equal(10, hit!.Value.Distance, 6);
    }

    [Fact]
    public void PlayerCannotWalkThroughWalls()
    {
        Level level = LevelBuilder.FromDrawing(Room(40, 40), wallHeight: 10, seed: 1);
        var game = new Game(level);
        // Try to sprint through the east wall for a long time, in large steps.
        game.Player.Angle = 0;
        var input = new GameInput { Forward = true, Run = true };
        for (int i = 0; i < 200; i++) game.Update(0.1, input);
        Assert.True(game.Player.Position.X < 2 - Game.PlayerRadius + 1e-3, $"x = {game.Player.Position.X}");
        Assert.True(game.Player.Position.X > 1.5);
    }

    [Fact]
    public void ThinLineCannotBeTunnelled()
    {
        var g = new DrawingGeometry { PlayerStart = new Vec2(0, 0) };
        g.Segments.Add(new DrawingSegment(new Vec2(1, -50), new Vec2(1, 50), 0xFFFFFF));
        Level level = LevelBuilder.FromDrawing(g, 1, 1);
        var game = new Game(level);
        Vec2 end = game.Move(game.Player.Position, new Vec2(5, 0), Game.PlayerRadius);
        Assert.True(end.X < 1);
    }

    [Fact]
    public void FromDrawingScalesAndPlacesEverythingInside()
    {
        DrawingGeometry g = Room(200, 120);
        Level level = LevelBuilder.FromDrawing(g, wallHeight: 10, seed: 3);
        Assert.Equal(4, level.Walls.Count);
        Assert.Equal(20, level.Walls[0].Length, 6);
        Assert.InRange(level.Enemies.Count, 3, 30);
        var reach = new Reachability(level.Index, level.PlayerStart, Game.PlayerRadius);
        Assert.All(level.Enemies, e => Assert.True(reach.IsReachable(e.Position)));
        Assert.All(level.Enemies, e => Assert.InRange(e.Position.X, -10, 10));
        Assert.All(level.Pickups, p => Assert.InRange(p.Position.Y, -6, 6));
    }

    [Fact]
    public void ExplicitEnemyPointsAreUsed()
    {
        DrawingGeometry g = Room(100, 100, 0, 0);
        g.PlayerStart = new Vec2(10, 10);
        g.EnemyPoints.Add(new Vec2(90, 90));
        Level level = LevelBuilder.FromDrawing(g, 10, 1);
        EnemySpawn e = Assert.Single(level.Enemies);
        Assert.Equal(8, e.Position.X, 6);
        Assert.Equal(Vec2.Zero, level.PlayerStart);
    }

    [Fact]
    public void StartInsideAWallIsNudgedClear()
    {
        var g = new DrawingGeometry { PlayerStart = new Vec2(0, 0) };
        g.Segments.Add(new DrawingSegment(new Vec2(-5, 0), new Vec2(5, 0), 0xFFFFFF));
        Level level = LevelBuilder.FromDrawing(g, 1, 1);
        Assert.True(Reachability.HasClearance(level.Index, level.PlayerStart, Game.PlayerRadius));
    }

    [Fact]
    public void ShootingKillsEnemyInFront()
    {
        Level level = AsciiMap.Parse("t", new[]
        {
            "##########",
            "#P....E..#",
            "##########",
        });
        var game = new Game(level);
        var fire = new GameInput { Fire = true };
        for (int i = 0; i < 100 && game.State == GameState.Playing; i++) game.Update(0.05, fire);
        Assert.Equal(GameState.Won, game.State);
        Assert.True(game.Player.AmmoOf("bullets") < 50);
    }

    [Fact]
    public void EnemiesFightBack()
    {
        Level level = AsciiMap.Parse("t", new[]
        {
            "##########",
            "#P....E..#",
            "##########",
        });
        var game = new Game(level);
        var idle = new GameInput();
        for (int i = 0; i < 20 * 60 && game.State == GameState.Playing; i++) game.Update(0.05, idle);
        Assert.Equal(GameState.Dead, game.State);
    }

    [Fact]
    public void PickupsAreCollected()
    {
        Level level = AsciiMap.Parse("t", new[] { "#####", "#PA.#", "#####" });
        var game = new Game(level);
        var fwd = new GameInput { Forward = true };
        for (int i = 0; i < 30; i++) game.Update(0.05, fwd);
        Assert.Equal(70, game.Player.AmmoOf("bullets"));
        Assert.All(game.Pickups, p => Assert.True(p.Taken));
    }

    [Fact]
    public void RendererDrawsAFrame()
    {
        var game = new Game(BuiltInLevels.DetailGrid());
        var renderer = new Renderer(320, 200);
        game.Player.MuzzleFlashTime = 0.05;
        renderer.Render(game);
        Assert.True(renderer.Pixels.Distinct().Count() > 20);
        Assert.All(renderer.Pixels, p => Assert.Equal(0xFF, (p >>> 24)));
        Screenshots.Save(renderer, "builtin.ppm");
    }

    [Fact]
    public void RendererDrawsSprites()
    {
        Level level = AsciiMap.Parse("t", new[]
        {
            "GGGGGGGGGGGG",
            "G......E...G",
            "GP..A...X..R",
            "G....H.E...R",
            "GGGGGGGGGGGR",
        });
        var game = new Game(level);
        var renderer = new Renderer(320, 200);
        renderer.Render(game);
        Screenshots.Save(renderer, "sprites.ppm");
        int[] withEnemies = (int[])renderer.Pixels.Clone();

        game.Enemies.Clear();
        renderer.Render(game);
        int changed = withEnemies.Zip(renderer.Pixels).Count(t => t.First != t.Second);
        Assert.True(changed > 500, $"only {changed} pixels belong to enemies");
    }
}

/// <summary>Set CIVDOOM_SCREENSHOTS to a folder to dump frames as PPM images for eyeballing.</summary>
internal static class Screenshots
{
    public static void Save(Renderer r, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("CIVDOOM_SCREENSHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var fs = File.Create(Path.Combine(dir, name));
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{r.Width} {r.Height}\n255\n");
        fs.Write(header);
        foreach (int p in r.Pixels)
        {
            fs.WriteByte((byte)(p >> 16));
            fs.WriteByte((byte)(p >> 8));
            fs.WriteByte((byte)p);
        }
    }
}
