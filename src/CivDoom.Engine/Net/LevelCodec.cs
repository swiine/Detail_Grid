using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CivDoom.Engine;

/// <summary>
/// Everything a joining player needs to build exactly the host's level: the walls, platforms, markers and
/// finish, plus which monster, weapon and theme the host's game picked for each random marker.
/// </summary>
public sealed class LevelPackage
{
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public string? Theme { get; set; }
    public GateRule Rule { get; set; }

    /// <summary>ax, ay, bx, by for each wall.</summary>
    public double[] Walls { get; set; } = Array.Empty<double>();
    public int[] Colors { get; set; } = Array.Empty<int>();

    /// <summary>Per wall: 1 = gate, plus 2 x the keycard colour for doors.</summary>
    public int[] Flags { get; set; } = Array.Empty<int>();

    /// <summary>x, y, angle.</summary>
    public double[] Start { get; set; } = Array.Empty<double>();
    public double[]? Exit { get; set; }

    /// <summary>Drawing origin x, y and wall height in drawing units (for viewport mode).</summary>
    public double[] Drawing { get; set; } = { 0, 0, 1 };

    /// <summary>x, y per monster marker, with the monster the host chose for each in <see cref="Designs"/>.</summary>
    public double[] Monsters { get; set; } = Array.Empty<double>();
    public string?[] Kinds { get; set; } = Array.Empty<string?>();
    public string[] Designs { get; set; } = Array.Empty<string>();

    /// <summary>x, y, kind, key per pickup, with the weapon lying there (if any) in <see cref="Weapons"/>.</summary>
    public double[] Pickups { get; set; } = Array.Empty<double>();
    public string?[] Weapons { get; set; } = Array.Empty<string?>();

    public List<PlatformPackage> Platforms { get; set; } = new();
}

public sealed class PlatformPackage
{
    public double[] Outline { get; set; } = Array.Empty<double>();
    public double Height { get; set; }
    public int Color { get; set; }
}

public static class LevelCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static double R(double v) => Math.Round(v, 5);

    /// <summary>Packs the level as the host's <paramref name="game"/> is playing it.</summary>
    public static LevelPackage Pack(Game game, int seed)
    {
        Level l = game.Level;
        var p = new LevelPackage
        {
            Name = l.Name,
            Seed = seed,
            Theme = game.Theme.Id,
            Rule = l.GateRule,
            Walls = l.Walls.SelectMany(w => new[] { R(w.A.X), R(w.A.Y), R(w.B.X), R(w.B.Y) }).ToArray(),
            Colors = l.Walls.Select(w => w.Color).ToArray(),
            Flags = l.Walls.Select(w => (w.IsGate ? 1 : 0) | ((int)w.Key << 1)).ToArray(),
            Start = new[] { R(l.PlayerStart.X), R(l.PlayerStart.Y), R(l.PlayerAngle) },
            Exit = l.Exit is { } e ? new[] { R(e.X), R(e.Y) } : null,
            Drawing = new[] { l.DrawingOrigin.X, l.DrawingOrigin.Y, l.DrawingScale },
            Monsters = l.Enemies.SelectMany(s => new[] { R(s.Position.X), R(s.Position.Y) }).ToArray(),
            Kinds = l.Enemies.Select(s => s.Kind).ToArray(),
            Designs = game.Enemies.Select(e => e.Design.Id).ToArray(),
            Pickups = l.Pickups.SelectMany(s => new[] { R(s.Position.X), R(s.Position.Y), (int)s.Kind, (int)s.Key }).ToArray(),
            Weapons = game.Pickups.Select(pk => pk.Weapon?.Id).ToArray(),
            Platforms = l.Terrain.Platforms.Select(pl => new PlatformPackage
            {
                Outline = pl.Outline.SelectMany(v => new[] { R(v.X), R(v.Y) }).ToArray(),
                Height = pl.Height,
                Color = pl.Color,
            }).ToList(),
        };
        return p;
    }

    /// <summary>Rebuilds the level on a joining player's computer.</summary>
    public static Level Unpack(LevelPackage p)
    {
        var walls = new List<Wall>(p.Colors.Length);
        for (int i = 0; i < p.Colors.Length && i * 4 + 3 < p.Walls.Length; i++)
        {
            int flags = i < p.Flags.Length ? p.Flags[i] : 0;
            walls.Add(new Wall(new Vec2(p.Walls[i * 4], p.Walls[i * 4 + 1]), new Vec2(p.Walls[i * 4 + 2], p.Walls[i * 4 + 3]), p.Colors[i], (flags & 1) != 0)
            {
                Key = (KeyColor)(flags >> 1),
            });
        }
        var enemies = new List<EnemySpawn>();
        for (int i = 0; i * 2 + 1 < p.Monsters.Length; i++)
            enemies.Add(new EnemySpawn(new Vec2(p.Monsters[i * 2], p.Monsters[i * 2 + 1]), i < p.Kinds.Length ? p.Kinds[i] : null));
        var pickups = new List<PickupSpawn>();
        for (int i = 0; i * 4 + 3 < p.Pickups.Length; i++)
            pickups.Add(new PickupSpawn(new Vec2(p.Pickups[i * 4], p.Pickups[i * 4 + 1]), (PickupKind)(int)p.Pickups[i * 4 + 2],
                i < p.Weapons.Length ? p.Weapons[i] : null, (KeyColor)(int)p.Pickups[i * 4 + 3]));
        var platforms = p.Platforms.Select(pl => new PlatformSpawn(
            Enumerable.Range(0, pl.Outline.Length / 2).Select(k => new Vec2(pl.Outline[k * 2], pl.Outline[k * 2 + 1])).ToList(), pl.Height, pl.Color));
        return new Level(p.Name, walls, new Vec2(p.Start[0], p.Start[1]), p.Start[2], enemies, pickups)
        {
            Exit = p.Exit is { Length: >= 2 } e ? new Vec2(e[0], e[1]) : null,
            GateRule = p.Rule,
            ThemeId = p.Theme,
            DrawingOrigin = p.Drawing.Length >= 2 ? new Vec2(p.Drawing[0], p.Drawing[1]) : Vec2.Zero,
            DrawingScale = p.Drawing.Length >= 3 && p.Drawing[2] > 0 ? p.Drawing[2] : 1,
            Terrain = Terrain.From(platforms),
        };
    }

    /// <summary>
    /// The joining player's game: the same level and seed, then the host's choice of monster for each marker
    /// and weapon on each pickup (in case this computer's monster or weapon files differ).
    /// </summary>
    public static Game CreateGame(LevelPackage p, GameContent content)
    {
        var game = new Game(Unpack(p), p.Seed, content);
        for (int i = 0; i < game.Enemies.Count && i < p.Designs.Length; i++)
        {
            if (content.Monsters.Find(p.Designs[i]) is not { } d || d == game.Enemies[i].Design) continue;
            game.Enemies[i].Design = d;
            game.Enemies[i].Health = d.Health;
        }
        for (int i = 0; i < game.Pickups.Count && i < p.Weapons.Length; i++)
            if (content.Weapons.Find(p.Weapons[i]) is { } w) game.Pickups[i].Weapon = w;
        return game;
    }

    public static byte[] ToBytes(LevelPackage p)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
            JsonSerializer.Serialize(gz, p, Json);
        return ms.ToArray();
    }

    public static LevelPackage FromBytes(byte[] bytes)
    {
        using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<LevelPackage>(gz, Json) ?? throw new InvalidDataException("Empty level package");
    }
}
