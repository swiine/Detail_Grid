namespace CivDoom.Engine;

/// <summary>
/// Builds a level from a character grid (one character per 1x1 cell, first row = north).
/// Wall characters: '#' concrete, 'B' blue, 'G' green, 'R' red, 'Y' yellow, 'W' white, 'D' locked gate.
/// Markers: 'P' player (facing east), 'E' imp, 'X' brute, 'M' random monster, 'Z' random boss,
/// 'F' finish line, 'H' health, 'A' ammo.
/// Weapons: 'c' chainsaw, 'k' katana, 'p' pistol, 's' shotgun, 'g' chaingun, 'r' rocket launcher, 'f' flamethrower,
/// 'w' any weapon. Anything else is floor.
/// </summary>
public static class AsciiMap
{
    private static readonly Dictionary<char, int> WallColors = new()
    {
        ['#'] = 0x8C8C8C,
        ['B'] = 0x3A6FD8,
        ['G'] = 0x3FA34D,
        ['R'] = 0xB23A3A,
        ['Y'] = 0xD8B43A,
        ['W'] = 0xD6D6D6,
        ['D'] = LevelBuilder.GateColor,
    };

    private static readonly Dictionary<char, string?> WeaponMarkers = new()
    {
        ['c'] = "chainsaw", ['k'] = "katana", ['p'] = "pistol", ['s'] = "shotgun",
        ['g'] = "chaingun", ['r'] = "rocketlauncher", ['f'] = "flamethrower", ['w'] = null,
    };

    public static Level Parse(string name, IReadOnlyList<string> rows)
    {
        int height = rows.Count;
        int width = rows.Max(r => r.Length);
        char At(int c, int r) => r < 0 || r >= height || c < 0 || c >= rows[r].Length ? ' ' : rows[r][c];
        bool IsWall(int c, int r) => WallColors.ContainsKey(At(c, r));
        // Row 0 is the top of the text, which is north (+Y).
        Vec2 Corner(int c, int r) => new(c, height - r);

        var walls = new List<Wall>();
        var enemies = new List<EnemySpawn>();
        var pickups = new List<PickupSpawn>();
        Vec2 start = new(1.5, height - 1.5);
        Vec2? exit = null;

        // Horizontal faces: merge runs along each grid line.
        for (int r = 0; r <= height; r++)
        {
            AddRuns(width, c =>
            {
                bool above = IsWall(c, r - 1), below = IsWall(c, r);
                if (above == below) return null;
                return above ? (WallColors[At(c, r - 1)], true) : (WallColors[At(c, r)], false);
            }, (c0, c1, color, flip) => walls.Add(flip
                ? new Wall(Corner(c1, r), Corner(c0, r), color, color == LevelBuilder.GateColor)
                : new Wall(Corner(c0, r), Corner(c1, r), color, color == LevelBuilder.GateColor)));
        }

        // Vertical faces.
        for (int c = 0; c <= width; c++)
        {
            AddRuns(height, r =>
            {
                bool left = IsWall(c - 1, r), right = IsWall(c, r);
                if (left == right) return null;
                return left ? (WallColors[At(c - 1, r)], true) : (WallColors[At(c, r)], false);
            }, (r0, r1, color, flip) => walls.Add(flip
                ? new Wall(Corner(c, r0), Corner(c, r1), color, color == LevelBuilder.GateColor)
                : new Wall(Corner(c, r1), Corner(c, r0), color, color == LevelBuilder.GateColor)));
        }

        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                Vec2 center = new(c + 0.5, height - r - 0.5);
                switch (rows[r][c])
                {
                    case 'P': start = center; break;
                    case 'E': enemies.Add(new EnemySpawn(center, "imp")); break;
                    case 'X': enemies.Add(new EnemySpawn(center, "brute")); break;
                    case 'M': enemies.Add(new EnemySpawn(center)); break;
                    case 'Z': enemies.Add(new EnemySpawn(center, MonsterSet.RandomBoss)); break;
                    case 'F': exit = center; break;
                    case 'H': pickups.Add(new PickupSpawn(center, PickupKind.Health)); break;
                    case 'A': pickups.Add(new PickupSpawn(center, PickupKind.Ammo)); break;
                    case var ch when WeaponMarkers.TryGetValue(ch, out string? weapon):
                        pickups.Add(new PickupSpawn(center, PickupKind.Weapon, weapon));
                        break;
                }
            }
        }

        return new Level(name, walls, start, 0, enemies, pickups) { Exit = exit };
    }

    /// <summary>Groups consecutive cells that produce the same face into single wall segments.</summary>
    private static void AddRuns(int count, Func<int, (int Color, bool Flip)?> face, Action<int, int, int, bool> emit)
    {
        int runStart = -1;
        (int Color, bool Flip)? runFace = null;
        for (int i = 0; i <= count; i++)
        {
            (int Color, bool Flip)? f = i < count ? face(i) : null;
            if (f == runFace && f != null) continue;
            if (runFace is { } done) emit(runStart, i, done.Color, done.Flip);
            runStart = i;
            runFace = f;
        }
    }
}
