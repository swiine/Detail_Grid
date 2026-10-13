using System.Globalization;
using System.Text;

namespace ProfileStacker.Core;

/// <summary>Station labels and names for the new layouts and profile views.</summary>
public static class Naming
{
    /// <summary>
    /// Station as it's written on drawings: 1250 → "1+250" (3 digits after the +, metric) or
    /// "12+50" (2 digits, imperial). Negative stations keep their sign: -50 → "-0+050".
    /// </summary>
    public static string Station(double station, int plusDigits = 3, int decimals = 0)
    {
        plusDigits = Math.Clamp(plusDigits, 1, 6);
        decimals = Math.Clamp(decimals, 0, 6);
        decimal value = Math.Round((decimal)Math.Abs(station), decimals, MidpointRounding.AwayFromZero);
        decimal unit = (decimal)Math.Pow(10, plusDigits);
        decimal major = Math.Floor(value / unit);
        decimal minor = value - major * unit;
        string minorText = minor.ToString(
            new string('0', plusDigits) + (decimals > 0 ? "." + new string('0', decimals) : ""),
            CultureInfo.InvariantCulture);
        string sign = station < 0 && value != 0 ? "-" : "";
        return $"{sign}{major.ToString(CultureInfo.InvariantCulture)}+{minorText}";
    }

    /// <summary>Reads a station typed as a number (1250.5) or as written on drawings (1+250.5, 12+50).</summary>
    public static bool TryParseStation(string text, out double station)
    {
        text = text.Trim().Replace(" ", "");
        int plus = text.IndexOf('+');
        if (plus > 0)
        {
            string major = text[..plus], minor = text[(plus + 1)..];
            if (major.Length == 0 || minor.Length == 0 || minor.Contains('+'))
            {
                station = 0;
                return false;
            }
            text = major + minor;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out station)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out station);
    }

    /// <summary>Layout name from the pattern: &lt;N&gt; sheet number (01, 02 …), &lt;START&gt; &lt;END&gt; stations.</summary>
    public static string Layout(string pattern, int sheetNumber, int sheetCount, string start, string end) =>
        CleanLayoutName(Replace(pattern, ("<N>", Number(sheetNumber, sheetCount)), ("<START>", start), ("<END>", end)));

    /// <summary>Profile view name: &lt;VIEW&gt; long view name, &lt;N&gt; view number, &lt;S&gt; sheet, &lt;START&gt; &lt;END&gt;.</summary>
    public static string View(string pattern, string viewName, int number, int count, int sheetNumber, int sheetCount, string start, string end) =>
        Replace(pattern,
            ("<VIEW>", viewName), ("<N>", Number(number, count)), ("<S>", Number(sheetNumber, sheetCount)),
            ("<START>", start), ("<END>", end)).Trim();

    /// <summary>Numbers padded so they sort: 2 digits (01) up to 99, then as many as the count needs.</summary>
    public static string Number(int n, int count) =>
        n.ToString(new string('0', Math.Max(2, count.ToString(CultureInfo.InvariantCulture).Length)), CultureInfo.InvariantCulture);

    private static readonly char[] LayoutInvalid = "<>/\\\":;?*|,=`".ToCharArray();

    /// <summary>Drops characters AutoCAD doesn't allow in layout names and keeps it to 255 characters.</summary>
    public static string CleanLayoutName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(LayoutInvalid, c) >= 0 || char.IsControl(c) ? ' ' : c);
        string clean = string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) clean = "Profile";
        return clean.Length > 255 ? clean[..255].TrimEnd() : clean;
    }

    /// <summary>The name itself if it's free, else "name (2)", "name (3)" …</summary>
    public static string Unique(string name, Func<string, bool> exists)
    {
        if (!exists(name))
            return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name} ({i})";
            if (!exists(candidate))
                return candidate;
        }
    }

    private static string Replace(string pattern, params (string Token, string Value)[] tokens)
    {
        foreach (var (token, value) in tokens)
            pattern = pattern.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        return pattern;
    }
}
