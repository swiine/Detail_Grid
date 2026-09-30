namespace CivDoom.Engine;

/// <summary>A block of same-coloured pixels: top-left corner, size, and 0xRRGGBB colour.</summary>
public readonly record struct SpriteRect(int X, int Y, int W, int H, int Color);

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

    private IReadOnlyList<SpriteRect>? _rects;

    /// <summary>
    /// The opaque pixels merged into as few same-coloured rectangles as a simple greedy pass finds
    /// (runs across, then grown downwards). Used to draw sprites as CAD geometry. Cached.
    /// </summary>
    public IReadOnlyList<SpriteRect> Rectangles()
    {
        if (_rects != null) return _rects;
        var done = new bool[Width * Height];
        var rects = new List<SpriteRect>();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int c = this[x, y];
                if (done[y * Width + x] || (c >>> 24) == 0) continue;
                int w = 1;
                while (x + w < Width && !done[y * Width + x + w] && this[x + w, y] == c) w++;
                int h = 1;
                while (y + h < Height && RowMatches(x, y + h, w, c, done)) h++;
                for (int j = y; j < y + h; j++)
                    for (int i = x; i < x + w; i++)
                        done[j * Width + i] = true;
                rects.Add(new SpriteRect(x, y, w, h, c & 0xFFFFFF));
            }
        }
        return _rects = rects;
    }

    private readonly Dictionary<int, SpriteImage> _simplified = new();

    /// <summary>
    /// A version of this picture that needs at most about <paramref name="maxRects"/> rectangles to draw
    /// (fewer colours first, then halving the resolution). Returns itself when it's already simple enough.
    /// </summary>
    public SpriteImage Simplified(int maxRects)
    {
        if (Rectangles().Count <= maxRects) return this;
        if (_simplified.TryGetValue(maxRects, out SpriteImage? cached)) return cached;
        SpriteImage img = Quantize(this, 4);
        while (img.Rectangles().Count > maxRects && Math.Max(img.Width, img.Height) > 8)
            img = Quantize(ImageSprites.Resize(img, Math.Max(img.Width, img.Height) * 2 / 3), 4);
        return _simplified[maxRects] = img;
    }

    private static SpriteImage Quantize(SpriteImage s, int bits)
    {
        int mask = (0xFF << (8 - bits)) & 0xFF, half = 1 << (7 - bits);
        var px = new int[s.Pixels.Length];
        for (int i = 0; i < px.Length; i++)
        {
            int c = s.Pixels[i];
            if ((c >>> 24) == 0) continue;
            int r = Math.Min(255, (((c >> 16) & 0xFF) & mask) + half);
            int g = Math.Min(255, (((c >> 8) & 0xFF) & mask) + half);
            int b = Math.Min(255, ((c & 0xFF) & mask) + half);
            px[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
        return new SpriteImage(s.Width, s.Height, px);
    }

    private bool RowMatches(int x, int y, int w, int c, bool[] done)
    {
        for (int i = x; i < x + w; i++)
            if (done[y * Width + i] || this[i, y] != c) return false;
        return true;
    }

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

/// <summary>
/// Item artwork, drawn as character grids so there are no binary assets to ship.
/// Monster and weapon artwork lives in the editable Monsters/*.txt and Weapons/*.txt files.
/// </summary>
public static class Art
{
    private static readonly Dictionary<char, int> FireballPalette = new()
    {
        ['o'] = 0xFF8A1C, ['r'] = 0xE04010, ['Y'] = 0xFFF2A0, ['w'] = 0xFFFFFF,
    };

    private static readonly Dictionary<char, int> ItemPalette = new()
    {
        ['w'] = 0xF0F0F0, ['g'] = 0xA0A0A0, ['R'] = 0xD02020, ['k'] = 0x202020,
        ['o'] = 0x6B6B2A, ['O'] = 0x8E8E3C, ['y'] = 0xE8C040, ['Y'] = 0xFFF2A0,
        ['r'] = 0xFF8A1C, ['s'] = 0xC7A27C, ['S'] = 0x9E7B5A, ['G'] = 0x5A5A66, ['m'] = 0x3A3A44,
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

    public static SpriteImage FireballSprite { get; } = SpriteImage.FromArt(Fireball, FireballPalette);
    public static SpriteImage MedkitSprite { get; } = SpriteImage.FromArt(Medkit, ItemPalette);
    public static SpriteImage AmmoSprite { get; } = SpriteImage.FromArt(AmmoBox, ItemPalette);

}
