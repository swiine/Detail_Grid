namespace ProfileStacker.Core;

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double CenterY => (MinY + MaxY) / 2;
}

/// <summary>
/// Where one new view goes in model space: <see cref="GraphX"/>/<see cref="GraphY"/> is the graph's
/// lower-left corner (start station, datum), <see cref="Frame"/> the whole view with axes and bands, and
/// <see cref="ViewCenterX"/>/<see cref="ViewCenterY"/> where its sheet viewport looks.
/// </summary>
public sealed record ViewPlacement(ViewPlan View, double GraphX, double GraphY, Bounds Frame, double ViewCenterX, double ViewCenterY);

/// <summary>
/// Lays the new views out in model space the way they sit on the sheets: one column per sheet, left to
/// right, with each sheet's views stacked top to bottom in viewport order.
/// </summary>
public static class ModelPlacement
{
    /// <param name="left">Left edge of the first column.</param>
    /// <param name="top">Top of the first view in every column.</param>
    /// <param name="gap">Space between views and between columns (0 = a tenth of the tallest view).</param>
    public static IReadOnlyList<ViewPlacement> Place(
        StackPlan plan, FrameGeometry frame, double left, double top, double gap, double paperClearance)
    {
        var result = new List<ViewPlacement>();
        if (plan.Views.Count == 0)
            return result;

        if (gap <= 0)
            gap = plan.Views.Max(v => frame.Height(v.ElevationMax - v.ElevationMin)) / 10;

        double columnLeft = left;
        foreach (var sheet in plan.Sheets)
        {
            double y = top;
            double columnWidth = 0;
            foreach (var view in sheet)
            {
                double graphWidth = view.Length * frame.HorizontalScale;
                double graphHeight = (view.ElevationMax - view.ElevationMin) * frame.VerticalScale;
                double graphX = columnLeft + frame.MarginLeft;
                double graphY = y - frame.MarginTop - graphHeight;
                var bounds = new Bounds(columnLeft, graphY - frame.MarginBottom, graphX + graphWidth + frame.MarginRight, y);

                // Viewport: frame against its left edge (plus the clearance), centred vertically.
                double clearance = Math.Max(0, paperClearance) / view.Slot.Scale;
                double centerX = bounds.MinX - clearance + view.Slot.ModelWidth / 2;
                result.Add(new ViewPlacement(view, graphX, graphY, bounds, centerX, bounds.CenterY));

                columnWidth = Math.Max(columnWidth, Math.Max(bounds.Width, view.Slot.ModelWidth));
                y = bounds.MinY - gap;
            }
            columnLeft += columnWidth + gap;
        }
        return result;
    }
}
