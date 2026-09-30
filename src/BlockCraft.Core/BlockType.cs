namespace BlockCraft.Core;

/// <summary>Every block the world can contain. Stored as one byte per voxel.</summary>
public enum BlockType : byte
{
    Air = 0,
    Grass,
    Dirt,
    Stone,
    Sand,
    Gravel,
    Water,
    Log,
    Leaves,
    Planks,
    Brick,
    Glass,
    Snow,
    Asphalt,
    Concrete,
    Bedrock,
}

/// <summary>Static per-block properties used by physics, rendering and CAD export.</summary>
public readonly record struct BlockInfo(
    string Name,
    bool Solid,
    bool Translucent,
    int TopColor,
    int SideColor,
    int BottomColor,
    short AciColor,
    bool Breakable = true);

public static class Blocks
{
    public const int Count = 16;

    private static readonly BlockInfo[] Infos = new BlockInfo[Count];

    static Blocks()
    {
        Set(BlockType.Air, new("Air", false, true, 0, 0, 0, 0));
        Set(BlockType.Grass, new("Grass", true, false, 0x5DA130, 0x866043, 0x866043, 3));
        Set(BlockType.Dirt, new("Dirt", true, false, 0x866043, 0x866043, 0x866043, 32));
        Set(BlockType.Stone, new("Stone", true, false, 0x7D7D7D, 0x7D7D7D, 0x7D7D7D, 8));
        Set(BlockType.Sand, new("Sand", true, false, 0xDBD3A0, 0xDBD3A0, 0xDBD3A0, 51));
        Set(BlockType.Gravel, new("Gravel", true, false, 0x857F7B, 0x857F7B, 0x857F7B, 9));
        Set(BlockType.Water, new("Water", false, true, 0x2F5AD8, 0x2F5AD8, 0x2F5AD8, 5));
        Set(BlockType.Log, new("Log", true, false, 0xA0824F, 0x66502F, 0xA0824F, 34));
        Set(BlockType.Leaves, new("Leaves", true, false, 0x3A7A1E, 0x3A7A1E, 0x3A7A1E, 94));
        Set(BlockType.Planks, new("Planks", true, false, 0xB08D57, 0xB08D57, 0xB08D57, 42));
        Set(BlockType.Brick, new("Brick", true, false, 0x96503C, 0x96503C, 0x96503C, 14));
        Set(BlockType.Glass, new("Glass", true, true, 0xC8E6F0, 0xC8E6F0, 0xC8E6F0, 131));
        Set(BlockType.Snow, new("Snow", true, false, 0xF4F8FA, 0xE8ECEE, 0x866043, 255));
        Set(BlockType.Asphalt, new("Asphalt", true, false, 0x38383C, 0x38383C, 0x38383C, 250));
        Set(BlockType.Concrete, new("Concrete", true, false, 0xB4B4AF, 0xB4B4AF, 0xB4B4AF, 254));
        Set(BlockType.Bedrock, new("Bedrock", true, false, 0x303030, 0x303030, 0x303030, 7, Breakable: false));
    }

    private static void Set(BlockType type, BlockInfo info) => Infos[(int)type] = info;

    public static BlockInfo Info(BlockType type) => Infos[(int)type];

    public static bool IsSolid(BlockType type) => Infos[(int)type].Solid;

    public static bool IsTranslucent(BlockType type) => Infos[(int)type].Translucent;

    /// <summary>Blocks offered on the hotbar, in slot order (keys 1-9, then scroll).</summary>
    public static readonly BlockType[] Placeable =
    {
        BlockType.Grass, BlockType.Dirt, BlockType.Stone, BlockType.Planks, BlockType.Log,
        BlockType.Brick, BlockType.Glass, BlockType.Concrete, BlockType.Asphalt,
        BlockType.Sand, BlockType.Gravel, BlockType.Leaves, BlockType.Snow, BlockType.Water,
    };
}
