using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class ThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "civdoom-theme-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("city")]
    [InlineData("industrial")]
    [InlineData("desert")]
    [InlineData("night")]
    [InlineData("hell")]
    public void ShippedThemesParseAndDraw(string id)
    {
        var warnings = new List<string>();
        ThemeDesign t = ThemeFile.Parse(id, ThemeSet.DefaultText(id), warnings);
        Assert.Empty(warnings);
        if (id == "classic")
        {
            Assert.Null(t.Panorama);
            Assert.True(t.UseDrawingColors);
        }
        else
        {
            SpriteImage pano = t.Panorama!;
            int opaque = pano.Pixels.Count(p => (p >>> 24) != 0);
            Assert.InRange(opaque, pano.Pixels.Length / 10, pano.Pixels.Length * 9 / 10); // has a skyline, and sky above it
        }

        var level = new Level("t", BuiltInLevels.DetailGrid().Walls, new Vec2(1.5, 18.5), 0,
                              Array.Empty<EnemySpawn>(), Array.Empty<PickupSpawn>()) { ThemeId = id };
        var game = new Game(level, 1);
        Assert.Equal(id, game.Theme.Id);
        var r = new Renderer(320, 200);
        r.Render(game);
        Assert.True(r.Pixels.Distinct().Count() > 30);
        Screenshots.Save(r, $"theme-{id}.ppm");
    }

    [Fact]
    public void RandomThemeNeverPicksClassicAndUnknownFallsBack()
    {
        ThemeSet set = ThemeSet.BuiltIn;
        var rng = new Random(2);
        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++) seen.Add(set.Resolve(ThemeSet.RandomTheme, rng).Id);
        Assert.DoesNotContain("classic", seen);
        Assert.Equal(5, seen.Count);
        Assert.Equal("classic", set.Resolve(null, rng).Id);
        Assert.Equal("classic", set.Resolve("nowhere", rng).Id);
    }

    [Fact]
    public void ThemesLoadFromFoldersWithPictures()
    {
        ThemeSet first = ThemeSet.Load(_dir);
        Assert.Empty(first.Warnings);
        Assert.True(File.Exists(Path.Combine(_dir, "city", "city.txt")));

        Directory.CreateDirectory(Path.Combine(_dir, "mine"));
        File.WriteAllText(Path.Combine(_dir, "mine", "mine.txt"), "name = Mine\nwall style = brick\nfloor grid = none\n");
        File.WriteAllBytes(Path.Combine(_dir, "mine", "wall.png"), ImageTests.EncodePng(8, 8, (x, y) => unchecked((int)0xFF000000) | (x * 30 << 16)));
        ThemeDesign mine = ThemeSet.Load(_dir).Find("mine")!;
        Assert.Equal(WallStyle.Brick, mine.WallStyle);
        Assert.Null(mine.FloorGrid);
        Assert.NotNull(mine.WallImage);
        Assert.Equal(mine.WallImage![3, 0] & 0xFFFFFF, mine.WallTexel(0, 3.4 / 8 + 7, 0.01));
    }

    [Fact]
    public void BadThemeSettingsAreWarnings()
    {
        var warnings = new List<string>();
        ThemeDesign t = ThemeFile.Parse("x", "wall style = marble\nsky top = blue\n", warnings);
        Assert.Equal(WallStyle.Blocks, t.WallStyle);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void PlayerLoadsAndHasRunningFrames()
    {
        PlayerDesign p = PlayerDesign.BuiltIn;
        Assert.Equal("Corrupted Marine", p.Name);
        Assert.NotSame(p.Run, p.Run2);

        string folder = Path.Combine(_dir, "player");
        PlayerDesign loaded = PlayerDesign.Load(folder);
        Assert.True(loaded.Warnings.Count == 0, string.Join("; ", loaded.Warnings));
        Assert.True(File.Exists(Path.Combine(folder, "player.txt")));

        File.WriteAllBytes(Path.Combine(folder, "player.png"), ImageTests.EncodePng(10, 20, (x, y) => x < 5 ? unchecked((int)0xFF20A020) : unchecked((int)0xFF101010)));
        PlayerDesign pic = PlayerDesign.Load(folder);
        Assert.Equal(20, pic.Idle.Height);
        Assert.Equal(pic.Run[0, 0], pic.Run2[9, 0]); // second frame mirrored
    }

    [Fact]
    public void CutsceneIsRenderedWithTheRunner()
    {
        var game = new Game(AsciiMap.Parse("t", new[] { "########", "#P...F.#", "########" }), 1);
        var walk = new GameInput { Forward = true };
        for (int i = 0; i < 60 && game.State == GameState.Playing; i++) game.Update(0.05, walk);
        Assert.Equal(GameState.Exiting, game.State);
        game.Update(0.3, walk);
        var r = new Renderer(320, 200);
        r.Render(game);
        Screenshots.Save(r, "cutscene.ppm");
        int[] withRunner = (int[])r.Pixels.Clone();

        // Move the runner far away: the frame changes, so the character was on screen.
        game.Player.Position += new Vec2(200, 0);
        r.Render(game);
        Assert.True(withRunner.Zip(r.Pixels).Count(t => t.First != t.Second) > 100);
    }
}
