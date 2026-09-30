namespace BlockCraft.Core;

/// <summary>Game state and rules: ties the world, player, block picking and hotbar together.</summary>
public sealed class Game
{
    public const double Reach = 6;

    public Game(World world)
    {
        World = world;
        var (sx, sz) = FindSpawn(world);
        Player.SpawnAt(world, sx, sz);
    }

    /// <summary>Nearest column to the world centre whose top block is dry land.</summary>
    public static (int X, int Z) FindSpawn(World world)
    {
        int cx = world.SizeX / 2, cz = world.SizeZ / 2;
        int maxR = Math.Max(world.SizeX, world.SizeZ) / 2;
        for (int r = 0; r <= maxR; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
            int x = cx + dx, z = cz + dz;
            int top = world.HighestBlock(x, z);
            if (top <= 0) continue;
            BlockType b = world.Get(x, top, z);
            if (b is BlockType.Grass or BlockType.Sand or BlockType.Snow or BlockType.Stone or BlockType.Asphalt or BlockType.Dirt)
                return (x, z);
        }
        return (cx, cz);
    }

    public World World { get; }
    public Player Player { get; } = new();
    public Renderer Renderer { get; } = new();

    public double MouseSensitivity { get; set; } = 0.0035;

    /// <summary>Selected hotbar slot, an index into <see cref="Blocks.Placeable"/>.</summary>
    public int SelectedSlot { get; private set; }

    public BlockType SelectedBlock => Blocks.Placeable[SelectedSlot];

    /// <summary>The block under the crosshair, if within reach.</summary>
    public RayHit? Target { get; private set; }

    public int BlocksPlaced { get; private set; }
    public int BlocksBroken { get; private set; }

    public void Update(InputState input, double dt)
    {
        dt = Math.Clamp(dt, 0, 0.05);

        if (input.SelectSlot >= 0 && input.SelectSlot < Blocks.Placeable.Length)
            SelectedSlot = input.SelectSlot;
        if (input.Scroll != 0)
        {
            int n = Blocks.Placeable.Length;
            SelectedSlot = ((SelectedSlot + input.Scroll) % n + n) % n;
        }

        Player.Update(World, input, dt, MouseSensitivity);
        Target = PickTarget();

        if (input.BreakPressed) Break();
        if (input.PlacePressed) Place();
        if (input.PickPressed) PickBlock();

        Target = PickTarget();
        input.ClearEdges();
    }

    public void Render(int[] pixels, int width, int height) =>
        Renderer.Render(World, Player, pixels, width, height, Target);

    private RayHit? PickTarget()
    {
        var (lx, ly, lz) = Player.Look;
        return VoxelRay.Cast(World, Player.X, Player.EyeY, Player.Z, lx, ly, lz, Reach);
    }

    public bool Break()
    {
        if (Target is not { } hit || !Blocks.Info(hit.Block).Breakable)
            return false;
        World.Edit(hit.X, hit.Y, hit.Z, BlockType.Air);
        BlocksBroken++;
        return true;
    }

    public bool Place()
    {
        if (Target is not { } hit)
            return false;

        var (x, y, z) = hit.Adjacent;
        if (!World.InBounds(x, y, z))
            return false;

        BlockType existing = World.Get(x, y, z);
        if (existing != BlockType.Air && existing != BlockType.Water)
            return false;

        BlockType block = SelectedBlock;
        if (Blocks.IsSolid(block) && Player.Overlaps(x, y, z))
            return false;

        World.Edit(x, y, z, block);
        BlocksPlaced++;
        return true;
    }

    /// <summary>Selects the hotbar slot matching the targeted block (middle click).</summary>
    public void PickBlock()
    {
        if (Target is not { } hit) return;
        int slot = Array.IndexOf(Blocks.Placeable, hit.Block);
        if (slot >= 0) SelectedSlot = slot;
    }

    /// <summary>Player feet position in drawing coordinates.</summary>
    public (double X, double Y, double Z) PlayerDrawingPosition =>
        World.Mapping.ToDrawing(Player.X, Player.Y, Player.Z);

    /// <summary>Compass heading (N/NE/E...) of the view direction; +Z is north.</summary>
    public string Heading
    {
        get
        {
            double deg = (Player.Yaw * 180 / Math.PI % 360 + 360) % 360;
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[(int)Math.Round(deg / 45) % 8];
        }
    }
}
