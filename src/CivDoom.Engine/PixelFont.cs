namespace CivDoom.Engine;

/// <summary>A 5x7 bitmap font for the HUD (upper case, digits and common symbols).</summary>
public static class PixelFont
{
    public const int GlyphWidth = 5, GlyphHeight = 7, Advance = 6;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
        ['C'] = new[] { " ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### " },
        ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
        ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
        ['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
        ['G'] = new[] { " ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####" },
        ['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['I'] = new[] { " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['J'] = new[] { "  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " },
        ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
        ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
        ['M'] = new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" },
        ['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
        ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
        ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
        ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
        ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
        ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
        ['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "# # #", " # # " },
        ['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
        ['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
        ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
        ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
        ['3'] = new[] { "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### " },
        ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
        ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
        ['6'] = new[] { "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### " },
        ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
        ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
        ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  " },
        [' '] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "     " },
        ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  " },
        [','] = new[] { "     ", "     ", "     ", "     ", " ##  ", "  #  ", " #   " },
        [':'] = new[] { "     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     " },
        ['!'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "     ", "  #  " },
        ['?'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", "     ", "  #  " },
        ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
        ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
        ['/'] = new[] { "     ", "    #", "   # ", "  #  ", " #   ", "#    ", "     " },
        ['%'] = new[] { "##   ", "##  #", "   # ", "  #  ", " #   ", "#  ##", "   ##" },
        ['('] = new[] { "   # ", "  #  ", " #   ", " #   ", " #   ", "  #  ", "   # " },
        [')'] = new[] { " #   ", "  #  ", "   # ", "   # ", "   # ", "  #  ", " #   " },
        ['\''] = new[] { "  #  ", "  #  ", " #   ", "     ", "     ", "     ", "     " },
        ['"'] = new[] { " # # ", " # # ", "     ", "     ", "     ", "     ", "     " },
        ['['] = new[] { " ### ", " #   ", " #   ", " #   ", " #   ", " #   ", " ### " },
        [']'] = new[] { " ### ", "   # ", "   # ", "   # ", "   # ", "   # ", " ### " },
        ['<'] = new[] { "   # ", "  #  ", " #   ", "#    ", " #   ", "  #  ", "   # " },
        ['>'] = new[] { " #   ", "  #  ", "   # ", "    #", "   # ", "  #  ", " #   " },
        ['='] = new[] { "     ", "     ", "#####", "     ", "#####", "     ", "     " },
        ['|'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['*'] = new[] { "     ", "# # #", " ### ", "#####", " ### ", "# # #", "     " },
        ['_'] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "#####" },
        ['#'] = new[] { " # # ", "#####", " # # ", " # # ", " # # ", "#####", " # # " },
        ['&'] = new[] { " ##  ", "#  # ", "#  # ", " ##  ", "# # #", "#  # ", " ## #" },
    };

    private static readonly Dictionary<char, bool[,]> Bits = Glyphs.ToDictionary(kv => kv.Key, kv => ToBits(kv.Value));

    private static bool[,] ToBits(string[] rows)
    {
        var b = new bool[GlyphWidth, GlyphHeight];
        for (int y = 0; y < GlyphHeight; y++)
            for (int x = 0; x < GlyphWidth; x++)
                b[x, y] = rows[y][x] == '#';
        return b;
    }

    public static IReadOnlyCollection<char> Characters => Glyphs.Keys;

    internal static IEnumerable<string[]> AllGlyphRows => Glyphs.Values;

    public static bool[,] Glyph(char c)
    {
        c = char.ToUpperInvariant(c);
        return Bits.TryGetValue(c, out bool[,]? g) ? g : Bits['?'];
    }

    public static int MeasureWidth(string text, int scale = 1) =>
        text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;
}

/// <summary>Simple drawing onto a 32-bit framebuffer, for the HUD.</summary>
public sealed class Canvas
{
    public Canvas(int[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public int[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }

    private static int Opaque(int rgb) => unchecked((int)0xFF000000) | (rgb & 0xFFFFFF);

    public void Set(int x, int y, int rgb)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height) Pixels[y * Width + x] = Opaque(rgb);
    }

    /// <summary>Mixes <paramref name="rgb"/> over the pixel by <paramref name="alpha"/> (0..1).</summary>
    public void Blend(int x, int y, int rgb, double alpha)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || alpha <= 0) return;
        if (alpha >= 1) { Pixels[y * Width + x] = Opaque(rgb); return; }
        Pixels[y * Width + x] = Opaque(ThemeArt.Mix(Pixels[y * Width + x] & 0xFFFFFF, rgb, alpha));
    }

    public int Get(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Pixels[y * Width + x] & 0xFFFFFF : 0;

    public void Fill(int x0, int y0, int w, int h, int rgb, double alpha = 1)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(Height, y0 + h); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(Width, x0 + w); x++)
                Blend(x, y, rgb, alpha);
    }

    /// <summary>A raised/sunken panel: fill plus light and dark edges.</summary>
    public void Bevel(int x0, int y0, int w, int h, int rgb, bool sunken = false)
    {
        Fill(x0, y0, w, h, rgb);
        int light = Renderer.Shade(rgb, 1.45), dark = Renderer.Shade(rgb, 0.5);
        int tl = sunken ? dark : light, br = sunken ? light : dark;
        for (int x = x0; x < x0 + w; x++) { Set(x, y0, tl); Set(x, y0 + h - 1, br); }
        for (int y = y0; y < y0 + h; y++) { Set(x0, y, tl); Set(x0 + w - 1, y, br); }
    }

    public void Line(int x0, int y0, int x1, int y1, int rgb, double alpha = 1, Func<int, int, bool>? clip = null)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int guard = 0; guard < 4096; guard++)
        {
            if (clip == null || clip(x0, y0)) Blend(x0, y0, rgb, alpha);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public void Disc(int cx, int cy, double r, int rgb, double alpha = 1)
    {
        int ri = (int)Math.Ceiling(r);
        for (int y = -ri; y <= ri; y++)
            for (int x = -ri; x <= ri; x++)
                if (x * x + y * y <= r * r) Blend(cx + x, cy + y, rgb, alpha);
    }

    /// <summary>Draws text in the pixel font. Returns the width drawn.</summary>
    public int Text(string text, int x, int y, int rgb, int scale = 1, double alpha = 1, int? shadow = 0x000000)
    {
        if (shadow is { } sh) DrawText(text, x + scale, y + scale, sh, scale, alpha * 0.85, null);
        DrawText(text, x, y, rgb, scale, alpha, null);
        return PixelFont.MeasureWidth(text, scale);
    }

    /// <summary>Text with a vertical colour gradient and a dark outline: the big status-bar numbers.</summary>
    public void BigText(string text, int x, int y, int top, int bottom, int scale = 3)
    {
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if (ox != 0 || oy != 0) DrawText(text, x + ox, y + oy, 0x100808, scale, 1, null);
        DrawText(text, x, y, 0, scale, 1, (gy) => ThemeArt.Mix(top, bottom, gy / (double)(PixelFont.GlyphHeight * scale)));
    }

    public void TextCentered(string text, int cx, int y, int rgb, int scale = 1, double alpha = 1) =>
        Text(text, cx - PixelFont.MeasureWidth(text, scale) / 2, y, rgb, scale, alpha);

    private void DrawText(string text, int x, int y, int rgb, int scale, double alpha, Func<int, int>? gradient)
    {
        int cx = x;
        foreach (char ch in text)
        {
            bool[,] g = PixelFont.Glyph(ch);
            for (int gy = 0; gy < PixelFont.GlyphHeight; gy++)
                for (int gx = 0; gx < PixelFont.GlyphWidth; gx++)
                {
                    if (!g[gx, gy]) continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int py = gy * scale + sy;
                            Blend(cx + gx * scale + sx, y + py, gradient?.Invoke(py) ?? rgb, alpha);
                        }
                }
            cx += PixelFont.Advance * scale;
        }
    }

    /// <summary>Copies a sprite onto the canvas at integer scale.</summary>
    public void Sprite(SpriteImage img, int left, int top, int scale = 1, double alpha = 1)
    {
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int c = img[x, y];
                if ((c >>> 24) == 0) continue;
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                        Blend(left + x * scale + sx, top + y * scale + sy, c & 0xFFFFFF, alpha);
            }
    }
}
