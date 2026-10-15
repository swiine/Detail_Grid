namespace PavementBuildup.Core.Tests;

public class SheetPackerTests
{
    private static bool Overlap(Pt a, PackItem ai, Pt b, PackItem bi) =>
        a.X < b.X + bi.Width - 1e-9 && b.X < a.X + ai.Width - 1e-9 && a.Y - ai.Height < b.Y - 1e-9 && b.Y - bi.Height < a.Y - 1e-9;

    [Fact]
    public void Details_fill_the_sheet_without_overlapping_and_stay_inside()
    {
        var items = new[] { new PackItem(120, 60), new PackItem(90, 80), new PackItem(150, 40), new PackItem(60, 60), new PackItem(100, 50), new PackItem(70, 30) };
        var r = SheetPacker.Pack(items, 0, 297, 420, 297, 10);
        Assert.All(r.Fits, Assert.True);
        for (int i = 0; i < items.Length; i++)
        {
            Assert.InRange(r.TopLefts[i].X, -1e-9, 420 - items[i].Width + 1e-9);
            Assert.InRange(r.TopLefts[i].Y - items[i].Height, -1e-9, 297);
            for (int j = i + 1; j < items.Length; j++) Assert.False(Overlap(r.TopLefts[i], items[i], r.TopLefts[j], items[j]), $"{i} overlaps {j}");
        }
        Assert.True(r.Rows <= 2);
    }

    [Fact]
    public void What_does_not_fit_goes_below_the_sheet_and_is_reported()
    {
        var items = new[] { new PackItem(200, 150), new PackItem(200, 150), new PackItem(200, 150) };
        var r = SheetPacker.Pack(items, 0, 297, 420, 297, 10);
        Assert.Equal(2, r.Fits.Count(f => f));
        int outside = Array.IndexOf(r.Fits.ToArray(), false);
        Assert.True(r.TopLefts[outside].Y < 0);
    }

    [Fact]
    public void A_sheet_with_no_size_is_refused() =>
        Assert.Throws<ArgumentException>(() => SheetPacker.Pack(new[] { new PackItem(1, 1) }, 0, 0, 0, 10, 1));
}
