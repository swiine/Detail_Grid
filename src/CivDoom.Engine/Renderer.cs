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

    /// <summary>Highest the camera goes (just under the top of the walls).</summary>
    public const double MaxEyeZ = 0.96;

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
        // Never above the walls' tops: walls have no top faces to show.
        double eyeZ = Math.Min(game.CameraZ, MaxEyeZ);
        Vec2 dir = Vec2.FromAngle(angle);
        Vec2 right = dir.PerpRight();
        int horizon = Height / 2;
        _theme = game.Theme;
        _time = game.Time;
        bool cutscene = game.ShowPlayerCharacter || game.Fade > 0; // no gun, crosshair or map while you watch yourself leave

        DrawSky(angle, horizon);
        DrawWorld(game.Level, eye, eyeZ, dir, right, horizon, game.ExitOpen);
        DrawSprites(game, eye, eyeZ, dir, right, horizon);
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

    private void DrawSky(double angle, int horizon)
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
    }

    private const int MaxOccluders = 24;
    private readonly List<RayHit> _ledgeHits = new();

    // Per column: distances at which raised floors start hiding things behind them, and the screen row
    // from which they do (everything at or below that row, further away, is hidden).
    private double[] _occDistance = Array.Empty<double>();
    private int[] _occClip = Array.Empty<int>();
    private int[] _occCount = Array.Empty<int>();

    /// <summary>
    /// Draws each screen column front to back: the floor up to the first platform edge, the edge's face
    /// if it steps up, the platform top beyond it, and so on until the wall at the end of the ray. Rows
    /// already drawn are never overwritten, so nearer platforms hide what's behind them.
    /// </summary>
    private void DrawWorld(Level level, Vec2 pos, double eyeZ, Vec2 dir, Vec2 right, int horizon, bool exitOpen)
    {
        if (_occCount.Length != Width)
        {
            _occCount = new int[Width];
            _occDistance = new double[Width * MaxOccluders];
            _occClip = new int[Width * MaxOccluders];
        }
        Terrain terrain = level.Terrain;
        Vec2 light = new Vec2(0.6, 0.8);
        Platform? startPlatform = terrain.PlatformAt(pos);

        for (int x = 0; x < Width; x++)
        {
            double camX = 2.0 * (x + 0.5) / Width - 1;
            Vec2 rayDir = dir + right * (_planeLength * camX);
            RayHit? wallHit = level.Index.CastRay(pos, rayDir, MaxViewDistance);
            double wallDist = wallHit is { } wh ? Math.Max(wh.Distance, 1e-4) : double.PositiveInfinity;
            _zBuffer[x] = wallDist;
            _occCount[x] = 0;

            if (terrain.IsFlat) _ledgeHits.Clear();
            else terrain.Ledges.CastAll(pos, rayDir, Math.Min(wallDist, MaxViewDistance), _ledgeHits);

            Platform? plat = startPlatform;
            double h = plat?.Height ?? 0;
            int clip = Height; // rows from here down are drawn
            foreach (RayHit lh in _ledgeHits)
            {
                double t = Math.Max(lh.Distance, 1e-4);
                clip = DrawFloorSpan(x, clip, pos, rayDir, eyeZ, h, plat, t, horizon, level.Exit, exitOpen);

                Platform? next = terrain.PlatformAt(pos + rayDir * (lh.Distance + 1e-4));
                double hn = next?.Height ?? 0;
                if (hn > h + 1e-9 && clip > 0)
                {
                    // The face of a step up, from the floor we're on to the top of the next platform.
                    double scale = FocalLength / t;
                    double top = horizon + (eyeZ - hn) * scale;
                    double bottom = horizon + (eyeZ - h) * scale;
                    int y0 = Math.Max(0, (int)Math.Ceiling(top)), y1 = Math.Min(clip - 1, (int)Math.Floor(bottom));
                    double fog = Fog(t) * (0.72 + 0.28 * Math.Abs(Vec2.Dot(lh.Wall.Normal, light)));
                    int baseColor = next?.Color ?? Terrain.DefaultColor;
                    for (int y = y0; y <= y1; y++)
                    {
                        double z = eyeZ - (y - horizon + 0.5) / scale; // world height of this pixel
                        Pixels[y * Width + x] = Opaque(Shade(LedgeTexel(baseColor, lh.WallOffset, z, hn), fog));
                    }
                    clip = Math.Min(clip, Math.Max(0, y0));
                }
                if (_occCount[x] < MaxOccluders)
                {
                    int k = x * MaxOccluders + _occCount[x]++;
                    _occDistance[k] = t;
                    _occClip[k] = clip;
                }
                plat = next;
                h = hn;
            }

            clip = DrawFloorSpan(x, clip, pos, rayDir, eyeZ, h, plat, wallDist, horizon, level.Exit, exitOpen);
            if (wallHit is not { } hit || clip <= 0) continue;

            {
                double scale = FocalLength / wallDist;
                double top = horizon - (1 - eyeZ) * scale;
                double bottom = horizon + eyeZ * scale; // where the wall meets the ground
                int y0 = Math.Max(0, (int)Math.Ceiling(top));
                int y1 = Math.Min(clip - 1, (int)Math.Floor(bottom));
                double side = 0.72 + 0.28 * Math.Abs(Vec2.Dot(hit.Wall.Normal, light));
                double fog = Fog(wallDist) * side;
                double s = hit.WallOffset;
                for (int y = y0; y <= y1; y++)
                {
                    double v = (y - top) / (bottom - top); // 0 at top of wall, 1 at floor
                    int texel = hit.Wall.IsGate ? GateTexel(s, v) : _theme.WallTexel(_theme.WallBase(hit.Wall), s, v);
                    Pixels[y * Width + x] = Opaque(Shade(texel, fog));
                }
            }
        }
    }

    /// <summary>
    /// Draws the floor at height <paramref name="h"/> in column <paramref name="x"/> out to distance
    /// <paramref name="far"/>, below row <paramref name="clip"/>. Returns the new clip row.
    /// </summary>
    private int DrawFloorSpan(int x, int clip, Vec2 pos, Vec2 rayDir, double eyeZ, double h, Platform? plat, double far,
                              int horizon, Vec2? exit, bool exitOpen)
    {
        double above = eyeZ - h;
        if (above <= 1e-6) return clip; // looking at it edge-on or from below
        int yFar = double.IsFinite(far) ? Math.Max(horizon, (int)Math.Ceiling(horizon + above * FocalLength / far - 0.5)) : horizon;
        ThemeDesign theme = _theme;
        // Platform tops are lighter concrete, so you can tell high ground from the floor below.
        int floorColor = plat is { } p ? ThemeArt.Mix(ThemeArt.Mix(theme.FloorColor, p.Color, 0.7), 0xFFFFFF, 0.12) : theme.FloorColor;
        int? gridColor = theme.FloorGrid is { } g ? (plat is { } p2 ? ThemeArt.Mix(g, p2.Color, 0.4) : g) : null;
        for (int y = Math.Max(yFar, 0); y < clip; y++)
        {
            double rowDist = FocalLength * above / (y - horizon + 0.5);
            Vec2 w = pos + rayDir * rowDist;
            int c = plat != null && plat.DistanceToEdge(w) < Math.Max(0.05, rowDist * 0.004)
                ? 0xE0B830 // yellow safety edge round the top
                : FloorTexel(w, rowDist, floorColor, gridColor, exit, exitOpen);
            Pixels[y * Width + x] = Opaque(Shade(c, Fog(rowDist)));
        }
        return Math.Min(clip, Math.Max(yFar, 0));
    }

    private static int FloorTexel(Vec2 w, double rowDist, int floorColor, int? gridColor, Vec2? exit, bool exitOpen)
    {
        if (exit is { } ex && (w - ex).LengthSquared < Game.ExitRadius * Game.ExitRadius)
        {
            // Chequered finish-line pad: black/white when open, red/black while locked.
            bool odd = (((int)Math.Floor(w.X * 6) + (int)Math.Floor(w.Y * 6)) & 1) == 1;
            return odd ? (exitOpen ? 0xF0F0F0 : 0xC02020) : 0x101010;
        }
        if (gridColor is not { } grid) return floorColor;
        // Line thickness grows with distance so the grid doesn't shimmer to nothing.
        double major = Math.Clamp(0.015 * rowDist, 0.02, 0.2);
        double minor = major * 0.6;
        double fx = w.X - Math.Floor(w.X), fy = w.Y - Math.Floor(w.Y);
        if (fx < major || fy < major) return grid;
        double gx = w.X * 4 - Math.Floor(w.X * 4), gy = w.Y * 4 - Math.Floor(w.Y * 4);
        if (rowDist < 8 && (gx < minor * 4 || gy < minor * 4)) return ThemeArt.Mix(floorColor, grid, 0.35);
        return floorColor;
    }

    /// <summary>The side of a platform: concrete block courses with a yellow safety edge along the top.</summary>
    private static int LedgeTexel(int color, double s, double z, double top)
    {
        if (top - z < 0.035) return 0xE0B830;
        double course = z * 8, frac = course - Math.Floor(course);
        if (frac < 0.1) return Shade(color, 0.62);
        double brick = s * 4 + ((int)Math.Floor(course) & 1) * 0.5;
        if (brick - Math.Floor(brick) < 0.04) return Shade(color, 0.7);
        return color;
    }

    /// <summary>The row from which things further than <paramref name="depth"/> are hidden by raised floors in column x.</summary>
    private int OcclusionClip(int x, double depth)
    {
        int clip = Height, n = _occCount[x], k0 = x * MaxOccluders;
        for (int k = 0; k < n && _occDistance[k0 + k] < depth; k++) clip = _occClip[k0 + k];
        return clip;
    }

    /// <summary>Locked gate: diagonal yellow/black hazard stripes with a red bar across the middle.</summary>
    private static int GateTexel(double s, double v)
    {
        if (v > 0.42 && v < 0.58) return (((int)Math.Floor(s * 8)) & 1) == 0 ? 0xD02020 : 0xF0F0F0;
        return (((int)Math.Floor((s + v) * 5)) & 1) == 0 ? 0xE8C020 : 0x202020;
    }

    /// <param name="Lift">Height of the picture's bottom edge above the ground.</param>
    private readonly record struct SpriteDraw(Vec2 Position, double Depth, SpriteImage Image, double WorldHeight, double WorldWidth, double Lift, int Tint,
                                              double Glitch = 0, int Seed = 0);

    private void DrawSprites(Game game, Vec2 pos, double eyeZ, Vec2 dir, Vec2 right, int horizon)
    {
        var list = new List<SpriteDraw>();
        Terrain terrain = game.Level.Terrain;
        void Add(Vec2 at, SpriteImage img, double h, double lift = 0, int tint = 0, double glitch = 0, int seed = 0, double? floor = null)
        {
            Vec2 rel = at - pos;
            double depth = Vec2.Dot(rel, dir);
            if (depth < 0.05 || depth > MaxViewDistance) return;
            lift += floor ?? terrain.FloorAt(at);
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
                e.IsAlive ? d.Glitch : d.Glitch * 0.3, RuntimeHelpers.GetHashCode(e), e.Z);
        }

        foreach (Projectile pr in game.Projectiles)
        {
            Add(pr.Position, pr.Sprite ?? Art.FireballSprite, pr.Size, lift: pr.Z - pr.Size * 0.2, floor: 0);
        }

        if (game.ShowPlayerCharacter)
        {
            SpriteImage frame = game.PlayerFrame;
            Add(game.Player.Position, frame, game.PlayerLook.Size * frame.Height / game.PlayerLook.Idle.Height, floor: game.Player.Z);
        }

        foreach (Effect fx in game.Effects)
        {
            // Explosions swell and rise a little as they fade.
            double t = fx.Age / fx.Duration;
            double size = fx.Size * (0.5 + t);
            double floor = terrain.FloorAt(fx.Position);
            Add(fx.Position, Art.FireballSprite, size, lift: Math.Max(floor, fx.Z - size / 2), floor: 0);
        }

        list.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        foreach (SpriteDraw sd in list) DrawSprite(sd, pos, eyeZ, right, horizon);
    }

    private void DrawSprite(SpriteDraw sd, Vec2 pos, double eyeZ, Vec2 right, int horizon)
    {
        double lateral = Vec2.Dot(sd.Position - pos, right);
        double scale = FocalLength / sd.Depth;
        double screenX = Width / 2.0 + lateral * scale;
        double h = sd.WorldHeight * scale;
        double w = sd.WorldWidth * scale;
        double bottom = horizon + (eyeZ - sd.Lift) * scale;
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
            int yEnd = Math.Min(y1, OcclusionClip(x, sd.Depth) - 1); // behind a ledge?
            for (int y = y0; y <= yEnd; y++)
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

    /// <summary>Darkens (f &lt; 1) or brightens a 0xRRGGBB colour.</summary>
    public static int Shade(int rgb, double f)
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
