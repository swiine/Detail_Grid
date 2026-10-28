using System.Globalization;
using netDxf;
using netDxf.Entities;

namespace ETWalls.Export;

/// <summary>
/// Pattern line definitions for the hatches the built-in standard uses, in acadiso (millimetre) units.
/// A DXF hatch carries its own pattern lines, so the DXF needs these to show the pattern.
/// ANSI31, ANSI37 and EARTH follow AutoCAD's acad.pat; AR-SAND, AR-CONC and GRAVEL are close
/// look-alikes (re-pick the hatch in AutoCAD to get the exact pattern).
/// </summary>
internal static class HatchPatterns
{
    // acad.pat style: "angle, x,y, dx,dy [, dash, gap ...]" in inches; converted to mm below.
    private static readonly Dictionary<string, string[]> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ANSI31"] = new[] { "45, 0,0, 0,.125" },
        ["ANSI37"] = new[] { "45, 0,0, 0,.125", "135, 0,0, 0,.125" },
        ["EARTH"] = new[]
        {
            "0, 0,0, .25,.25, .25,-.25",
            "0, 0,.09375, .25,.25, .25,-.25",
            "0, 0,.1875, .25,.25, .25,-.25",
            "90, .03125,.21875, .25,.25, .25,-.25",
            "90, .125,.21875, .25,.25, .25,-.25",
            "90, .21875,.21875, .25,.25, .25,-.25",
        },
        ["AR-SAND"] = new[]
        {
            "37.5, 0,0, 1.123,.682, 0,-1.37",
            "7.5, 0,0, 2.25,.293, 0,-2.27",
            "-32.5, -.25,0, 1.11,-.69, 0,-1.32",
            "-82.5, -.4,0, .53,-.07, 0,-.55",
            "12.5, .3,.15, 1.6,.45, 0,-1.9",
        },
        ["AR-CONC"] = new[]
        {
            "50, 0,0, 4.12,-5.89, .75,-8.25",
            "355, 0,0, -2.5,6.08, .6,-7.65",
            "100.45, .59,-.05, 5.6,8.12, .64,-7.84",
            "46.18, 0,2, 6.24,-8.91, 1.12,-12.37",
            "96.18, .88,1.91, 7.48,10.68, .96,-11.77",
            "351.18, 0,2, 7.48,10.68, .9,-11.74",
            "21, 1,1.5, 4.32,-6.17, 0,-6.11",
            "326, 1.5,1.25, -3.65,5.04, 0,-5.6",
            "71, 2.5,.6, 5.6,8, 0,-9.3",
        },
        ["GRAVEL"] = new[]
        {
            "0, 0,0, 1,.8, .22,-.78",
            "62, .22,0, 1.1,.85, .2,-.9",
            "118, .31,.18, 1.2,.9, .2,-1",
            "195, .1,.35, .9,.75, .18,-.72",
            "300, .35,.5, 1.05,.95, .16,-.89",
            "25, .5,.7, 1.3,.7, .15,-1.15",
        },
    };

    /// <summary>The DXF pattern for a hatch spec, scaled and rotated; SOLID is a solid fill.</summary>
    public static HatchPattern For(string name, double scale, double angleDeg)
    {
        if (string.Equals(name, "SOLID", StringComparison.OrdinalIgnoreCase))
            return HatchPattern.Solid;

        var lines = Definitions.TryGetValue(name, out var known) ? known : Definitions["ANSI31"];
        var pattern = new HatchPattern(name.ToUpperInvariant(), name.ToUpperInvariant());
        foreach (var line in lines)
            pattern.LineDefinitions.Add(Parse(line));
        pattern.Type = HatchType.Predefined;
        pattern.Scale = scale > 0 ? scale : 1;
        pattern.Angle = angleDeg;
        return pattern;
    }

    private static HatchPatternLineDefinition Parse(string line)
    {
        const double inch = 25.4; // acad.pat inches -> acadiso millimetres
        var v = line.Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        var def = new HatchPatternLineDefinition
        {
            Angle = v[0],
            Origin = new Vector2(v[1] * inch, v[2] * inch),
            Delta = new Vector2(v[3] * inch, v[4] * inch),
        };
        foreach (var dash in v.Skip(5))
            def.DashPattern.Add(dash * inch);
        return def;
    }
}
