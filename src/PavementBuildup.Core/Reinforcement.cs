using System.Globalization;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

public enum BarFace { Bottom, Top }

/// <summary>
/// One mat of reinforcement in a course: main bars cut by the section (drawn as dots) at a cover
/// from the top or bottom face, with optional transverse bars running along the section (drawn as a line).
/// </summary>
public sealed class Reinforcement
{
    /// <summary>Face the cover is measured from.</summary>
    public BarFace Face { get; set; } = BarFace.Bottom;

    /// <summary>Cover in mm from the face to the outside of the main bars.</summary>
    public double CoverMm { get; set; } = 50;

    public double DiameterMm { get; set; } = 16;
    public double SpacingMm { get; set; } = 150;

    /// <summary>Bar type prefix as typed (e.g. "H", "T", "N"). Blank uses the CAD standard's prefix.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Transverse bars (0 = none). They sit on the inner side of the main bars.</summary>
    public double TransverseDiameterMm { get; set; }
    public double TransverseSpacingMm { get; set; }

    public bool HasTransverse => TransverseDiameterMm > 0 && TransverseSpacingMm > 0;

    /// <summary>Total depth the mat occupies from the face: cover + main bar + transverse bar.</summary>
    public double DepthFromFaceMm => CoverMm + DiameterMm + (HasTransverse ? TransverseDiameterMm : 0);

    public Reinforcement Clone() => (Reinforcement)MemberwiseClone();
}

/// <summary>
/// Short notation for reinforcement, one mat per comma:
/// <c>H16@150 c50</c>, <c>top H12@200 cover 40 + H10@300</c>, <c>bottom 16@150 c/c c50</c>.
/// Face defaults to bottom; the "+ ..." part is the transverse bars.
/// </summary>
public static partial class ReinforcementParser
{
    [GeneratedRegex(
        @"^(?:(?<face>top|bottom|btm|bot)\b\s*:?\s*)?" +
        @"(?<prefix>[a-z]{0,3}?)\s*(?<dia>\d+(?:\.\d+)?)\s*(?:mm)?\s*@\s*(?<sp>\d+(?:\.\d+)?)\s*(?:mm)?\s*(?:c/c|crs|ctrs|centres|centers)?\s*,?\s*" +
        @"(?:cover|cov|cvr|c)\s*[=:]?\s*(?<cover>\d+(?:\.\d+)?)\s*(?:mm)?" +
        @"(?:\s*(?:\+|&|and)\s*(?<tprefix>[a-z]{0,3}?)\s*(?<tdia>\d+(?:\.\d+)?)\s*(?:mm)?\s*@\s*(?<tsp>\d+(?:\.\d+)?)\s*(?:mm)?\s*(?:c/c|crs|ctrs|centres|centers)?)?" +
        @"\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex MatRegex();

    // Mats are separated by commas, but "H16@150, c50" style commas before the cover belong to the mat.
    [GeneratedRegex(@",(?!\s*(?:cover|cov|cvr|c)\s*[=:]?\s*\d)", RegexOptions.IgnoreCase)]
    private static partial Regex MatSeparatorRegex();

    public const string Help = "e.g. \"H16@150 c50\" or \"top H12@200 c40, bottom H16@150 c50 + H10@300\"";

    public static List<Reinforcement> Parse(string? text)
    {
        var mats = new List<Reinforcement>();
        if (string.IsNullOrWhiteSpace(text))
            return mats;

        foreach (var raw in MatSeparatorRegex().Split(text))
        {
            var part = raw.Trim();
            if (part.Length == 0)
                continue;

            var m = MatRegex().Match(part);
            if (!m.Success)
                throw new FormatException($"Can't read reinforcement \"{part}\". Use {Help}.");

            var face = m.Groups["face"].Value.ToLowerInvariant();
            var mat = new Reinforcement
            {
                Face = face == "top" ? BarFace.Top : BarFace.Bottom,
                Prefix = m.Groups["prefix"].Value.ToUpperInvariant(),
                DiameterMm = Num(m.Groups["dia"].Value),
                SpacingMm = Num(m.Groups["sp"].Value),
                CoverMm = Num(m.Groups["cover"].Value),
            };
            if (m.Groups["tdia"].Success)
            {
                mat.TransverseDiameterMm = Num(m.Groups["tdia"].Value);
                mat.TransverseSpacingMm = Num(m.Groups["tsp"].Value);
            }
            if (mat.DiameterMm <= 0 || mat.SpacingMm <= 0 || (m.Groups["tdia"].Success && (mat.TransverseDiameterMm <= 0 || mat.TransverseSpacingMm <= 0)))
                throw new FormatException($"Bar size and spacing must be greater than zero in \"{part}\".");
            mats.Add(mat);
        }

        if (mats.Count(r => r.Face == BarFace.Top) > 1 || mats.Count(r => r.Face == BarFace.Bottom) > 1)
            throw new FormatException("Give at most one top mat and one bottom mat per course.");
        return mats;
    }

    /// <summary>Canonical text for the table / quick entry, readable back by <see cref="Parse"/>.</summary>
    public static string Format(IEnumerable<Reinforcement>? mats) =>
        mats is null ? "" : string.Join(", ", mats.Select(Format));

    public static string Format(Reinforcement r)
    {
        var s = $"{(r.Face == BarFace.Top ? "top " : "")}{r.Prefix}{N(r.DiameterMm)}@{N(r.SpacingMm)} c{N(r.CoverMm)}";
        if (r.HasTransverse)
            s += $" + {r.Prefix}{N(r.TransverseDiameterMm)}@{N(r.TransverseSpacingMm)}";
        return s;
    }

    /// <summary>
    /// Problems with the mats in a course (bars outside the course, mats clashing). Empty when they fit.
    /// </summary>
    public static List<string> Check(PavementLayer layer)
    {
        var errors = new List<string>();
        var mats = layer.Reinforcement;
        if (mats.Count == 0)
            return errors;

        string who = string.IsNullOrWhiteSpace(layer.Description) ? "a course" : $"\"{layer.Description}\"";
        if (layer.ThicknessMm <= 0)
        {
            errors.Add($"Reinforcement on {who} needs a course thickness greater than zero.");
            return errors;
        }
        foreach (var r in mats)
        {
            if (r.CoverMm < 0 || r.DiameterMm <= 0 || r.SpacingMm <= 0)
                errors.Add($"Reinforcement in {who}: cover cannot be negative and bar size/spacing must be greater than zero.");
            else if (r.DepthFromFaceMm > layer.TotalMm)
                errors.Add($"Reinforcement in {who}: {N(r.CoverMm)}mm cover + bars ({N(r.DepthFromFaceMm)}mm) is more than the {N(layer.TotalMm)}mm course.");
        }
        var top = mats.FirstOrDefault(r => r.Face == BarFace.Top);
        var bottom = mats.FirstOrDefault(r => r.Face == BarFace.Bottom);
        if (top is not null && bottom is not null && top.DepthFromFaceMm + bottom.DepthFromFaceMm > layer.TotalMm)
            errors.Add($"Reinforcement in {who}: the top and bottom mats overlap in a {N(layer.TotalMm)}mm course.");
        return errors;
    }

    private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
