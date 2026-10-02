using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TTWLinemarking.Core
{
    public static class Scales
    {
        // One TTW_stdState_Linetype_Linemk [scale].lin file ships per scale.
        public static readonly IReadOnlyList<int> Supported = new[] { 100, 200, 250, 500, 1000 };

        // "1:<n>" anywhere in an annotation scale name. TTW's own names wrap it
        // ("ttw_scale_1:500m"), and AutoCAD adds suffixes ("1:100_XREF").
        private static readonly Regex ScaleName = new Regex(@"(?<!\d)1\s*:\s*(\d+)(?!\d)", RegexOptions.CultureInvariant);

        // CANNOSCALE name -> supported scale, or null. Matches the whole number, so "1:1000" is
        // never mistaken for "1:100" and "1:10" or "1:2000" is rejected.
        public static int? Parse(string cannoscale)
        {
            if (string.IsNullOrWhiteSpace(cannoscale)) return null;
            var m = ScaleName.Match(cannoscale);
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
            return Supported.Contains(n) ? n : (int?)null;
        }

        public static string LinFileName(int scale) => $"TTW_stdState_Linetype_Linemk {scale}.lin";

        public static string SupportedList() =>
            string.Join(", ", Supported.Take(Supported.Count - 1).Select(s => "1:" + s)) + " or 1:" + Supported[Supported.Count - 1];
    }
}
