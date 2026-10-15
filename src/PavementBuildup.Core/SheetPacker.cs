namespace PavementBuildup.Core;

public sealed record PackItem(double Width, double Height);

public sealed record PackResult(IReadOnlyList<Pt> TopLefts, IReadOnlyList<bool> Fits, int Rows);

public static class SheetPacker
{
    public static PackResult Pack(IReadOnlyList<PackItem> items, double left, double top, double width, double height, double gap)
    {
        if (!(width > 0) || !(height > 0)) throw new ArgumentException("The sheet area has no size.");
        var order = Enumerable.Range(0, items.Count).OrderByDescending(i => items[i].Height).ThenByDescending(i => items[i].Width).ToList();
        var shelves = new List<(double Top, double Height, double Used)>();
        var positions = new Pt[items.Count];
        var fits = new bool[items.Count];
        double nextTop = top;
        var overflow = new List<int>();
        foreach (var i in order)
        {
            var item = items[i];
            int shelf = shelves.FindIndex(s => s.Used + (s.Used > 0 ? gap : 0) + item.Width <= width + 1e-9 && item.Height <= s.Height + 1e-9);
            if (shelf < 0 && item.Width <= width + 1e-9 && (top - nextTop) + item.Height <= height + 1e-9)
            {
                shelves.Add((nextTop, item.Height, 0));
                nextTop -= item.Height + gap;
                shelf = shelves.Count - 1;
            }
            if (shelf < 0)
            {
                overflow.Add(i);
                continue;
            }
            var s = shelves[shelf];
            double x = left + s.Used + (s.Used > 0 ? gap : 0);
            positions[i] = new Pt(x, s.Top);
            fits[i] = true;
            shelves[shelf] = (s.Top, s.Height, x - left + item.Width);
        }
        double below = top - height - gap * 3, cursor = left, rowHeight = 0;
        foreach (var i in overflow)
        {
            var item = items[i];
            if (cursor > left && cursor + item.Width > left + width)
            {
                below -= rowHeight + gap;
                cursor = left;
                rowHeight = 0;
            }
            positions[i] = new Pt(cursor, below);
            cursor += item.Width + gap;
            rowHeight = Math.Max(rowHeight, item.Height);
        }
        return new PackResult(positions, fits, shelves.Count);
    }
}
