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
        "###.####...YYY...GGG..GGG#",
        "#......#...YFY...........#",
        "#..E...#...YDY.......A...#",
        "#......#....Z........g...#",
        "#......####..####RRRR..RR#",
        "#........................#",
        "#..H......E...c...R..E...#",
        "BBBBB..BBBB.......R......#",
        "#..........####.WWWW.WWWW#",
        "#..E.......#..#..........#",
        "#......A...#E.#....X..r..#",
        "#..........#..#..........#",
        "#...E....................#",
        "#.....f...H..........H...#",
        "##########################",
    };

    /// <summary>The demo level, in a random theme each time.</summary>
    public static Level DetailGrid()
    {
        Level l = AsciiMap.Parse("E1M1: Detail Grid", DetailGridMap);
        return new Level(l.Name, l.Walls, l.PlayerStart, l.PlayerAngle, l.Enemies, l.Pickups)
        {
            Exit = l.Exit,
            GateRule = l.GateRule,
            ThemeId = ThemeSet.RandomTheme,
        };
    }
}
