namespace BlockCraft.Core;

/// <summary>
/// Software voxel ray caster. Casts one ray per pixel through the grid (no GPU or graphics
/// API needed), which keeps the plug-in free of DirectX/OpenGL dependencies inside Civil 3D.
/// </summary>
public sealed class Renderer
{
    private const int SkyHorizon = 0xB9D6FF;
    private const int SkyZenith = 0x5E8FE6;
    private const int WaterTint = 0x1E46B4;

    public double FieldOfViewDegrees { get; set; } = 75;
    public float ViewDistance { get; set; } = 96;

    /// <summary>Renders the view from <paramref name="player"/> into an ARGB buffer of width*height pixels.</summary>
    public void Render(World world, Player player, int[] pixels, int width, int height, RayHit? target)
    {
        if (pixels.Length < width * height)
            throw new ArgumentException("Pixel buffer is smaller than width*height.", nameof(pixels));

        float yaw = (float)player.Yaw, pitch = (float)player.Pitch;
        float fx = MathF.Sin(yaw) * MathF.Cos(pitch), fy = MathF.Sin(pitch), fz = MathF.Cos(yaw) * MathF.Cos(pitch);
        float rx = MathF.Cos(yaw), ry = 0, rz = -MathF.Sin(yaw);
        // up = forward x right
        float ux = fy * rz - fz * ry, uy = fz * rx - fx * rz, uz = fx * ry - fy * rx;

        float tanHalf = MathF.Tan((float)(FieldOfViewDegrees * Math.PI / 360));
        float aspect = width / (float)height;
        float ox = (float)player.X, oy = (float)player.EyeY, oz = (float)player.Z;
        bool eyeInWater = world.Get((int)MathF.Floor(ox), (int)MathF.Floor(oy), (int)MathF.Floor(oz)) == BlockType.Water;
        float maxDist = eyeInWater ? 24 : ViewDistance;

        Parallel.For(0, height, py =>
        {
            float sy = (1 - 2 * (py + 0.5f) / height) * tanHalf;
            int row = py * width;
            for (int px = 0; px < width; px++)
            {
                float sx = (2 * (px + 0.5f) / width - 1) * tanHalf * aspect;
                float dx = fx + rx * sx + ux * sy;
                float dy = fy + ry * sx + uy * sy;
                float dz = fz + rz * sx + uz * sy;
                float inv = 1 / MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                dx *= inv; dy *= inv; dz *= inv;

                int color = Trace(world, ox, oy, oz, dx, dy, dz, maxDist, eyeInWater, target);
                if (eyeInWater)
                    color = Textures.Lerp(color, WaterTint, 0.45f);
                pixels[row + px] = unchecked((int)0xFF000000) | color;
            }
        });
    }

    private static int SkyGradient(float dy) => Textures.Lerp(SkyHorizon, SkyZenith, MathF.Max(0, dy) * 1.4f);

    private static int Sky(float dy, float dx, float dz)
    {
        // Sun: fixed direction, drawn as a soft disc.
        const float sx = 0.40f, sy = 0.62f, sz = 0.67f;
        float sun = dx * sx + dy * sy + dz * sz;
        if (sun > 0.9985f) return 0xFFF8D0;
        int sky = SkyGradient(dy);
        if (sun > 0.99f) sky = Textures.Lerp(sky, 0xFFF4C0, (sun - 0.99f) / 0.0085f);
        return sky;
    }

    private static int Trace(World world, float ox, float oy, float oz, float dx, float dy, float dz,
        float maxDist, bool eyeInWater, RayHit? target)
    {
        int x = (int)MathF.Floor(ox), y = (int)MathF.Floor(oy), z = (int)MathF.Floor(oz);
        int stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0;
        int stepY = dy > 0 ? 1 : dy < 0 ? -1 : 0;
        int stepZ = dz > 0 ? 1 : dz < 0 ? -1 : 0;
        float tDeltaX = stepX != 0 ? MathF.Abs(1 / dx) : float.MaxValue;
        float tDeltaY = stepY != 0 ? MathF.Abs(1 / dy) : float.MaxValue;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(1 / dz) : float.MaxValue;
        float tMaxX = stepX > 0 ? (x + 1 - ox) * tDeltaX : stepX < 0 ? (ox - x) * tDeltaX : float.MaxValue;
        float tMaxY = stepY > 0 ? (y + 1 - oy) * tDeltaY : stepY < 0 ? (oy - y) * tDeltaY : float.MaxValue;
        float tMaxZ = stepZ > 0 ? (z + 1 - oz) * tDeltaZ : stepZ < 0 ? (oz - z) * tDeltaZ : float.MaxValue;

        int axis = -1;
        float t = 0;
        float tintAmount = 0;
        int tint = 0;

        while (t < maxDist)
        {
            // Leaving the world through the top or the sides: nothing more to hit.
            if ((y >= world.SizeY && stepY >= 0) || (y < 0 && stepY <= 0)
                || (x < 0 && stepX <= 0) || (x >= world.SizeX && stepX >= 0)
                || (z < 0 && stepZ <= 0) || (z >= world.SizeZ && stepZ >= 0))
                break;

            BlockType b = world.Get(x, y, z);
            if (b != BlockType.Air && axis >= 0)
            {
                float hx = ox + dx * t, hy = oy + dy * t, hz = oz + dz * t;
                float u, v;
                int face;
                float light;
                switch (axis)
                {
                    case 0: u = hz - MathF.Floor(hz); v = 1 - (hy - MathF.Floor(hy)); face = Textures.Side; light = 0.82f; break;
                    case 1: u = hx - MathF.Floor(hx); v = hz - MathF.Floor(hz); face = stepY < 0 ? Textures.Top : Textures.Bottom; light = stepY < 0 ? 1f : 0.5f; break;
                    default: u = hx - MathF.Floor(hx); v = 1 - (hy - MathF.Floor(hy)); face = Textures.Side; light = 0.66f; break;
                }

                if (b == BlockType.Water)
                {
                    // Only the water surface is drawn; the rest is a tint applied to whatever is behind it.
                    if (!eyeInWater && tintAmount == 0)
                    {
                        tint = Textures.Mul(Textures.Sample(b, face, u, v), light);
                        tintAmount = 0.6f;
                    }
                }
                else
                {
                    int texel = Textures.Sample(b, face, u, v);
                    bool isTarget = target is { } tg && tg.X == x && tg.Y == y && tg.Z == z;
                    bool edge = u < 0.045f || u > 0.955f || v < 0.045f || v > 0.955f;

                    if (b == BlockType.Glass && !(isTarget && edge))
                    {
                        if (texel == 0xF0FAFF)
                            return Finish(Textures.Mul(texel, light), t, maxDist, dx, dy, dz, tint, tintAmount);
                        tint = tintAmount > 0 ? tint : texel;
                        tintAmount = MathF.Max(tintAmount, 0.3f);
                    }
                    else
                    {
                        int c = Textures.Mul(texel, light);
                        if (isTarget && edge) c = 0x101010;
                        return Finish(c, t, maxDist, dx, dy, dz, tint, tintAmount);
                    }
                }
            }

            if (tMaxX < tMaxY && tMaxX < tMaxZ) { x += stepX; t = tMaxX; tMaxX += tDeltaX; axis = 0; }
            else if (tMaxY < tMaxZ) { y += stepY; t = tMaxY; tMaxY += tDeltaY; axis = 1; }
            else { z += stepZ; t = tMaxZ; tMaxZ += tDeltaZ; axis = 2; }
        }

        int sky = eyeInWater ? WaterTint : Sky(dy, dx, dz);
        return tintAmount > 0 ? Textures.Lerp(sky, tint, tintAmount) : sky;
    }

    private static int Finish(int color, float t, float maxDist, float dx, float dy, float dz, int tint, float tintAmount)
    {
        if (tintAmount > 0)
            color = Textures.Lerp(color, tint, tintAmount);
        float fogStart = maxDist * 0.55f;
        if (t > fogStart)
            color = Textures.Lerp(color, SkyGradient(dy), (t - fogStart) / (maxDist - fogStart));
        return color;
    }
}
