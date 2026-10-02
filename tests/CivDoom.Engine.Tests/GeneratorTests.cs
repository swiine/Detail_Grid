using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class GeneratorTests
{
    [Theory]
    [InlineData(1, LevelSize.Small)]
    [InlineData(2, LevelSize.Medium)]
    [InlineData(3, LevelSize.Large)]
    [InlineData(4, LevelSize.Medium)]
    [InlineData(5, LevelSize.Medium)]
    public void GeneratedLevelsArePlayable(int seed, LevelSize size)
    {
        GeneratedLevel gen = LevelGenerator.Generate(seed, size);
        Assert.NotEmpty(gen.Walls);
        Assert.NotEmpty(gen.Monsters);
        Assert.Contains(gen.Pickups, p => p.Kind == PickupKind.Weapon);

        // Build it the same way CIVDOOM reads a drawing: 10 drawing units per cell, wall height 10.
        Level level = LevelBuilder.FromDrawing(gen.ToGeometry(new Vec2(5000, 3000), 10), 10, seed);
        var reach = new Reachability(level.Index, level.PlayerStart, Game.PlayerRadius);

        // Everything is reachable from the start, and the player can't walk out of the level.
        Assert.All(level.Enemies, e => Assert.True(reach.IsReachable(e.Position), $"monster at {e.Position}"));
        Assert.All(level.Pickups, p => Assert.True(reach.IsReachable(p.Position), $"pickup at {p.Position}"));
        Assert.False(reach.IsReachable(level.Index.Min - new Vec2(0.5, 0.5)));

        // Markers were used verbatim, not auto-placed.
        Assert.Equal(gen.Monsters.Count, level.Enemies.Count);
        Assert.Equal(gen.Pickups.Count, level.Pickups.Count);
    }

    [Fact]
    public void OutlinesAreClosedAxisAlignedLoops()
    {
        GeneratedLevel gen = LevelGenerator.Generate(9);
        foreach (List<Vec2> loop in gen.Walls)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Vec2 a = loop[i], b = loop[(i + 1) % loop.Count];
                Assert.True(a.X == b.X || a.Y == b.Y, "edges should be horizontal or vertical");
                Assert.NotEqual(a, b);
            }
        }
        // Every floor/solid boundary edge is covered exactly once.
        double perimeter = gen.Walls.Sum(l => l.Select((p, i) => Vec2.Distance(p, l[(i + 1) % l.Count])).Sum());
        int edges = 0;
        for (int x = 0; x < gen.Width; x++)
            for (int y = 0; y < gen.Height; y++)
                if (gen.Floor[x, y])
                    edges += new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Count(d => !IsFloor(gen, x + d.Item1, y + d.Item2));
        Assert.Equal(edges, perimeter, 6);
    }

    private static bool IsFloor(GeneratedLevel g, int x, int y) =>
        x >= 0 && y >= 0 && x < g.Width && y < g.Height && g.Floor[x, y];

    [Fact]
    public void SameSeedSameLevel()
    {
        GeneratedLevel a = LevelGenerator.Generate(42), b = LevelGenerator.Generate(42);
        Assert.Equal(a.Walls.Count, b.Walls.Count);
        Assert.Equal(a.Monsters, b.Monsters);
    }
}

public class SpriteRectTests
{
    [Fact]
    public void RectanglesCoverEveryOpaquePixelExactlyOnce()
    {
        foreach (MonsterDesign d in MonsterSet.BuiltIn.Designs)
        foreach (SpriteImage img in new[] { d.Idle, d.Walk, d.Attack, d.Dead })
        {
            var cover = new int[img.Width * img.Height];
            foreach (SpriteRect r in img.Rectangles())
                for (int y = r.Y; y < r.Y + r.H; y++)
                    for (int x = r.X; x < r.X + r.W; x++)
                    {
                        cover[y * img.Width + x]++;
                        Assert.Equal(img[x, y] & 0xFFFFFF, r.Color);
                    }
            for (int i = 0; i < cover.Length; i++)
                Assert.Equal((img.Pixels[i] >>> 24) == 0 ? 0 : 1, cover[i]);
            Assert.True(img.Rectangles().Count <= img.Pixels.Count(p => (p >>> 24) != 0));
        }
        // Solid pictures merge into far fewer rectangles than pixels.
        Assert.True(Art.MedkitSprite.Rectangles().Count < Art.MedkitSprite.Pixels.Count(p => (p >>> 24) != 0) / 4);
    }
}
