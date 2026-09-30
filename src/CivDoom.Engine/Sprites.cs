namespace CivDoom.Engine;

/// <summary>Small ARGB image. Pixels with alpha 0 are transparent.</summary>
public sealed class SpriteImage
{
    public SpriteImage(int width, int height, int[] pixels)
    {
        if (pixels.Length != width * height) throw new ArgumentException("Pixel count mismatch.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>0xAARRGGBB, row-major, top row first.</summary>
    public int[] Pixels { get; }

    public int this[int x, int y] => Pixels[y * Width + x];

    /// <summary>Parses character art. Each character maps to a color through <paramref name="palette"/>; '.' is transparent.</summary>
    public static SpriteImage FromArt(IReadOnlyList<string> rows, IReadOnlyDictionary<char, int> palette)
    {
        int h = rows.Count, w = rows[0].Length;
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            if (rows[y].Length != w) throw new ArgumentException($"Sprite row {y} is {rows[y].Length} wide, expected {w}.");
            for (int x = 0; x < w; x++)
            {
                char ch = rows[y][x];
                if (ch == '.') continue;
                if (!palette.TryGetValue(ch, out int rgb)) throw new ArgumentException($"No palette entry for '{ch}'.");
                px[y * w + x] = unchecked((int)0xFF000000) | rgb;
            }
        }
        return new SpriteImage(w, h, px);
    }
}

/// <summary>All of the game's artwork, drawn as character grids so there are no binary assets to ship.</summary>
public static class Art
{
    private static readonly Dictionary<char, int> ImpPalette = new()
    {
        ['R'] = 0xA8322A, ['r'] = 0x6E1F1A, ['h'] = 0xE8DDB5, ['y'] = 0xFFE040,
        ['k'] = 0x1A0A08, ['w'] = 0xF4F4F4, ['o'] = 0xFF8A1C, ['Y'] = 0xFFF2A0,
    };

    private static readonly Dictionary<char, int> BrutePalette = new(ImpPalette)
    {
        ['R'] = 0x4E7F3A, ['r'] = 0x2E4F22, ['y'] = 0xFF3030,
    };

    private static readonly Dictionary<char, int> ItemPalette = new()
    {
        ['w'] = 0xF0F0F0, ['g'] = 0xA0A0A0, ['R'] = 0xD02020, ['k'] = 0x202020,
        ['o'] = 0x6B6B2A, ['O'] = 0x8E8E3C, ['y'] = 0xE8C040, ['Y'] = 0xFFF2A0,
        ['r'] = 0xFF8A1C, ['s'] = 0xC7A27C, ['S'] = 0x9E7B5A, ['G'] = 0x5A5A66, ['m'] = 0x3A3A44,
    };

    private static readonly string[] MonsterIdle =
    {
        "..h..........h..",
        "..hh........hh..",
        "...hRRRRRRRRh...",
        "...RRRRRRRRRR...",
        "...RyyRRRRyyR...",
        "...RRRRRRRRRR...",
        "....RkkkkkkR....",
        "....RRwRRwRR....",
        "..rrRRRRRRRRrr..",
        ".rrRRRRRRRRRRrr.",
        ".rR.RRRRRRRR.Rr.",
        ".rR.RRRRRRRR.Rr.",
        ".kk.rrrrrrrr.kk.",
        "....rrr..rrr....",
        "....rrr..rrr....",
        "...kkkk..kkkk...",
    };

    private static readonly string[] MonsterWalk =
    {
        "..h..........h..",
        "..hh........hh..",
        "...hRRRRRRRRh...",
        "...RRRRRRRRRR...",
        "...RyyRRRRyyR...",
        "...RRRRRRRRRR...",
        "....RkkkkkkR....",
        "....RRwRRwRR....",
        "..rrRRRRRRRRrr..",
        ".rrRRRRRRRRRRrr.",
        ".rR.RRRRRRRR.Rr.",
        ".kk.RRRRRRRR.Rr.",
        "....rrrrrrrr.kk.",
        "...rrr....rrr...",
        "..rrr......rrr..",
        ".kkkk......kkkk.",
    };

    private static readonly string[] MonsterAttack =
    {
        ".Yo..........oY.",
        "YooY........YooY",
        ".oohRRRRRRRRhoo.",
        "..rRRRRRRRRRRr..",
        "..rRyyRRRRyyRr..",
        "..rRRRRRRRRRRr..",
        "..r.RkkkkkkR.r..",
        "..r.RwkkkkwR.r..",
        "..rrRRRRRRRRrr..",
        "....RRRRRRRR....",
        "....RRRRRRRR....",
        "....RRRRRRRR....",
        "....rrrrrrrr....",
        "....rrr..rrr....",
        "....rrr..rrr....",
        "...kkkk..kkkk...",
    };

    private static readonly string[] MonsterDead =
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "......h..h......",
        "....rRRRRRRr....",
        "..rrRkRRRRkRrr..",
        ".rRRRRRRRRRRRRr.",
        "rrrrrrrrrrrrrrrr",
    };

    private static readonly string[] Fireball =
    {
        "....oooo....",
        "..oorrrroo..",
        ".orrYYYYrro.",
        ".orYYwwYYro.",
        "orYYwwwwYYro",
        "orYwwwwwwYro",
        "orYwwwwwwYro",
        "orYYwwwwYYro",
        ".orYYwwYYro.",
        ".orrYYYYrro.",
        "..oorrrroo..",
        "....oooo....",
    };

    private static readonly string[] Medkit =
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "......kkkk......",
        ".....k....k.....",
        "..wwwwwwwwwwww..",
        "..wwwwwRRwwwww..",
        "..wwwwwRRwwwww..",
        "..wwwRRRRRRwww..",
        "..wwwRRRRRRwww..",
        "..wwwwwRRwwwww..",
        "..wwwwwRRwwwww..",
        "..gggggggggggg..",
        "................",
    };

    private static readonly string[] AmmoBox =
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "....yY.yY.yY....",
        "....yy.yy.yy....",
        "..oooooooooooo..",
        "..oOOOOOOOOOOo..",
        "..oOkkOOOOkkOo..",
        "..oOOOOOOOOOOo..",
        "..oooooooooooo..",
        "..oooooooooooo..",
        "..kkkkkkkkkkkk..",
        "................",
    };

    // A chunky pistol, held in a hand at the bottom of the screen.
    private static readonly string[] Pistol =
    {
        "........kk........",
        ".......kGGk.......",
        ".......kGGk.......",
        ".......kGGk.......",
        "......kGmmGk......",
        "......kGmmGk......",
        ".....kGGmmGGk.....",
        ".....kGGGGGGk.....",
        ".....kmGGGGmk.....",
        "....ssskGGkss.....",
        "...sssssskksss....",
        "..sSssssssssssS...",
        "..sSsssssssssssS..",
        ".sSSsssssssssssS..",
        ".sSSssssssssssssS.",
        "sSSSssssssssssssS.",
    };

    private static readonly string[] MuzzleFlash =
    {
        "....Y....Y....",
        "..Y..rr.rr..Y.",
        "....ryyYYyr...",
        ".Y.ryYwwwYyr.Y",
        "...ryYwwwwYr..",
        "..rryYwwwYyrr.",
        "....ryyYYyr...",
        "..Y..r...r..Y.",
    };

    public sealed record MonsterSprites(SpriteImage Idle, SpriteImage Walk, SpriteImage Attack, SpriteImage Dead);

    public static MonsterSprites Imp { get; } = BuildMonster(ImpPalette);
    public static MonsterSprites Brute { get; } = BuildMonster(BrutePalette);
    public static SpriteImage FireballSprite { get; } = SpriteImage.FromArt(Fireball, ImpPalette);
    public static SpriteImage MedkitSprite { get; } = SpriteImage.FromArt(Medkit, ItemPalette);
    public static SpriteImage AmmoSprite { get; } = SpriteImage.FromArt(AmmoBox, ItemPalette);
    public static SpriteImage PistolSprite { get; } = SpriteImage.FromArt(Pistol, ItemPalette);
    public static SpriteImage MuzzleFlashSprite { get; } = SpriteImage.FromArt(MuzzleFlash, ItemPalette);

    private static MonsterSprites BuildMonster(Dictionary<char, int> palette) => new(
        SpriteImage.FromArt(MonsterIdle, palette),
        SpriteImage.FromArt(MonsterWalk, palette),
        SpriteImage.FromArt(MonsterAttack, palette),
        SpriteImage.FromArt(MonsterDead, palette));
}
