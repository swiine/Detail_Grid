namespace CivDoom.Engine;

/// <summary>A hand-made level for when the drawing is empty (or you just want to play).</summary>
public static class BuiltInLevels
{
    public static readonly string[] DetailGridMap =
    {
        "##########################",
        "#P.....#.........#...H...#",
        "#..k...#....E....#.......#",
        "#..A...B.........G...E...#",
        "#...s..B.........G.......#",
        "###.####...YYY...GGG]]GGG#",
        "#......#...YFY...........#",
        "#..E.(.#...YDY.......A...#",
        "#......#....Z........g...#",
        "#......####..####RRRR..RR#",
        "#........................#",
        "#..H......E...c...R..E...#",
        "BBBBB))BBBB.......R......#",
        "#..........####)WWWW)WWWW#",
        "#..E....44.#..#.....33333#",
        "#......A44.#E.#....X.....#",
        "#..[.....2.#..#..........#",
        "#...E....................#",
        "#.....f...H..........H...#",
        "##########################",
    };

    /// <summary>The demo level, in a random theme each time.</summary>
    public static Level DetailGrid()
    {
        Level l = AsciiMap.Parse("E1M1: Detail Grid", DetailGridMap);
        // Prizes up on the platforms (the map can't put an item and a height in one cell).
        var pickups = l.Pickups.Concat(new[]
        {
            new PickupSpawn(new Vec2(9, 5), PickupKind.Weapon, "rocketlauncher"), // top of the tower (bottom left)
            new PickupSpawn(new Vec2(22.5, 5.5), PickupKind.Health),              // the balcony (bottom right)
        }).ToList();
        return new Level(l.Name, l.Walls, l.PlayerStart, l.PlayerAngle, l.Enemies, pickups)
        {
            Exit = l.Exit,
            GateRule = l.GateRule,
            ThemeId = ThemeSet.RandomTheme,
            Terrain = l.Terrain,
        };
    }
}
