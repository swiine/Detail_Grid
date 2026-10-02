using System;
using System.Collections.Generic;
using System.Linq;

namespace TTWLinemarking.Core
{
    public enum Paint
    {
        White,  // plain road paint - entity colour is left ByLayer
        Red,
        Yellow,
    }

    // A second painted line generated beside the first, for markings the standard draws as a pair.
    // The selected polyline becomes an unpainted centreline reference and both lines are created
    // from it (see OffsetMath).
    public sealed class LinePair
    {
        // Clear gap between the painted edges of the two lines, as the RMS diagrams dimension it.
        public double Gap { get; }

        // Linetype code for the other line. Equal to the item's own code for identical pairs (BB/BB1).
        public string PartnerCode { get; }

        public LinePair(double gap, string partnerCode)
        {
            Gap = gap;
            PartnerCode = partnerCode;
        }
    }

    public sealed class LinemarkingItem
    {
        public string Code { get; init; }
        public string Label { get; init; }          // plain-language purpose shown in the workflow list
        public string Description { get; init; }    // official description, verbatim from the .lin comment
        public string RmsCode { get; init; }        // null when the .lin comment has no "(RMS Code = ...)"
        public string Category { get; init; }       // .lin section heading the code sits under
        public string StandardNote { get; init; }   // width note, verbatim from the .lin comment, display only
        public double Width { get; init; }          // polyline width actually applied
        public Paint Paint { get; init; }

        // Dash/gap at 1:100, as written in the 1:100 .lin file. Null for continuous lines.
        public double[] Dash { get; init; }

        // The standard draws this marking as two lines.
        public bool Double { get; init; }

        // How to generate the second line. Null when Double is false, or when no gap distance has
        // been confirmed yet (the drafter is told to offset by hand rather than us guessing).
        public LinePair Pair { get; init; }

        public bool Solid => Dash == null;
        public bool NeedsManualOffset => Double && Pair == null;
    }

    public static class Catalogue
    {
        public const string Layer = "geom-prop-lmk";

        // Never plots, whatever the layer settings say. Centreline references go here.
        public const string ReferenceLayer = "Defpoints";

        public static readonly IReadOnlyList<string> Categories = new[]
        {
            "SECTION 6 RTA",
            "TFNSW DIVIDING LINES",
            "TFNSW BARRIER LINES",
            "TFNSW LANE LINES",
            "TFNSW EDGE AND OUTLINE LINES",
            "TFNSW PARKING CONTROL LINES",
            "TFNSW CONTINUITY LINES",
            "TFNSW TURN LINES",
        };

        private static readonly double[] Short = { 6, -6 };
        private static readonly double[] Dl = { 30, -90 };
        private static readonly double[] Sp = { 90, -30 };
        private static readonly double[] Cont = { 10, -30 };

        // Widths, labels and patterns carry over unchanged from the production LISP tool (V1.9.1).
        // Pair gaps are from the TfNSW Delineation Manual Part 4: Table 4.2 (BS/BB) and the Enhanced
        // Dividing Lines table (BS1/BB1). In every confirmed case the gap equals the line width.
        public static readonly IReadOnlyList<LinemarkingItem> Items = new List<LinemarkingItem>
        {
            Item("T1", "Turning Path Line", "Turning path Line", "T1", 0, "0.2 wide", 0.10, Short),
            Item("TF", "Stop Line (at Stop Sign)", "Stop Line when used with stop sign", "TF", 0, "0.3 wide", 0.30, null),
            Item("TB", "Give Way Line", "Give way Line", "TB", 0, "0.3 wide", 0.30, Short),
            Item("TB1", "Give Way Line - Right Side of Road", "Give Way Line on right side of road", "TB1", 0, "0.15 wide", 0.15, Short),
            // Pedestrian crossings aren't covered by Part 4, so there's no confirmed gap to offset by.
            Item("PCW", "Pedestrian Cross Walk", "Pedestrian Cross Walk Lines", "PCW", 0, "Double 0.15 wide", 0.10, new double[] { 10, -3 }, dbl: true),
            Item("PX", "Pedestrian Crossing", "Pedestrian Crossing", "PX", 0, "3.6 wide TBA", 0.10, Short),

            Item("DL1", "Dividing Line - 2 Lane Road", "Dividing Line on 2 lane road", "S1", 1, "0.1 wide", 0.10, Dl),
            // The .lin note says "Double", but S2 is superseded by S6, which Part 4 shows as a single
            // line. Treated as single, per the current standard.
            Item("DL4", "Dividing Line - Enhanced (Double Width)", "Enhanced DL1", "S2", 1, "Double 0.15 wide", 0.15, Dl),
            Item("DL4 WCL", "Dividing Line - 2 Lane Road (WCL)", "Dividing Line on 2 lane road applied as a WCL", null, 1, "Double 0.15 wide", 0.15, Dl),
            Item("DLP", "Bicycle Separation - Off-Road Straight Path", "Bicycle Separation Line for straight off road path", "S5", 1, "0.1 wide", 0.10, Cont),

            Item("BL1 DASHED", "Sight Restriction - 1 Direction (Dashed)", "Dividing Line For Sight Restriction in 1 Direction", "BS", 2, "0.1 wide", 0.10, Dl, pair: new LinePair(0.10, "BL1 SOLID")),
            Item("BL1 SOLID", "Sight Restriction - 1 Direction (Solid)", "Dividing Line For Sight Restriction in 1 Direction", null, 2, "0.1 wide", 0.10, null, pair: new LinePair(0.10, "BL1 DASHED")),
            Item("BL2", "Sight Restriction - Both Directions", "Dividing Line For Sight Restriction in Both Directions", "BB", 2, "Double 0.1 wide", 0.10, null, dbl: true, pair: new LinePair(0.10, "BL2")),
            Item("BL5 DASHED", "Sight Restriction - 1 Direction, Enhanced (Dashed)", "Enhanced BL1 Dashed Line", "BS1", 2, "0.15 wide", 0.15, Dl, pair: new LinePair(0.15, "BL5 SOLID")),
            Item("BL5 SOLID", "Sight Restriction - 1 Direction, Enhanced (Solid)", "Enhanced BL1 Solid Line", null, 2, "0.15 wide", 0.15, null, pair: new LinePair(0.15, "BL5 DASHED")),
            Item("BL6", "Sight Restriction - Both Directions, Enhanced", "Enhanced BL2", null, 2, "Double 0.15 wide", 0.15, null, dbl: true, pair: new LinePair(0.15, "BL6")),
            Item("BLP", "Bicycle Separation - Off-Road Path", "Bicycle Separation Line for off-road path", null, 2, "0.1 wide", 0.10, null),

            Item("LL1", "Lane Line - Multi Lane Road / Freeway", "Lane Line on Multi Lane Road & Freeways", "L1", 3, "0.1 wide", 0.10, Dl),
            Item("LL2", "Special Purpose Lane Line", "Special Purpose Lane Line", null, 3, "0.1 wide", 0.10, Sp),
            Item("LL3", "Special Purpose Lane Line - Freeway", "Special Purpose Lane Line for Freeways", null, 3, "0.15 wide", 0.10, Sp),
            Item("LL4", "Lane Line - Multi Lane Roads (Solid)", "Lane Line on Multi Lane Roads", null, 3, "0.1 wide", 0.10, null),
            Item("BU1", "Bus Lane - Broken Line", "Special Purpose Broken Lane Line for Bus Lane", null, 3, "0.1 wide RED", 0.10, Sp, Paint.Red),
            Item("BU2", "Bus Lane - Solid Line", "Lane Line on Multi Lane Roads for Bus Lane", null, 3, "0.1 wide RED", 0.10, null, Paint.Red),
            Item("TR1", "Tram - Mixed Traffic Lane", "Mixed traffic lane with tram tracks", null, 3, "0.1 wide YELLOW", 0.10, new double[] { 60, -60 }, Paint.Yellow),
            Item("TR2", "Tram Lane Line", "Tram Lane Line", null, 3, "0.1 wide YELLOW", 0.10, null, Paint.Yellow),
            // Tramways aren't covered by Part 4 either - manual offset, same as PCW.
            Item("TR3", "Tramway Line", "Tramway Line", null, 3, "Double 0.1 wide YELLOW", 0.10, null, Paint.Yellow, dbl: true),

            Item("EL1", "Edge Of Road", "Left and right edges of roads, divided carriageways and freeways", "E1", 4, "0.15 wide", 0.15, null),
            Item("OL1", "Outline - Splays / Medians / Islands / Shoulders", "Outline of splays, medians, islands and shoulders", null, 4, "0.15 wide", 0.15, null),
            Item("ELP", "Bicycle Edge Line - Off-Road Path", "Bicycle edge line for off-road paths", null, 4, "0.1 wide", 0.10, null),

            Item("PS1", "Parking Bay Edge", "Mark the edge of parking bay", null, 5, "0.1 wide", 0.10, null),
            Item("NS1", "No Stopping - Fulltime", "Kerbside line marking for fulltime no stopping", null, 5, "0.1 wide YELLOW", 0.10, null, Paint.Yellow),
            Item("NS3", "Clearway Restriction", "Kerbside line marking for clearway restriction", null, 5, "0.1 wide YELLOW", 0.10, new double[] { 30, -30 }, Paint.Yellow),

            Item("CL1", "Continuity Line - Edge of Through Carriageway", "Defines edge of through carriageway", "C1", 6, "0.15 wide", 0.15, Cont),
            Item("BU4", "Continuity Line - Edge of Bus Lane", "Defines edge of Bus Lane", null, 6, "0.15 wide RED", 0.15, Cont, Paint.Red),
            Item("TR4", "Continuity Line - Edge of Tram Lane", "Defines edge of Tram Lane", null, 6, "0.15 wide YELLOW", 0.15, Cont, Paint.Yellow),

            Item("TL", "Turning / Through Paths - Complex Intersection", "Defines turning and through-paths at complex intersections", null, 7, "0.1 wide", 0.10, Short),
        };

        private static readonly Dictionary<string, LinemarkingItem> ByCode =
            Items.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);

        public static LinemarkingItem Find(string code) =>
            code != null && ByCode.TryGetValue(code, out var item) ? item : null;

        // Pattern length the linetype should have when loaded from the .lin file for this scale.
        // Every TTW .lin file is the 1:100 file with each length multiplied by 100/scale. Continuous
        // lines are written "A,1,1" (two dashes), hence 2 at 1:100.
        public static double ExpectedPatternLength(LinemarkingItem item, int scale)
        {
            double at100 = item.Solid ? 2.0 : item.Dash.Sum(Math.Abs);
            return at100 * 100.0 / scale;
        }

        private static LinemarkingItem Item(string code, string label, string description, string rms,
            int category, string note, double width, double[] dash, Paint paint = Paint.White,
            bool dbl = false, LinePair pair = null)
        {
            return new LinemarkingItem
            {
                Code = code,
                Label = label,
                Description = description,
                RmsCode = rms,
                Category = Categories[category],
                StandardNote = note,
                Width = width,
                Dash = dash,
                Paint = paint,
                Double = dbl,
                Pair = pair,
            };
        }
    }
}
