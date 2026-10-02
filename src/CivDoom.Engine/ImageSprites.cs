namespace CivDoom.Engine;

/// <summary>
/// Loads PNG/JPEG pictures that sit next to a design file (e.g. proxy.png beside proxy.txt) and turns them
/// into sprites: background removed, empty borders trimmed, scaled down to a sprite-sized picture.
/// </summary>
public static class ImageSprites
{
    public const int DefaultMaxSize = 64;

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>
    /// Turns image file bytes into ARGB pixels. The engine's default reads PNG only; the Windows host
    /// installs a System.Drawing decoder that also handles JPEG, BMP and GIF.
    /// </summary>
    public static Func<byte[], (int Width, int Height, int[] Argb)?> Decoder { get; set; } = PngDecoder.Decode;

    /// <summary>Finds "&lt;name&gt;.png/.jpg/.jpeg" (also accepting "imp-walk" for "imp_walk") in <paramref name="folder"/>.</summary>
    public static string? Find(string folder, string name)
    {
        foreach (string n in new[] { name, name.Replace('_', '-'), name.Replace('_', ' ') }.Distinct())
            foreach (string ext in Extensions)
            {
                string path = Path.Combine(folder, n + ext);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    /// <summary>How to decide which pixels are background.</summary>
    /// <param name="Auto">Use the image's own transparency; if it has none (e.g. a JPEG), remove the colour around its edges.</param>
    /// <param name="Color">Remove this 0xRRGGBB colour (when not auto).</param>
    public readonly record struct Transparency(bool Auto, int? Color)
    {
        public static readonly Transparency Default = new(true, null);
    }

    /// <returns>The sprite, or null (with a warning) if the file can't be read.</returns>
    public static SpriteImage? Load(string path, int maxSize, Transparency transparency, List<string> warnings)
    {
        (int Width, int Height, int[] Argb)? decoded;
        try
        {
            decoded = Decoder(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
        if (decoded is not { } img)
        {
            warnings.Add($"{Path.GetFileName(path)}: couldn't read this picture");
            return null;
        }
        return FromPixels(img.Width, img.Height, img.Argb, maxSize, transparency);
    }

    /// <summary>The processing pipeline, separated from file loading for testing.</summary>
    public static SpriteImage? FromPixels(int w, int h, int[] argb, int maxSize, Transparency transparency)
    {
        int[] px = (int[])argb.Clone();
        bool hasAlpha = px.Any(p => (p >>> 24) < 250);
        if (transparency.Color is { } key) RemoveColor(px, key, 40);
        else if (transparency.Auto && !hasAlpha) FloodRemoveBackground(px, w, h);

        for (int i = 0; i < px.Length; i++)
            px[i] = (px[i] >>> 24) >= 128 ? unchecked((int)0xFF000000) | (px[i] & 0xFFFFFF) : 0;

        // Trim empty borders so the picture's feet touch the floor.
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x] != 0) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        if (x1 < 0) return null;
        int tw = x1 - x0 + 1, th = y1 - y0 + 1;
        var trimmed = new int[tw * th];
        for (int y = 0; y < th; y++) Array.Copy(px, (y + y0) * w + x0, trimmed, y * tw, tw);

        var sprite = new SpriteImage(tw, th, trimmed);
        maxSize = Math.Clamp(maxSize, 4, DesignText.MaxPictureSize);
        return Math.Max(tw, th) > maxSize ? Resize(sprite, maxSize) : sprite;
    }

    private static int Diff(int a, int b) =>
        Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) + Math.Abs((a & 0xFF) - (b & 0xFF));

    private static void RemoveColor(int[] px, int rgb, int tolerance)
    {
        for (int i = 0; i < px.Length; i++)
            if (Diff(px[i], rgb) <= tolerance) px[i] = 0;
    }

    /// <summary>Removes the background colour connected to the image's edges (so matching colours inside the figure survive).</summary>
    private static void FloodRemoveBackground(int[] px, int w, int h)
    {
        int bg = px[0] & 0xFFFFFF;
        var seen = new bool[px.Length];
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (seen[i]) continue;
            seen[i] = true;
            if (Diff(px[i], bg) > 60) continue;
            px[i] = 0;
            int x = i % w, y = i / w;
            if (x > 0) stack.Push(i - 1);
            if (x < w - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - w);
            if (y < h - 1) stack.Push(i + w);
        }
    }

    /// <summary>Box-filter downscale keeping the aspect ratio. A cell is opaque if at least half of it was.</summary>
    public static SpriteImage Resize(SpriteImage src, int maxSize)
    {
        double scale = (double)maxSize / Math.Max(src.Width, src.Height);
        int w = Math.Max(1, (int)Math.Round(src.Width * scale)), h = Math.Max(1, (int)Math.Round(src.Height * scale));
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy0 = y * src.Height / h, sy1 = Math.Max(sy0 + 1, (y + 1) * src.Height / h);
            for (int x = 0; x < w; x++)
            {
                int sx0 = x * src.Width / w, sx1 = Math.Max(sx0 + 1, (x + 1) * src.Width / w);
                long r = 0, g = 0, b = 0;
                int opaque = 0, total = 0;
                for (int sy = sy0; sy < sy1; sy++)
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        total++;
                        int c = src[sx, sy];
                        if ((c >>> 24) == 0) continue;
                        opaque++;
                        r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF;
                    }
                if (opaque * 2 >= total && opaque > 0)
                    px[y * w + x] = unchecked((int)0xFF000000) | (int)((r / opaque) << 16 | (g / opaque) << 8 | (b / opaque));
            }
        }
        return new SpriteImage(w, h, px);
    }

    public static SpriteImage Mirror(SpriteImage s)
    {
        var px = new int[s.Pixels.Length];
        for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
                px[y * s.Width + x] = s[s.Width - 1 - x, y];
        return new SpriteImage(s.Width, s.Height, px);
    }

    /// <summary>Blends every opaque pixel toward <paramref name="rgb"/>.</summary>
    public static SpriteImage Tint(SpriteImage s, int rgb, double amount)
    {
        var px = new int[s.Pixels.Length];
        for (int i = 0; i < px.Length; i++)
        {
            int c = s.Pixels[i];
            if ((c >>> 24) == 0) continue;
            int r = (int)(((c >> 16) & 0xFF) * (1 - amount) + ((rgb >> 16) & 0xFF) * amount);
            int g = (int)(((c >> 8) & 0xFF) * (1 - amount) + ((rgb >> 8) & 0xFF) * amount);
            int b = (int)((c & 0xFF) * (1 - amount) + (rgb & 0xFF) * amount);
            px[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
        return new SpriteImage(s.Width, s.Height, px);
    }

    /// <summary>A "fallen over" version: squashed to a fraction of the height and darkened.</summary>
    public static SpriteImage Squash(SpriteImage s, double heightFraction = 0.3)
    {
        int h = Math.Max(1, (int)Math.Round(s.Height * heightFraction));
        var px = new int[s.Width * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(s.Height - 1, y * s.Height / h);
            for (int x = 0; x < s.Width; x++) px[y * s.Width + x] = s[x, sy];
        }
        return Tint(new SpriteImage(s.Width, h, px), 0x300000, 0.45);
    }
}
