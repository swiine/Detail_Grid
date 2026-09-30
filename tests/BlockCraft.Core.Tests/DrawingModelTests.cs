using BlockCraft.Core;

namespace BlockCraft.Core.Tests;

public class DrawingModelTests
{
    private static World Slab()
    {
        var world = new World(8, 16, 8);
        for (int x = 0; x < 8; x++)
        for (int z = 0; z < 8; z++)
        for (int y = 0; y <= 4; y++)
            world.Set(x, y, z, y == 0 ? BlockType.Bedrock : y == 4 ? BlockType.Grass : BlockType.Stone);
        return world;
    }

    [Fact]
    public void OnlyTheVisibleShellIsDrawn()
    {
        var world = Slab();
        // World walls and floor are not drawn, so a flat slab draws only its top layer.
        Assert.Equal(64, world.DrawnVoxels().Count());
        Assert.All(world.DrawnVoxels(), v => Assert.Equal(4, v.Y));
    }

    [Fact]
    public void DiggingExposesTheBlocksAroundTheHole()
    {
        var world = Slab();
        world.Set(3, 4, 3, BlockType.Air);
        Assert.True(world.IsDrawn(3, 3, 3));   // floor of the hole
        Assert.True(world.IsDrawn(2, 4, 3));   // wall of the hole (already drawn as a top)
        Assert.False(world.IsDrawn(2, 3, 3));  // still buried
    }

    [Fact]
    public void WaterIsDrawnOnlyAtItsSurfaceAndGlassRevealsNeighbours()
    {
        var world = Slab();
        world.Set(1, 5, 1, BlockType.Water);
        world.Set(1, 6, 1, BlockType.Water);
        Assert.False(world.IsDrawn(1, 5, 1));
        Assert.True(world.IsDrawn(1, 6, 1));

        world.Set(5, 4, 5, BlockType.Glass);
        Assert.True(world.IsDrawn(5, 3, 5));
    }

    [Fact]
    public void BedrockFloorOutsideASurfaceIsNotDrawn()
    {
        var world = new World(4, 8, 4);
        world.Set(1, 0, 1, BlockType.Bedrock);
        Assert.False(world.IsDrawn(1, 0, 1));
    }

    [Fact]
    public void SerializationRoundTrips()
    {
        var mapping = new WorldMapping(123.5, -44, 17.25, 2, 0.5);
        var world = TerrainGenerator.Procedural(40, 32, 30, seed: 9, mapping: mapping);
        world.Source = "test world";

        byte[] data = world.Serialize();
        var copy = World.Deserialize(data);

        Assert.Equal((40, 32, 30), (copy.SizeX, copy.SizeY, copy.SizeZ));
        Assert.Equal(mapping, copy.Mapping);
        Assert.Equal("test world", copy.Source);
        for (int x = 0; x < 40; x++)
        for (int y = 0; y < 32; y++)
        for (int z = 0; z < 30; z++)
            Assert.Equal(world.Get(x, y, z), copy.Get(x, y, z));
        Assert.True(data.Length < 40 * 32 * 30 / 4, $"not compressed: {data.Length} bytes");
    }

    [Fact]
    public void VoxelCornersMapBothWays()
    {
        var m = new WorldMapping(1000, 2000, 50, 2.5, 0.5);
        var (x, y, z) = m.VoxelCorner(7, 11, 3);
        Assert.Equal((7, 11, 3), m.NearestVoxelCorner(x, y, z));
        // A block nudged slightly off-grid still snaps back to its voxel.
        Assert.Equal((7, 11, 3), m.NearestVoxelCorner(x + 0.4, y - 0.4, z + 0.1));
    }

    [Fact]
    public void DefaultProceduralWorldHasAReasonableBlockCount()
    {
        int drawn = TerrainGenerator.Procedural(96, 64, 96, seed: 1).DrawnVoxels().Count();
        Assert.InRange(drawn, 96 * 96, 60_000);
    }
}
