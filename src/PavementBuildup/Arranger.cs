using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

internal static class Arranger
{
    public static bool Run(Document doc)
    {
        var ed = doc.Editor;
        var db = doc.Database;

        var frameOptions = new PromptEntityOptions("\nSelect the sheet frame (closed polyline, rectangle or block), Enter = pick two corners, Esc = back to menu: ") { AllowNone = true };
        var frame = ed.GetEntity(frameOptions);
        Extents3d sheet;
        ObjectId frameId = ObjectId.Null;
        if (frame.Status == PromptStatus.OK)
        {
            frameId = frame.ObjectId;
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            sheet = ((Entity)tr.GetObject(frameId, OpenMode.ForRead)).GeometricExtents;
            tr.Commit();
        }
        else if (frame.Status == PromptStatus.None)
        {
            var a = ed.GetPoint("\nFirst corner of the sheet area: ");
            if (a.Status != PromptStatus.OK) return true;
            var b = ed.GetCorner("\nOther corner: ", a.Value);
            if (b.Status != PromptStatus.OK) return true;
            var ucs = ed.CurrentUserCoordinateSystem;
            var p = a.Value.TransformBy(ucs);
            var q = b.Value.TransformBy(ucs);
            sheet = new Extents3d(new Point3d(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), 0), new Point3d(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), 0));
        }
        else return true;

        var select = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the details to arrange (Esc = back to menu): " });
        if (select.Status != PromptStatus.OK) return true;

        var units = new List<(List<ObjectId> Ids, Extents3d Box)>();
        int skipped = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var done = new HashSet<ObjectId>();
            foreach (var id in select.Value.GetObjectIds())
            {
                if (id == frameId || done.Contains(id)) continue;
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                List<ObjectId>? members = null;
                if (DetailStore.DetailGroup(tr, ent) is { } group) members = group.GetAllEntityIds().ToList();
                else if (ent is BlockReference) members = new List<ObjectId> { id };
                if (members is null) { skipped++; continue; }
                foreach (var m in members) done.Add(m);
                Extents3d? box = null;
                foreach (var m in members)
                {
                    try
                    {
                        var e = ((Entity)tr.GetObject(m, OpenMode.ForRead)).GeometricExtents;
                        if (box is { } bx) { bx.AddExtents(e); box = bx; } else box = e;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                if (box is { } found) units.Add((members, found));
            }
            tr.Commit();
        }
        if (units.Count == 0)
        {
            ed.WriteMessage("\nNo pavement details or blocks in that selection.");
            return false;
        }

        double width = sheet.MaxPoint.X - sheet.MinPoint.X, height = sheet.MaxPoint.Y - sheet.MinPoint.Y;
        double margin = Math.Min(width, height) * 0.03;
        var packed = SheetPacker.Pack(
            units.Select(u => new PackItem(u.Box.MaxPoint.X - u.Box.MinPoint.X, u.Box.MaxPoint.Y - u.Box.MinPoint.Y)).ToList(),
            sheet.MinPoint.X + margin, sheet.MaxPoint.Y - margin, width - 2 * margin, height - 2 * margin, margin);

        using (var tr = db.TransactionManager.StartTransaction())
        {
            for (int i = 0; i < units.Count; i++)
            {
                var (ids, box) = units[i];
                var target = packed.TopLefts[i];
                var move = Matrix3d.Displacement(new Vector3d(target.X - box.MinPoint.X, target.Y - box.MaxPoint.Y, 0));
                foreach (var id in ids)
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).TransformBy(move);
            }
            tr.Commit();
        }
        int outside = packed.Fits.Count(f => !f);
        ed.WriteMessage($"\nArranged {units.Count - outside} detail(s) in {packed.Rows} row(s) on the sheet."
                        + (outside > 0 ? $" {outside} didn't fit and are placed below the sheet: use a bigger sheet or a second one." : "")
                        + (skipped > 0 ? $" {skipped} other object(s) left where they were." : ""));
        ed.Regen();
        return false;
    }
}
