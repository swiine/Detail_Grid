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
        "#......#...Y.Y...........#",
        "#..E...#...YXY.......A...#",
        "#......#.............g...#",
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

    public static Level DetailGrid() => AsciiMap.Parse("E1M1: Detail Grid", DetailGridMap);
}
