using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class EdgeProfileTests
{
    private static readonly List<Pt> Left = new() { new(0, 0), new(0, -40), new(5, -45), new(-5, -55), new(0, -60), new(0, -100) };
    private static readonly List<Pt> Right = new() { new(100, 0), new(100, -100) };

    [Fact]
    public void X_follows_the_break()
    {
        Assert.Equal(0, EdgeProfile.XAt(Left, -10), 9);
        Assert.Equal(5, EdgeProfile.XAt(Left, -45), 9);
        Assert.Equal(0, EdgeProfile.XAt(Left, -50), 9);
        Assert.Equal(-5, EdgeProfile.XAt(Left, -55), 9);
        Assert.Equal(0, EdgeProfile.XAt(Left, 10), 9);
        Assert.Equal(0, EdgeProfile.XAt(Left, -200), 9);
    }

    [Fact]
    public void A_band_crossing_the_break_follows_it()
    {
        var loop = EdgeProfile.Band(Left, Right, -42, -58);
        Assert.Equal(new[]
        {
            new Pt(2, -42), new Pt(5, -45), new Pt(-5, -55), new Pt(-2, -58),
            new Pt(100, -58), new Pt(100, -42),
        }, loop);
    }

    [Fact]
    public void A_band_away_from_the_break_is_a_plain_rectangle()
    {
        var loop = EdgeProfile.Band(Left, Right, 0, -30);
        Assert.Equal(new[] { new Pt(0, 0), new Pt(0, -30), new Pt(100, -30), new Pt(100, 0) }, loop);
    }
}
