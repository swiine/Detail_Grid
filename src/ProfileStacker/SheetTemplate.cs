using Autodesk.AutoCAD.DatabaseServices;
using ProfileStacker.Core;

namespace ProfileStacker;

/// <summary>
/// The sheet template (.dwt or .dwg), opened in the background: its layouts, the profile viewports on a
/// layout, and copies of that layout for the active drawing.
/// </summary>
internal sealed class SheetTemplate : IDisposable
{
    private readonly Database _db;

    public string Path { get; }

    private SheetTemplate(string path, Database db)
    {
        Path = path;
        _db = db;
    }

    public static SheetTemplate Open(string path)
    {
        var db = new Database(false, true);
        try
        {
            db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            db.CloseInput(true);
            return new SheetTemplate(path, db);
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>Paper-space layouts in tab order.</summary>
    public IReadOnlyList<string> LayoutNames()
    {
        using var tr = _db.TransactionManager.StartTransaction();
        var dict = (DBDictionary)tr.GetObject(_db.LayoutDictionaryId, OpenMode.ForRead);
        var layouts = new List<(int, string)>();
        foreach (DBDictionaryEntry entry in dict)
        {
            var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
            if (!layout.ModelType)
                layouts.Add((layout.TabOrder, layout.LayoutName));
        }
        tr.Commit();
        return layouts.OrderBy(l => l.Item1).Select(l => l.Item2).ToList();
    }

    /// <summary>
    /// The layout's viewports, ordered top to bottom and left to right, leaving out the layout's own
    /// paper-space viewport and (when a filter is given) any whose layer doesn't contain the filter text.
    /// </summary>
    public IReadOnlyList<ViewportSlot> Slots(string layoutName, string layerFilter)
    {
        using var tr = _db.TransactionManager.StartTransaction();
        var slots = StackPlanner.OrderSlots(ProfileViewports(tr, LayoutId(tr, layoutName), layerFilter).Select(v => ToSlot(v)));
        tr.Commit();
        return slots;
    }

    /// <summary>
    /// Makes a new layout in <paramref name="target"/> (the active drawing) that is a copy of the template
    /// layout: its page setup and everything on it (title block, viewports, notes). Layers and styles the
    /// drawing already has are kept as they are. Returns the new layout's id.
    /// </summary>
    public ObjectId CopyLayoutTo(Database target, string layoutName, string newName)
    {
        ObjectId copyId = LayoutManager.Current.CreateLayout(newName);

        using var source = _db.TransactionManager.StartTransaction();
        var sourceLayout = (Layout)source.GetObject(LayoutId(source, layoutName), OpenMode.ForRead);
        var sourceBlock = (BlockTableRecord)source.GetObject(sourceLayout.BlockTableRecordId, OpenMode.ForRead);
        var viewports = sourceBlock.Cast<ObjectId>()
            .Where(id => id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Viewport))))
            .Select(id => (Viewport)source.GetObject(id, OpenMode.ForRead))
            .ToList();
        ObjectId paper = PaperViewport(sourceLayout, viewports);
        var entities = new ObjectIdCollection();
        foreach (ObjectId id in sourceBlock)
            if (id != paper)
                entities.Add(id);

        using (var tr = target.TransactionManager.StartTransaction())
        {
            var copy = (Layout)tr.GetObject(copyId, OpenMode.ForWrite);
            int tabOrder = copy.TabOrder;
            string name = copy.LayoutName;
            copy.CopyFrom(sourceLayout); // page setup: paper size, plot device, plot style, scale
            // CopyFrom can bring the template's name and tab position along; keep the new layout's own.
            if (!string.Equals(copy.LayoutName, name, StringComparison.Ordinal))
                copy.LayoutName = name;
            copy.TabOrder = tabOrder;
            try { copy.Initialize(); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* AutoCAD sets up the paper viewport when the tab is first opened */ }

            if (entities.Count > 0)
            {
                var map = new IdMapping();
                _db.WblockCloneObjects(entities, copy.BlockTableRecordId, map, DuplicateRecordCloning.Ignore, false);
            }
            tr.Commit();
        }
        source.Commit();
        return copyId;
    }

    /// <summary>The same viewports in a copied layout (matched by position), in the same order as <see cref="Slots"/>.</summary>
    public static IReadOnlyList<(ObjectId Id, ViewportSlot Slot)> MatchViewports(Transaction tr, ObjectId layoutId, IReadOnlyList<ViewportSlot> slots, string layerFilter)
    {
        var found = ProfileViewports(tr, layoutId, layerFilter).Select(v => (v.ObjectId, Slot: ToSlot(v))).ToList();
        var result = new List<(ObjectId, ViewportSlot)>();
        foreach (var slot in slots)
        {
            double tol = Math.Max(1e-6, Math.Min(slot.PaperWidth, slot.PaperHeight) * 1e-3);
            var match = found.FirstOrDefault(f =>
                Math.Abs(f.Slot.CenterX - slot.CenterX) < tol && Math.Abs(f.Slot.CenterY - slot.CenterY) < tol);
            result.Add((match.ObjectId, slot));
        }
        return result;
    }

    private ObjectId LayoutId(Transaction tr, string layoutName)
    {
        var dict = (DBDictionary)tr.GetObject(_db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dict)
        {
            var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
            if (string.Equals(layout.LayoutName, layoutName, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        throw new PlanException($"The template has no layout called \"{layoutName}\".");
    }

    private static IEnumerable<Viewport> ProfileViewports(Transaction tr, ObjectId layoutId, string layerFilter)
    {
        var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
        var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
        var viewports = btr.Cast<ObjectId>()
            .Where(id => id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Viewport))))
            .Select(id => (Viewport)tr.GetObject(id, OpenMode.ForRead))
            .ToList();

        ObjectId overall = PaperViewport(layout, viewports);
        return viewports.Where(v =>
            v.ObjectId != overall
            && v.Width > 0 && v.Height > 0
            && (string.IsNullOrWhiteSpace(layerFilter) || v.Layer.Contains(layerFilter.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The layout's own paper-space viewport (the sheet itself, not a view into model space). It's the
    /// first viewport in the layout; checked against the others' positions and the sheet size, since a
    /// layout that has never been opened may not have one yet.
    /// </summary>
    private static ObjectId PaperViewport(Layout layout, IReadOnlyList<Viewport> viewports)
    {
        var stack = layout.GetViewports();
        if (stack is { Count: > 0 })
            return stack[0];
        if (viewports.Count == 0)
            return ObjectId.Null;

        var first = viewports[0];
        if (first.Number == 1)
            return first.ObjectId;
        if (viewports.Count > 1)
        {
            bool holdsAll = viewports.Skip(1).All(v =>
                Math.Abs(v.CenterPoint.X - first.CenterPoint.X) <= first.Width / 2 &&
                Math.Abs(v.CenterPoint.Y - first.CenterPoint.Y) <= first.Height / 2);
            return holdsAll ? first.ObjectId : ObjectId.Null;
        }
        var limits = layout.Limits;
        bool sheetSized = first.Width >= 0.9 * (limits.MaxPoint.X - limits.MinPoint.X)
                          && first.Height >= 0.9 * (limits.MaxPoint.Y - limits.MinPoint.Y);
        return sheetSized ? first.ObjectId : ObjectId.Null;
    }

    private static ViewportSlot ToSlot(Viewport v) =>
        new(v.CenterPoint.X, v.CenterPoint.Y, v.Width, v.Height, v.CustomScale) { Layer = v.Layer };

    public void Dispose() => _db.Dispose();
}
