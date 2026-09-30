namespace BlockCraft.Core;

/// <summary>
/// A fixed-size voxel grid. Game axes: X east, Y up, Z north (Y is up, as in Minecraft).
/// Tracks which voxels the player changed so they can be exported back to the drawing.
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

    /// <summary>Voxels the player placed that are still present.</summary>
    public IEnumerable<(int X, int Y, int Z, BlockType Type)> PlacedBlocks() =>
        _edits.Where(e => e.Value != BlockType.Air)
              .Select(e => (e.Key.X, e.Key.Y, e.Key.Z, e.Value));
}
