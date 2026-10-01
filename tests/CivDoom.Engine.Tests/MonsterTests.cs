using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class MonsterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "civdoom-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private const string Tiny = """
        name = Blob
        health = 7
        size = 0.5
        damage = 3
        [colors]
        g = 00FF00
        [idle]
        .gg.
        gggg
        """;

    [Theory]
    [InlineData("imp")]
    [InlineData("brute")]
    [InlineData("cacodemon")]
    [InlineData("lostsoul")]
    [InlineData("arachnotron")]
    [InlineData("surveyor")]
    [InlineData("cyberdemon")]
    [InlineData("mastermind")]
    [InlineData("baron")]
    [InlineData("excavator")]
    [InlineData("inspector")]
    public void ShippedFilesParseCleanly(string id)
    {
        var warnings = new List<string>();
        MonsterDesign d = MonsterFile.Parse(id, MonsterSet.DefaultText(id), warnings);
        Assert.Empty(warnings);
        Assert.NotSame(d.Idle, d.Walk);
        Assert.NotSame(d.Idle, d.Dead);
        Assert.True(d.Dead.Height < d.Idle.Height);
    }

    [Fact]
    public void BuiltInHasImpAndBrute()
    {
        MonsterSet set = MonsterSet.BuiltIn;
        Assert.Equal(50, set.Find("imp")!.Health);
        Assert.Equal(180, set.Find("brute")!.Health);
        Assert.Equal(20, set.Find("brute")!.DamageMin);
    }

    [Fact]
    public void ParsesSettingsColorsAndPictures()
    {
        var warnings = new List<string>();
        MonsterDesign d = MonsterFile.Parse("blob", Tiny, warnings);
        Assert.Equal("Blob", d.Name);
        Assert.Equal(7, d.Health);
        Assert.Equal((3, 3), (d.DamageMin, d.DamageMax));
        Assert.Equal(4, d.Idle.Width);
        Assert.Equal(2, d.Idle.Height);
        Assert.Equal(0, d.Idle[0, 0]);
        Assert.Equal(unchecked((int)0xFF00FF00), d.Idle[1, 0]);
        Assert.Same(d.Idle, d.Dead); // missing frames fall back to idle...
        Assert.Contains(warnings, w => w.Contains("[dead]")); // ...with a warning
    }

    [Fact]
    public void MistakesAreForgiven()
    {
        string text = """
            health = lots
            [colors]
            a = FF0000
            [idle]
            aaaa
            aZ
            """;
        var warnings = new List<string>();
        MonsterDesign d = MonsterFile.Parse("oops", text, warnings);
        Assert.Equal(50, d.Health);
        Assert.Equal("Oops", d.Name);
        Assert.Contains(warnings, w => w.Contains("not a number"));
        Assert.Contains(warnings, w => w.Contains("different widths"));
        Assert.Contains(warnings, w => w.Contains("Z"));
        Assert.Equal(0, d.Idle[1, 1]); // unknown letter is see-through
    }

    [Fact]
    public void MissingIdleIsAnError()
    {
        Assert.Throws<FormatException>(() => MonsterFile.Parse("x", "health = 5\n[walk]\naa", new List<string>()));
    }

    [Fact]
    public void LoadCreatesDefaultFilesAndPicksUpNewMonsters()
    {
        MonsterSet first = MonsterSet.Load(_dir);
        Assert.True(File.Exists(Path.Combine(_dir, "imp", "imp.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "cyberdemon", "cyberdemon.txt")));
        Assert.Empty(first.Warnings);
        Assert.Equal(11, first.Designs.Count);

        File.WriteAllText(Path.Combine(_dir, "blob.txt"), Tiny);
        MonsterSet second = MonsterSet.Load(_dir);
        Assert.Equal(12, second.Designs.Count);
        Assert.Equal("Blob", second.Find("blob")!.Name);
    }

    [Fact]
    public void BrokenShippedFileFallsBackToOriginal()
    {
        MonsterSet.Load(_dir);
        File.WriteAllText(Path.Combine(_dir, "imp", "imp.txt"), "health = 5");
        MonsterSet set = MonsterSet.Load(_dir);
        Assert.Equal(50, set.Find("imp")!.Health);
        Assert.Contains(set.Warnings, w => w.StartsWith("imp.txt") && w.Contains("original"));
    }

    [Fact]
    public void LevelMarkersAndRandomSpawnsUseTheSet()
    {
        MonsterDesign blob = MonsterFile.Parse("blob", Tiny, new List<string>());
        var set = new MonsterSet(new[] { blob });
        Level level = AsciiMap.Parse("t", new[] { "#####", "#PEM#", "#####" });
        var game = new Game(level, 1, set);
        // "imp" isn't in this set, so both become blobs.
        Assert.All(game.Enemies, e => Assert.Same(blob, e.Design));
        Assert.All(game.Enemies, e => Assert.Equal(7, e.Health));
    }

    [Fact]
    public void ReloadKeepsDamageTaken()
    {
        Level level = AsciiMap.Parse("t", new[] { "#####", "#P.E#", "#####" });
        var game = new Game(level, 1);
        Enemy imp = game.Enemies[0];
        imp.Health -= 20;

        string buffed = MonsterSet.DefaultText("imp").Replace("health         = 50", "health         = 100");
        var set = new MonsterSet(new[] { MonsterFile.Parse("imp", buffed, new List<string>()) });
        game.ReloadMonsters(set);
        Assert.Equal(80, imp.Health);
        Assert.Equal(100, imp.Design.Health);
    }

    [Fact]
    public void SubFolderLayoutWinsOverOldFlatFiles()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "blob"));
        File.WriteAllText(Path.Combine(_dir, "blob", "blob.txt"), Tiny);
        File.WriteAllText(Path.Combine(_dir, "blob.txt"), Tiny.Replace("health = 7", "health = 99"));
        File.WriteAllText(Path.Combine(_dir, "flat.txt"), Tiny);
        MonsterSet set = MonsterSet.Load(_dir);
        Assert.Equal(7, set.Find("blob")!.Health);
        Assert.NotNull(set.Find("flat")); // the old flat layout still works
        Assert.Contains(set.Warnings, w => w.Contains("blob.txt ignored"));
    }

    [Fact]
    public void BossesNeverSpawnAsRandomMonsters()
    {
        MonsterSet set = MonsterSet.BuiltIn;
        Assert.Equal(5, set.Bosses.Count());
        var rng = new Random(3);
        for (int i = 0; i < 500; i++) Assert.False(set.Resolve(null, rng).Boss);
        for (int i = 0; i < 50; i++) Assert.True(set.Resolve(MonsterSet.RandomBoss, rng).Boss);
    }
}
