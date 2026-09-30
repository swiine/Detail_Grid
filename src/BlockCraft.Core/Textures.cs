namespace BlockCraft.Core;

/// <summary>Procedurally generated 16x16 block textures (top, side and bottom per block), ARGB.</summary>
public static class Textures
{
    public const int Size = 16;
    public const int Top = 0, Side = 1, Bottom = 2;

    private static readonly int[][] Data = Build();

    /// <summary>Texel colour (0xRRGGBB) for a block face at texture coordinate (u, v) in [0, 1).</summary>
    public static int Sample(BlockType block, int face, float u, float v)
    {
        int tx = Math.Clamp((int)(u * Size), 0, Size - 1);
        int ty = Math.Clamp((int)(v * Size), 0, Size - 1);
        return Data[(int)block * 3 + face][ty * Size + tx];
    }

    private static int[][] Build()
    {
        var data = new int[Blocks.Count * 3][];
        var noise = new Noise(4242);
        for (int b = 0; b < Blocks.Count; b++)
        {
            var type = (BlockType)b;
            var info = Blocks.Info(type);
            for (int face = 0; face < 3; face++)
            {
                int baseColor = face switch { Top => info.TopColor, Side => info.SideColor, _ => info.BottomColor };
                var tex = new int[Size * Size];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = noise.Cell(x + b * 97 + face * 31, y + b * 53);
                    tex[y * Size + x] = Texel(type, face, x, y, r, baseColor);
                }
                data[b * 3 + face] = tex;
            }
        }
        return data;
    }

    private static int Texel(BlockType type, int face, int x, int y, float r, int color)
    {
        float shade = 0.86f + r * 0.24f;
        switch (type)
        {
            case BlockType.Grass when face == Side:
                // Green fringe hanging over the dirt side.
                int fringe = 3 + (int)(r * 3);
                return Mul(y < fringe ? Blocks.Info(BlockType.Grass).TopColor : color, shade);

            case BlockType.Snow when face == Side:
                return Mul(y < 3 + (int)(r * 2) ? Blocks.Info(BlockType.Snow).TopColor : Blocks.Info(BlockType.Dirt).SideColor, shade);

            case BlockType.Log when face == Side:
                return Mul(color, (x % 4 == 0 ? 0.75f : 1f) * shade);

            case BlockType.Log:
                int dx = x - 8, dy = y - 8;
                int ring = (int)MathF.Sqrt(dx * dx + dy * dy);
                return ring >= 7 ? Mul(Blocks.Info(BlockType.Log).SideColor, shade)
                                 : Mul(color, (ring % 2 == 0 ? 0.85f : 1f) * shade);

            case BlockType.Planks:
                bool seam = y % 4 == 3 || (x == ((y / 4) % 2 == 0 ? 3 : 11));
                return Mul(color, (seam ? 0.7f : 1f) * (0.92f + r * 0.1f));

            case BlockType.Brick:
                int row = y / 4;
                bool mortar = y % 4 == 3 || (x + (row % 2) * 4) % 8 == 7;
                return mortar ? Mul(0xC8C0B4, 0.9f + r * 0.1f) : Mul(color, shade);

            case BlockType.Glass:
                bool frame = x == 0 || y == 0 || x == Size - 1 || y == Size - 1 || (x == y && x > 3 && x < 7);
                return frame ? 0xF0FAFF : color;

            case BlockType.Leaves:
                return Mul(color, r < 0.25f ? 0.6f : shade);

            case BlockType.Concrete:
                return Mul(color, 0.95f + r * 0.08f);

            case BlockType.Asphalt:
                return Mul(color, r > 0.9f ? 1.5f : 0.9f + r * 0.15f);

            case BlockType.Water:
                return Mul(color, 0.9f + 0.15f * MathF.Sin((x + y * 2) * 0.8f));

            case BlockType.Stone:
            case BlockType.Gravel:
            case BlockType.Bedrock:
                return Mul(color, 0.7f + r * 0.5f);

            default:
                return Mul(color, shade);
        }
    }

    public static int Mul(int rgb, float k)
    {
        int r = Math.Clamp((int)(((rgb >> 16) & 0xFF) * k), 0, 255);
        int g = Math.Clamp((int)(((rgb >> 8) & 0xFF) * k), 0, 255);
        int b = Math.Clamp((int)((rgb & 0xFF) * k), 0, 255);
        return (r << 16) | (g << 8) | b;
    }

    public static int Lerp(int a, int b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        int ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
        int br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
        return ((int)(ar + (br - ar) * t) << 16) | ((int)(ag + (bg - ag) * t) << 8) | (int)(ab + (bb - ab) * t);
    }
}
