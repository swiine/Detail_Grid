namespace BlockCraft.Core;

/// <summary>
/// Maps voxel coordinates to drawing coordinates.
/// Voxel (x, y, z) occupies drawing X [OriginX + x*CellSize, +CellSize],
/// drawing Y [OriginY + z*CellSize, +CellSize] and elevation [BaseElevation + y*CellHeight, +CellHeight].
/// </summary>
public readonly record struct WorldMapping(double OriginX, double OriginY, double BaseElevation, double CellSize, double CellHeight)
{
    public static WorldMapping Identity => new(0, 0, 0, 1, 1);

    /// <summary>Drawing coordinates of the centre of a voxel's footprint and its bottom elevation.</summary>
    public (double X, double Y, double Z) VoxelBase(int x, int y, int z) =>
        (OriginX + (x + 0.5) * CellSize, OriginY + (z + 0.5) * CellSize, BaseElevation + y * CellHeight);

    /// <summary>Drawing coordinates of a voxel's minimum corner (south-west, bottom).</summary>
    public (double X, double Y, double Z) VoxelCorner(int x, int y, int z) =>
        (OriginX + x * CellSize, OriginY + z * CellSize, BaseElevation + y * CellHeight);

    /// <summary>The voxel whose minimum corner is nearest to a drawing point (inverse of <see cref="VoxelCorner"/>).</summary>
    public (int X, int Y, int Z) NearestVoxelCorner(double x, double y, double z) =>
        ((int)Math.Round((x - OriginX) / CellSize),
         (int)Math.Round((z - BaseElevation) / CellHeight),
         (int)Math.Round((y - OriginY) / CellSize));

    /// <summary>Converts a continuous game-space position to drawing coordinates.</summary>
    public (double X, double Y, double Z) ToDrawing(double gx, double gy, double gz) =>
        (OriginX + gx * CellSize, OriginY + gz * CellSize, BaseElevation + gy * CellHeight);

    /// <summary>Converts a drawing elevation to a (fractional) voxel layer.</summary>
    public double ElevationToLayer(double elevation) => (elevation - BaseElevation) / CellHeight;
}
