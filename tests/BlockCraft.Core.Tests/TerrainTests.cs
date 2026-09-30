using BlockCraft.Core;

namespace BlockCraft.Core.Tests;

public class TerrainTests
{
    [Fact]
    public void ProceduralWorldIsDeterministicPerSeed()
    {
        var a = TerrainGenerator.Procedural(48, 48, 48, seed: 7);
        var b = TerrainGenerator.Procedural(48, 48, 48, seed: 7);
        var c = TerrainGenerator.Procedural(48, 48, 48, seed: 8);

        bool same = true, differs = false;
        for (int x = 0; x < 48; x++)
        for (int z = 0; z < 48; z++)
        {
            same &= a.HighestBlock(x, z) == b.HighestBlock(x, z);
            differs |= a.HighestBlock(x, z) != c.HighestBlock(x, z);
        }
        Assert.True(same);
        Assert.True(differs);
    }

    [Fact]
    public void HeightFieldTopFaceMatchesSurfaceElevation()
    {
        // 1 unit blocks, world floor at elevation 100.
        var mapping = new WorldMapping(1000, 2000, 100, 1, 1);
        var layers = new double[3, 3];
        for (int x = 0; x < 3; x++)
        for (int z = 0; z < 3; z++)
            layers[x, z] = mapping.ElevationToLayer(110); // surface at elevation 110
        layers[2, 2] = double.NaN; // outside the surface

        var world = TerrainGenerator.FromHeightField(layers, 48, mapping, trees: false);

        int top = world.HighestBlock(1, 1);
        var (_, _, bottomOfTop) = mapping.VoxelBase(1, top, 1);
        Assert.Equal(110, bottomOfTop + mapping.CellHeight, 6); // top face sits on the surface
        Assert.Equal(BlockType.Grass, world.Get(1, top, 1));
        Assert.Equal(BlockType.Bedrock, world.Get(1, 0, 1));
        Assert.Equal(0, world.HighestBlock(2, 2)); // only bedrock outside the surface
    }

    [Fact]
    public void RoadCellsArePaved()
    {
        var layers = new double[4, 4];
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 4; z++)
            layers[x, z] = 10;

        var roads = new HashSet<(int, int)> { (1, 1), (1, 2) };
        var world = TerrainGenerator.FromHeightField(layers, 32, WorldMapping.Identity, roads, trees: false);

        Assert.Equal(BlockType.Asphalt, world.Get(1, world.HighestBlock(1, 1), 1));
        Assert.Equal(BlockType.Grass, world.Get(0, world.HighestBlock(0, 0), 0));
    }

    [Fact]
    public void MappingConvertsVoxelsToDrawingCoordinates()
    {
        var m = new WorldMapping(500, 800, 20, 2, 0.5);
        var (x, y, z) = m.VoxelBase(3, 4, 5);
        Assert.Equal(500 + 3.5 * 2, x);
        Assert.Equal(800 + 5.5 * 2, y);
        Assert.Equal(20 + 4 * 0.5, z);
        Assert.Equal(4, m.ElevationToLayer(22), 9);
    }

    [Fact]
    public void RendererFillsEveryPixel()
    {
        var game = new Game(TerrainGenerator.Procedural(32, 32, 32, seed: 3));
        var pixels = new int[64 * 36];
        game.Render(pixels, 64, 36);
        Assert.All(pixels, p => Assert.Equal(0xFF, (p >> 24) & 0xFF));
    }
}
