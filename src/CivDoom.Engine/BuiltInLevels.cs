namespace CivDoom.Engine;

/// <summary>A hand-made level for when the drawing is empty (or you just want to play).</summary>
public static class BuiltInLevels
{
    public static readonly string[] DetailGridMap =
    {
        "##########################",
        "#P.....#.........#...H...#",
        "#......#....E....#.......#",
        "#..A...B.........G...E...#",
        "#......B.........G.......#",
        "###.####...YYY...GGG..GGG#",
        "#......#...Y.Y...........#",
        "#..E...#...YXY.......A...#",
        "#......#.................#",
        "#......####..####RRRR..RR#",
        "#........................#",
        "#..H......E.......R..E...#",
        "BBBBB..BBBB.......R......#",
        "#..........####.WWWW.WWWW#",
        "#..E.......#..#..........#",
        "#......A...#E.#....X.....#",
        "#..........#..#..........#",
        "#...E....................#",
        "#.........H..........H...#",
        "##########################",
    };

    public static Level DetailGrid() => AsciiMap.Parse("E1M1: Detail Grid", DetailGridMap);
}
