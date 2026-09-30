using System.IO.Compression;

namespace CivDoom.Engine;

/// <summary>
/// A small, dependency-free PNG reader (non-interlaced; greyscale, RGB, palette, with or without alpha;
/// 1-16 bit). The Windows host swaps in a full decoder (System.Drawing) that also reads JPEG; this one
/// keeps the engine usable and testable anywhere.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <returns>ARGB pixels, or null if this isn't a PNG it understands.</returns>
    public static (int Width, int Height, int[] Argb)? Decode(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature)) return null;

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int len = ReadInt(data, pos);
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int body = pos + 8;
            if (len < 0 || body + len > data.Length) return null;
            switch (type)
            {
                case "IHDR":
                    width = ReadInt(data, body);
                    height = ReadInt(data, body + 4);
                    bitDepth = data[body + 8];
                    colorType = data[body + 9];
                    interlace = data[body + 12];
                    break;
                case "PLTE": palette = data[body..(body + len)]; break;
                case "tRNS": trns = data[body..(body + len)]; break;
                case "IDAT": idat.Write(data, body, len); break;
                case "IEND": pos = data.Length; continue;
            }
            pos = body + len + 4; // skip CRC
        }

        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || interlace != 0) return null;
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || bitDepth is not (1 or 2 or 4 or 8 or 16)) return null;

        byte[] raw;
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        using (var outStream = new MemoryStream())
        {
            z.CopyTo(outStream);
            raw = outStream.ToArray();
        }

        int bitsPerPixel = channels * bitDepth;
        int stride = (width * bitsPerPixel + 7) / 8;
        int bpp = Math.Max(1, bitsPerPixel / 8);
        if (raw.Length < height * (stride + 1)) return null;

        var prev = new byte[stride];
        var cur = new byte[stride];
        var argb = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
            Unfilter(filter, cur, prev, bpp);
            for (int x = 0; x < width; x++) argb[y * width + x] = Pixel(cur, x, colorType, bitDepth, palette, trns);
            (prev, cur) = (cur, prev);
        }
        return (width, height, argb);
    }

    private static int ReadInt(byte[] d, int i) => (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];

    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
            cur[i] = filter switch
            {
                1 => (byte)(cur[i] + a),
                2 => (byte)(cur[i] + b),
                3 => (byte)(cur[i] + (a + b) / 2),
                4 => (byte)(cur[i] + Paeth(a, b, c)),
                _ => cur[i],
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Reads sample <paramref name="index"/> of a row, scaled to 0-255.</summary>
    private static int Sample(byte[] row, int index, int bitDepth)
    {
        switch (bitDepth)
        {
            case 8: return row[index];
            case 16: return row[index * 2];
            default:
            {
                int bit = index * bitDepth;
                int v = (row[bit >> 3] >> (8 - bitDepth - (bit & 7))) & ((1 << bitDepth) - 1);
                return v * 255 / ((1 << bitDepth) - 1);
            }
        }
    }

    private static int RawSample(byte[] row, int index, int bitDepth)
    {
        if (bitDepth >= 8) return bitDepth == 8 ? row[index] : (row[index * 2] << 8) | row[index * 2 + 1];
        int bit = index * bitDepth;
        return (row[bit >> 3] >> (8 - bitDepth - (bit & 7))) & ((1 << bitDepth) - 1);
    }

    private static int Pixel(byte[] row, int x, int colorType, int bitDepth, byte[]? palette, byte[]? trns)
    {
        int r, g, b, a = 255;
        switch (colorType)
        {
            case 0:
                r = g = b = Sample(row, x, bitDepth);
                if (trns is { Length: >= 2 } && RawSample(row, x, bitDepth) == ((trns[0] << 8) | trns[1])) a = 0;
                break;
            case 2:
                r = Sample(row, x * 3, bitDepth); g = Sample(row, x * 3 + 1, bitDepth); b = Sample(row, x * 3 + 2, bitDepth);
                if (trns is { Length: >= 6 }
                    && RawSample(row, x * 3, bitDepth) == ((trns[0] << 8) | trns[1])
                    && RawSample(row, x * 3 + 1, bitDepth) == ((trns[2] << 8) | trns[3])
                    && RawSample(row, x * 3 + 2, bitDepth) == ((trns[4] << 8) | trns[5])) a = 0;
                break;
            case 3:
            {
                int i = RawSample(row, x, bitDepth);
                if (palette == null || i * 3 + 2 >= palette.Length) return 0;
                r = palette[i * 3]; g = palette[i * 3 + 1]; b = palette[i * 3 + 2];
                if (trns != null && i < trns.Length) a = trns[i];
                break;
            }
            case 4:
                r = g = b = Sample(row, x * 2, bitDepth); a = Sample(row, x * 2 + 1, bitDepth);
                break;
            default:
                r = Sample(row, x * 4, bitDepth); g = Sample(row, x * 4 + 1, bitDepth);
                b = Sample(row, x * 4 + 2, bitDepth); a = Sample(row, x * 4 + 3, bitDepth);
                break;
        }
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
