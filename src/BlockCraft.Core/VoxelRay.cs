namespace BlockCraft.Core;

/// <summary>Result of a voxel ray cast: the block hit and the face it was entered through.</summary>
public readonly record struct RayHit(int X, int Y, int Z, int NormalX, int NormalY, int NormalZ, double Distance, BlockType Block)
{
    /// <summary>The empty voxel in front of the hit face, where a new block would be placed.</summary>
    public (int X, int Y, int Z) Adjacent => (X + NormalX, Y + NormalY, Z + NormalZ);
}

public static class VoxelRay
{
    /// <summary>
    /// Amanatides-Woo traversal. Returns the first block (other than air/water) within
    /// <paramref name="maxDistance"/>, or null.
    /// </summary>
    public static RayHit? Cast(World world, double ox, double oy, double oz, double dx, double dy, double dz, double maxDistance)
    {
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-9) return null;
        dx /= len; dy /= len; dz /= len;

        int x = (int)Math.Floor(ox), y = (int)Math.Floor(oy), z = (int)Math.Floor(oz);
        int stepX = Math.Sign(dx), stepY = Math.Sign(dy), stepZ = Math.Sign(dz);

        double tDeltaX = stepX != 0 ? Math.Abs(1 / dx) : double.PositiveInfinity;
        double tDeltaY = stepY != 0 ? Math.Abs(1 / dy) : double.PositiveInfinity;
        double tDeltaZ = stepZ != 0 ? Math.Abs(1 / dz) : double.PositiveInfinity;

        double tMaxX = stepX > 0 ? (x + 1 - ox) * tDeltaX : stepX < 0 ? (ox - x) * tDeltaX : double.PositiveInfinity;
        double tMaxY = stepY > 0 ? (y + 1 - oy) * tDeltaY : stepY < 0 ? (oy - y) * tDeltaY : double.PositiveInfinity;
        double tMaxZ = stepZ > 0 ? (z + 1 - oz) * tDeltaZ : stepZ < 0 ? (oz - z) * tDeltaZ : double.PositiveInfinity;

        int nx = 0, ny = 0, nz = 0;
        double t = 0;

        while (t <= maxDistance)
        {
            BlockType b = world.Get(x, y, z);
            if (b != BlockType.Air && b != BlockType.Water)
                return new RayHit(x, y, z, nx, ny, nz, t, b);

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                x += stepX; t = tMaxX; tMaxX += tDeltaX;
                nx = -stepX; ny = 0; nz = 0;
            }
            else if (tMaxY < tMaxZ)
            {
                y += stepY; t = tMaxY; tMaxY += tDeltaY;
                nx = 0; ny = -stepY; nz = 0;
            }
            else
            {
                z += stepZ; t = tMaxZ; tMaxZ += tDeltaZ;
                nx = 0; ny = 0; nz = -stepZ;
            }
        }
        return null;
    }
}
