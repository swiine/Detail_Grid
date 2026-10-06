using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>
/// Runs Express Tools _BREAKLINE (with the company break block) up the side edges of a detail,
/// then moves what it drew into the detail's block definition or group so the break lines travel
/// and update with the detail.
/// </summary>
/// <remarks>
/// _BREAKLINE is an AutoLISP command, which .NET can't call synchronously, so the input is queued with
/// SendStringToExecute and followed by PAVEADOPTBREAKS, which collects what was created.
/// </remarks>
internal static class BreakLineRunner
{
    private sealed class Pending
    {
        public required Database Db { get; init; }
        public required ObjectId Container { get; init; }
        public required bool IsBlock { get; init; }
        public required Matrix3d Placement { get; init; }
        public required string Layer { get; init; }
        public object? OriginalFiledia { get; init; }
        public List<ObjectId> Appended { get; } = new();
        public void OnAppended(object? sender, ObjectEventArgs e)
        {
            if (e.DBObject is Entity)
                Appended.Add(e.DBObject.ObjectId);
        }
    }

    private static Pending? _pending;

    /// <summary>True when _BREAKLINE should be used: standard says so and the block can be found.</summary>
    public static bool ShouldUse(Database db, CadStandard standard, DetailSettings settings, List<string> warnings)
    {
        if (!settings.ShowBreakLines || !string.Equals(standard.BreakLineMethod, CadStandard.BreakLineCommand, StringComparison.OrdinalIgnoreCase))
            return false;
        if (BlockAvailable(db, standard.BreakLineBlock))
            return true;
        warnings.Add($"Break line block \"{standard.BreakLineBlock}\" is not in this drawing, the standard drawing, or on the support path. Drew simple break lines instead.");
        return false;
    }

    private static bool BlockAvailable(Database db, string name)
    {
        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name))
                return true;
        }
        try
        {
            return !string.IsNullOrEmpty(HostApplicationServices.Current.FindFile(name + ".dwg", db, FindFileHint.Default));
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    /// <summary>Queues _BREAKLINE for each broken side edge of <paramref name="g"/>, then PAVEADOPTBREAKS.</summary>
    public static void Queue(Document doc, ObjectId container, bool isBlock, Matrix3d placement, DetailGeometry g,
        CadStandard standard, string outlineLayer)
    {
        Cancel();
        var edges = g.Edges.Where(e => e.Count > 2)
            .Select(e => (Top: W(placement, e[0]), Bottom: W(placement, e[^1])))
            .ToList();
        if (edges.Count == 0)
            return;

        double size = standard.BreakLineSizeMm * g.AnnotationScale;
        double extension = Math.Max(0, standard.BreakLineExtensionMm) * g.AnnotationScale;
        var script = BreakLineScript.Build(standard.BreakLineBlock, size, extension, edges) + "PAVEADOPTBREAKS\n";

        // The block-name prompt opens a file dialog when FILEDIA=1, which would stall queued input.
        // Restored in Adopt, or by the next Queue if the user cancelled part-way.
        object? filedia = null;
        try
        {
            filedia = Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("FILEDIA");
            Autodesk.AutoCAD.ApplicationServices.Core.Application.SetSystemVariable("FILEDIA", (short)0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { }

        var pending = new Pending { Db = doc.Database, Container = container, IsBlock = isBlock, Placement = placement, Layer = outlineLayer, OriginalFiledia = filedia };
        doc.Database.ObjectAppended += pending.OnAppended;
        _pending = pending;
        doc.SendStringToExecute(script, true, false, false);
    }

    /// <summary>PAVEADOPTBREAKS: moves the entities _BREAKLINE created into the detail.</summary>
    public static void Adopt(Document doc)
    {
        var pending = _pending;
        Cancel();
        if (pending is null || pending.Db != doc.Database)
            return;

        int adopted = 0;
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var created = pending.Appended.Distinct()
                .Where(id => !id.IsErased)
                .Select(id => (Entity)tr.GetObject(id, OpenMode.ForWrite))
                // Only drawing-space results, not the contents of a block definition _BREAKLINE inserted.
                .Where(e => ((BlockTableRecord)tr.GetObject(e.OwnerId, OpenMode.ForRead)).IsLayout)
                .ToList();

            if (pending.IsBlock)
            {
                var def = (BlockTableRecord)tr.GetObject(pending.Container, OpenMode.ForWrite);
                var toLocal = pending.Placement.Inverse();
                foreach (var e in created)
                {
                    var copy = (Entity)e.Clone();
                    copy.TransformBy(toLocal);
                    copy.Layer = pending.Layer;
                    def.AppendEntity(copy);
                    tr.AddNewlyCreatedDBObject(copy, true);
                    e.Erase();
                    adopted++;
                }
                foreach (ObjectId refId in def.GetBlockReferenceIds(true, false))
                    ((BlockReference)tr.GetObject(refId, OpenMode.ForWrite)).RecordGraphicsModified(true);
            }
            else
            {
                var group = (Group)tr.GetObject(pending.Container, OpenMode.ForWrite);
                var ids = new ObjectIdCollection();
                foreach (var e in created)
                {
                    e.Layer = pending.Layer;
                    ids.Add(e.ObjectId);
                    adopted++;
                }
                if (ids.Count > 0)
                    group.Append(ids);
            }
            tr.Commit();
        }

        if (adopted == 0)
            doc.Editor.WriteMessage("\nBREAKLINE didn't draw anything. Check Express Tools is installed and the break block has its two connection points. " +
                                    "Set Break line method to BUILTIN in PAVESTANDARD to use simple break lines.");
        else
            doc.Editor.Regen();
    }

    private static void Cancel()
    {
        if (_pending is { } p)
        {
            p.Db.ObjectAppended -= p.OnAppended;
            if (p.OriginalFiledia is not null)
            {
                try { Autodesk.AutoCAD.ApplicationServices.Core.Application.SetSystemVariable("FILEDIA", p.OriginalFiledia); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
        }
        _pending = null;
    }

    private static (double, double, double) W(Matrix3d placement, Pt p)
    {
        var w = new Point3d(p.X, p.Y, 0).TransformBy(placement);
        return (w.X, w.Y, w.Z);
    }
}
