using System.Globalization;
using System.Text;

namespace PavementBuildup.Core;

/// <summary>Builds the command-line input that runs Express Tools _BREAKLINE for a detail's side edges.</summary>
public static class BreakLineScript
{
    /// <summary>
    /// One _BREAKLINE per edge: set Block, Size and Extension, then the edge's top and bottom points
    /// (WCS, "*x,y,z" so the UCS doesn't matter) and Enter for the symbol at the midpoint.
    /// </summary>
    public static string Build(string block, double size, double extension, IEnumerable<((double X, double Y, double Z) Top, (double X, double Y, double Z) Bottom)> edges)
    {
        var sb = new StringBuilder();
        foreach (var (top, bottom) in edges)
        {
            // BREAKLINE is an AutoLISP (Express Tools) command: plain names, no "_." prefixes.
            sb.Append("BREAKLINE\n")
              .Append("Block\n").Append(block).Append('\n')
              .Append("Size\n").Append(N(size)).Append('\n')
              .Append("Extension\n").Append(N(extension)).Append('\n')
              .Append(P(top)).Append('\n')
              .Append(P(bottom)).Append('\n')
              .Append('\n'); // symbol location <Midpoint>
        }
        return sb.ToString();
    }

    private static string P((double X, double Y, double Z) p) => $"*{N(p.X)},{N(p.Y)},{N(p.Z)}";

    private static string N(double v) => v.ToString("0.##########", CultureInfo.InvariantCulture);
}
