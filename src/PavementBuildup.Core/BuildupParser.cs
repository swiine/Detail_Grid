using System.Globalization;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

/// <summary>
/// Parses a one-line build-up such as
/// <c>40 SMA 10 surface; 60 AC 20 binder; 150mm Type 1 sub-base</c>.
/// Courses are separated by ';', '|', new lines or a slash with spaces round it (" / "),
/// so "40/60" bitumen grades survive. The thickness may lead ("40mm SMA") or trail ("SMA 40mm").
/// </summary>
public static partial class BuildupParser
{
    [GeneratedRegex(@"\s+/\s+|[;|\r\n]+")]
    private static partial Regex SeparatorRegex();

    // "40 SMA", "40mm SMA", "40 mm - SMA", "40.5mm: SMA"
    [GeneratedRegex(@"^(?<t>\d+(?:[.,]\d+)?)\s*(?:mm\b)?\s*[-:–]?\s*(?<d>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingThicknessRegex();

    // "SMA 10 surface 40mm", "SMA - 40 mm"
    [GeneratedRegex(@"^(?<d>.*?)\s*[-:–]?\s*(?<t>\d+(?:[.,]\d+)?)\s*mm$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingThicknessRegex();

    public static List<PavementLayer> Parse(string? text)
    {
        var errors = new List<string>();
        var layers = Parse(text, errors);
        if (errors.Count > 0)
            throw new FormatException(string.Join(Environment.NewLine, errors));
        return layers;
    }

    /// <summary>Parses what it can and reports courses it could not read in <paramref name="errors"/>.</summary>
    public static List<PavementLayer> Parse(string? text, List<string> errors)
    {
        var layers = new List<PavementLayer>();
        if (string.IsNullOrWhiteSpace(text))
            return layers;

        foreach (var raw in SeparatorRegex().Split(text))
        {
            var part = raw.Trim();
            if (part.Length == 0)
                continue;

            var m = LeadingThicknessRegex().Match(part);
            if (!m.Success || m.Groups["d"].Value.Trim().Length == 0)
            {
                var trailing = TrailingThicknessRegex().Match(part);
                if (trailing.Success && trailing.Groups["d"].Value.Trim().Length > 0)
                    m = trailing;
            }

            if (!m.Success || !TryParseNumber(m.Groups["t"].Value, out var thickness))
            {
                errors.Add($"Could not find a thickness in \"{part}\". Use e.g. \"40 SMA 10 surface course\".");
                continue;
            }

            var description = m.Groups["d"].Value.Trim();
            if (description.Length == 0)
            {
                errors.Add($"\"{part}\" has a thickness but no material description.");
                continue;
            }

            layers.Add(new PavementLayer { Description = description, ThicknessMm = thickness });
        }

        return layers;
    }

    /// <summary>Writes layers back to the one-line format accepted by <see cref="Parse(string?)"/>.</summary>
    public static string Format(IEnumerable<PavementLayer> layers) =>
        string.Join("; ", layers.Select(l =>
            $"{l.ThicknessMm.ToString("0.##", CultureInfo.InvariantCulture)} {l.Description}".Trim()));

    private static bool TryParseNumber(string s, out double value) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
