namespace CivDoom.Engine;

/// <summary>
/// Software renderer: Wolfenstein/Doom style column ray casting into a 32-bit framebuffer.
/// Unlike a classic grid raycaster it casts against arbitrary line segments, so any
/// linework from a drawing can be a wall.
/// </summary>
public sealed class Renderer
{
    public const double EyeHeight = 0.5;
    public const double MaxViewDistance = 48;

    private readonly double[] _zBuffer;
    private readonly double _planeLength;

    public Renderer(int width, int height, double fovDegrees = 70)
    {
        Width = width;
        Height = height;
        Pixels = new int[width * height];
        _zBuffer = new double[width];
        _planeLength = Math.Tan(fovDegrees * Math.PI / 360);
        FocalLength = width / 2.0 / _planeLength;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>0xAARRGGBB framebuffer, row-major, top row first.</summary>
    public int[] Pixels { get; }

    /// <summary>Pixels per world unit at distance 1.</summary>
    public double FocalLength { get; }

    public bool ShowMap { get; set; } = true;

    public void Render(Game game)
    {
        Player p = game.Player;
        Vec2 dir = p.Direction;
        Vec2 right = dir.PerpRight();
        int horizon = Height / 2;

        DrawSkyAndFloor(p.Position, dir, right, horizon);
        DrawWalls(game.Level.Index, p.Position, dir, right, horizon);
        DrawSprites(game, p.Position, dir, right, horizon);
        DrawWeapon(p, game.State);

        if (p.DamageFlash > 0) Tint(0xFF2020, Math.Min(0.6, p.DamageFlash * 0.6));
        if (p.PickupFlash > 0) Tint(0xFFE060, p.PickupFlash * 0.25);
        if (game.State == GameState.Dead) Tint(0x800000, 0.45);

        DrawCrosshair();
        if (ShowMap) DrawMinimap(game);
    }

    // ---------------------------------------------------------------- world

    private void DrawSkyAndFloor(Vec2 pos, Vec2 dir, Vec2 right, int horizon)
    {
        // Sky: a dusky gradient, since drawings have no ceiling.
        for (int y = 0; y < horizon; y++)
        {
            double t = (double)y / horizon;
            int c = Lerp(0x0B1026, 0x4A3A6A, t);
            Array.Fill(Pixels, Opaque(c), y * Width, Width);
        }

        // Floor: cast each row onto the ground plane and draw a CAD-style grid.
        Vec2 leftRay = dir - right * _planeLength;
        Vec2 rightRay = dir + right * _planeLength;
        for (int y = horizon; y < Height; y++)
        {
            double rowDist = FocalLength * EyeHeight / (y - horizon + 0.5);
            double fog = Fog(rowDist);
            Vec2 start = pos + leftRay * rowDist;
            Vec2 stepVec = (rightRay - leftRay) * (rowDist / Width);
            // Line thickness grows with distance so the grid doesn't shimmer to nothing.
            double major = Math.Clamp(0.015 * rowDist, 0.02, 0.2);
            double minor = major * 0.6;
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                Vec2 w = start + stepVec * (x + 0.5);
                double fx = w.X - Math.Floor(w.X), fy = w.Y - Math.Floor(w.Y);
                double gx = w.X * 4 - Math.Floor(w.X * 4), gy = w.Y * 4 - Math.Floor(w.Y * 4);
                int c;
                if (fx < major || fy < major) c = 0x2E6FA8;
                else if (rowDist < 8 && (gx < minor * 4 || gy < minor * 4)) c = 0x1E2E3E;
                else c = 0x151A20;
                Pixels[row + x] = Opaque(Shade(c, fog));
            }
        }
    }

    private void DrawWalls(SpatialIndex index, Vec2 pos, Vec2 dir, Vec2 right, int horizon)
    {
        Vec2 light = new Vec2(0.6, 0.8);
        for (int x = 0; x < Width; x++)
        {
            double camX = 2.0 * (x + 0.5) / Width - 1;
            Vec2 rayDir = dir + right * (_planeLength * camX);
            RayHit? hit = index.CastRay(pos, rayDir, MaxViewDistance);
            if (hit is not { } h)
            {
                _zBuffer[x] = double.PositiveInfinity;
                continue;
            }

            // rayDir has unit length along the view direction, so this is the perpendicular distance.
            double dist = Math.Max(h.Distance, 1e-4);
            _zBuffer[x] = dist;

            double scale = FocalLength / dist;
            double top = horizon - (1 - EyeHeight) * scale;
            double bottom = horizon + EyeHeight * scale;
            int y0 = Math.Max(0, (int)Math.Ceiling(top));
            int y1 = Math.Min(Height - 1, (int)Math.Floor(bottom));

            double side = 0.72 + 0.28 * Math.Abs(Vec2.Dot(h.Wall.Normal, light));
            double fog = Fog(dist) * side;
            double s = h.WallOffset;

            for (int y = y0; y <= y1; y++)
            {
                double v = (y - top) / (bottom - top); // 0 at top of wall, 1 at floor
                Pixels[y * Width + x] = Opaque(Shade(WallTexel(h.Wall.Color, s, v), fog));
            }
        }
    }

    /// <summary>Procedural block-wall texture tinted with the entity color.</summary>
    private static int WallTexel(int baseColor, double s, double v)
    {
        const int courses = 6;
        const double blockLength = 0.5;
        double vv = v * courses;
        int course = (int)vv;
        double offset = (course & 1) * blockLength * 0.5;
        double u = (s + offset) / blockLength;
        int block = (int)Math.Floor(u);
        bool mortar = vv - course < 0.07 || u - block < 0.035;
        if (mortar) return Shade(baseColor, 0.45);
        // Hash per block for a little variation.
        uint hsh = (uint)(block * 73856093) ^ (uint)(course * 19349663);
        hsh ^= hsh >> 13;
        double jitter = 0.88 + (hsh % 25) / 100.0;
        // Darken toward the floor for some grounding.
        double grime = 1 - Math.Max(0, v - 0.85) * 1.5;
        return Shade(baseColor, jitter * grime);
    }

    private readonly record struct SpriteDraw(Vec2 Position, double Depth, SpriteImage Image, double WorldHeight, double WorldWidth, double Lift, int Tint);

    private void DrawSprites(Game game, Vec2 pos, Vec2 dir, Vec2 right, int horizon)
    {
        var list = new List<SpriteDraw>();
        void Add(Vec2 at, SpriteImage img, double h, double lift = 0, int tint = 0)
        {
            Vec2 rel = at - pos;
            double depth = Vec2.Dot(rel, dir);
            if (depth < 0.05 || depth > MaxViewDistance) return;
            list.Add(new SpriteDraw(at, depth, img, h, h * img.Width / img.Height, lift, tint));
        }

        foreach (Pickup pk in game.Pickups)
        {
            if (pk.Taken) continue;
            Add(pk.Position, pk.Kind == PickupKind.Health ? Art.MedkitSprite : Art.AmmoSprite, 0.3);
        }

        foreach (Enemy e in game.Enemies)
        {
            Art.MonsterSprites set = e.Kind == EnemyKind.Brute ? Art.Brute : Art.Imp;
            SpriteImage img = e.State switch
            {
                EnemyState.Dead => set.Dead,
                EnemyState.Attack => set.Attack,
                EnemyState.Chase => ((int)e.WalkPhase & 1) == 0 ? set.Idle : set.Walk,
                _ => set.Idle,
            };
            Add(e.Position, img, e.Height, 0, e.PainTime > 0 ? 0xFFFFFF : 0);
        }

        foreach (Projectile pr in game.Projectiles)
        {
            Add(pr.Position, Art.FireballSprite, 0.18, lift: 0.3);
        }

        list.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach (SpriteDraw sd in list) DrawSprite(sd, pos, right, horizon);
    }

    private void DrawSprite(SpriteDraw sd, Vec2 pos, Vec2 right, int horizon)
    {
        double lateral = Vec2.Dot(sd.Position - pos, right);
        double scale = FocalLength / sd.Depth;
        double screenX = Width / 2.0 + lateral * scale;
        double h = sd.WorldHeight * scale;
        double w = sd.WorldWidth * scale;
        double bottom = horizon + (EyeHeight - sd.Lift) * scale;
        double top = bottom - h;
        double left = screenX - w / 2;

        int x0 = Math.Max(0, (int)Math.Ceiling(left)), x1 = Math.Min(Width - 1, (int)Math.Floor(left + w));
        int y0 = Math.Max(0, (int)Math.Ceiling(top)), y1 = Math.Min(Height - 1, (int)Math.Floor(bottom));
        double fog = Fog(sd.Depth);
        SpriteImage img = sd.Image;

        for (int x = x0; x <= x1; x++)
        {
            if (sd.Depth >= _zBuffer[x]) continue;
            int tx = Math.Clamp((int)((x - left) / w * img.Width), 0, img.Width - 1);
            for (int y = y0; y <= y1; y++)
            {
                int ty = Math.Clamp((int)((y - top) / h * img.Height), 0, img.Height - 1);
                int c = img[tx, ty];
                if ((c >>> 24) == 0) continue;
                int rgb = sd.Tint != 0 ? Blend(c & 0xFFFFFF, sd.Tint, 0.6) : c & 0xFFFFFF;
                Pixels[y * Width + x] = Opaque(Shade(rgb, Math.Max(fog, 0.35)));
            }
        }
    }

    // ---------------------------------------------------------------- HUD

    private void DrawWeapon(Player p, GameState state)
    {
        if (state == GameState.Dead) return;
        SpriteImage gun = Art.PistolSprite;
        int scale = Math.Max(1, Height / 50);
        double bob = p.BobAmount;
        int bobX = (int)(Math.Sin(p.BobPhase) * 6 * bob);
        int bobY = (int)(Math.Abs(Math.Cos(p.BobPhase)) * 5 * bob);
        int recoil = p.MuzzleFlashTime > 0 ? -scale * 2 : 0;
        int gx = Width / 2 - gun.Width * scale / 2 + bobX;
        int gy = Height - gun.Height * scale + bobY + 4 - recoil;

        if (p.MuzzleFlashTime > 0)
        {
            SpriteImage flash = Art.MuzzleFlashSprite;
            BlitScaled(flash, Width / 2 - flash.Width * scale / 2 + bobX, gy - flash.Height * scale + scale * 2, scale);
        }
        BlitScaled(gun, gx, gy, scale);
    }

    private void BlitScaled(SpriteImage img, int left, int top, int scale)
    {
        for (int sy = 0; sy < img.Height; sy++)
        {
            for (int sx = 0; sx < img.Width; sx++)
            {
                int c = img[sx, sy];
                if ((c >>> 24) == 0) continue;
                for (int dy = 0; dy < scale; dy++)
                {
                    int y = top + sy * scale + dy;
                    if (y < 0 || y >= Height) continue;
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int x = left + sx * scale + dx;
                        if (x >= 0 && x < Width) Pixels[y * Width + x] = c;
                    }
                }
            }
        }
    }

    private void DrawCrosshair()
    {
        int cx = Width / 2, cy = Height / 2;
        int c = Opaque(0x9CFF9C);
        for (int i = 2; i <= 4; i++)
        {
            SetPixel(cx + i, cy, c); SetPixel(cx - i, cy, c);
            SetPixel(cx, cy + i, c); SetPixel(cx, cy - i, c);
        }
    }

    private void DrawMinimap(Game game)
    {
        int size = Math.Min(Width, Height) / 3;
        int mx0 = Width - size - 4, my0 = 4;
        double zoom = size / 16.0; // pixels per world unit: shows ~16 units across
        Vec2 center = game.Player.Position;

        // Darken the backdrop.
        for (int y = my0; y < my0 + size; y++)
            for (int x = mx0; x < mx0 + size; x++)
                Pixels[y * Width + x] = Opaque(Shade(Pixels[y * Width + x] & 0xFFFFFF, 0.3));

        (int X, int Y) ToMap(Vec2 w) =>
            ((int)Math.Round(mx0 + size / 2.0 + (w.X - center.X) * zoom),
             (int)Math.Round(my0 + size / 2.0 - (w.Y - center.Y) * zoom)); // screen Y is flipped

        double half = size / 2.0 / zoom;
        Vec2 r = new(half, half);
        foreach (Wall wall in game.Level.Index.Query(center - r, center + r))
        {
            var a = ToMap(wall.A);
            var b = ToMap(wall.B);
            DrawLine(a.X, a.Y, b.X, b.Y, Opaque(wall.Color), mx0, my0, size);
        }

        foreach (Pickup pk in game.Pickups)
        {
            if (pk.Taken) continue;
            var m = ToMap(pk.Position);
            Dot(m.X, m.Y, pk.Kind == PickupKind.Health ? 0x40FF40 : 0xE8C040, mx0, my0, size);
        }
        foreach (Enemy e in game.Enemies)
        {
            var m = ToMap(e.Position);
            Dot(m.X, m.Y, e.IsAlive ? 0xFF3030 : 0x602020, mx0, my0, size);
        }

        var pm = ToMap(center);
        var tip = ToMap(center + game.Player.Direction * 0.8);
        DrawLine(pm.X, pm.Y, tip.X, tip.Y, Opaque(0xFFFFFF), mx0, my0, size);
        Dot(pm.X, pm.Y, 0xFFFFFF, mx0, my0, size);

        int border = Opaque(0x2E6FA8);
        for (int i = 0; i < size; i++)
        {
            SetPixel(mx0 + i, my0, border); SetPixel(mx0 + i, my0 + size - 1, border);
            SetPixel(mx0, my0 + i, border); SetPixel(mx0 + size - 1, my0 + i, border);
        }
    }

    private void Dot(int x, int y, int rgb, int clipX, int clipY, int size)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (x + dx >= clipX && x + dx < clipX + size && y + dy >= clipY && y + dy < clipY + size)
                    SetPixel(x + dx, y + dy, Opaque(rgb));
    }

    private void DrawLine(int x0, int y0, int x1, int y1, int argb, int clipX, int clipY, int size)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        for (int guard = 0; guard < 4096; guard++)
        {
            if (x0 >= clipX && x0 < clipX + size && y0 >= clipY && y0 < clipY + size) SetPixel(x0, y0, argb);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void Tint(int rgb, double amount)
    {
        for (int i = 0; i < Pixels.Length; i++) Pixels[i] = Opaque(Blend(Pixels[i] & 0xFFFFFF, rgb, amount));
    }

    private void SetPixel(int x, int y, int argb)
    {
        if (x >= 0 && y >= 0 && x < Width && y < Height) Pixels[y * Width + x] = argb;
    }

    // ---------------------------------------------------------------- color helpers

    private static double Fog(double dist) => Math.Clamp(1.15 - dist / 18.0, 0.12, 1);

    private static int Opaque(int rgb) => unchecked((int)0xFF000000) | (rgb & 0xFFFFFF);

    internal static int Shade(int rgb, double f)
    {
        int r = Math.Clamp((int)(((rgb >> 16) & 0xFF) * f), 0, 255);
        int g = Math.Clamp((int)(((rgb >> 8) & 0xFF) * f), 0, 255);
        int b = Math.Clamp((int)((rgb & 0xFF) * f), 0, 255);
        return (r << 16) | (g << 8) | b;
    }

    private static int Blend(int a, int b, double t)
    {
        int r = (int)(((a >> 16) & 0xFF) * (1 - t) + ((b >> 16) & 0xFF) * t);
        int g = (int)(((a >> 8) & 0xFF) * (1 - t) + ((b >> 8) & 0xFF) * t);
        int bl = (int)((a & 0xFF) * (1 - t) + (b & 0xFF) * t);
        return (r << 16) | (g << 8) | bl;
    }

    private static int Lerp(int a, int b, double t) => Blend(a, b, Math.Clamp(t, 0, 1));
}
