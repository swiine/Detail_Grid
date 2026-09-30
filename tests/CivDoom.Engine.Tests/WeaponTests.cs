using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class WeaponTests
{
    private static readonly string[] Shipped = { "chainsaw", "katana", "pistol", "shotgun", "chaingun", "rocketlauncher", "flamethrower" };

    private static Game Play(params string[] map) => new(AsciiMap.Parse("t", map), 1);

    private static WeaponDesign W(string id) => WeaponSet.BuiltIn.Find(id)!;

    private static void Give(Game game, string id, int ammo = 100)
    {
        WeaponDesign w = W(id);
        game.Player.Weapons.Add(w);
        if (w.UsesAmmo) game.Player.Ammo[w.AmmoType] = ammo;
        game.Player.Weapon = w;
    }

    private static void Run(Game game, GameInput input, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.02) game.Update(0.02, input);
    }

    [Theory]
    [MemberData(nameof(ShippedIds))]
    public void ShippedFilesParseCleanly(string id)
    {
        var warnings = new List<string>();
        WeaponDesign w = WeaponFile.Parse(id, WeaponSet.DefaultText(id), warnings);
        Assert.Empty(warnings);
        Assert.NotSame(w.Hand, w.Fire);
        Assert.NotSame(w.Hand, w.Pickup);
    }

    public static IEnumerable<object[]> ShippedIds() => Shipped.Select(id => new object[] { id });

    [Fact]
    public void StartWithPistolOnly()
    {
        Game game = Play("#####", "#P..#", "#####");
        Assert.Equal("pistol", game.Player.Weapon.Id);
        Assert.Single(game.Player.Weapons);
        Assert.Equal(50, game.Player.AmmoOf("bullets"));
        Assert.Equal(7, WeaponSet.BuiltIn.Designs.Count);
    }

    [Fact]
    public void WalkingOverAWeaponPicksItUpAndEquipsIt()
    {
        Game game = Play("#####", "#Ps.#", "#####");
        Run(game, new GameInput { Forward = true }, 1);
        Assert.Equal("shotgun", game.Player.Weapon.Id);
        Assert.Equal(8, game.Player.AmmoOf("shells"));
        Assert.Contains(game.Player.Weapons, w => w.Id == "pistol");
    }

    [Fact]
    public void AmmoBoxFillsTheHeldWeapon()
    {
        Game game = Play("#####", "#PA.#", "#####");
        Give(game, "shotgun", 1);
        Run(game, new GameInput { Forward = true }, 1);
        Assert.Equal(7, game.Player.AmmoOf("shells"));
    }

    [Fact]
    public void KatanaSweepsEverythingInFrontWithoutAmmo()
    {
        // Two imps right in front of the player (one slightly off to each side).
        var level = new Level("t", Array.Empty<Wall>(), new Vec2(0, 0), 0,
            new[] { new EnemySpawn(new Vec2(0.55, 0.25), "imp"), new EnemySpawn(new Vec2(0.55, -0.25), "imp"), new EnemySpawn(new Vec2(3, 0), "imp") },
            Array.Empty<PickupSpawn>());
        var game = new Game(level, 1);
        Give(game, "katana");
        Run(game, new GameInput { Fire = true }, 1.5);
        Assert.False(game.Enemies[0].IsAlive);
        Assert.False(game.Enemies[1].IsAlive);
        Assert.Equal(50, game.Enemies[2].Health); // out of reach
    }

    [Fact]
    public void ChainsawNeverRunsOut()
    {
        Game game = Play("#######", "#P.X..#", "#######");
        Give(game, "chainsaw");
        game.Player.Position = new Vec2(2.8, 1.5);
        Run(game, new GameInput { Fire = true }, 5);
        Assert.False(game.Enemies[0].IsAlive);
        Assert.Equal("chainsaw", game.Player.Weapon.Id);
    }

    [Fact]
    public void ShotgunFiresSevenPellets()
    {
        Game game = Play("##########", "#P..X....#", "##########");
        Give(game, "shotgun", 1);
        Run(game, new GameInput { Fire = true }, 0.05);
        Enemy brute = game.Enemies[0];
        Assert.True(brute.Health <= 180 - 7 * 5 * 0.5, $"brute health {brute.Health}"); // most pellets hit at this range
        Assert.Equal(0, game.Player.AmmoOf("shells"));
    }

    [Fact]
    public void RocketSplashHurtsNeighbours()
    {
        Game game = Play("##########", "#P...EE..#", "##########");
        Give(game, "rocketlauncher", 1);
        Run(game, new GameInput { Fire = true }, 1.5);
        Assert.False(game.Enemies[0].IsAlive);
        Assert.True(game.Enemies[1].Health < 50);
        Assert.DoesNotContain(game.Projectiles, p => p.FromPlayer); // the rocket exploded
    }

    [Fact]
    public void RocketsHurtYouUpClose()
    {
        Game game = Play("###", "#P#", "###");
        Give(game, "rocketlauncher", 1);
        Run(game, new GameInput { Fire = true }, 0.5);
        Assert.True(game.Player.Health < 100);
    }

    [Fact]
    public void FlamesBurnOutAfterTheirRange()
    {
        Game game = Play("############", "#P.E.....E.#", "############");
        Give(game, "flamethrower", 300);
        Run(game, new GameInput { Fire = true }, 2);
        Assert.True(game.Enemies[0].Health < 50, "near imp should burn");
        Assert.Equal(50, game.Enemies[1].Health);
        Assert.True(game.Player.AmmoOf("fuel") < 300);
    }

    [Fact]
    public void NumberKeysCycleWeaponsSharingASlot()
    {
        Game game = Play("#####", "#P..#", "#####");
        Give(game, "chainsaw");
        Give(game, "katana");
        game.Player.Weapon = W("pistol");
        game.Update(0.02, new GameInput { SelectSlot = 1 });
        string first = game.Player.Weapon.Id;
        game.Update(0.02, new GameInput { SelectSlot = 1 });
        Assert.NotEqual(first, game.Player.Weapon.Id);
        Assert.Contains(first, new[] { "chainsaw", "katana" });
        game.Update(0.02, new GameInput { SelectSlot = 2 });
        Assert.Equal("pistol", game.Player.Weapon.Id);
    }

    [Fact]
    public void RunningDryPicksAnotherWeapon()
    {
        Game game = Play("#####", "#P..#", "#####");
        Give(game, "shotgun", 0);
        Run(game, new GameInput { Fire = true }, 0.1);
        Assert.Equal("pistol", game.Player.Weapon.Id);
    }

    [Fact]
    public void DrawingsGetOneOfEachWeapon()
    {
        var g = new DrawingGeometry();
        Vec2[] c = { new(0, 0), new(300, 0), new(300, 300), new(0, 300) };
        for (int i = 0; i < 4; i++) g.Segments.Add(new DrawingSegment(c[i], c[(i + 1) % 4], 0xFFFFFF));
        var game = new Game(LevelBuilder.FromDrawing(g, 10, 1), 1);
        List<string> ids = game.Pickups.Where(p => p.Weapon != null).Select(p => p.Weapon!.Id).ToList();
        Assert.Equal(6, ids.Count);
        Assert.Equal(6, ids.Distinct().Count());
        Assert.DoesNotContain("pistol", ids);
    }

    [Fact]
    public void ReloadSwapsHeldWeapon()
    {
        Game game = Play("#####", "#P..#", "#####");
        string text = WeaponSet.DefaultText("pistol").Replace("name = Pistol", "name = Blaster");
        var set = new WeaponSet(new[] { WeaponFile.Parse("pistol", text, new List<string>()) });
        game.ReloadWeapons(set);
        Assert.Equal("Blaster", game.Player.Weapon.Name);
    }
}
