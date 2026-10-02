using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TTWLinemarking.Core
{
    public sealed class LinEntry
    {
        public string Name { get; init; }
        public string Section { get; init; }       // the bare heading line above it, e.g. "TFNSW LANE LINES"
        public string Comment { get; init; }       // everything after the comma on the *NAME line
        public string WidthNote { get; init; }     // first parenthetical, e.g. "0.1 wide RED"
        public string RmsCode { get; init; }       // from "(RMS Code = X)", or null
        public string Description { get; init; }   // comment text with parentheticals and ASCII art removed
        public double[] Pattern { get; init; }     // the "A," line's numbers
    }

    // Minimal reader for simple ("A,"-aligned, no embedded shapes/text) AutoCAD linetype files,
    // which is all TTW's linemarking files contain.
    public static class LinFile
    {
        private static readonly Regex Parenthetical = new Regex(@"\(([^)]*)\)", RegexOptions.CultureInvariant);
        private static readonly Regex Rms = new Regex(@"^RMS Code\s*=\s*(.+)$", RegexOptions.CultureInvariant);

        public static List<LinEntry> Parse(string text)
        {
            var result = new List<LinEntry>();
            string section = null;
            string name = null, comment = null;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(";")) continue;

                if (trimmed.StartsWith("*"))
                {
                    int comma = trimmed.IndexOf(',');
                    name = (comma < 0 ? trimmed.Substring(1) : trimmed.Substring(1, comma - 1)).Trim();
                    comment = comma < 0 ? "" : trimmed.Substring(comma + 1);
                }
                else if (trimmed.StartsWith("A,", StringComparison.OrdinalIgnoreCase) && name != null)
                {
                    result.Add(Build(name, section, comment, trimmed.Substring(2)));
                    name = null;
                }
                else
                {
                    section = trimmed;
                }
            }
            return result;
        }

        private static LinEntry Build(string name, string section, string comment, string patternText)
        {
            var parens = Parenthetical.Matches(comment).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
            string rms = parens.Select(p => Rms.Match(p)).Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).FirstOrDefault();

            // Description is the prose after the parentheticals, with the trailing "___   ___" art cut off.
            string prose = Parenthetical.Replace(comment, "");
            int art = prose.IndexOf('_');
            if (art >= 0) prose = prose.Substring(0, art);

            return new LinEntry
            {
                Name = name,
                Section = section,
                Comment = comment,
                WidthNote = parens.FirstOrDefault(),
                RmsCode = rms,
                Description = prose.Trim(),
                Pattern = patternText.Split(',')
                    .Select(s => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture))
                    .ToArray(),
            };
        }
    }
}
