using BlockCraft.Core;

namespace BlockCraft.Core.Tests;

public class WorldTests
{
    private static World Flat(int size = 16, int ground = 4)
    {
        var world = new World(size, 32, size);
        for (int x = 0; x < size; x++)
        for (int z = 0; z < size; z++)
        for (int y = 0; y <= ground; y++)
            world.Set(x, y, z, y == ground ? BlockType.Grass : BlockType.Stone);
        return world;
    }

    [Fact]
    public void OutOfBoundsReadsAsAir()
    {
        var world = Flat();
        Assert.Equal(BlockType.Air, world.Get(-1, 0, 0));
        Assert.Equal(BlockType.Air, world.Get(0, 99, 0));
        Assert.False(world.Edit(0, 99, 0, BlockType.Stone));
    }

    [Fact]
    public void GenerationIsNotRecordedButEditsAre()
    {
        var world = Flat();
        Assert.Empty(world.Edits);

        world.Edit(1, 5, 1, BlockType.Brick);
        world.Edit(2, 4, 2, BlockType.Air);

        Assert.Equal(2, world.Edits.Count);
        var placed = Assert.Single(world.PlacedBlocks());
        Assert.Equal((1, 5, 1, BlockType.Brick), placed);
    }

    [Fact]
    public void RayHitsTopFaceOfGround()
    {
        var world = Flat();
        var hit = VoxelRay.Cast(world, 8.5, 10, 8.5, 0, -1, 0, 20);

        Assert.NotNull(hit);
        Assert.Equal((8, 4, 8), (hit.Value.X, hit.Value.Y, hit.Value.Z));
        Assert.Equal((0, 1, 0), (hit.Value.NormalX, hit.Value.NormalY, hit.Value.NormalZ));
        Assert.Equal((8, 5, 8), hit.Value.Adjacent);
        Assert.Equal(5, hit.Value.Distance, 6);
    }

    [Fact]
    public void RayRespectsMaxDistanceAndIgnoresWater()
    {
        var world = Flat();
        world.Set(8, 5, 8, BlockType.Water);
        Assert.Null(VoxelRay.Cast(world, 8.5, 20, 8.5, 0, -1, 0, 5));
        Assert.Equal(BlockType.Grass, VoxelRay.Cast(world, 8.5, 20, 8.5, 0, -1, 0, 30)!.Value.Block);
    }

    [Fact]
    public void PlayerLandsOnGroundAndCannotWalkThroughWalls()
    {
        var world = Flat();
        for (int y = 5; y < 8; y++)
        for (int z = 0; z < 16; z++)
            world.Set(10, y, z, BlockType.Stone);

        var player = new Player { X = 5.5, Y = 12, Z = 8.5, Yaw = Math.PI / 2 }; // facing +X
        var input = new InputState();
        for (int i = 0; i < 120; i++) player.Update(world, input, 1 / 60.0, 0);

        Assert.True(player.OnGround);
        Assert.Equal(5, player.Y, 3);

        input.Forward = true;
        for (int i = 0; i < 240; i++) player.Update(world, input, 1 / 60.0, 0);

        Assert.True(player.X < 10 - Player.Width / 2 + 1e-6, $"walked into the wall: x={player.X}");
        Assert.True(player.X > 9, $"stopped short of the wall: x={player.X}");
    }

    [Fact]
    public void FlyingIgnoresGravity()
    {
        var world = Flat();
        var player = new Player { X = 5.5, Y = 12, Z = 5.5, Flying = true };
        for (int i = 0; i < 60; i++) player.Update(world, new InputState(), 1 / 60.0, 0);
        Assert.Equal(12, player.Y, 6);
    }

    [Fact]
    public void GamePlacesAndBreaksTargetedBlocks()
    {
        var world = Flat();
        var game = new Game(world);
        game.Player.Pitch = -1.5; // look almost straight down
        var input = new InputState();
        game.Update(input, 0.016);
        Assert.NotNull(game.Target);

        // Cannot place a solid block inside yourself.
        var (tx, ty, tz) = game.Target!.Value.Adjacent;
        Assert.True(game.Player.Overlaps(tx, ty, tz));
        input.PlacePressed = true;
        game.Update(input, 0.016);
        Assert.Equal(0, game.BlocksPlaced);

        input.BreakPressed = true;
        game.Update(input, 0.016);
        Assert.Equal(1, game.BlocksBroken);
        Assert.Contains(world.Edits, e => e.Value == BlockType.Air);
    }

    [Fact]
    public void BedrockCannotBeBroken()
    {
        var world = new World(4, 8, 4);
        world.Set(1, 0, 1, BlockType.Bedrock);
        var game = new Game(world);
        game.Player.X = 1.5; game.Player.Y = 1; game.Player.Z = 1.5; game.Player.Pitch = -1.55;
        var input = new InputState { BreakPressed = true };
        game.Update(input, 0.016);
        Assert.Equal(BlockType.Bedrock, world.Get(1, 0, 1));
    }

    [Fact]
    public void HotbarWrapsWhenScrolling()
    {
        var game = new Game(Flat());
        game.Update(new InputState { Scroll = -1 }, 0.016);
        Assert.Equal(Blocks.Placeable.Length - 1, game.SelectedSlot);
        game.Update(new InputState { Scroll = 2 }, 0.016);
        Assert.Equal(1, game.SelectedSlot);
    }
}
