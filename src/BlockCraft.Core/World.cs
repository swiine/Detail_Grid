using System.IO.Compression;

namespace BlockCraft.Core;

/// <summary>
/// A fixed-size voxel grid. Game axes: X east, Y up, Z north (Y is up, as in Minecraft).
/// Tracks which voxels the player changed so those changes can be written back to the drawing.
/// </summary>
public sealed class World
{
    private readonly BlockType[] _blocks;
    private readonly Dictionary<(int X, int Y, int Z), BlockType> _edits = new();

    public World(int sizeX, int sizeY, int sizeZ, WorldMapping? mapping = null)
    {
        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeX), "World dimensions must be positive.");

        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _blocks = new BlockType[(long)sizeX * sizeY * sizeZ];
        Mapping = mapping ?? WorldMapping.Identity;
    }

    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }

    /// <summary>Links voxel coordinates to drawing coordinates.</summary>
    public WorldMapping Mapping { get; }

    /// <summary>Human readable origin of the world, e.g. the Civil 3D surface name.</summary>
    public string Source { get; set; } = "Procedural";

    /// <summary>Voxels changed by the player since the world was generated (Air = broken).</summary>
    public IReadOnlyDictionary<(int X, int Y, int Z), BlockType> Edits => _edits;

    public bool InBounds(int x, int y, int z) =>
        (uint)x < (uint)SizeX && (uint)y < (uint)SizeY && (uint)z < (uint)SizeZ;

    private int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;

    /// <summary>Returns the block at the voxel, or Air outside the world.</summary>
    public BlockType Get(int x, int y, int z) =>
        InBounds(x, y, z) ? _blocks[Index(x, y, z)] : BlockType.Air;

    /// <summary>Sets a block during generation (not recorded as a player edit).</summary>
    public void Set(int x, int y, int z, BlockType type)
    {
        if (InBounds(x, y, z))
            _blocks[Index(x, y, z)] = type;
    }

    /// <summary>Sets a block as a player action, recording it for export.</summary>
    public bool Edit(int x, int y, int z, BlockType type)
    {
        if (!InBounds(x, y, z))
            return false;

        _blocks[Index(x, y, z)] = type;
        _edits[(x, y, z)] = type;
        return true;
    }

    public bool IsSolid(int x, int y, int z) => Blocks.IsSolid(Get(x, y, z));

    /// <summary>Y of the highest non-air block in the column, or -1 if the column is empty.</summary>
    public int HighestBlock(int x, int z)
    {
        for (int y = SizeY - 1; y >= 0; y--)
        {
            if (Get(x, y, z) != BlockType.Air)
                return y;
        }
        return -1;
    }

    /// <summary>Forgets recorded player edits (e.g. once they have been written to the drawing).</summary>
    public void ClearEdits() => _edits.Clear();

    /// <summary>
    /// True if the voxel should be drawn as geometry: it is solid and at least one face can be seen.
    /// Only these "shell" voxels become entities in the drawing; buried voxels stay as data.
    /// Water is drawn only at its surface, the world's outer walls and floor are not drawn, and
    /// the bedrock-only floor outside a surface boundary is skipped.
    /// </summary>
    public bool IsDrawn(int x, int y, int z)
    {
        BlockType b = Get(x, y, z);
        if (b == BlockType.Air)
            return false;
        if (b == BlockType.Water)
            return Get(x, y + 1, z) == BlockType.Air;
        if (b == BlockType.Bedrock && y == 0 && Get(x, 1, z) == BlockType.Air)
            return false;

        return Reveals(x + 1, y, z) || Reveals(x - 1, y, z)
            || Reveals(x, y + 1, z) || Reveals(x, y - 1, z)
            || Reveals(x, y, z + 1) || Reveals(x, y, z - 1);
    }

    /// <summary>Whether a neighbouring voxel lets you see through to the voxel next to it.</summary>
    private bool Reveals(int x, int y, int z)
    {
        if (y >= SizeY) return true;             // open sky
        if (!InBounds(x, y, z)) return false;    // world walls and floor are never seen
        BlockType b = Get(x, y, z);
        return b is BlockType.Air or BlockType.Water or BlockType.Glass;
    }

    /// <summary>All voxels for which <see cref="IsDrawn"/> is true.</summary>
    public IEnumerable<(int X, int Y, int Z)> DrawnVoxels()
    {
        for (int y = 0; y < SizeY; y++)
        for (int z = 0; z < SizeZ; z++)
        for (int x = 0; x < SizeX; x++)
        {
            if (IsDrawn(x, y, z))
                yield return (x, y, z);
        }
    }

    private const int FormatVersion = 1;

    /// <summary>Compact, compressed snapshot of the world (sizes, mapping, source and every voxel).</summary>
    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)))
        {
            w.Write(FormatVersion);
            w.Write(SizeX); w.Write(SizeY); w.Write(SizeZ);
            w.Write(Mapping.OriginX); w.Write(Mapping.OriginY); w.Write(Mapping.BaseElevation);
            w.Write(Mapping.CellSize); w.Write(Mapping.CellHeight);
            w.Write(Source);
            var raw = new byte[_blocks.Length];
            for (int i = 0; i < raw.Length; i++) raw[i] = (byte)_blocks[i];
            w.Write(raw);
        }
        return ms.ToArray();
    }

    public static World Deserialize(byte[] data)
    {
        using var r = new BinaryReader(new DeflateStream(new MemoryStream(data), CompressionMode.Decompress));
        int version = r.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported BlockCraft world format {version}.");

        int sx = r.ReadInt32(), sy = r.ReadInt32(), sz = r.ReadInt32();
        var mapping = new WorldMapping(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
        var world = new World(sx, sy, sz, mapping) { Source = r.ReadString() };
        byte[] raw = r.ReadBytes(world._blocks.Length);
        if (raw.Length != world._blocks.Length)
            throw new InvalidDataException("BlockCraft world data is truncated.");
        for (int i = 0; i < raw.Length; i++)
            world._blocks[i] = raw[i] < Blocks.Count ? (BlockType)raw[i] : BlockType.Stone;
        return world;
    }

    /// <summary>Voxels the player placed that are still present.</summary>
    public IEnumerable<(int X, int Y, int Z, BlockType Type)> PlacedBlocks() =>
        _edits.Where(e => e.Value != BlockType.Air)
              .Select(e => (e.Key.X, e.Key.Y, e.Key.Z, e.Value));
}
