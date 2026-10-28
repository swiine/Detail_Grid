using System.Globalization;
using System.Text.RegularExpressions;

namespace ETWalls.Core.Design;

public enum ValueKind { Number, Length, Angle }

/// <summary>What a typed value was read as.</summary>
public sealed record ReadResult(bool Ok, double Value, string? Message);

/// <summary>
/// Reads values the way people type them: "2.4", "2400mm", "2.4 m", "30°", "1e3", "10kPa".
/// A length with no unit that is 20 or more is taken as mm ("2400" = 2.4 m) and says so.
/// </summary>
public static partial class Units
{
    [GeneratedRegex(@"^(-?\d*\.?\d+(?:e[-+]?\d+)?)(mm|m|cm|°|deg|kpa|kn/m3|kn/m³|mpa|%|\$)?$")]
    private static partial Regex Pattern();

    public static ReadResult Read(string? text, ValueKind kind)
    {
        var t = (text ?? "").Trim().ToLowerInvariant().Replace(",", "").Replace(" ", "").TrimStart('$');
        if (t.Length == 0) return new(false, 0, "Needs a number.");
        var m = Pattern().Match(t);
        if (!m.Success) return new(false, 0, $"Can't read \"{text}\". Type a number, e.g. 2.4 or 2400mm.");
        double v = double.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        string u = m.Groups[2].Value;
        if (kind == ValueKind.Length)
        {
            if (u == "mm") v /= 1000;
            else if (u == "cm") v /= 100;
            else if (u == "" && v >= 20) return new(true, v / 1000, $"Read as {v.ToString(CultureInfo.InvariantCulture)} mm = {(v / 1000).ToString("F3", CultureInfo.InvariantCulture)} m.");
        }
        return new(true, v, null);
    }
}
