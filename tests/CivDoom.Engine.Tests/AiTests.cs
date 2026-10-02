using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class AiTests
{
    private static MonsterDesign Design(string settings) => MonsterFile.Parse("proxy",
        "name = Tester\nhealth = 100\nattack delay = 60\n" + settings + "\n[colors]\nx = 808080\n[idle]\nxx\nxx", new List<string>());

    private static Game Arena(string[] map, MonsterDesign design)
    {
        var game = new Game(AsciiMap.Parse("t", map), 1, new MonsterSet(new[] { design }));
        game.Player.Health = 100_000; // the player is a training post here
        return game;
    }

    private static void Run(Game game, double seconds, GameInput? input = null)
    {
        input ??= new GameInput();
        for (double t = 0; t < seconds; t += 0.02) game.Update(0.02, input);
    }

    [Fact]
    public void MonstersFindTheirWayAroundWalls()
    {
        // The only way from the monster to you is round the end of the wall.
        Game game = Arena(new[]
        {
            "############",
            "#P.........#",
            "#..........#",
            "#########..#",
            "#E.........#",
            "############",
        }, Design("behavior = rusher\nspeed = 2.5\ndodge = 0\nmelee damage = 1"));
        Enemy e = game.Enemies[0];
        e.State = EnemyState.Chase;
        e.LastKnownPlayer = game.Player.Position;
        Run(game, 10);
        Assert.True(Vec2.Distance(e.Position, game.Player.Position) < 1.0, $"monster got stuck at {e.Position}");
    }

    [Fact]
    public void SkirmishersKeepTheirDistanceAndCircle()
    {
        Game game = Arena(new[]
        {
            "###############",
            "#.............#",
            "#.............#",
            "#.............#",
            "#......P......#",
            "#.......E.....#",
            "#.............#",
            "#.............#",
            "#.............#",
            "###############",
        }, Design("behavior = skirmisher\npreferred range = 3.5\nstrafe = 0.8\nspeed = 2\ndodge = 0"));
        Enemy e = game.Enemies[0];
        Vec2 me = game.Player.Position;
        Run(game, 1);
        double startAngle = Math.Atan2(e.Position.Y - me.Y, e.Position.X - me.X);
        Run(game, 4);
        double dist = Vec2.Distance(e.Position, me);
        Assert.InRange(dist, 2.3, 4.8);
        double endAngle = Math.Atan2(e.Position.Y - me.Y, e.Position.X - me.X);
        double turned = Math.Abs(Math.IEEERemainder(endAngle - startAngle, 2 * Math.PI));
        Assert.True(turned > 0.3, $"it should circle you, but only moved {turned:0.00} rad around");
    }

    [Fact]
    public void RushersCloseInAndBite()
    {
        Game game = Arena(new[] { "############", "#P.......E.#", "############" },
                          Design("behavior = rusher\nspeed = 2.5\nmelee damage = 7\ndodge = 0"));
        Run(game, 4);
        Assert.True(game.Player.Health < 100_000, "the rusher should have hit you");
        Assert.Empty(game.Projectiles); // it bit rather than shot
        Assert.Contains(game.DamageEvents, d => d.Amount == 7);
    }

    [Fact]
    public void GunfireWakesMonstersYouCantSee()
    {
        Game game = Arena(new[]
        {
            "#########",
            "#P......#",
            "####.####",
            "#E......#",
            "#########",
        }, Design("behavior = skirmisher"));
        Enemy e = game.Enemies[0];
        Run(game, 0.5);
        Assert.Equal(EnemyState.Idle, e.State); // can't see you
        game.Update(0.02, new GameInput { Fire = true });
        Assert.Equal(EnemyState.Chase, e.State); // heard that
    }

    [Fact]
    public void SnipersLeadAMovingTarget()
    {
        Game game = Arena(new[]
        {
            "###############",
            "#.............#",
            "#.............#",
            "#P...........E#",
            "#.............#",
            "#.............#",
            "###############",
        }, Design("behavior = sniper\naim lead = 1\nattack delay = 0.5\nspeed = 0\nfireball speed = 3\ndodge = 0"));
        Enemy e = game.Enemies[0];
        e.State = EnemyState.Chase;
        e.AttackCooldown = 0;
        game.Player.Angle = Math.PI / 2; // walk north, across its line of fire
        var walk = new GameInput { Forward = true };
        for (int i = 0; i < 100 && game.Projectiles.Count == 0; i++) game.Update(0.02, walk);
        Projectile shot = Assert.Single(game.Projectiles);
        Vec2 toPlayer = game.Player.Position - shot.Position;
        // The shot points north of where you are now, i.e. where you're going to be.
        Assert.True(Vec2.Cross(toPlayer, shot.Velocity) < 0, "the shot should be aimed ahead of you");
    }

    [Fact]
    public void BossesEnrageAtHalfHealth()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "##########", "#P....Z..#", "##########" }), 1);
        game.Player.Health = 100_000;
        Enemy boss = game.Enemies.Single(x => x.Design.Boss);
        boss.Health = boss.Design.Health / 2 - 1;
        Run(game, 0.1);
        Assert.True(boss.Enraged);
        Assert.Contains(game.MessageLog, m => m.Text.Contains("enraged"));
    }

    [Fact]
    public void ShippedMonstersHaveDistinctStyles()
    {
        MonsterSet set = MonsterSet.BuiltIn;
        Assert.Equal(Behavior.Rusher, set.Find("strayvertex")!.Behavior);
        Assert.Equal(Behavior.Tank, set.Find("xref")!.Behavior);
        Assert.Equal(Behavior.Sniper, set.Find("fatalerror")!.Behavior);
        Assert.Equal(Behavior.Skirmisher, set.Find("proxy")!.Behavior);
        Assert.True(set.Find("strayvertex")!.MeleeMax > 0);
        Assert.Equal(4, set.Designs.Select(d => d.Behavior).Distinct().Count());
    }

    [Fact]
    public void NavGridOpensUpWhenTheGateDoes()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "##########", "#P...Z.DF#", "##########" }), 1);
        game.Nav.SetTarget(game.Level.Exit!.Value);
        Assert.Null(game.Nav.DistanceFrom(game.Player.Position)); // the gate blocks the way
        Enemy boss = game.Enemies.Single(x => x.Design.Boss);
        boss.Health = 1;
        game.Player.Position = boss.Position - new Vec2(0.6, 0);
        var fire = new GameInput { Fire = true };
        for (int i = 0; i < 40 && boss.IsAlive; i++) game.Update(0.05, fire);
        game.Nav.SetTarget(game.Level.Exit!.Value + new Vec2(0.01, 0));
        Assert.NotNull(game.Nav.DistanceFrom(game.Player.Position));
    }
}
