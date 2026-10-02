using System.Runtime.CompilerServices;

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

    /// <summary>Show the radar (Tab / M).</summary>
    public bool ShowMap
    {
        get => Hud.ShowRadar;
        set => Hud.ShowRadar = value;
    }

    public void Render(Game game)
    {
        Player p = game.Player;
        (Vec2 eye, double angle) = game.Camera;
        Vec2 dir = Vec2.FromAngle(angle);
        Vec2 right = dir.PerpRight();
        int horizon = Height / 2;
        _theme = game.Theme;
        _time = game.Time;
        bool cutscene = game.ShowPlayerCharacter || game.Fade > 0; // no gun, crosshair or map while you watch yourself leave

        DrawSkyAndFloor(eye, angle, dir, right, horizon, game.Level.Exit, game.ExitOpen);
        DrawWalls(game.Level.Index, eye, dir, right, horizon);
        DrawSprites(game, eye, dir, right, horizon);
        if (!cutscene) DrawWeapon(p, game.State);

        if (p.DamageFlash > 0 && !cutscene) Tint(0xFF2020, Math.Min(0.3, p.DamageFlash * 0.3)); // the HUD shows where it came from
        if (p.PickupFlash > 0 && !cutscene) Tint(0xFFE060, p.PickupFlash * 0.25);
        if (game.State == GameState.Dead) Tint(0x800000, 0.45);
        if (game.Fade > 0) Tint(0x000000, game.Fade);

        Hud.Draw(new Canvas(Pixels, Width, Height), game, _planeLength);
    }

    /// <summary>The heads-up display drawn over each frame.</summary>
    public Hud Hud { get; } = new();

    private ThemeDesign _theme = ThemeSet.BuiltIn.Resolve(null, new Random(0));

    // ---------------------------------------------------------------- world

    private void DrawSkyAndFloor(Vec2 pos, double angle, Vec2 dir, Vec2 right, int horizon, Vec2? exit, bool exitOpen)
    {
        ThemeDesign theme = _theme;
        // Sky: a gradient, since drawings have no ceiling.
        for (int y = 0; y < horizon; y++)
        {
            double t = (double)y / horizon;
            int c = Lerp(theme.SkyTop, theme.SkyHorizon, t);
            Array.Fill(Pixels, Opaque(c), y * Width, Width);
        }

        // Skyline: a 360-degree panorama that turns with you, sitting on the horizon.
        if (theme.Panorama is { } pano)
        {
            int band = Math.Max(1, (int)(horizon * theme.SkylineHeight));
            int top = horizon - band;
            for (int x = 0; x < Width; x++)
            {
                double camX = 2.0 * (x + 0.5) / Width - 1;
                double rayAngle = angle - Math.Atan(camX * _planeLength);
                double u = -rayAngle / (2 * Math.PI);
                int px = (int)((u - Math.Floor(u)) * pano.Width) % pano.Width;
                for (int y = top; y < horizon; y++)
                {
                    int py = (y - top) * pano.Height / band;
                    int c = pano[px, py];
                    if ((c >>> 24) != 0) Pixels[y * Width + x] = Opaque(c & 0xFFFFFF);
                }
            }
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
                int c = theme.FloorColor;
                if (theme.FloorGrid is { } grid)
                {
                    if (fx < major || fy < major) c = grid;
                    else if (rowDist < 8 && (gx < minor * 4 || gy < minor * 4)) c = ThemeArt.Mix(theme.FloorColor, grid, 0.35);
                }
                if (exit is { } ex && (w - ex).LengthSquared < Game.ExitRadius * Game.ExitRadius)
                {
                    // Chequered finish-line pad: black/white when open, red/black while locked.
                    bool odd = (((int)Math.Floor(w.X * 6) + (int)Math.Floor(w.Y * 6)) & 1) == 1;
                    c = odd ? (exitOpen ? 0xF0F0F0 : 0xC02020) : 0x101010;
                }
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
                int texel = h.Wall.IsGate ? GateTexel(s, v) : _theme.WallTexel(_theme.WallBase(h.Wall), s, v);
                Pixels[y * Width + x] = Opaque(Shade(texel, fog));
            }
        }
    }

    /// <summary>Locked gate: diagonal yellow/black hazard stripes with a red bar across the middle.</summary>
    private static int GateTexel(double s, double v)
    {
        if (v > 0.42 && v < 0.58) return (((int)Math.Floor(s * 8)) & 1) == 0 ? 0xD02020 : 0xF0F0F0;
        return (((int)Math.Floor((s + v) * 5)) & 1) == 0 ? 0xE8C020 : 0x202020;
    }

    private readonly record struct SpriteDraw(Vec2 Position, double Depth, SpriteImage Image, double WorldHeight, double WorldWidth, double Lift, int Tint,
                                              double Glitch = 0, int Seed = 0);

    private void DrawSprites(Game game, Vec2 pos, Vec2 dir, Vec2 right, int horizon)
    {
        var list = new List<SpriteDraw>();
        void Add(Vec2 at, SpriteImage img, double h, double lift = 0, int tint = 0, double glitch = 0, int seed = 0)
        {
            Vec2 rel = at - pos;
            double depth = Vec2.Dot(rel, dir);
            if (depth < 0.05 || depth > MaxViewDistance) return;
            list.Add(new SpriteDraw(at, depth, img, h, h * img.Width / img.Height, lift, tint, glitch, seed));
        }

        if (game.Level.Exit is { } exit)
        {
            if (game.ExitOpen) Add(exit, Art.FinishFlagSprite, 0.75);
            else Add(exit, Art.LockedGateSprite, 0.4);
        }

        foreach (Pickup pk in game.Pickups)
        {
            if (pk.Taken) continue;
            if (pk.Weapon is { } w) Add(pk.Position, w.Pickup, w.PickupSize);
            else Add(pk.Position, pk.Kind == PickupKind.Health ? Art.MedkitSprite : Art.AmmoSprite, 0.3);
        }

        foreach (Enemy e in game.Enemies)
        {
            MonsterDesign d = e.Design;
            SpriteImage img = e.State switch
            {
                EnemyState.Dead => d.Dead,
                EnemyState.Attack => d.Attack,
                EnemyState.Chase => ((int)e.WalkPhase & 1) == 0 ? d.Idle : d.Walk,
                _ => d.Idle,
            };
            // Every frame uses the same pixel size as [idle], so a short [dead] picture stays short.
            double lift = e.IsAlive ? d.FloatHeight : 0; // floaters drop when they die
            Add(e.Position, img, img.Height * d.PixelSize, lift, e.PainTime > 0 ? 0xFFFFFF : 0,
                e.IsAlive ? d.Glitch : d.Glitch * 0.3, RuntimeHelpers.GetHashCode(e));
        }

        foreach (Projectile pr in game.Projectiles)
        {
            Add(pr.Position, pr.Sprite ?? Art.FireballSprite, pr.Size, lift: pr.FromPlayer ? 0.36 : 0.3);
        }

        if (game.ShowPlayerCharacter)
        {
            SpriteImage frame = game.PlayerFrame;
            Add(game.Player.Position, frame, game.PlayerLook.Size * frame.Height / game.PlayerLook.Idle.Height);
        }

        foreach (Effect fx in game.Effects)
        {
            // Explosions swell and rise a little as they fade.
            double t = fx.Age / fx.Duration;
            double size = fx.Size * (0.5 + t);
            Add(fx.Position, Art.FireballSprite, size, lift: Math.Max(0, 0.35 - size / 2));
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

        // Glitching monsters: bands of rows tear sideways and colour channels swap, changing ~24 times a second.
        int frame = (int)(_time * 24);
        int threshold = (int)(sd.Glitch * 350), swapThreshold = (int)(sd.Glitch * 120);

        for (int x = x0; x <= x1; x++)
        {
            if (sd.Depth >= _zBuffer[x]) continue;
            int tx = Math.Clamp((int)((x - left) / w * img.Width), 0, img.Width - 1);
            for (int y = y0; y <= y1; y++)
            {
                int ty = Math.Clamp((int)((y - top) / h * img.Height), 0, img.Height - 1);
                bool swap = false;
                int sx = tx;
                if (threshold > 0)
                {
                    uint hsh = GlitchHash(ty >> 1, frame, sd.Seed);
                    if (hsh % 1000 < threshold) sx = Math.Clamp(tx + (int)((hsh >> 10) % 7) - 3, 0, img.Width - 1);
                    swap = (hsh >> 14) % 1000 < swapThreshold;
                }
                int c = img[sx, ty];
                if ((c >>> 24) == 0) continue;
                int rgb = sd.Tint != 0 ? Blend(c & 0xFFFFFF, sd.Tint, 0.6) : c & 0xFFFFFF;
                if (swap) rgb = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
                Pixels[y * Width + x] = Opaque(Shade(rgb, Math.Max(fog, 0.35)));
            }
        }
    }

    private static uint GlitchHash(int a, int b, int c)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return h;
    }

    private double _time;

    // ---------------------------------------------------------------- HUD

    private void DrawWeapon(Player p, GameState state)
    {
        if (state == GameState.Dead) return;
        WeaponDesign w = p.Weapon;
        bool firing = p.MuzzleFlashTime > 0;
        SpriteImage gun = firing ? w.Fire : w.Hand;
        int scale = Math.Max(1, Height / 50);
        double bob = p.BobAmount;
        int bobX = (int)(Math.Sin(p.BobPhase) * 6 * bob);
        int bobY = (int)(Math.Abs(Math.Cos(p.BobPhase)) * 5 * bob);
        int kick = firing && !w.IsMelee ? scale * 2 : 0;
        // Centred on the [hand] picture so a wider [fire] picture doesn't jump sideways.
        int gx = Width / 2 - w.Hand.Width * scale / 2 - (gun.Width - w.Hand.Width) * scale / 2 + bobX;
        int gy = Height - Hud.BarHeight - gun.Height * scale + bobY + 6 + kick; // sits on the status bar
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
