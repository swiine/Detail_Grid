using System.IO.Compression;
using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class ImageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "civdoom-img-" + Guid.NewGuid().ToString("N"));

    public ImageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Writes an RGBA PNG, cycling through all five row filters to exercise the decoder.</summary>
    internal static byte[] EncodePng(int w, int h, Func<int, int, int> argbAt)
    {
        var raw = new MemoryStream();
        byte[] prev = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            byte[] row = new byte[w * 4];
            for (int x = 0; x < w; x++)
            {
                int c = argbAt(x, y);
                row[x * 4] = (byte)(c >> 16); row[x * 4 + 1] = (byte)(c >> 8); row[x * 4 + 2] = (byte)c; row[x * 4 + 3] = (byte)(c >>> 24);
            }
            int filter = y % 5;
            raw.WriteByte((byte)filter);
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= 4 ? row[i - 4] : 0, b = prev[i], c = i >= 4 ? prev[i - 4] : 0;
                int pred = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                raw.WriteByte((byte)(row[i] - pred));
            }
            prev = row;
        }

        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(zs);

        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] body)
        {
            png.Write(BE(body.Length));
            png.Write(System.Text.Encoding.ASCII.GetBytes(type));
            png.Write(body);
            png.Write(new byte[4]); // CRC isn't checked by the decoder
        }
        byte[] ihdr = BE(w).Concat(BE(h)).Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray();
        Chunk("IHDR", ihdr);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", Array.Empty<byte>());
        return png.ToArray();

        static byte[] BE(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static int Checker(int x, int y) => unchecked((int)0xFF000000) | (x * 20 << 16) | (y * 25 << 8) | ((x ^ y) * 7 & 0xFF);

    [Fact]
    public void PngDecoderRoundTripsEveryFilter()
    {
        byte[] png = EncodePng(9, 10, (x, y) => (x + y) % 3 == 0 ? 0 : Checker(x, y));
        var img = PngDecoder.Decode(png);
        Assert.NotNull(img);
        Assert.Equal(9, img!.Value.Width);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 9; x++)
                Assert.Equal((x + y) % 3 == 0 ? 0 : Checker(x, y), img.Value.Argb[y * 9 + x]);
    }

    [Fact]
    public void JpegStyleBackgroundIsRemovedButInsideColoursSurvive()
    {
        // White background, a red square with a white "window" fully inside it.
        int w = 20, h = 20;
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool body = x >= 5 && x < 15 && y >= 4 && y < 20;
                bool window = x >= 8 && x < 12 && y >= 8 && y < 12;
                px[y * w + x] = unchecked((int)0xFF000000) | (body && !window ? 0xC02020 : 0xFFFFFF);
            }
        SpriteImage s = ImageSprites.FromPixels(w, h, px, 64, ImageSprites.Transparency.Default)!;
        Assert.Equal(10, s.Width);  // trimmed to the red body
        Assert.Equal(16, s.Height);
        Assert.NotEqual(0, s[4, 5]); // the white window inside the body is kept
    }

    [Fact]
    public void BigPicturesAreShrunk()
    {
        var px = Enumerable.Repeat(unchecked((int)0xFF3060A0), 400 * 300).ToArray();
        px[0] = 0; // has alpha, so no background keying
        SpriteImage s = ImageSprites.FromPixels(400, 300, px, 48, ImageSprites.Transparency.Default)!;
        Assert.Equal(48, s.Width);
        Assert.Equal(36, s.Height);
    }

    [Fact]
    public void PngNextToTheFileReplacesTheArt()
    {
        File.WriteAllText(Path.Combine(_dir, "blob.txt"), "name = Blob\nsize = 0.5\n");
        File.WriteAllBytes(Path.Combine(_dir, "blob.png"), EncodePng(12, 16, (x, y) => x < 2 ? 0 : Checker(x, y)));
        File.WriteAllBytes(Path.Combine(_dir, "blob_dead.png"), EncodePng(12, 4, Checker));

        MonsterSet set = MonsterSet.Load(_dir);
        MonsterDesign d = set.Find("blob")!;
        Assert.Empty(set.Warnings);
        Assert.Equal(10, d.Idle.Width);   // two transparent columns trimmed
        Assert.Equal(16, d.Idle.Height);
        Assert.Equal(d.Idle[0, 0], d.Walk[9, 0]); // walk = mirrored idle
        Assert.Equal(4, d.Dead.Height);           // its own dead picture
        Assert.NotEqual(d.Idle[0, 0], d.Attack[0, 0]); // attack = tinted
    }

    [Fact]
    public void WeaponPicturesWork()
    {
        File.WriteAllText(Path.Combine(_dir, "ray.txt"), "name = Ray Gun\nslot = 7\n");
        File.WriteAllBytes(Path.Combine(_dir, "ray-pickup.png"), EncodePng(8, 4, Checker));
        File.WriteAllBytes(Path.Combine(_dir, "ray.jpg"), EncodePng(10, 10, Checker)); // PNG bytes, .jpg name: decoder sniffs content
        WeaponSet set = WeaponSet.Load(_dir);
        WeaponDesign w = set.Find("ray")!;
        Assert.Equal(10, w.Hand.Width);
        Assert.Equal(4, w.Pickup.Height);
        Assert.Same(w.Hand, w.Fire);
    }

    [Fact]
    public void UnreadablePictureIsAWarningNotACrash()
    {
        File.WriteAllText(Path.Combine(_dir, "imp.txt"), MonsterSet.DefaultText("imp"));
        File.WriteAllText(Path.Combine(_dir, "imp.png"), "not really a png");
        MonsterSet set = MonsterSet.Load(_dir);
        Assert.Contains(set.Warnings, w => w.Contains("imp.png"));
        Assert.Equal(16, set.Find("imp")!.Idle.Width); // falls back to the character art
    }

    [Fact]
    public void SimplifiedKeepsDetailedPicturesCheap()
    {
        var rng = new Random(1);
        var px = Enumerable.Range(0, 64 * 64).Select(_ => unchecked((int)0xFF000000) | rng.Next(0x1000000)).ToArray();
        var noisy = new SpriteImage(64, 64, px);
        Assert.True(noisy.Rectangles().Count > 1000);
        SpriteImage simple = noisy.Simplified(250);
        Assert.True(simple.Rectangles().Count <= 250, $"{simple.Rectangles().Count} rects");
        Assert.Same(Art.MedkitSprite, Art.MedkitSprite.Simplified(250));
    }

    [Fact]
    public void FloatersHover()
    {
        Assert.True(MonsterSet.BuiltIn.Find("cacodemon")!.FloatHeight > 0);
        Assert.Equal(0, MonsterSet.BuiltIn.Find("imp")!.FloatHeight);
    }
}
