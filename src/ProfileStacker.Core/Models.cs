namespace ProfileStacker.Core;

/// <summary>
/// One profile viewport on the sheet template, in paper units. <see cref="Scale"/> is the viewport's
/// custom scale (paper units per drawing unit, the "XP" factor), so a 780 mm wide viewport at 1/1000xp
/// shows 780 000 drawing units across; in a metre drawing on a millimetre sheet, 1:1000 is 1xp.
/// </summary>
public sealed record ViewportSlot(double CenterX, double CenterY, double PaperWidth, double PaperHeight, double Scale)
{
    /// <summary>Position on the sheet once the slots are ordered (0 = top left).</summary>
    public int Index { get; init; }

    /// <summary>Layer of the viewport in the template (used to pick out the profile viewports).</summary>
    public string Layer { get; init; } = "";

    public double ModelWidth => PaperWidth / Scale;
    public double ModelHeight => PaperHeight / Scale;
    public double Left => CenterX - PaperWidth / 2;
    public double Top => CenterY + PaperHeight / 2;
}

/// <summary>
/// The shape of the long profile view, measured in the drawing: how far the axes, bands and title stick
/// out around the graph (drawing units), and the drawing units per station and per unit of elevation
/// (the vertical one includes the vertical exaggeration).
/// </summary>
public sealed record FrameGeometry(
    double MarginLeft,
    double MarginRight,
    double MarginTop,
    double MarginBottom,
    double HorizontalScale = 1.0,
    double VerticalScale = 1.0)
{
    public double VerticalExaggeration => VerticalScale / HorizontalScale;

    public double Width(double stationLength) => MarginLeft + stationLength * HorizontalScale + MarginRight;
    public double Height(double elevationRange) => MarginBottom + elevationRange * VerticalScale + MarginTop;
}

/// <summary>How much profile one viewport can show: station length and elevation range.</summary>
public readonly record struct SlotCapacity(double StationLength, double ElevationRange);

public enum LengthMode
{
    /// <summary>Each view is as long as its viewport allows; the last one takes what is left.</summary>
    Fill,

    /// <summary>The fewest views that fit, with the length shared out evenly between them.</summary>
    Even,
}

public enum DatumMode
{
    /// <summary>The datum sits just under the lowest point of the profiles in that view.</summary>
    LowPoint,

    /// <summary>The profiles sit in the middle of the view height.</summary>
    Centred,
}

/// <summary>The station and elevation range of one new profile view, and which sheet and viewport it goes in.</summary>
public sealed record ViewPlan(
    int Number,
    int Sheet,
    ViewportSlot Slot,
    double StationStart,
    double StationEnd,
    double ElevationMin,
    double ElevationMax,
    SlotCapacity Capacity)
{
    /// <summary>False when the profiles in this range need more height than the viewport has.</summary>
    public bool FitsHeight { get; init; } = true;

    /// <summary>False when no profile has data in this range (the datum was carried over).</summary>
    public bool HasProfileData { get; init; } = true;

    public double Length => StationEnd - StationStart;
}

public sealed record StackPlan(IReadOnlyList<ViewPlan> Views, int SlotsPerSheet, IReadOnlyList<string> Warnings)
{
    public int SheetCount => Views.Count == 0 ? 0 : Views[^1].Sheet + 1;

    public IEnumerable<IGrouping<int, ViewPlan>> Sheets => Views.GroupBy(v => v.Sheet);
}
