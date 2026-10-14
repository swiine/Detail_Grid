using PavementBuildup.Core;

namespace PavementBuildup.Export;

/// <summary>How much room a laid-out detail takes, labels, dimensions and titles included (estimated text widths).</summary>
internal static class DetailExtents
{
    public readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    public static Box Of(DetailGeometry g)
    {
        double th = g.TextHeight;
        double longest = g.Labels.Count == 0 ? 0 : g.Labels.Max(l => l.Text.Split('\n').Max(line => line.Length)) * th * 0.62;
        double titleWidth = g.Titles.Count == 0 ? 0 : g.Titles.Max(t => t.Text.Length * t.Height * 0.62);
        double minX = Math.Min(g.Dimensions.Count > 0 ? g.Dimensions.Min(d => d.X) - th * 3 : -th, g.Width / 2 - titleWidth / 2 - th);
        double maxX = Math.Max(g.Labels.Count > 0 ? g.Labels[0].TextAt.X + longest : g.Width, g.Width / 2 + titleWidth / 2) + th;
        double minY = new[] { g.Subgrade?.Bottom ?? (g.InterfaceLevels.Count > 0 ? g.InterfaceLevels[^1] : 0) }
            .Concat(g.Labels.Select(l => l.TextAt.Y - l.LineCount * th)).Concat(g.Titles.Select(t => t.At.Y - t.Height)).Min() - th * 2;
        double maxY = th * 2;
        return new Box(minX, minY, maxX, maxY);
    }
}
