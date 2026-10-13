using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using ProfileStacker.Core;

namespace ProfileStacker;

/// <summary>What was made, for the command line.</summary>
internal sealed record BuildResult(int Views, int Layouts, IReadOnlyList<string> Notes);

/// <summary>
/// Makes the plan real: one profile view per planned view (same alignment, style, bands and profiles as
/// the long view, with its own station and elevation range), placed in model space, then a copy of the
/// template layout per sheet with each viewport framed on its view.
/// </summary>
internal static class StackBuilder
{
    public static BuildResult Build(Database db, SourceView source, StackPlan plan, StackerSettings s, SheetTemplate? template)
    {
        var notes = new List<string>();

        // To the right of the long view, tops level with it.
        double gap = s.ModelGap > 0 ? s.ModelGap : 0;
        double left = source.Extents.MaxX + Math.Max(gap, source.Extents.Height / 4);
        var placements = ModelPlacement.Place(plan, source.Frame, left, source.Extents.MaxY, gap, s.PaperClearance);

        string Sta(double station) => Naming.Station(station, s.StationPlusDigits, s.StationDecimals);

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var src = (ProfileView)tr.GetObject(source.Id, OpenMode.ForRead);
            ObjectId bandStyleId = SaveBands(src, notes);
            var usedNames = ExistingViewNames(tr, db);

            foreach (var p in placements)
            {
                var v = p.View;
                string name = Naming.Unique(
                    Naming.View(s.ViewNamePattern, source.Name, v.Number, plan.Views.Count, v.Sheet + 1, plan.SheetCount, Sta(v.StationStart), Sta(v.StationEnd)),
                    usedNames.Contains);
                usedNames.Add(name);

                var id = ProfileView.Create(source.AlignmentId, new Point3d(p.GraphX, p.GraphY, 0));
                var pv = (ProfileView)tr.GetObject(id, OpenMode.ForWrite);
                Try(() => pv.Name = name, $"View {v.Number}: couldn't name it \"{name}\".", notes);
                Try(() => pv.StyleId = source.StyleId, $"View {v.Number}: couldn't use the long view's style.", notes);
                CopyBands(src, pv, bandStyleId, v.Number, notes);
                CopyOverrides(source, pv);

                SetStations(pv, v.StationStart, v.StationEnd);
                SetElevations(pv, v.ElevationMin, v.ElevationMax);

                // Put the graph's lower-left corner exactly where the plan has it.
                double x = 0, y = 0;
                if (pv.FindXYAtStationAndElevation(v.StationStart, v.ElevationMin, ref x, ref y))
                    pv.Location = pv.Location + new Vector3d(p.GraphX - x, p.GraphY - y, 0);
            }

            if (!bandStyleId.IsNull)
                Try(() => tr.GetObject(bandStyleId, OpenMode.ForWrite).Erase(), "", null);

            if (s.EraseSourceView)
                tr.GetObject(source.Id, OpenMode.ForWrite).Erase();
            tr.Commit();
        }

        int layouts = 0;
        if (s.CreateLayouts && template is not null)
            layouts = MakeLayouts(db, plan, placements, s, template, Sta, notes);

        return new BuildResult(placements.Count, layouts, notes);
    }

    private static int MakeLayouts(
        Database db, StackPlan plan, IReadOnlyList<ViewPlacement> placements, StackerSettings s,
        SheetTemplate template, Func<double, string> sta, List<string> notes)
    {
        var slots = template.Slots(s.TemplateLayout, s.ViewportLayerFilter);
        var taken = ExistingLayoutNames(db);
        int made = 0;

        foreach (var sheet in placements.GroupBy(p => p.View.Sheet))
        {
            var views = sheet.ToList();
            string name = Naming.Unique(
                Naming.Layout(s.LayoutNamePattern, sheet.Key + 1, plan.SheetCount, sta(views[0].View.StationStart), sta(views[^1].View.StationEnd)),
                taken.Contains);
            taken.Add(name);

            var layoutId = template.CopyLayoutTo(db, s.TemplateLayout, name);
            made++;

            using var tr = db.TransactionManager.StartTransaction();
            var viewports = SheetTemplate.MatchViewports(tr, layoutId, slots, s.ViewportLayerFilter);
            for (int i = 0; i < viewports.Count; i++)
            {
                var (vpId, _) = viewports[i];
                if (vpId.IsNull)
                {
                    notes.Add($"{name}: viewport {i + 1} wasn't found in the copied layout.");
                    continue;
                }
                var vp = (Viewport)tr.GetObject(vpId, OpenMode.ForWrite);
                var p = views.FirstOrDefault(x => x.View.Slot.Index == i);
                if (p is null)
                {
                    // Last sheet with fewer views than viewports: drop the empty ones.
                    vp.Erase();
                    continue;
                }
                vp.ViewDirection = Vector3d.ZAxis;
                vp.ViewTarget = Point3d.Origin;
                vp.TwistAngle = 0;
                vp.CustomScale = p.View.Slot.Scale;
                vp.ViewCenter = new Point2d(p.ViewCenterX, p.ViewCenterY);
                vp.Locked = s.LockViewports;
            }
            tr.Commit();
        }
        return made;
    }

    private static void SetStations(ProfileView pv, double start, double end)
    {
        pv.StationRangeMode = StationRangeType.UserSpecified;
        // Order matters: the start can't pass the current end, nor the end the current start.
        if (start >= pv.StationEnd) { pv.StationEnd = end; pv.StationStart = start; }
        else { pv.StationStart = start; pv.StationEnd = end; }
    }

    private static void SetElevations(ProfileView pv, double min, double max)
    {
        pv.ElevationRangeMode = ElevationRangeType.UserSpecified;
        if (min >= pv.ElevationMax) { pv.ElevationMax = max; pv.ElevationMin = min; }
        else { pv.ElevationMin = min; pv.ElevationMax = max; }
    }

    /// <summary>Saves the long view's bands as a band set style so the new views can import the same set.</summary>
    private static ObjectId SaveBands(ProfileView src, List<string> notes)
    {
        string name = $"Profile Stacker {DateTime.Now:yyyyMMddHHmmssfff}";
        try { return src.Bands.SaveAsBandSetStyle(name); }
        catch (System.Exception)
        {
            notes.Add("Couldn't copy the long view's bands; the new views have the default band set.");
            return ObjectId.Null;
        }
    }

    /// <summary>Same bands as the long view, each reading the same profiles and alignments.</summary>
    private static void CopyBands(ProfileView src, ProfileView pv, ObjectId bandStyleId, int number, List<string> notes)
    {
        if (bandStyleId.IsNull)
            return;
        Try(() =>
        {
            pv.Bands.ImportBandSetStyle(bandStyleId);
            pv.Bands.SetBottomBandItems(CopyData(src.Bands.GetBottomBandItems(), pv.Bands.GetBottomBandItems()));
            pv.Bands.SetTopBandItems(CopyData(src.Bands.GetTopBandItems(), pv.Bands.GetTopBandItems()));
        }, $"View {number}: couldn't copy the bands' profiles; check its band properties.", notes);
    }

    private static ProfileViewBandItemCollection CopyData(ProfileViewBandItemCollection from, ProfileViewBandItemCollection to)
    {
        var source = from.Cast<ProfileViewBandItem>().ToList();
        var target = to.Cast<ProfileViewBandItem>().ToList();
        for (int i = 0; i < Math.Min(source.Count, target.Count); i++)
        {
            var (a, b) = (source[i], target[i]);
            if (a.BandType != b.BandType)
                continue;
            Try(() => b.Profile1Id = a.Profile1Id);
            Try(() => b.Profile2Id = a.Profile2Id);
            Try(() => b.Alignment2Id = a.Alignment2Id);
            Try(() => b.DataSourceId = a.DataSourceId);
            Try(() => b.MaterialName = a.MaterialName);
            Try(() => b.Gap = a.Gap);
        }
        return to;
    }

    /// <summary>Show and hide the same profiles, with the same style overrides, as the long view.</summary>
    private static void CopyOverrides(SourceView source, ProfileView pv)
    {
        if (source.Overrides.Count == 0)
            return;
        foreach (ProfileOverride o in pv.GraphOverrides)
        {
            var match = source.Overrides.FirstOrDefault(x => x.ProfileId == o.ProfileId);
            if (match.ProfileId.IsNull)
                continue;
            Try(() => o.Draw = match.Draw);
            if (!match.OverrideStyleId.IsNull)
                Try(() => o.OverrideStyleId = match.OverrideStyleId);
        }
    }

    /// <summary>Names of every profile view in the drawing (Civil 3D wants them unique).</summary>
    private static HashSet<string> ExistingViewNames(Transaction tr, Database db)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var civil = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId alignmentId in civil.GetAlignmentIds())
        {
            var alignment = (Alignment)tr.GetObject(alignmentId, OpenMode.ForRead);
            foreach (ObjectId id in alignment.GetProfileViewIds())
                names.Add(((ProfileView)tr.GetObject(id, OpenMode.ForRead)).Name);
        }
        return names;
    }

    private static HashSet<string> ExistingLayoutNames(Database db)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var tr = db.TransactionManager.StartTransaction();
        var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dict)
            names.Add(((Layout)tr.GetObject(entry.Value, OpenMode.ForRead)).LayoutName);
        tr.Commit();
        return names;
    }

    private static void Try(Action action, string failure = "", List<string>? notes = null)
    {
        try { action(); }
        catch (System.Exception)
        {
            if (notes is not null && failure.Length > 0)
                notes.Add(failure);
        }
    }
}
