using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class KeyDoorTests
{
    private static readonly string[] Map =
    {
        "##############",
        "#P.(....)...F#",
        "##############",
    };

    private static void Walk(Game g, double seconds)
    {
        var forward = new GameInput { Forward = true };
        for (int i = 0; i < seconds * 60; i++) g.Update(1 / 60.0, forward);
    }

    [Fact]
    public void MapSymbolsMakeKeysAndDoors()
    {
        Level level = AsciiMap.Parse("t", Map);
        Assert.Equal(2, level.Doors.Count()); // the two faces of the door cell
        Assert.All(level.Doors, d => Assert.Equal(KeyColor.Red, d.Key));
        PickupSpawn key = Assert.Single(level.Pickups, p => p.Kind == PickupKind.Key);
        Assert.Equal(KeyColor.Red, key.Key);
    }

    [Fact]
    public void DoorStaysShutWithoutTheKey()
    {
        Level level = AsciiMap.Parse("t", new[] { "##############", "#P......)...F#", "##############" });
        var g = new Game(level, 1);
        Walk(g, 3);
        Assert.True(g.Player.Position.X < 8);
        Assert.All(level.Doors, d => Assert.False(d.IsOpen));
        Assert.Contains(g.MessageLog, m => m.Text.Contains("RED keycard"));
    }

    [Fact]
    public void KeyOpensItsDoor()
    {
        var g = new Game(AsciiMap.Parse("t", Map), 1);
        Assert.Contains("RED keycard", g.Objective);
        Assert.Equal("RED KEY", g.ObjectiveTarget?.Label);
        Walk(g, 7);
        Assert.Contains(KeyColor.Red, g.Player.Keys);
        Assert.All(g.Level.Doors, d => Assert.True(d.IsOpen));
        Assert.True(g.Player.Position.X > 9, "walked through the opened door");
        Assert.True(g.State is GameState.Exiting or GameState.Won); // and on to the finish
    }

    [Fact]
    public void WrongColourDoesntOpenIt()
    {
        var g = new Game(AsciiMap.Parse("t", new[] { "##############", "#P.[....)...F#", "##############" }), 1);
        Walk(g, 3);
        Assert.Contains(KeyColor.Blue, g.Player.Keys);
        Assert.All(g.Level.Doors, d => Assert.False(d.IsOpen));
    }

    [Fact]
    public void LockedDoorsKeepMonstersBack()
    {
        Level level = AsciiMap.Parse("t", new[] { "##############", "#P......)..E.#", "##############" });
        var nav = new NavGrid(level.Index, level.Terrain);
        nav.SetTarget(level.PlayerStart);
        Assert.Null(nav.DistanceFrom(level.Enemies[0].Position));
    }

    [Fact]
    public void ReplayingRelocksDoors()
    {
        Level level = AsciiMap.Parse("t", Map);
        Walk(new Game(level, 1), 4);
        Assert.All(level.Doors, d => Assert.True(d.IsOpen));
        _ = new Game(level, 2);
        Assert.All(level.Doors, d => Assert.False(d.IsOpen));
    }

    [Theory]
    [InlineData(LevelSize.Small)]
    [InlineData(LevelSize.Medium)]
    [InlineData(LevelSize.Large)]
    public void GeneratedKeysComeBeforeTheirDoors(LevelSize size)
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            GeneratedLevel gen = LevelGenerator.Generate(seed, size);
            Level level = LevelBuilder.FromDrawing(gen.ToGeometry(Vec2.Zero, 10), 10, seed);
            List<KeyColor> colors = gen.Doors.Select(d => d.Key).Distinct().ToList();
            Assert.True(colors.Count >= 1, $"{size} seed {seed}: no doors");
            Assert.True(colors.Count <= LevelGenerator.KeyDoors(size));
            foreach (Wall g in level.Gates) g.IsOpen = true; // only the doors matter here
            Vec2 exit = level.Exit!.Value;

            for (int k = 0; k < colors.Count; k++)
            {
                foreach (Wall d in level.Doors) d.IsOpen = colors.IndexOf(d.Key) < k;
                var reach = new Reachability(level.Index, level.PlayerStart, 0.1);
                PickupSpawn key = level.Pickups.Single(p => p.Kind == PickupKind.Key && p.Key == colors[k]);
                Assert.True(reach.IsReachable(key.Position), $"{size} seed {seed}: the {colors[k]} key is locked away");
                Assert.False(reach.IsReachable(exit), $"{size} seed {seed}: you can get round the {colors[k]} door");
            }
            foreach (Wall d in level.Doors) d.IsOpen = true;
            Assert.True(new Reachability(level.Index, level.PlayerStart, 0.1).IsReachable(exit));
        }
    }
}
