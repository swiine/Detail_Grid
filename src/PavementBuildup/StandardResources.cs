using Autodesk.AutoCAD.DatabaseServices;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>Copies what a CAD standard needs (layers, linetypes, styles, blocks) from the company standard drawing.</summary>
internal static class StandardResources
{
    /// <summary>
    /// Imports into <paramref name="db"/> every layer, text style, dimension style and block the standard
    /// names that <paramref name="db"/> doesn't have yet, from <see cref="CadStandard.StandardDrawing"/>.
    /// Existing definitions are never overwritten. Returns the names that were imported.
    /// </summary>
    public static List<string> Import(Database db, CadStandard standard, List<string> warnings)
    {
        var imported = new List<string>();
        var path = standard.StandardDrawing?.Trim();
        if (string.IsNullOrEmpty(path))
            return imported;
        if (!File.Exists(path))
        {
            warnings.Add($"Standard drawing not found: {path}. Layers, styles and blocks it would provide weren't imported.");
            return imported;
        }

        var wanted = new (Func<Database, ObjectId> Table, IEnumerable<string> Names, string Kind)[]
        {
            (d => d.LayerTableId, standard.AllLayerNames(), "layer"),
            (d => d.TextStyleTableId, new[] { standard.TextStyle }, "text style"),
            (d => d.DimStyleTableId, new[] { standard.DimensionStyle }, "dimension style"),
            (d => d.LinetypeTableId, standard.Layers.Select(l => l.Linetype), "linetype"),
            (d => d.BlockTableId, new[] { standard.BreakLineBlock }, "block"),
        };

        try
        {
            using var source = new Database(false, true);
            source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            source.CloseInput(true);

            foreach (var (table, names, kind) in wanted)
            {
                var ids = new ObjectIdCollection();
                var found = new List<string>();
                using (var str = source.TransactionManager.StartOpenCloseTransaction())
                using (var ttr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var sourceTable = (SymbolTable)str.GetObject(table(source), OpenMode.ForRead);
                    var targetTable = (SymbolTable)ttr.GetObject(table(db), OpenMode.ForRead);
                    foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (targetTable.Has(name) || !sourceTable.Has(name))
                            continue;
                        ids.Add(sourceTable[name]);
                        found.Add($"{kind} {name}");
                    }
                    str.Commit();
                    ttr.Commit();
                }
                if (ids.Count == 0)
                    continue;

                // Ignore = keep anything the drawing already has; dependencies (linetypes, text styles) come along.
                source.WblockCloneObjects(ids, table(db), new IdMapping(), DuplicateRecordCloning.Ignore, false);
                imported.AddRange(found);
            }
        }
        catch (Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not read the standard drawing {path}: {ex.Message}");
        }
        return imported;
    }
}
