namespace ProfileStacker.Core;

/// <summary>Thrown when the template and settings can't produce any views (e.g. the viewports are too small).</summary>
public sealed class PlanException(string message) : Exception(message);

/// <summary>
/// Splits a long profile view into stacked views that each exactly fill a viewport of the sheet template:
/// works out the station range of every view, which sheet and viewport it goes in, and its datum.
/// </summary>
public static class StackPlanner
{
    private const double Eps = 1e-6;
    private const int MaxViews = 2000;

    /// <summary>
    /// Orders the template's viewports the way the sheet is read: rows from the top, left to right in each
    /// row (viewports whose centres sit inside another's height are on the same row), numbering them.
    /// </summary>
    public static IReadOnlyList<ViewportSlot> OrderSlots(IEnumerable<ViewportSlot> slots)
    {
        var rows = new List<List<ViewportSlot>>();
        foreach (var slot in slots.OrderByDescending(s => s.Top).ThenBy(s => s.Left))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].CenterY - slot.CenterY) < r[0].PaperHeight / 2);
            if (row is null)
                rows.Add(row = new List<ViewportSlot>());
            row.Add(slot);
        }
        return rows
            .SelectMany(r => r.OrderBy(s => s.Left))
            .Select((s, i) => s with { Index = i })
            .ToList();
    }

    /// <summary>Station length and elevation range a profile view can have and still fit inside the viewport.</summary>
    public static SlotCapacity Capacity(ViewportSlot slot, FrameGeometry frame, double paperClearance)
    {
        double gap = Math.Max(0, paperClearance) / slot.Scale;
        double width = slot.ModelWidth - 2 * gap - frame.MarginLeft - frame.MarginRight;
        double height = slot.ModelHeight - 2 * gap - frame.MarginTop - frame.MarginBottom;
        return new SlotCapacity(Math.Max(0, width) / frame.HorizontalScale, Math.Max(0, height) / frame.VerticalScale);
    }

    /// <summary>
    /// Plans the stacked views. <paramref name="fallback"/> is the elevation range used for a view no
    /// profile reaches when there's no earlier view to copy it from (normally the long view's own range).
    /// </summary>
    public static StackPlan Plan(
        IReadOnlyList<ViewportSlot> slots,
        FrameGeometry frame,
        StackerSettings s,
        ProfileSamples samples,
        (double Low, double High)? fallback = null)
    {
        if (slots.Count == 0)
            throw new PlanException("The template layout has no profile viewports. Check the layout and the viewport layer filter.");
        if (frame.HorizontalScale <= 0 || frame.VerticalScale <= 0)
            throw new PlanException("The profile view's scale couldn't be measured.");

        var used = slots.Select(x => s.ScaleOverride > 0 ? x with { Scale = s.ScaleOverride } : x).ToList();
        if (used.Any(x => x.Scale <= 0 || x.PaperWidth <= 0 || x.PaperHeight <= 0))
            throw new PlanException("A template viewport has no size or scale. Set the viewport scales in the template, or use the scale override.");

        double start = s.StartStation, end = Math.Min(s.EndStation, s.MaxStation);
        start = Math.Max(start, s.MinStation);
        if (end - start < Eps)
            throw new PlanException("The end station must be after the start station.");

        double r = s.StationRounding > 0 ? s.StationRounding : 0;
        if (s.RoundStart && r > 0)
            start = Math.Max(s.MinStation, FloorTo(start, r));

        var warnings = new List<string>();
        var caps = used.Select(x => Capacity(x, frame, s.PaperClearance)).ToList();
        var lengths = caps.Select(c => r > 0 && c.StationLength >= r ? FloorTo(c.StationLength, r) : c.StationLength).ToList();
        double overlap = Math.Max(0, s.Overlap);
        for (int i = 0; i < used.Count; i++)
        {
            if (lengths[i] - overlap <= Eps)
                throw new PlanException(caps[i].StationLength <= Eps
                    ? $"Viewport {i + 1} is too small for the profile view's axes and bands at this scale."
                    : $"Viewport {i + 1} only fits {caps[i].StationLength:0.##} of station, which isn't more than the overlap.");
            if (caps[i].ElevationRange <= Eps)
                throw new PlanException($"Viewport {i + 1} is too short for the profile view's bands and title at this scale.");
            if (r > 0 && caps[i].StationLength < r)
                warnings.Add($"Viewport {i + 1} fits less than one station rounding step ({r:0.##}); its views aren't rounded.");
        }

        // Fill: every view as long as its viewport allows. Even: the same number of views, sharing the length.
        var ranges = Partition(start, end, lengths, overlap, s, null);
        if (s.LengthMode == LengthMode.Even && ranges.Count > 1)
        {
            double share = (end - start + (ranges.Count - 1) * overlap) / ranges.Count;
            if (r > 0) share = CeilTo(share, r);
            ranges = Partition(start, end, lengths, overlap, s, share);
        }

        var views = new List<ViewPlan>();
        (double Low, double High)? previous = null;
        foreach (var (a, b, k) in ranges)
        {
            int slotIndex = k % used.Count;
            var slot = used[slotIndex];
            var cap = caps[slotIndex];
            var range = samples.Range(a, b);
            bool hasData = range is not null;
            range ??= previous ?? fallback;
            if (range is null)
            {
                range = (0, 0);
                warnings.Add($"View {k + 1}: no profile data to set the datum from.");
            }
            previous = range;

            var (min, max, fits) = FitDatum(range.Value.Low, range.Value.High, cap.ElevationRange, s);
            views.Add(new ViewPlan(k + 1, k / used.Count, slot, a, b, min, max, cap) { FitsHeight = fits, HasProfileData = hasData });
        }

        var tall = views.Where(v => !v.FitsHeight).Select(v => v.Number).ToList();
        if (tall.Count > 0)
            warnings.Add($"View{(tall.Count > 1 ? "s" : "")} {string.Join(", ", tall)}: the profile rises or falls more than the viewport height shows; the view is taller than its viewport. Use a larger viewport, a smaller scale or shorter views.");
        var empty = views.Where(v => !v.HasProfileData).Select(v => v.Number).ToList();
        if (empty.Count > 0)
            warnings.Add($"View{(empty.Count > 1 ? "s" : "")} {string.Join(", ", empty)}: no profile in this station range; the datum is carried over.");

        return new StackPlan(views, used.Count, warnings);
    }

    private static List<(double Start, double End, int Number)> Partition(
        double start, double end, IReadOnlyList<double> lengths, double overlap, StackerSettings s, double? share)
    {
        var result = new List<(double, double, int)>();
        double a = start;
        for (int k = 0; ; k++)
        {
            if (k >= MaxViews)
                throw new PlanException($"That would make more than {MaxViews} views. Check the template viewport scales.");
            double length = share is double sh ? Math.Min(sh, lengths[k % lengths.Count]) : lengths[k % lengths.Count];
            double b = a + length;
            bool last = b >= end - Eps;
            if (last)
                b = s.TrimLastView ? end : Math.Min(b, s.MaxStation);
            result.Add((a, b, k));
            if (last)
                return result;
            a = b - overlap;
        }
    }

    /// <summary>
    /// Datum (min and max elevation) for a view whose profiles run from <paramref name="low"/> to
    /// <paramref name="high"/>, using the whole viewport height. Round values are kept where they fit.
    /// Fits is false when the profiles plus clearances need more height than the viewport has.
    /// </summary>
    public static (double Min, double Max, bool Fits) FitDatum(double low, double high, double capacity, StackerSettings s)
    {
        double lo = low - Math.Max(0, s.ClearanceBelow);
        double hi = high + Math.Max(0, s.ClearanceAbove);
        double r = s.DatumRounding > 0 ? s.DatumRounding : 0;

        if (hi - lo > capacity + Eps)
            return (FloorTo(lo, r), CeilTo(hi, r), false);

        // Prefer a whole number of rounding steps for the height (both datum lines round); else the full height.
        foreach (double height in r > 0 && capacity >= r ? new[] { FloorTo(capacity, r), capacity } : new[] { capacity })
        {
            if (hi - lo > height + Eps)
                continue;
            // Any min in [hi - height, lo] shows everything; pick the one the datum mode asks for, rounded.
            double lowest = hi - height, highest = lo;
            double min = s.DatumMode == DatumMode.Centred ? RoundTo((lo + hi - height) / 2, r) : FloorTo(lo, r);
            if (min < lowest - Eps) min = CeilTo(lowest, r);
            if (min > highest + Eps) continue;
            return (min, min + height, true);
        }
        return (lo, lo + capacity, true);
    }

    public static double FloorTo(double value, double step) =>
        step > 0 ? Math.Floor(value / step + 1e-9) * step : value;

    public static double RoundTo(double value, double step) =>
        step > 0 ? Math.Round(value / step, MidpointRounding.AwayFromZero) * step : value;

    public static double CeilTo(double value, double step) =>
        step > 0 ? Math.Ceiling(value / step - 1e-9) * step : value;
}
