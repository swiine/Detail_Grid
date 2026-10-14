using System.Globalization;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core.Design;

public static partial class Inputs
{
    public static bool TryRead(string? text, string unit, out double value, out string? error)
    {
        value = 0;
        error = null;
        var s = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace(",", "").Replace("×", "x");
        if (s.Length == 0) { error = "Enter a value."; return false; }

        var m = NumberRegex().Match(s);
        if (!m.Success) { error = $"\"{text}\" isn't a number."; return false; }
        double number = double.Parse(m.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (m.Groups["p"].Success) number *= Math.Pow(10, int.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture));
        string suffix = m.Groups["u"].Value;
        string want = unit.Trim().ToLowerInvariant();

        value = (suffix, want) switch
        {
            ("" or "%", _) => number,
            ("t" or "tonne" or "tonnes", "kn") => number * 9.81,
            ("kn", "tonne") => number / 9.81,
            ("t" or "tonne" or "tonnes", "tonne") => number,
            ("kn", "kn") => number,
            ("mm", "m") => number / 1000,
            ("m", "mm") => number * 1000,
            ("mm", "mm") or ("m", "m") => number,
            ("mpa", "mpa") or ("kpa", "kpa") or ("esa", "esa") => number,
            ("mpa", "kpa") => number * 1000,
            ("kpa", "mpa") => number / 1000,
            _ => double.NaN,
        };
        if (double.IsNaN(value))
        {
            error = $"\"{suffix}\" can't be used here (this box is in {unit}).";
            return false;
        }
        return true;
    }

    [GeneratedRegex(@"^(?<n>-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)(?:x10\^?(?<p>-?\d+))?(?<u>%|t|tonnes?|kn|mm|m|mpa|kpa|esa)?$")]
    private static partial Regex NumberRegex();
}
