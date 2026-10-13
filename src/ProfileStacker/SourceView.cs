using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using ProfileStacker.Core;

namespace ProfileStacker;

/// <summary>
/// The long profile view the user picked, measured once: its station and elevation range, the shape of
/// its frame (axes, bands and title around the graph, and the vertical exaggeration), and the profiles
/// it shows, sampled so each new view's datum can be fitted without going back to the drawing.
/// </summary>
internal sealed class SourceView
{
    public required ObjectId Id { get; init; }
    public required string Name { get; init; }
    public required ObjectId AlignmentId { get; init; }
    public required string AlignmentName { get; init; }
    public required ObjectId StyleId { get; init; }
    public required double StationStart { get; init; }
    public required double StationEnd { get; init; }
    public required double ElevationMin { get; init; }
    public required double ElevationMax { get; init; }
    public required double AlignmentStart { get; init; }
    public required double AlignmentEnd { get; init; }
    public required FrameGeometry Frame { get; init; }
    public required Bounds Extents { get; init; }
    public required ProfileSamples Samples { get; init; }
    public required bool IsSplit { get; init; }

    /// <summary>Profiles the long view draws, with their style overrides, to repeat on the new views.</summary>
    public required IReadOnlyList<(ObjectId ProfileId, bool Draw, ObjectId OverrideStyleId)> Overrides { get; init; }

    private const int SamplesPerProfile = 2000;

    public static SourceView Read(Transaction tr, ObjectId viewId)
    {
        var pv = (ProfileView)tr.GetObject(viewId, OpenMode.ForRead);
        var alignment = (Alignment)tr.GetObject(pv.AlignmentId, OpenMode.ForRead);

        double s0 = pv.StationStart, s1 = pv.StationEnd, e0 = pv.ElevationMin, e1 = pv.ElevationMax;
        if (s1 - s0 <= 0 || e1 - e0 <= 0)
            throw new PlanException("The profile view has no station or elevation range.");

        // Graph corners in the drawing give both scales; the extents around them give the margins.
        double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
        if (!pv.FindXYAtStationAndElevation(s0, e0, ref x0, ref y0) || !pv.FindXYAtStationAndElevation(s1, e1, ref x1, ref y1))
            throw new PlanException("Couldn't measure the profile view's graph.");
        var ext = pv.GeometricExtents;
        var frame = new FrameGeometry(
            MarginLeft: Math.Max(0, x0 - ext.MinPoint.X),
            MarginRight: Math.Max(0, ext.MaxPoint.X - x1),
            MarginTop: Math.Max(0, ext.MaxPoint.Y - y1),
            MarginBottom: Math.Max(0, y0 - ext.MinPoint.Y),
            HorizontalScale: (x1 - x0) / (s1 - s0),
            VerticalScale: (y1 - y0) / (e1 - e0));

        var overrides = new List<(ObjectId, bool, ObjectId)>();
        foreach (ProfileOverride o in pv.GraphOverrides)
            overrides.Add((o.ProfileId, o.Draw, o.OverrideStyleId));

        var profileIds = overrides.Count > 0
            ? overrides.Where(o => o.Item2).Select(o => o.Item1)
            : alignment.GetProfileIds().Cast<ObjectId>();

        var samples = new ProfileSamples();
        foreach (var id in profileIds)
        {
            if (id.IsNull || id.IsErased)
                continue;
            var profile = (Profile)tr.GetObject(id, OpenMode.ForRead);
            samples.Add(Sample(profile));
        }

        return new SourceView
        {
            Id = viewId,
            Name = pv.Name,
            AlignmentId = pv.AlignmentId,
            AlignmentName = alignment.Name,
            StyleId = pv.StyleId,
            StationStart = s0,
            StationEnd = s1,
            ElevationMin = e0,
            ElevationMax = e1,
            AlignmentStart = alignment.StartingStation,
            AlignmentEnd = alignment.EndingStation,
            Frame = frame,
            Extents = new Bounds(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y),
            Samples = samples,
            IsSplit = pv.SplitProfileView,
            Overrides = overrides,
        };
    }

    /// <summary>PVIs plus evenly spaced points, so vertical curves and surface detail are both covered.</summary>
    private static IEnumerable<(double, double)> Sample(Profile profile)
    {
        var points = new List<(double, double)>();
        double a = profile.StartingStation, b = profile.EndingStation;
        if (!(b > a))
            return points;

        foreach (ProfilePVI pvi in profile.PVIs)
            points.Add((pvi.RawStation, pvi.Elevation));

        double step = (b - a) / SamplesPerProfile;
        for (int i = 0; i <= SamplesPerProfile; i++)
        {
            double station = i == SamplesPerProfile ? b : a + i * step;
            try { points.Add((station, profile.ElevationAt(station))); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* gap in the profile (e.g. off the surface) */ }
            catch (ArgumentException) { }
        }
        return points;
    }
}
