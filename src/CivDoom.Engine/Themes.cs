using System.Globalization;

namespace CivDoom.Engine;

/// <summary>How walls are textured.</summary>
public enum WallStyle
{
    /// <summary>The original concrete blocks.</summary>
    Blocks,

    /// <summary>Office building: rows of windows (some lit) between concrete floor bands.</summary>
    Office,

    /// <summary>Glass curtain wall with mullions.</summary>
    Glass,

    Brick,

    /// <summary>Corrugated metal sheds with roller doors.</summary>
    Corrugated,

    Sandstone,

    /// <summary>Dark rock with glowing lava cracks.</summary>
    Rock,
}

/// <summary>What fills the horizon.</summary>
public enum SkylineStyle
{
    None,
    City,
    Night,
    Industrial,
    Desert,
    Hell,
}

/// <summary>The look of a place: sky, skyline, floor and wall facades.</summary>
public sealed class ThemeDesign
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public int SkyTop { get; init; } = 0x0B1026;
    public int SkyHorizon { get; init; } = 0x4A3A6A;

    public SkylineStyle Skyline { get; init; }
    public int SkylineColor { get; init; } = 0x1A1E2A;
    public int SkylineLights { get; init; } = 0xFFD27A;

    /// <summary>Height of the skyline band as a fraction of the space above the horizon.</summary>
    public double SkylineHeight { get; init; } = 0.6;

    public WallStyle WallStyle { get; init; }
    public int WallColor { get; init; } = 0x8C8C8C;
    public int WallAccent { get; init; } = 0x4A4E58;
    public int WallLights { get; init; } = 0xFFE6A0;

    /// <summary>Use each wall's drawing/layer colour instead of <see cref="WallColor"/>.</summary>
    public bool UseDrawingColors { get; init; }

    public int FloorColor { get; init; } = 0x151A20;

    /// <summary>Grid line colour on the floor, or null for no grid.</summary>
    public int? FloorGrid { get; init; } = 0x2E6FA8;

    /// <summary>Optional pictures (skyline.png / wall.png in the theme's folder).</summary>
    public SpriteImage? SkylineImage { get; init; }
    public SpriteImage? WallImage { get; init; }

    private SpriteImage? _panorama;

    /// <summary>The 360-degree skyline: the picture if one was supplied, else generated from <see cref="Skyline"/>.</summary>
    public SpriteImage? Panorama => SkylineImage ?? (Skyline == SkylineStyle.None ? null : _panorama ??= ThemeArt.Skyline(this));

    public int WallBase(Wall w) => UseDrawingColors ? w.Color : WallColor;

    /// <summary>Wall colour at position <paramref name="s"/> along the wall (world units) and height <paramref name="v"/> (0 top, 1 floor).</summary>
    public int WallTexel(int baseColor, double s, double v)
    {
        if (WallImage is { } img)
        {
            double fs = s - Math.Floor(s);
            int tx = Math.Clamp((int)(fs * img.Width), 0, img.Width - 1), ty = Math.Clamp((int)(v * img.Height), 0, img.Height - 1);
            int c = img[tx, ty];
            if ((c >>> 24) != 0) return c & 0xFFFFFF;
        }
        return ThemeArt.Wall(WallStyle, baseColor, WallAccent, WallLights, s, v);
    }
}

/// <summary>The themes available, normally loaded from the "themes" folder.</summary>
public sealed class ThemeSet
{
    public const string RandomTheme = "@random";
    public const string DefaultTheme = "classic";

    private static readonly string[] DefaultFiles = { "classic", "city", "industrial", "desert", "night", "hell" };
    private static ThemeSet? _builtIn;

    public ThemeSet(IEnumerable<ThemeDesign> themes, IReadOnlyList<string>? warnings = null)
    {
        Themes = themes.ToList();
        if (Themes.Count == 0) throw new ArgumentException("At least one theme is required.", nameof(themes));
        Warnings = warnings ?? Array.Empty<string>();
    }

    public IReadOnlyList<ThemeDesign> Themes { get; }
    public IReadOnlyList<string> Warnings { get; }

    public static ThemeSet BuiltIn => _builtIn ??= new ThemeSet(
        DefaultFiles.Select(id => ThemeFile.Parse(id, DefaultText(id), new List<string>(), null)));

    public ThemeDesign? Find(string? id) =>
        id == null ? null : Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A theme by id; <see cref="RandomTheme"/> picks any theme except Classic; null/unknown gives Classic.</summary>
    public ThemeDesign Resolve(string? id, Random rng)
    {
        if (id == RandomTheme)
        {
            List<ThemeDesign> pool = Themes.Where(t => t.Id != DefaultTheme).ToList();
            if (pool.Count == 0) pool = Themes.ToList();
            return pool[rng.Next(pool.Count)];
        }
        return Find(id) ?? Find(DefaultTheme) ?? Themes[0];
    }

    public static string DefaultText(string id) => DesignFolder.EmbeddedText("Themes", id);

    public static void WriteDefaults(string folder) => DesignFolder.WriteDefaults(folder, "Themes", DefaultFiles);

    public static ThemeSet Load(string folder)
    {
        var warnings = new List<string>();
        List<ThemeDesign> themes = DesignFolder.Load(
            folder, "theme", () => WriteDefaults(folder), ThemeFile.Parse, BuiltIn.Themes, t => t.Id, warnings);
        return new ThemeSet(themes, warnings);
    }
}

/// <summary>Reads theme files (see Themes/city/city.txt).</summary>
public static class ThemeFile
{
    public static ThemeDesign Parse(string id, string text, List<string> warnings, string? folder = null)
    {
        DesignText d = DesignText.Parse(text, Array.Empty<string>(), warnings);

        int Color(string key, int fallback)
        {
            string? t = d.Text(key)?.Trim().TrimStart('#');
            if (t == null) return fallback;
            if (t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return rgb;
            warnings.Add($"{key} should be a colour like 8A8E96");
            return fallback;
        }

        T Enum<T>(string key, T fallback) where T : struct, System.Enum
        {
            string? t = d.Text(key);
            if (t == null) return fallback;
            if (System.Enum.TryParse(t.Trim(), true, out T value)) return value;
            warnings.Add($"{key} should be one of: {string.Join(", ", System.Enum.GetNames<T>()).ToLowerInvariant()}");
            return fallback;
        }

        string? grid = d.Text("floor grid");
        return new ThemeDesign
        {
            Id = id,
            Name = d.Text("name") ?? DesignText.Capitalize(id),
            SkyTop = Color("sky top", 0x0B1026),
            SkyHorizon = Color("sky horizon", 0x4A3A6A),
            Skyline = Enum("skyline", SkylineStyle.None),
            SkylineColor = Color("skyline color", 0x1A1E2A),
            SkylineLights = Color("skyline lights", 0xFFD27A),
            SkylineHeight = d.Number("skyline height", 0.6, 0.05, 1),
            WallStyle = Enum("wall style", WallStyle.Blocks),
            WallColor = Color("wall color", 0x8C8C8C),
            WallAccent = Color("wall accent", 0x4A4E58),
            WallLights = Color("wall lights", 0xFFE6A0),
            UseDrawingColors = d.Flag("use drawing colors", false),
            FloorColor = Color("floor color", 0x151A20),
            FloorGrid = grid != null && grid.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ? null : Color("floor grid", 0x2E6FA8),
            SkylineImage = LoadPicture(folder, "skyline", 720, warnings),
            WallImage = LoadPicture(folder, "wall", 64, warnings),
        };
    }

    private static SpriteImage? LoadPicture(string? folder, string name, int maxSize, List<string> warnings)
    {
        if (folder == null || ImageSprites.Find(folder, name) is not { } path) return null;
        return ImageSprites.Load(path, maxSize, new ImageSprites.Transparency(false, null), warnings);
    }
}

/// <summary>Procedural skylines and wall facades.</summary>
public static class ThemeArt
{
    private const int PanoramaWidth = 720, PanoramaHeight = 120;

    private static uint Hash(int a, int b, int c = 0)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return h;
    }

    private static int Shade(int rgb, double f) => Renderer.Shade(rgb, f);

    public static int Mix(int a, int b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        int r = (int)(((a >> 16) & 0xFF) * (1 - t) + ((b >> 16) & 0xFF) * t);
        int g = (int)(((a >> 8) & 0xFF) * (1 - t) + ((b >> 8) & 0xFF) * t);
        int bl = (int)((a & 0xFF) * (1 - t) + (b & 0xFF) * t);
        return (r << 16) | (g << 8) | bl;
    }

    /// <summary>Builds a 360-degree skyline picture (transparent above the shapes).</summary>
    public static SpriteImage Skyline(ThemeDesign t)
    {
        int w = PanoramaWidth, h = PanoramaHeight;
        var px = new int[w * h];
        var rng = new Random(t.Id.Aggregate(17, (acc, ch) => acc * 31 + ch));
        void Set(int x, int y, int rgb)
        {
            x = ((x % w) + w) % w;
            if (y >= 0 && y < h) px[y * w + x] = unchecked((int)0xFF000000) | (rgb & 0xFFFFFF);
        }
        void Rect(int x0, int y0, int x1, int y1, int rgb)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
                for (int x = x0; x <= x1; x++) Set(x, y, rgb);
        }

        switch (t.Skyline)
        {
            case SkylineStyle.City:
            case SkylineStyle.Night:
            {
                bool night = t.Skyline == SkylineStyle.Night;
                if (night)
                {
                    // Moon.
                    for (int y = -9; y <= 9; y++)
                        for (int x = -9; x <= 9; x++)
                            if (x * x + y * y <= 81) Set(560 + x, 16 + y, x * x + y * y > 49 ? 0xC8C8B8 : 0xEFEFDF);
                }
                // Two layers: far (lighter, shorter) then near (darker, taller).
                for (int layer = 0; layer < 2; layer++)
                {
                    int x = 0;
                    int baseCol = layer == 0 ? Mix(t.SkylineColor, t.SkyHorizon, 0.45) : t.SkylineColor;
                    while (x < w)
                    {
                        int bw = rng.Next(12, layer == 0 ? 30 : 42);
                        int bh = layer == 0 ? rng.Next(25, 80) : rng.Next(35, h - 8);
                        int col = Shade(baseCol, 0.85 + rng.NextDouble() * 0.3);
                        Rect(x, h - bh, x + bw - 1, h - 1, col);
                        if (rng.Next(4) == 0) Rect(x + bw / 2, h - bh - rng.Next(6, 16), x + bw / 2, h - bh - 1, col); // antenna
                        if (layer == 1 || night)
                        {
                            double litChance = night ? 0.45 : 0.18;
                            for (int wy = h - bh + 4; wy < h - 3; wy += 5)
                                for (int wx = x + 2; wx < x + bw - 2; wx += 4)
                                    if (rng.NextDouble() < litChance) Rect(wx, wy, wx + 1, wy + 1, t.SkylineLights);
                        }
                        x += bw + rng.Next(0, 4);
                    }
                }
                break;
            }

            case SkylineStyle.Industrial:
            {
                int x = 0;
                while (x < w)
                {
                    int kind = rng.Next(4);
                    if (kind == 0)
                    {
                        // Chimney with a smoke puff.
                        int ch = rng.Next(55, 105);
                        Rect(x, h - ch, x + 5, h - 1, t.SkylineColor);
                        Rect(x, h - ch, x + 5, h - ch + 2, Mix(t.SkylineColor, 0xC03020, 0.5));
                        for (int i = 0; i < 4; i++)
                        {
                            int sx = x + 3 + i * 4, sy = h - ch - 6 - i * 5, r = 3 + i;
                            for (int yy = -r; yy <= r; yy++)
                                for (int xx = -r; xx <= r; xx++)
                                    if (xx * xx + yy * yy <= r * r) Set(sx + xx, sy + yy, Mix(t.SkyHorizon, 0x9A9A9A, 0.5));
                        }
                        x += 10;
                    }
                    else if (kind == 1)
                    {
                        // Tower crane: mast and jib.
                        int ch = rng.Next(70, 110), jib = rng.Next(30, 60);
                        for (int y = h - ch; y < h; y++) { Set(x, y, t.SkylineColor); Set(x + 3, y, t.SkylineColor); if (y % 4 == 0) Rect(x, y, x + 3, y, t.SkylineColor); }
                        Rect(x - jib / 3, h - ch, x + jib, h - ch + 1, t.SkylineColor);
                        Set(x + jib - 2, h - ch + 2, t.SkylineLights);
                        x += jib + 6;
                    }
                    else
                    {
                        // Factory shed with a sawtooth roof.
                        int bw = rng.Next(30, 60), bh = rng.Next(25, 50);
                        Rect(x, h - bh, x + bw - 1, h - 1, t.SkylineColor);
                        for (int i = 0; i < bw; i++) Rect(x + i, h - bh - (i % 10), x + i, h - bh, t.SkylineColor);
                        for (int wx = x + 3; wx < x + bw - 3; wx += 6)
                            if (rng.Next(3) == 0) Rect(wx, h - bh / 2, wx + 2, h - bh / 2 + 2, t.SkylineLights);
                        x += bw + rng.Next(2, 10);
                    }
                }
                break;
            }

            case SkylineStyle.Desert:
            {
                double p1 = rng.NextDouble() * 6, p2 = rng.NextDouble() * 6;
                for (int x = 0; x < w; x++)
                {
                    double a = x * 2 * Math.PI / w;
                    int far = (int)(38 + 14 * Math.Sin(a * 3 + p1) + 8 * Math.Sin(a * 7 + p2));
                    int near = (int)(18 + 10 * Math.Sin(a * 5 + p2) + 5 * Math.Sin(a * 11 + p1));
                    Rect(x, h - far, x, h - 1, Mix(t.SkylineColor, t.SkyHorizon, 0.4));
                    Rect(x, h - near, x, h - 1, t.SkylineColor);
                }
                // Mesas: flat-topped plateaus.
                for (int i = 0; i < 5; i++)
                {
                    int mx = rng.Next(w), mw = rng.Next(25, 60), mh = rng.Next(50, 85);
                    for (int x = 0; x < mw; x++)
                    {
                        int edge = Math.Min(x, mw - 1 - x);
                        int top = h - mh + Math.Max(0, 6 - edge * 2);
                        Rect(mx + x, top, mx + x, h - 1, Shade(t.SkylineColor, 0.8));
                    }
                }
                break;
            }

            case SkylineStyle.Hell:
            {
                int y = 60;
                for (int x = 0; x < w; x++)
                {
                    y = Math.Clamp(y + rng.Next(-4, 5), 25, h - 15);
                    int top = h - y;
                    Rect(x, top, x, h - 1, t.SkylineColor);
                    if (rng.Next(9) == 0) Rect(x, top, x, top + 1, t.SkylineLights);
                    // Lava glow at the bottom.
                    for (int g = 0; g < 10; g++) Set(x, h - 1 - g, Mix(t.SkylineLights, t.SkylineColor, g / 10.0));
                }
                for (int i = 0; i < 6; i++)
                {
                    int sx = rng.Next(w), sh = rng.Next(80, 115);
                    for (int yy = 0; yy < sh; yy++) Rect(sx - (sh - yy) / 18, h - yy - 1, sx + (sh - yy) / 18, h - yy - 1, Shade(t.SkylineColor, 0.7));
                }
                break;
            }
        }
        return new SpriteImage(w, h, px);
    }

    /// <summary>Procedural facade texel for a wall style.</summary>
    public static int Wall(WallStyle style, int baseColor, int accent, int lights, double s, double v)
    {
        switch (style)
        {
            case WallStyle.Office:
            {
                // Three storeys per wall: concrete bands, then windows with frames; some windows lit.
                double fv = v * 3, fs = s / 0.34;
                int floor = (int)Math.Floor(fv), bay = (int)Math.Floor(fs);
                double lv = fv - floor, ls = fs - bay;
                if (lv < 0.22) return Shade(baseColor, 0.95 + (Hash(bay, floor) % 10) / 100.0); // spandrel band
                if (ls < 0.12 || ls > 0.88 || lv > 0.93) return accent;                            // frame
                bool lit = Hash(bay, floor, 7) % 5 == 0;
                int glass = lit ? lights : Mix(0x1A2430, baseColor, 0.25);
                return Shade(glass, lit ? 1 : 0.8 + 0.4 * (1 - lv));                                // reflection gradient
            }
            case WallStyle.Glass:
            {
                double fv = v * 5, fs = s / 0.25;
                double lv = fv - Math.Floor(fv), ls = fs - Math.Floor(fs);
                if (lv < 0.06 || ls < 0.05) return accent;
                bool lit = Hash((int)Math.Floor(fs), (int)Math.Floor(fv), 3) % 9 == 0;
                if (lit) return lights;
                return Mix(Shade(baseColor, 0.7 + 0.5 * ls), 0xBFD8F0, (1 - v) * 0.35);
            }
            case WallStyle.Brick:
            {
                double vv = v * 12;
                int course = (int)vv;
                double u = (s + (course & 1) * 0.08) / 0.16;
                int brick = (int)Math.Floor(u);
                if (vv - course < 0.14 || u - brick < 0.07) return accent;
                return Shade(baseColor, 0.85 + (Hash(brick, course) % 25) / 100.0);
            }
            case WallStyle.Corrugated:
            {
                double door = s - 3 * Math.Floor(s / 3);
                if (door > 1 && door < 2.2 && v > 0.3)
                {
                    if (door < 1.05 || door > 2.15) return accent;          // door frame
                    return Shade(Mix(baseColor, accent, 0.3), (((int)(v * 30)) & 1) == 0 ? 0.95 : 0.75); // roller slats
                }
                return Shade(baseColor, 0.8 + 0.2 * Math.Sin(s * 2 * Math.PI * 14));
            }
            case WallStyle.Sandstone:
            {
                double vv = v * 3;
                int course = (int)vv;
                double u = (s + (course & 1) * 0.35) / 0.7;
                int block = (int)Math.Floor(u);
                if (vv - course < 0.04 || u - block < 0.02) return accent;
                double grain = (Hash((int)(s * 40), (int)(v * 40)) % 12) / 100.0;
                return Shade(baseColor, 0.9 + grain + (Hash(block, course) % 10) / 100.0);
            }
            case WallStyle.Rock:
            {
                double crack = Math.Abs(Math.Sin(s * 6.3 + Math.Sin(v * 9) * 1.7) * Math.Cos(v * 4.1 + s * 1.3));
                if (crack < 0.05) return lights;
                if (crack < 0.1) return Mix(lights, baseColor, 0.6);
                double n = (Hash((int)(s * 12), (int)(v * 12)) % 30) / 100.0;
                return Shade(baseColor, 0.7 + n);
            }
            default:
            {
                // The original blocks.
                const int courses = 6;
                const double blockLength = 0.5;
                double vv = v * courses;
                int course = (int)vv;
                double offset = (course & 1) * blockLength * 0.5;
                double u = (s + offset) / blockLength;
                int block = (int)Math.Floor(u);
                if (vv - course < 0.07 || u - block < 0.035) return Shade(baseColor, 0.45);
                uint hsh = (uint)(block * 73856093) ^ (uint)(course * 19349663);
                hsh ^= hsh >> 13;
                double jitter = 0.88 + (hsh % 25) / 100.0;
                double grime = 1 - Math.Max(0, v - 0.85) * 1.5;
                return Shade(baseColor, jitter * grime);
            }
        }
    }
}
