namespace CivDoom.Engine;

/// <summary>
/// The heads-up display, drawn in pixel art on top of the 3D view: a Doom-style status bar with a live
/// face, compass with objective marker, rotating radar, boss bar, damage arrows, hit markers, low-health
/// vignette, message feed, weapon popup, and the death and level-complete screens.
/// </summary>
public sealed class Hud
{
    public const int BarHeight = 36;

    private const int Gold = 0xFFD24A, Red = 0xE03030, White = 0xF0F0F0, Grey = 0x8A8E96, Green = 0x7CFF7C;

    private readonly Dictionary<Enemy, double> _bossChip = new(ReferenceEqualityComparer.Instance);
    private double _lastTime = double.NaN;
    private double _dt;

    /// <summary>Hide everything except the cutscene/death/complete screens (H key).</summary>
    public bool Visible { get; set; } = true;

    public bool ShowRadar { get; set; } = true;

    /// <summary>A hint line to show (e.g. "click to capture the mouse"), or null.</summary>
    public string? Hint { get; set; }

    public void Draw(Canvas c, Game g, double fovTan)
    {
        _dt = double.IsNaN(_lastTime) || g.Time < _lastTime ? 0 : g.Time - _lastTime;
        _lastTime = g.Time;
        (Vec2 eye, double heading) = g.Camera;

        if (g.State == GameState.Won && g.Fade >= 1)
        {
            DrawCompleteCard(c, g, finished: true);
            return;
        }
        if (g.ShowPlayerCharacter || g.Fade > 0) return; // let the cutscene play clean

        if (g.State == GameState.Playing) DrawVignette(c, g);
        if (Visible)
        {
            if (g.State == GameState.Playing)
            {
                DrawDamageArrows(c, g, eye, heading);
                DrawCrosshair(c, g);
                DrawWeaponPopup(c, g);
            }
            DrawCompass(c, g, eye, heading);
            DrawObjective(c, g);
            DrawBossBar(c, g);
            if (ShowRadar) DrawRadar(c, g, eye, heading);
            DrawFeed(c, g);
            DrawStatusBar(c, g);
            if (Hint != null && g.State == GameState.Playing) c.TextCentered(Hint, c.Width / 2, c.Height - BarHeight - 46, 0xC8C8C8);
        }

        if (g.State == GameState.Dead) DrawDeathScreen(c, g);
        if (g.State == GameState.Won) DrawCompleteCard(c, g, finished: false);
    }

    // ------------------------------------------------------------------ status bar

    private void DrawStatusBar(Canvas c, Game g)
    {
        Player p = g.Player;
        int y0 = c.Height - BarHeight, w = c.Width;
        int panel = ThemeArt.Mix(0x3A3D44, g.Theme.WallAccent, 0.35);
        c.Bevel(0, y0, w, BarHeight, panel);
        // Rivets.
        for (int x = 6; x < w; x += 48) { c.Set(x, y0 + 2, Renderer.Shade(panel, 1.6)); c.Set(x, y0 + BarHeight - 3, Renderer.Shade(panel, 0.4)); }

        int sunk = Renderer.Shade(panel, 0.55);
        double scale = w / 400.0;
        int X(double v) => (int)(v * scale);

        // AMMO
        int ax = X(3), aw = X(66);
        c.Bevel(ax, y0 + 2, aw, BarHeight - 4, sunk, sunken: true);
        int? shots = p.ShotsLeft(p.Weapon);
        string ammo = shots is { } n ? n.ToString() : "--";
        int ammoW = PixelFont.MeasureWidth(ammo, 3);
        bool dry = shots == 0;
        c.BigText(ammo, ax + (aw - ammoW) / 2, y0 + 4, dry ? 0xFF6060 : 0xFFE0A0, dry ? 0x801010 : 0xC86010);
        c.TextCentered(Truncate(p.Weapon.Name.ToUpperInvariant(), aw / PixelFont.Advance), ax + aw / 2, y0 + 26, Grey);

        // HEALTH
        int hx = X(71), hw = X(82);
        c.Bevel(hx, y0 + 2, hw, BarHeight - 4, sunk, sunken: true);
        string hp = $"{p.Health}%";
        bool critical = p.Health <= 25 && ((int)(g.Time * 4) & 1) == 0;
        (int top, int bottom) = p.Health > 60 ? (0xC8FFC8, 0x30A030) : p.Health > 25 ? (0xFFF0A0, 0xC89010) : (0xFFA0A0, 0xA01010);
        if (critical) (top, bottom) = (0xFFFFFF, 0xFF4040);
        c.BigText(hp, hx + (hw - PixelFont.MeasureWidth(hp, 3)) / 2, y0 + 4, top, bottom);
        c.TextCentered("HEALTH", hx + hw / 2, y0 + 26, Grey);

        // ARMS: weapon slots you own, current one boxed.
        int wx = X(156), ww = X(50);
        c.Bevel(wx, y0 + 2, ww, BarHeight - 4, sunk, sunken: true);
        var owned = p.Weapons.Select(x => x.Slot).ToHashSet();
        for (int slot = 1; slot <= 6; slot++)
        {
            int col = (slot - 1) % 3, row = (slot - 1) / 3;
            int sx = wx + 6 + col * 14, sy = y0 + 5 + row * 10;
            bool current = p.Weapon.Slot == slot;
            if (current) c.Fill(sx - 2, sy - 1, 9, 9, Renderer.Shade(Gold, 0.45));
            c.Text(slot.ToString(), sx, sy, current ? White : owned.Contains(slot) ? Gold : Renderer.Shade(panel, 0.9), shadow: null);
        }
        c.TextCentered("ARMS", wx + ww / 2, y0 + 26, Grey);

        // FACE
        int fx = X(209), fw = 32;
        c.Bevel(fx, y0 + 2, fw, BarHeight - 4, 0x101010, sunken: true);
        c.Sprite(FaceFor(g), fx + 3, y0 + 3);

        // KILLS / BOSSES / TIME
        int kx = X(244), kw = X(74);
        c.Bevel(kx, y0 + 2, kw, BarHeight - 4, sunk, sunken: true);
        int bossCount = g.BossCount;
        c.Text($"KILLS {g.Kills}/{g.Enemies.Count}", kx + 3, y0 + 5, White);
        c.Text(bossCount > 0 ? $"BOSS  {g.BossesKilled}/{bossCount}" : "BOSS  -", kx + 3, y0 + 14, bossCount > 0 && g.BossesKilled == bossCount ? Green : White);
        double t = g.State == GameState.Playing ? g.Time : Math.Max(g.CompletionTime, g.Time);
        c.Text($"TIME  {(int)(t / 60)}:{(int)t % 60:00}", kx + 3, y0 + 23, Grey);

        // AMMO INVENTORY
        int ix = X(321), iw = w - ix - 3;
        c.Bevel(ix, y0 + 2, iw, BarHeight - 4, sunk, sunken: true);
        var types = g.Weapons.Designs.Where(d => d.UsesAmmo)
            .GroupBy(d => d.AmmoType).Select(grp => (Type: grp.Key, Max: grp.Max(d => d.MaxAmmo))).Take(4).ToList();
        for (int i = 0; i < types.Count; i++)
        {
            (string type, int max) = types[i];
            bool cur = p.Weapon.UsesAmmo && p.Weapon.AmmoType == type;
            int have = p.AmmoOf(type);
            int col = cur ? Gold : have > 0 ? White : Renderer.Shade(Grey, 0.7);
            int ry = y0 + 4 + i * 7;
            c.Text(type.ToUpperInvariant()[..Math.Min(4, type.Length)], ix + 3, ry, col, shadow: null);
            string v = $"{have}/{max}";
            c.Text(v, ix + iw - 3 - PixelFont.MeasureWidth(v), ry, col, shadow: null);
        }
    }

    // ------------------------------------------------------------------ face

    /// <summary>The current status-bar face, e.g. for hosts that draw their own HUD.</summary>
    public SpriteImage CurrentFace(Game g) => FaceFor(g);

    private enum Expression { Neutral, Grin, Ouch, Dead }

    private SpriteImage FaceFor(Game g)
    {
        Player p = g.Player;
        Expression expr = Expression.Neutral;
        int look = 0;
        DamageEvent? hit = g.DamageEvents.Count > 0 ? g.DamageEvents[^1] : null;
        if (g.State == GameState.Dead) expr = Expression.Dead;
        else if (hit is { } h && g.Time - h.Time < 0.5)
        {
            expr = Expression.Ouch;
            double rel = Bearing(g.Camera.Position, g.Camera.Angle, h.From);
            look = Math.Abs(rel) < 0.35 ? 0 : rel > 0 ? -1 : 1; // look toward the attacker
        }
        else if (p.PickupFlash > 0.4 || g.Time - g.WeaponSwitchTime < 0.6 && g.Time - g.LastShotTime > 0.6 && p.PickupFlash > 0) expr = Expression.Grin;
        else if (g.Time - g.LastKillTime < 0.8) expr = Expression.Grin;
        else look = (int)(Hash((int)(g.Time / 1.3)) % 3) - 1;

        return Face(p.Health, expr, look, g.Time);
    }

    private static uint Hash(int a)
    {
        uint h = (uint)a * 2654435761u;
        h ^= h >> 15;
        return h;
    }

    /// <summary>The corrupted marine's portrait (26 x 30). More corruption and blood as health drops.</summary>
    private static SpriteImage Face(int health, Expression expr, int look, double time)
    {
        const int W = 26, H = 30;
        var px = new int[W * H];
        void P(int x, int y, int rgb) { if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = unchecked((int)0xFF000000) | rgb; }
        void Ell(double cx, double cy, double rx, double ry, int rgb)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (((x + 0.5 - cx) / rx) * ((x + 0.5 - cx) / rx) + ((y + 0.5 - cy) / ry) * ((y + 0.5 - cy) / ry) <= 1) P(x, y, rgb);
        }
        void Rect(int x0, int y0, int x1, int y1, int rgb) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) P(x, y, rgb); }

        double hurt = 1 - Math.Clamp(health / 100.0, 0, 1);
        int skin = expr == Expression.Dead ? 0x8A8A80 : ThemeArt.Mix(0xE0B890, 0x9FB58A, hurt);
        int shadow = Renderer.Shade(skin, 0.75);

        // Helmet, then the face.
        Ell(13, 13, 12.5, 13, 0x3E5A3A);
        Ell(13, 10, 11, 8, 0x4E7A3E);
        Rect(4, 10, 21, 10, 0x2A3E28);
        Ell(13, 19, 8.5, 10, skin);
        Ell(13, 25, 7, 4, shadow);

        // Brows.
        int brow = 0x2A2018;
        int slant = expr is Expression.Ouch ? 1 : 0;
        Rect(7, 13 - slant, 10, 13 - slant, brow); Rect(16, 13 - slant, 19, 13 - slant, brow);
        if (expr == Expression.Ouch) { P(10, 13, brow); P(16, 13, brow); }

        // Eyes: the left one is corrupted and glows; the right one looks around.
        if (expr == Expression.Dead)
        {
            foreach (int ex in new[] { 8, 17 }) { P(ex, 15, 0x200000); P(ex + 2, 17, 0x200000); P(ex + 1, 16, 0x200000); P(ex + 2, 15, 0x200000); P(ex, 17, 0x200000); }
        }
        else
        {
            int glow = ThemeArt.Mix(0xA020C0, 0xFF60FF, 0.5 + 0.5 * Math.Sin(time * 6));
            Rect(8, 15, 10, 17, 0x1A0A20); Rect(8 + Math.Max(0, look), 15, 9 + Math.Max(0, look), 16, glow);
            Rect(16, 15, 18, 17, 0xF4F4F4); Rect(17 + look, 15, 17 + look, 17, 0x101010);
            if (expr == Expression.Ouch) { Rect(16, 15, 18, 15, skin); Rect(8, 15, 10, 15, skin); } // squint
        }

        // Nose and mouth.
        P(13, 19, shadow); P(13, 20, shadow);
        switch (expr)
        {
            case Expression.Grin:
                Rect(9, 23, 17, 24, 0x200808); Rect(10, 23, 16, 23, 0xF0F0E8); P(9, 22, 0x200808); P(17, 22, 0x200808);
                break;
            case Expression.Ouch:
                Ell(13, 24, 2.5, 2.2, 0x300808); P(13, 25, 0x802020);
                break;
            case Expression.Dead:
                Rect(10, 24, 16, 24, 0x200808); Rect(12, 25, 13, 26, 0xB04050);
                break;
            default:
                Rect(10, 24, 16, 24, 0x401818);
                break;
        }

        // Corruption veins spread from the glowing eye as you get hurt.
        int veins = (int)(hurt * 6);
        int vein = 0x7A2A8A;
        (int, int)[][] paths =
        {
            new[] { (7, 17), (6, 18), (6, 19), (5, 20) },
            new[] { (8, 18), (8, 19), (7, 20), (7, 21) },
            new[] { (7, 14), (6, 13), (5, 12) },
            new[] { (10, 18), (11, 19), (11, 20) },
            new[] { (6, 16), (5, 16), (4, 17), (4, 18) },
            new[] { (9, 19), (9, 21), (10, 22) },
        };
        for (int i = 0; i < veins && i < paths.Length; i++) foreach ((int x, int y) in paths[i]) P(x, y, vein);

        // Blood.
        (int, int)[] blood = { (19, 12), (20, 13), (6, 22), (18, 21), (19, 22), (15, 27), (8, 26), (20, 18), (5, 15), (12, 12), (17, 25), (10, 21) };
        int drops = (int)(hurt * blood.Length);
        for (int i = 0; i < drops; i++) { (int x, int y) = blood[i]; P(x, y, 0xA01010); if (i % 3 == 0) P(x, y + 1, 0x700808); }

        return new SpriteImage(W, H, px);
    }

    // ------------------------------------------------------------------ compass, objective, boss bar

    /// <summary>Angle of <paramref name="target"/> relative to the view (radians, positive = to the left).</summary>
    private static double Bearing(Vec2 eye, double heading, Vec2 target)
    {
        Vec2 d = target - eye;
        double a = Math.Atan2(d.Y, d.X) - heading;
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private static void DrawCompass(Canvas c, Game g, Vec2 eye, double heading)
    {
        int cx = c.Width / 2, y = 2, half = 80, h = 11;
        const double spanDeg = 90;
        c.Fill(cx - half - 2, y, half * 2 + 5, h, 0x000000, 0.55);
        c.Line(cx - half - 2, y + h, cx + half + 2, y + h, 0x606870);

        double headDeg = heading * 180 / Math.PI;
        for (int deg = 0; deg < 360; deg += 15)
        {
            double rel = deg - headDeg;
            rel = ((rel % 360) + 540) % 360 - 180;
            if (Math.Abs(rel) > spanDeg) continue;
            int x = cx - (int)Math.Round(rel / spanDeg * half);
            string? label = deg switch { 0 => "E", 90 => "N", 180 => "W", 270 => "S", _ => null };
            if (label != null) c.Text(label, x - 2, y + 2, label == "N" ? Red : White, shadow: null);
            else c.Line(x, y + h - (deg % 45 == 0 ? 5 : 3), x, y + h - 1, 0xA0A8B0);
        }
        // Heading notch.
        c.Set(cx, y + h + 1, Gold); c.Line(cx - 1, y + h + 2, cx + 1, y + h + 2, Gold);

        if (g.ObjectiveTarget is { } obj)
        {
            double rel = Bearing(eye, heading, obj.Position) * 180 / Math.PI;
            bool boss = obj.Label != "FINISH" && obj.Label != "HOSTILE";
            int col = boss ? 0xFF50FF : Gold;
            double dist = Vec2.Distance(eye, obj.Position) * g.Level.DrawingScale;
            string distText = dist >= 100 ? $"{dist:0}" : $"{dist:0.0}";
            if (Math.Abs(rel) <= spanDeg)
            {
                int x = cx - (int)Math.Round(rel / spanDeg * half);
                // Diamond marker.
                for (int i = 0; i < 4; i++) { c.Line(x - (3 - i), y + 1 + i, x + (3 - i), y + 1 + i, col); c.Line(x - (3 - i), y + 7 - i, x + (3 - i), y + 7 - i, col); }
                c.TextCentered(distText, x, y + h + 3, col);
            }
            else
            {
                // Off to the side: an arrow at the edge of the strip.
                bool left = rel > 0;
                int x = left ? cx - half - 10 : cx + half + 6;
                c.Text(left ? "<" : ">", x, y + 2, col);
                c.TextCentered(distText, x + 2, y + h + 3, col);
            }
        }
    }

    private static void DrawObjective(Canvas c, Game g)
    {
        if (g.State != GameState.Playing) return;
        string text = Truncate(g.Objective.ToUpperInvariant(), 52);
        c.TextCentered(text, c.Width / 2, 24, Gold);
    }

    private void DrawBossBar(Canvas c, Game g)
    {
        if (g.ActiveBoss is not { } boss) return;
        double frac = Math.Clamp((double)boss.Health / boss.Design.Health, 0, 1);
        double chip = _bossChip.TryGetValue(boss, out double old) ? Math.Max(frac, old - _dt * 0.35) : frac;
        _bossChip[boss] = chip;

        int w = (int)(c.Width * 0.45), x0 = (c.Width - w) / 2, y = 42;
        c.TextCentered(boss.Design.Name.ToUpperInvariant(), c.Width / 2, y - 9, 0xFF8080);
        c.Fill(x0 - 1, y - 1, w + 2, 8, 0x000000);
        c.Fill(x0, y, w, 6, 0x401010);
        c.Fill(x0, y, (int)(w * chip), 6, 0xF0E0E0);          // recent damage, draining away
        for (int x = 0; x < (int)(w * frac); x++)
            for (int yy = 0; yy < 6; yy++)
                c.Set(x0 + x, y + yy, ThemeArt.Mix(0xFF5050, 0xA01010, yy / 6.0));
        for (int i = 1; i < 10; i++) c.Line(x0 + w * i / 10, y, x0 + w * i / 10, y + 5, 0x000000, 0.5);
    }

    // ------------------------------------------------------------------ radar

    private void DrawRadar(Canvas c, Game g, Vec2 eye, double heading)
    {
        int r = 30, cx = c.Width - r - 5, cy = r + 4;
        const double range = 9; // world units shown from centre to rim
        double scale = r / range;
        Vec2 fwd = Vec2.FromAngle(heading), right = fwd.PerpRight();
        bool Inside(int x, int y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= (r - 1) * (r - 1);
        (int X, int Y) Map(Vec2 p)
        {
            Vec2 rel = p - eye;
            return (cx + (int)Math.Round(Vec2.Dot(rel, right) * scale), cy - (int)Math.Round(Vec2.Dot(rel, fwd) * scale));
        }
        (int X, int Y, bool Clamped) MapClamped(Vec2 p)
        {
            Vec2 rel = p - eye;
            double x = Vec2.Dot(rel, right) * scale, y = -Vec2.Dot(rel, fwd) * scale;
            double len = Math.Sqrt(x * x + y * y);
            bool clamp = len > r - 4;
            if (clamp) { x *= (r - 4) / len; y *= (r - 4) / len; }
            return (cx + (int)Math.Round(x), cy + (int)Math.Round(y), clamp);
        }

        c.Disc(cx, cy, r, 0x05080C, 0.7);
        // Range rings.
        for (int a = 0; a < 360; a += 6)
        {
            double rad = a * Math.PI / 180;
            c.Blend(cx + (int)(Math.Cos(rad) * r * 0.5), cy + (int)(Math.Sin(rad) * r * 0.5), 0x2E6FA8, 0.5);
        }

        Vec2 box = new(range, range);
        foreach (Wall w in g.Level.Index.Query(eye - box, eye + box))
        {
            var a = Map(w.A);
            var b = Map(w.B);
            c.Line(a.X, a.Y, b.X, b.Y, w.IsGate ? 0xE8C020 : 0xB8C0C8, 1, Inside);
        }

        foreach (Pickup pk in g.Pickups)
        {
            if (pk.Taken) continue;
            var m = Map(pk.Position);
            if (!Inside(m.X, m.Y)) continue;
            c.Set(m.X, m.Y, pk.Kind switch { PickupKind.Health => 0x40FF40, PickupKind.Weapon => 0x40E0FF, _ => 0xE8C040 });
        }
        foreach (Projectile pr in g.Projectiles)
        {
            if (pr.FromPlayer) continue;
            var m = Map(pr.Position);
            if (Inside(m.X, m.Y)) c.Set(m.X, m.Y, 0xFF9020);
        }
        double pulse = 0.5 + 0.5 * Math.Sin(g.Time * 6);
        foreach (Enemy e in g.Enemies)
        {
            if (!e.IsAlive) continue;
            if (e.Design.Boss)
            {
                var m = MapClamped(e.Position);
                c.Disc(m.X, m.Y, 2 + pulse, 0xFF40FF);
            }
            else
            {
                var m = Map(e.Position);
                if (Inside(m.X, m.Y)) c.Disc(m.X, m.Y, 1, 0xFF3030);
            }
        }
        if (g.Level.Exit is { } exit)
        {
            var m = MapClamped(exit);
            int col = g.ExitOpen ? 0xFFFFFF : 0xC02020;
            for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                    c.Set(m.X + x, m.Y + y, ((x + y) & 1) == 0 ? col : 0x000000);
        }

        // You: an arrow pointing up (the way you face).
        c.Line(cx, cy - 4, cx - 3, cy + 3, White); c.Line(cx, cy - 4, cx + 3, cy + 3, White); c.Line(cx - 3, cy + 3, cx + 3, cy + 3, White);

        // Rim, with north marked.
        for (int a = 0; a < 360; a += 2)
        {
            double rad = a * Math.PI / 180;
            c.Set(cx + (int)Math.Round(Math.Cos(rad) * r), cy + (int)Math.Round(Math.Sin(rad) * r), 0x606870);
        }
        double north = Bearing(eye, heading, eye + new Vec2(0, 1));
        int nx = cx - (int)Math.Round(Math.Sin(north) * (r - 5)), ny = cy - (int)Math.Round(Math.Cos(north) * (r - 5));
        c.Text("N", nx - 2, ny - 3, Red, shadow: 0x000000);
    }

    // ------------------------------------------------------------------ combat feedback

    private static void DrawCrosshair(Canvas c, Game g)
    {
        int cx = c.Width / 2, cy = c.Height / 2; // the view's centre (the horizon)
        WeaponDesign w = g.Player.Weapon;
        double kick = g.Time - g.LastShotTime < 0.12 ? 3 : 0;
        int gap = (int)(2 + (w.IsMelee ? 0 : w.Spread * 0.35) + kick);
        int len = w.IsMelee ? 2 : 4;
        int col = g.Player.CanFire(w) ? 0x9CFF9C : 0xFF6060;
        void Tick(int x0, int y0, int x1, int y1)
        {
            c.Line(x0 + 1, y0 + 1, x1 + 1, y1 + 1, 0x000000, 0.6);
            c.Line(x0, y0, x1, y1, col);
        }
        Tick(cx - gap - len, cy, cx - gap, cy);
        Tick(cx + gap, cy, cx + gap + len, cy);
        Tick(cx, cy - gap - len, cx, cy - gap);
        Tick(cx, cy + gap, cx, cy + gap + len);
        c.Set(cx, cy, col);

        // Hit marker (white X) and kill marker (bigger, red).
        bool kill = g.Time - g.LastKillTime < 0.3, hit = g.Time - g.LastHitTime < 0.12;
        if (kill || hit)
        {
            int m = kill ? 7 : 5, i = kill ? gap + 1 : gap;
            int mc = kill ? 0xFF3030 : 0xFFFFFF;
            c.Line(cx - i - m, cy - i - m, cx - i, cy - i, mc); c.Line(cx + i, cy - i, cx + i + m, cy - i - m, mc);
            c.Line(cx - i - m, cy + i + m, cx - i, cy + i, mc); c.Line(cx + i, cy + i, cx + i + m, cy + i + m, mc);
        }
    }

    private static void DrawDamageArrows(Canvas c, Game g, Vec2 eye, double heading)
    {
        int cx = c.Width / 2, cy = c.Height / 2;
        double rx = c.Width * 0.3, ry = c.Height * 0.32;
        foreach (DamageEvent d in g.DamageEvents)
        {
            double age = g.Time - d.Time;
            if (age > 1.0) continue;
            double rel = Bearing(eye, heading, d.From);
            double alpha = 1 - age;
            for (double a = rel - 0.28; a <= rel + 0.28; a += 0.01)
            {
                double fade = 1 - Math.Abs(a - rel) / 0.28;
                for (int t = 0; t < 4; t++)
                {
                    double k = 1 + t * 0.02;
                    c.Blend(cx - (int)(Math.Sin(a) * rx * k), cy - (int)(Math.Cos(a) * ry * k), 0xFF2020, alpha * (0.4 + 0.6 * fade));
                }
            }
        }
    }

    private static void DrawVignette(Canvas c, Game g)
    {
        int health = g.Player.Health;
        if (health >= 35) return;
        double intensity = (35 - health) / 35.0 * (0.45 + 0.25 * Math.Sin(g.Time * 5));
        int h = c.Height - BarHeight;
        double cx = c.Width / 2.0, cy = h / 2.0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < c.Width; x++)
            {
                double nx = (x - cx) / cx, ny = (y - cy) / cy;
                double edge = Math.Clamp((nx * nx + ny * ny - 0.35) / 0.9, 0, 1);
                if (edge > 0) c.Blend(x, y, 0xB00000, edge * intensity);
            }
    }

    private static void DrawWeaponPopup(Canvas c, Game g)
    {
        double age = g.Time - g.WeaponSwitchTime;
        if (age > 1.6) return;
        double alpha = 1 - Math.Max(0, (age - 1.0) / 0.6);
        WeaponDesign w = g.Player.Weapon;
        c.TextCentered($"[{w.Slot}] {w.Name.ToUpperInvariant()}", c.Width / 2, (int)(c.Height * 0.62), Gold, 2, alpha);
    }

    private static void DrawFeed(Canvas c, Game g)
    {
        int y = c.Height - BarHeight - 10;
        int shown = 0;
        for (int i = g.MessageLog.Count - 1; i >= 0 && shown < 4; i--)
        {
            HudMessage m = g.MessageLog[i];
            double age = g.Time - m.Time;
            if (age > 5) break;
            double alpha = Math.Clamp(5 - age, 0, 1);
            string text = Truncate(m.Text.ToUpperInvariant(), 60);
            bool important = text.Contains("BOSS") || text.Contains("GATE") || text.Contains("FINISH") || text.Contains(" IS DEAD");
            c.Text(text, 4, y, important ? Gold : White, 1, alpha);
            y -= 9;
            shown++;
        }
    }

    // ------------------------------------------------------------------ end screens

    private static void DrawDeathScreen(Canvas c, Game g)
    {
        int cx = c.Width / 2, y = (int)(c.Height * 0.22);
        c.Fill(0, y - 8, c.Width, 70, 0x000000, 0.45);
        c.TextCentered("YOU DIED", cx, y, 0xFF3030, 4);
        string by = g.KilledBy is { } k ? (k.StartsWith("your") ? $"KILLED BY {k.ToUpperInvariant()}" : $"KILLED BY THE {k.ToUpperInvariant()}") : "KILLED";
        c.TextCentered(by, cx, y + 36, White);
        if (((int)(g.Time * 2) & 1) == 0) c.TextCentered("PRESS ENTER TO TRY AGAIN", cx, y + 50, Gold);
    }

    /// <summary>Grade for the level-complete card.</summary>
    public static string Grade(Game g)
    {
        double kills = g.Enemies.Count == 0 ? 1 : (double)g.Kills / g.Enemies.Count;
        double bosses = g.BossCount == 0 ? 1 : (double)g.BossesKilled / g.BossCount;
        double health = 1 - Math.Min(g.DamageTaken, 300) / 300.0;
        double score = kills * 50 + health * 30 + bosses * 20;
        return score >= 90 ? "S" : score >= 75 ? "A" : score >= 55 ? "B" : score >= 35 ? "C" : "D";
    }

    private static void DrawCompleteCard(Canvas c, Game g, bool finished)
    {
        int cx = c.Width / 2, y = (int)(c.Height * 0.12);
        if (!finished) c.Fill(0, 0, c.Width, c.Height - BarHeight, 0x000000, 0.55);
        c.TextCentered(finished ? "LEVEL COMPLETE" : "LEVEL CLEAR", cx, y, Gold, 3);
        double t = finished ? g.CompletionTime : g.Time;
        (string, string)[] rows =
        {
            ("TIME", $"{(int)(t / 60)}:{(int)t % 60:00}"),
            ("KILLS", $"{g.Kills}/{g.Enemies.Count}"),
            ("BOSSES", g.BossCount > 0 ? $"{g.BossesKilled}/{g.BossCount}" : "-"),
            ("DAMAGE", g.DamageTaken.ToString()),
            ("AREA", g.Theme.Name.ToUpperInvariant()),
        };
        int ry = y + 34;
        foreach ((string label, string value) in rows)
        {
            c.Text(label, cx - 110, ry, Grey, 2);
            c.Text(value, cx + 110 - PixelFont.MeasureWidth(value, 2), ry, White, 2);
            ry += 18;
        }
        string grade = Grade(g);
        int gc = grade switch { "S" => 0xFFD24A, "A" => 0x7CFF7C, "B" => 0x7CC8FF, "C" => 0xF0F0F0, _ => 0xFF8080 };
        c.Text("GRADE", cx - 110, ry + 6, Grey, 2);
        c.BigText(grade, cx + 110 - PixelFont.MeasureWidth(grade, 4), ry, 0xFFFFFF, gc, 4);
        if (((int)(g.Time * 2) & 1) == 0) c.TextCentered("PRESS ENTER TO PLAY AGAIN", cx, ry + 40, Gold);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..Math.Max(1, max - 1)] + ".";
}
