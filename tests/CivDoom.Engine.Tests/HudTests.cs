using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class HudTests
{
    private static readonly string[] Arena =
    {
        "##############",
        "#P...Z.....F.#",
        "##############",
    };

    [Fact]
    public void EveryGlyphIsFiveBySeven()
    {
        Assert.All(PixelFont.AllGlyphRows, rows =>
        {
            Assert.Equal(PixelFont.GlyphHeight, rows.Length);
            Assert.All(rows, r => Assert.Equal(PixelFont.GlyphWidth, r.Length));
        });
        foreach (char ch in "ABCXYZ0123456789%/:-!?")
            Assert.Contains(ch, PixelFont.Characters);
        Assert.Equal(PixelFont.Glyph('?'), PixelFont.Glyph('~')); // unknown characters draw as '?'
    }

    [Fact]
    public void HudDrawsInEveryState()
    {
        var game = new Game(AsciiMap.Parse("t", Arena), 1);
        var r = new Renderer(400, 250);

        r.Render(game);
        int[] playing = (int[])r.Pixels.Clone();
        r.Hud.Visible = false;
        r.Render(game);
        Assert.True(playing.Zip(r.Pixels).Count(t => t.First != t.Second) > 5000, "the HUD should cover a good part of the screen");
        r.Hud.Visible = true;
        Screenshots.Save(r, "hud-playing.ppm");

        // The status bar occupies the bottom rows.
        int barTop = 250 - Hud.BarHeight;
        Assert.True(Enumerable.Range(0, 400).Select(x => playing[(barTop + 10) * 400 + x]).Distinct().Count() > 10);
    }

    [Fact]
    public void HitAndKillMarkersAreTracked()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "#########", "#P..E...#", "#########" }), 1);
        Assert.True(double.IsNegativeInfinity(game.LastHitTime));
        var fire = new GameInput { Fire = true };
        for (int i = 0; i < 100 && game.Enemies[0].IsAlive; i++) game.Update(0.05, fire);
        Assert.False(game.Enemies[0].IsAlive);
        Assert.True(game.LastKillTime > 0);
        Assert.True(game.LastHitTime >= game.LastKillTime);
        Assert.True(game.LastShotTime > 0);
        Assert.Contains(game.MessageLog, m => m.Text.Contains("down") || m.Text.Contains("eliminated"));
    }

    [Fact]
    public void DeathRecordsWhoDidItAndWhereFrom()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "##########", "#P....E..#", "##########" }), 1);
        game.Player.Health = 1;
        var idle = new GameInput();
        for (int i = 0; i < 400 && game.State == GameState.Playing; i++) game.Update(0.05, idle);
        Assert.Equal(GameState.Dead, game.State);
        Assert.Equal("Proxy Object", game.KilledBy);
        DamageEvent hit = Assert.Single(game.DamageEvents);
        Assert.True(hit.From.X > game.Player.Position.X, "the shot came from the imp's side");

        var r = new Renderer(400, 250);
        r.Render(game);
        Screenshots.Save(r, "hud-dead.ppm");
    }

    [Fact]
    public void ObjectiveTargetPointsAtTheBossThenTheFinish()
    {
        var game = new Game(AsciiMap.Parse("t", Arena), 1);
        Enemy boss = game.Enemies.Single(e => e.Design.Boss);
        var target = game.ObjectiveTarget!.Value;
        Assert.Equal(boss.Position, target.Position);
        Assert.Equal(boss.Design.Name.ToUpperInvariant(), target.Label);

        boss.Health = 1;
        game.Player.Position = boss.Position - new Vec2(0.6, 0);
        var fire = new GameInput { Fire = true };
        for (int i = 0; i < 40 && boss.IsAlive; i++) game.Update(0.05, fire);
        target = game.ObjectiveTarget!.Value;
        Assert.Equal("FINISH", target.Label);
        Assert.Equal(game.Level.Exit, target.Position);
    }

    [Fact]
    public void GradeRewardsCleanRuns()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "#######", "#P.E.F#", "#######" }), 1);
        Assert.Equal("C", Hud.Grade(game)); // no kills yet, no damage taken, no bosses
        game.Enemies[0].State = EnemyState.Dead;
        Assert.Equal("S", Hud.Grade(game));
    }

    [Fact]
    public void WeaponSwitchIsTimedForThePopup()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "#######", "#Ps...#", "#######" }), 1);
        var walk = new GameInput { Forward = true };
        for (int i = 0; i < 20 && game.Player.Weapon.Id != "shotgun"; i++) game.Update(0.05, walk);
        Assert.Equal("shotgun", game.Player.Weapon.Id);
        Assert.True(game.Time - game.WeaponSwitchTime < 1.0);
    }
}
