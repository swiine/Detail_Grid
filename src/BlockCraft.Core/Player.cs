namespace BlockCraft.Core;

/// <summary>First-person player with AABB collision against the voxel world.</summary>
public sealed class Player
{
    public const double Width = 0.6;
    public const double Height = 1.8;
    public const double EyeHeight = 1.62;

    private const double Gravity = 28;
    private const double JumpSpeed = 8.6;
    private const double WalkSpeed = 4.6;
    private const double SprintSpeed = 7.2;
    private const double FlySpeed = 12;
    private const double MaxFall = 50;

    /// <summary>Feet position (centre of the bottom of the bounding box).</summary>
    public double X, Y, Z;
    public double VX, VY, VZ;

    /// <summary>Yaw in radians (0 = looking +Z/north), pitch in radians (positive = up).</summary>
    public double Yaw, Pitch;

    public bool OnGround { get; private set; }
    public bool Flying { get; set; }
    public bool InWater { get; private set; }

    public double EyeY => Y + EyeHeight;

    /// <summary>Unit view direction.</summary>
    public (double X, double Y, double Z) Look =>
        (Math.Sin(Yaw) * Math.Cos(Pitch), Math.Sin(Pitch), Math.Cos(Yaw) * Math.Cos(Pitch));

    public void SpawnAt(World world, int x, int z)
    {
        x = Math.Clamp(x, 0, world.SizeX - 1);
        z = Math.Clamp(z, 0, world.SizeZ - 1);
        int top = world.HighestBlock(x, z);
        X = x + 0.5;
        Z = z + 0.5;
        Y = Math.Min(top + 1, world.SizeY - Height);
        VX = VY = VZ = 0;
    }

    public void Update(World world, InputState input, double dt, double mouseSensitivity)
    {
        Yaw += input.MouseDX * mouseSensitivity;
        Pitch = Math.Clamp(Pitch - input.MouseDY * mouseSensitivity, -1.55, 1.55);

        if (input.ToggleFly)
        {
            Flying = !Flying;
            VY = 0;
        }

        InWater = world.Get((int)Math.Floor(X), (int)Math.Floor(Y + 0.4), (int)Math.Floor(Z)) == BlockType.Water;

        // Horizontal intent relative to yaw.
        double fx = Math.Sin(Yaw), fz = Math.Cos(Yaw);
        double rx = Math.Cos(Yaw), rz = -Math.Sin(Yaw);
        double mx = 0, mz = 0;
        if (input.Forward) { mx += fx; mz += fz; }
        if (input.Back) { mx -= fx; mz -= fz; }
        if (input.Right) { mx += rx; mz += rz; }
        if (input.Left) { mx -= rx; mz -= rz; }
        double mlen = Math.Sqrt(mx * mx + mz * mz);
        if (mlen > 0) { mx /= mlen; mz /= mlen; }

        double speed = Flying ? FlySpeed * (input.Sprint ? 2.5 : 1) : input.Sprint ? SprintSpeed : WalkSpeed;
        if (InWater && !Flying) speed *= 0.5;
        VX = mx * speed;
        VZ = mz * speed;

        if (Flying)
        {
            VY = (input.Jump ? FlySpeed : 0) - (input.Sneak ? FlySpeed : 0);
        }
        else if (InWater)
        {
            VY = input.Jump ? 3.5 : Math.Max(VY - Gravity * 0.25 * dt, -2.5);
        }
        else
        {
            if (input.Jump && OnGround)
                VY = JumpSpeed;
            VY = Math.Max(VY - Gravity * dt, -MaxFall);
        }

        OnGround = false;
        MoveAxis(world, VX * dt, 0);
        MoveAxis(world, VZ * dt, 2);
        MoveAxis(world, VY * dt, 1);

        // Fell out of the world (e.g. off the edge of a surface): put the player back on top.
        if (Y < -16)
            SpawnAt(world, (int)X, (int)Z);
    }

    private void MoveAxis(World world, double delta, int axis)
    {
        if (delta == 0) return;

        // Sub-step so fast movement cannot tunnel through a block.
        int steps = (int)Math.Ceiling(Math.Abs(delta) / 0.4);
        double step = delta / steps;
        for (int i = 0; i < steps; i++)
        {
            switch (axis)
            {
                case 0: X += step; break;
                case 1: Y += step; break;
                default: Z += step; break;
            }

            if (!Collides(world))
                continue;

            switch (axis)
            {
                case 0: X -= step; VX = 0; break;
                case 1:
                    Y -= step;
                    if (delta < 0)
                    {
                        OnGround = true;
                        SnapDown(world);
                    }
                    VY = 0;
                    break;
                default: Z -= step; VZ = 0; break;
            }
            return;
        }
    }

    /// <summary>After landing, settle feet exactly onto the block surface to avoid hovering.</summary>
    private void SnapDown(World world)
    {
        double target = Math.Floor(Y);
        double saved = Y;
        Y = target;
        if (Collides(world))
            Y = saved;
    }

    /// <summary>True if the player's bounding box overlaps a solid block.</summary>
    public bool Collides(World world)
    {
        double half = Width / 2;
        int x0 = (int)Math.Floor(X - half), x1 = (int)Math.Floor(X + half - 1e-9);
        int y0 = (int)Math.Floor(Y), y1 = (int)Math.Floor(Y + Height - 1e-9);
        int z0 = (int)Math.Floor(Z - half), z1 = (int)Math.Floor(Z + half - 1e-9);

        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        {
            if (world.IsSolid(x, y, z))
                return true;
        }
        return false;
    }

    /// <summary>True if the player's bounding box overlaps the given voxel.</summary>
    public bool Overlaps(int bx, int by, int bz)
    {
        double half = Width / 2;
        return X + half > bx && X - half < bx + 1
            && Y + Height > by && Y < by + 1
            && Z + half > bz && Z - half < bz + 1;
    }
}
