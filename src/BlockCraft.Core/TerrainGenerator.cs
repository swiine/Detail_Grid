namespace BlockCraft.Core;

/// <summary>Builds worlds: purely procedural, or from a sampled elevation grid (e.g. a Civil 3D surface).</summary>
public static class TerrainGenerator
{
    /// <summary>Generates a rolling procedural landscape with water, beaches, snow caps and trees.</summary>
    public static World Procedural(int sizeX = 192, int sizeY = 64, int sizeZ = 192, int seed = 1337, WorldMapping? mapping = null)
    {
        var world = new World(sizeX, sizeY, sizeZ, mapping);
        var noise = new Noise(seed);
        int seaLevel = sizeY * 5 / 16;
        int snowLine = sizeY * 11 / 16;

        for (int x = 0; x < sizeX; x++)
        for (int z = 0; z < sizeZ; z++)
        {
            float n = noise.Fractal(x / 48f, z / 48f, 5);
            float ridges = noise.Fractal(x / 20f + 100, z / 20f + 100, 3);
            int height = (int)(sizeY * 0.15f + n * n * sizeY * 0.75f + ridges * 4);
            height = Math.Clamp(height, 1, sizeY - 12);

            BlockType top = height >= snowLine ? BlockType.Snow
                          : height <= seaLevel + 1 ? BlockType.Sand
                          : BlockType.Grass;
            BlockType filler = top == BlockType.Sand ? BlockType.Sand : BlockType.Dirt;

            FillColumn(world, x, z, height, top, filler);

            for (int y = height + 1; y <= seaLevel; y++)
                world.Set(x, y, z, BlockType.Water);
        }

        PlantTrees(world, noise, density: 0.012f);
        world.Source = $"Procedural (seed {seed})";
        return world;
    }

    /// <summary>
    /// Builds a world from an elevation grid. <paramref name="layers"/> holds, for each column [x, z],
    /// the surface elevation in voxel layers above the world floor (NaN = outside the surface); the top
    /// face of the column's highest block is placed as close as possible to that level. Cells in <paramref name="roadCells"/>
    /// are paved with asphalt.
    /// </summary>
    public static World FromHeightField(
        double[,] layers,
        int sizeY,
        WorldMapping mapping,
        ISet<(int X, int Z)>? roadCells = null,
        bool trees = true,
        int seed = 1337)
    {
        int sizeX = layers.GetLength(0);
        int sizeZ = layers.GetLength(1);
        var world = new World(sizeX, sizeY, sizeZ, mapping);
        var noise = new Noise(seed);

        double min = double.MaxValue, max = double.MinValue;
        foreach (double v in layers)
        {
            if (double.IsNaN(v)) continue;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        if (min > max) { min = 1; max = 1; }
        double snowLine = min + (max - min) * 0.85;

        for (int x = 0; x < sizeX; x++)
        for (int z = 0; z < sizeZ; z++)
        {
            double h = layers[x, z];
            if (double.IsNaN(h))
            {
                world.Set(x, 0, z, BlockType.Bedrock);
                continue;
            }

            int height = Math.Clamp((int)Math.Round(h) - 1, 1, sizeY - 10);
            bool road = roadCells?.Contains((x, z)) == true;

            // Steep cells show exposed rock instead of grass.
            double slope = MaxNeighbourDelta(layers, x, z);
            BlockType top = road ? BlockType.Asphalt
                          : slope >= 3 ? BlockType.Stone
                          : max - min > 8 && h >= snowLine ? BlockType.Snow
                          : BlockType.Grass;
            BlockType filler = road ? BlockType.Gravel : top == BlockType.Stone ? BlockType.Stone : BlockType.Dirt;
            FillColumn(world, x, z, height, top, filler);
        }

        if (trees)
            PlantTrees(world, noise, density: 0.006f);
        return world;
    }

    private static double MaxNeighbourDelta(double[,] layers, int x, int z)
    {
        double h = layers[x, z];
        double worst = 0;
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if ((uint)nx >= (uint)layers.GetLength(0) || (uint)nz >= (uint)layers.GetLength(1)) continue;
            double n = layers[nx, nz];
            if (!double.IsNaN(n))
                worst = Math.Max(worst, Math.Abs(n - h));
        }
        return worst;
    }

    private static void FillColumn(World world, int x, int z, int height, BlockType top, BlockType filler)
    {
        world.Set(x, 0, z, BlockType.Bedrock);
        for (int y = 1; y < height; y++)
            world.Set(x, y, z, y >= height - 3 ? filler : BlockType.Stone);
        world.Set(x, height, z, top);
    }

    private static void PlantTrees(World world, Noise noise, float density)
    {
        for (int x = 3; x < world.SizeX - 3; x++)
        for (int z = 3; z < world.SizeZ - 3; z++)
        {
            if (noise.Cell(x, z) >= density) continue;

            int ground = world.HighestBlock(x, z);
            if (ground < 0 || world.Get(x, ground, z) != BlockType.Grass) continue;

            int trunk = 4 + (int)(noise.Cell(z, x) * 3);
            if (ground + trunk + 3 >= world.SizeY) continue;

            for (int y = 1; y <= trunk; y++)
                world.Set(x, ground + y, z, BlockType.Log);

            int crown = ground + trunk;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            for (int dy = -1; dy <= 2; dy++)
            {
                int r = dy >= 1 ? 1 : 2;
                if (Math.Abs(dx) > r || Math.Abs(dz) > r) continue;
                if (Math.Abs(dx) == 2 && Math.Abs(dz) == 2) continue;
                if (world.Get(x + dx, crown + dy, z + dz) == BlockType.Air)
                    world.Set(x + dx, crown + dy, z + dz, BlockType.Leaves);
            }
        }
    }
}
