using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class PlatformTests
{
    private static Game Play(params string[] map)
    {
        var game = new Game(AsciiMap.Parse("t", map), 1);
        game.Enemies.Clear();
        return game;
    }

    private static void Run(Game g, GameInput input, double seconds, bool jumpFirst = false)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            input.Jump = jumpFirst && i < 2;
            g.Update(1 / 60.0, input);
        }
    }

    [Fact]
    public void DigitsBecomeMergedPlatforms()
    {
        Level level = AsciiMap.Parse("t", new[]
        {
            "#######",
            "#P.33.#",
            "#..33.#",
            "#.1...#",
            "#######",
        });
        Assert.Equal(2, level.Terrain.Platforms.Count);
        Platform block = level.Terrain.Platforms.Single(p => p.Height > 0.4);
        Assert.Equal(0.45, block.Height, 6);
        Assert.Equal(new Vec2(3, 2), block.Min); // 2x2 cells, merged into one rectangle
        Assert.Equal(new Vec2(5, 4), block.Max);
        Assert.Equal(0.45, level.Terrain.FloorAt(new Vec2(4, 3)), 6);
        Assert.Equal(0, level.Terrain.FloorAt(new Vec2(1.5, 3.5)));
    }

    [Fact]
    public void LedgeBlocksUntilYouJump()
    {
        Game g = Play("##########", "#P...333.#", "##########");
        var forward = new GameInput { Forward = true };
        Run(g, forward, 2);
        Assert.True(g.Player.Position.X < 5, "walked up a ledge without jumping");
        Assert.Equal(0, g.Player.Z);

        Run(g, forward, 0.6, jumpFirst: true);
        Assert.True(g.Player.Position.X > 5.2, "the jump didn't get onto the ledge");
        Assert.Equal(0.45, g.Player.Z, 6);
        Assert.True(g.Player.OnGround);
    }

    [Fact]
    public void StepsAreWalkedUpAndEdgesWalkedOff()
    {
        Game g = Play("##########", "#P..1....#", "##########");
        var forward = new GameInput { Forward = true };
        for (int i = 0; i < 120 && g.Player.Position.X < 4.5; i++) g.Update(1 / 60.0, forward);
        Assert.Equal(AsciiMap.HeightPerDigit, g.Player.Z, 6); // stepped straight up, no jump

        Run(g, forward, 1);
        Assert.True(g.Player.Position.X > 6);
        Assert.Equal(0, g.Player.Z); // walked off and landed
    }

    [Fact]
    public void TooTallToJump()
    {
        Assert.True(Terrain.JumpHeight is > 0.5 and < 0.75);
        Game g = Play("##########", "#P...555.#", "##########"); // 0.75: higher than a jump
        Run(g, new GameInput { Forward = true }, 1, jumpFirst: true);
        Assert.True(g.Player.Position.X < 5);
    }

    [Fact]
    public void PickupsUpOnALedgeNeedTheClimb()
    {
        Level l = AsciiMap.Parse("t", new[] { "##########", "#P..333..#", "##########" });
        var level = new Level("t", l.Walls, l.PlayerStart, 0, Array.Empty<EnemySpawn>(),
                              new[] { new PickupSpawn(new Vec2(5.5, 1.5), PickupKind.Health) }) { Terrain = l.Terrain };
        var g = new Game(level, 1);
        g.Player.Health = 50;
        // Standing right underneath (pushed up against the ledge) doesn't get it.
        g.Player.Position = new Vec2(4.2, 1.5);
        g.Update(0.016, new GameInput());
        Assert.False(g.Pickups[0].Taken);

        Run(g, new GameInput { Forward = true }, 0.5, jumpFirst: true);
        Assert.True(g.Pickups[0].Taken);
    }

    [Fact]
    public void WalkersCantPathUpALedgeButCanDropOffIt()
    {
        Level level = AsciiMap.Parse("t", new[]
        {
            "############",
            "#..........#",
            "#.....333..#",
            "#.....333..#",
            "#..........#",
            "############",
        });
        var nav = new NavGrid(level.Index, level.Terrain);
        Vec2 top = new(7.5, 3), ground = new(2, 3);

        nav.SetTarget(top);
        Assert.Null(nav.DistanceFrom(ground)); // can't climb up to you

        nav.SetTarget(ground);
        Assert.NotNull(nav.DistanceFrom(top)); // can jump down after you
    }

    private static MonsterSet Turret() => new(new[]
    {
        MonsterFile.Parse("proxy", "name = Turret\nhealth = 50\nspeed = 0\ndodge = 0\nstrafe = 0\nattack delay = 0.5\n[colors]\nx = 808080\n[idle]\nxx\nxx", new List<string>()),
    });

    [Fact]
    public void FireballsAimUpAtYouOnALedge()
    {
        var level = AsciiMap.Parse("t", new[] { "############", "#E.....333.#", "############" });
        var g = new Game(level, 1, Turret());
        g.Player.Position = new Vec2(8.5, 1.5);
        g.Update(0.016, new GameInput());
        Assert.Equal(0.45, g.Player.Z, 6);
        for (int i = 0; i < 400 && g.Player.Health == Player.MaxHealth; i++) g.Update(1 / 60.0, new GameInput());
        Assert.True(g.Player.Health < Player.MaxHealth, "fireballs flew under the player");
    }

    [Fact]
    public void ShotsHitTheSideOfATallPlatform()
    {
        // A monster behind a block you can't shoot over: rockets fired along the ground hit the block.
        var level = AsciiMap.Parse("t", new[] { "############", "#P...555.E.#", "############" });
        var g = new Game(level, 1, WeaponTests.Dummies());
        Enemy e = g.Enemies[0];
        g.Projectiles.Add(new Projectile { FromPlayer = true, Position = new Vec2(2, 1.5), Velocity = new Vec2(8, 0), Z = 0.36, Damage = 1000 });
        for (int i = 0; i < 90; i++) g.Update(1 / 60.0, new GameInput());
        Assert.True(e.IsAlive);
        Assert.DoesNotContain(g.Projectiles, p => p.FromPlayer); // it blew up against the block
    }

    [Fact]
    public void TriangulationCoversTheOutline()
    {
        var l = new Platform(new[] { new Vec2(0, 0), new Vec2(3, 0), new Vec2(3, 1), new Vec2(1, 1), new Vec2(1, 3), new Vec2(0, 3) }, 0.3);
        var tris = l.Triangulate();
        Assert.Equal(4, tris.Count);
        double area = tris.Sum(t => Math.Abs(Vec2.Cross(t.B - t.A, t.C - t.A)) / 2);
        Assert.Equal(5, area, 6);
        Assert.True(l.Contains(new Vec2(0.5, 2.5)));
        Assert.False(l.Contains(new Vec2(2, 2)));
        Assert.Equal(0.5, l.DistanceToEdge(new Vec2(0.5, 2)), 6);
    }

    [Fact]
    public void GeneratedLevelsHaveJumpableHighGroundWithPrizes()
    {
        int withPlatforms = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            GeneratedLevel gen = LevelGenerator.Generate(seed, LevelSize.Medium);
            Level level = LevelBuilder.FromDrawing(gen.ToGeometry(new Vec2(100, 50), 10), 10, seed);
            Assert.Equal(gen.Platforms.Count, level.Terrain.Platforms.Count);
            if (gen.Platforms.Count > 0) withPlatforms++;
            foreach (Platform p in level.Terrain.Platforms)
            {
                // Every platform can be climbed onto: somewhere along its edge, the floor beside it is within a jump.
                double easiest = double.PositiveInfinity;
                for (int i = 0; i < p.Outline.Count; i++)
                {
                    Vec2 a = p.Outline[i], b = p.Outline[(i + 1) % p.Outline.Count];
                    Vec2 outward = (b - a).Normalized().PerpRight() * 0.05;
                    for (double t = 0.1; t < 1; t += 0.1)
                    {
                        Vec2 at = a + (b - a) * t;
                        double inside = Math.Max(level.Terrain.FloorAt(at + outward), level.Terrain.FloorAt(at - outward));
                        double outside = Math.Min(level.Terrain.FloorAt(at + outward), level.Terrain.FloorAt(at - outward));
                        if (inside >= p.Height - 1e-9) easiest = Math.Min(easiest, p.Height - outside);
                    }
                }
                Assert.True(easiest < Terrain.JumpHeight - 0.1, $"seed {seed}: a {p.Height:0.00} platform you can't get onto");
                Assert.True(p.Height <= 0.6 + 1e-9);
            }
            Assert.Equal(0, level.Terrain.FloorAt(level.PlayerStart));
            if (level.Exit is { } exit) Assert.Equal(0, level.Terrain.FloorAt(exit));
        }
        Assert.True(withPlatforms >= 6);

        GeneratedLevel g = LevelGenerator.Generate(3, LevelSize.Large);
        Level l = LevelBuilder.FromDrawing(g.ToGeometry(Vec2.Zero, 1), 1, 3);
        Assert.Contains(l.Pickups, p => l.Terrain.FloorAt(p.Position) > 0.3); // something worth the climb
    }

    [Fact]
    public void DrawingPlatformsAreMeasuredFromTheStartBlock()
    {
        var geo = new DrawingGeometry { PlayerStart = new Vec2(0, 0), FloorElevation = 100 };
        geo.Segments.Add(new DrawingSegment(new Vec2(-50, -50), new Vec2(50, -50), 0));
        geo.Segments.Add(new DrawingSegment(new Vec2(50, -50), new Vec2(50, 50), 0));
        geo.Platforms.Add(new PlatformSpawn(new[] { new Vec2(10, 10), new Vec2(30, 10), new Vec2(30, 30) }, 104));
        geo.Platforms.Add(new PlatformSpawn(new[] { new Vec2(-10, 10), new Vec2(-30, 10), new Vec2(-30, 30) }, 500)); // capped
        Level level = LevelBuilder.FromDrawing(geo, 10, 1);
        Assert.Equal(0.4, level.Terrain.Platforms[0].Height, 6);
        Assert.Equal(LevelBuilder.MaxPlatformHeight, level.Terrain.Platforms[1].Height, 6);
    }

    [Fact]
    public void RendersPlatformsFromAboveAndBelow()
    {
        Level level = BuiltInLevels.DetailGrid();
        Assert.NotEmpty(level.Terrain.Platforms);
        var g = new Game(level, 1);
        var r = new Renderer(160, 100);
        g.Player.Position = new Vec2(4.5, 3);
        g.Player.Angle = Math.Atan2(2, 4.5);
        r.Render(g);
        g.Player.Position = new Vec2(9.9, 4.6);
        g.Update(0.016, new GameInput());
        Assert.Equal(0.6, g.Player.Z, 6);
        r.Render(g);
    }
}
