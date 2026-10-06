using System.Globalization;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

/// <summary>Names for detail blocks/groups from the standard's pattern, e.g. "TTW_pavement-profile_#".</summary>
public static class DetailNaming
{
    public const string DefaultFormat = "TTW_pavement-profile_#";

    private static readonly char[] Invalid = "<>/\\\":;?*|,='`".ToCharArray();

    /// <summary>
    /// The next name for a detail. '#' becomes one more than the highest number already used by a name
    /// of the same pattern (so numbers are never reused); {name} becomes the build-up name. Without '#',
    /// a clash gets "_2", "_3"... appended. Comparison is case-insensitive, like AutoCAD symbol names.
    /// </summary>
    public static string Next(string? format, string buildupName, IEnumerable<string> existingNames)
    {
        format = string.IsNullOrWhiteSpace(format) ? DefaultFormat : format.Trim();
        string withName = Clean(format.Replace("{name}", buildupName, StringComparison.OrdinalIgnoreCase));
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        int hash = withName.IndexOf('#');
        if (hash < 0)
        {
            string candidate = withName;
            for (int i = 2; existing.Contains(candidate); i++)
                candidate = $"{withName}_{i}";
            return candidate;
        }

        string prefix = withName[..hash], suffix = withName[(hash + 1)..].Replace("#", "");
        var pattern = new Regex("^" + Regex.Escape(prefix) + @"(\d+)" + Regex.Escape(suffix) + "$", RegexOptions.IgnoreCase);
        int max = existing.Select(n => pattern.Match(n)).Where(m => m.Success)
            .Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return prefix + (max + 1).ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string Clean(string s)
    {
        var clean = new string(s.Select(c => Invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "PAVEMENT_DETAIL" : clean;
    }
}
