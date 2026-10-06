using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>
/// Break lines built the way Express Tools _BREAKLINE builds them: the break block placed at the middle
/// of the line, aligned with it and scaled; its two POINT objects mark where the line stops and restarts.
/// </summary>
internal static class BreakSymbol
{
    /// <summary>
    /// The break block's id, loading NAME.dwg from the support path if the drawing doesn't have it.
    /// Null (with a warning) when the standard doesn't use it or it can't be found.
    /// </summary>
    public static ObjectId Ensure(Database db, CadStandard standard, DetailSettings settings, List<string> warnings)
    {
        if (!settings.ShowBreakLines || !string.Equals(standard.BreakLineMethod, CadStandard.BreakLineCommand, StringComparison.OrdinalIgnoreCase))
            return ObjectId.Null;
        string name = standard.BreakLineBlock?.Trim() ?? "";
        if (name.Length == 0)
            return ObjectId.Null;

        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name))
                return bt[name];
        }

        try
        {
            var path = HostApplicationServices.Current.FindFile(name + ".dwg", db, FindFileHint.Default);
            using var source = new Database(false, true);
            source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            source.CloseInput(true);
            return db.Insert(name, source, true);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            warnings.Add($"Break line block \"{name}\" is not in this drawing, the standard drawing, or on the support path as {name}.dwg. Drew simple break lines instead.");
            return ObjectId.Null;
        }
    }

    /// <summary>
    /// Entities for a break line from <paramref name="start"/> to <paramref name="end"/> (local detail coordinates):
    /// the block's geometry at the midpoint, plus the line from each end to the block's connection points.
    /// </summary>
    public static List<Entity> Build(Transaction tr, ObjectId blockId, Point3d start, Point3d end, double size, double extension, ObjectId layer)
    {
        var dir = (end - start).GetNormal();
        var mid = start + (end - start) / 2;
        var def = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
        var xform = Matrix3d.Displacement(mid - Point3d.Origin)
                    * Matrix3d.Rotation(Vector3d.XAxis.GetAngleTo(dir, Vector3d.ZAxis), Vector3d.ZAxis, Point3d.Origin)
                    * Matrix3d.Scaling(size, Point3d.Origin)
                    * Matrix3d.Displacement(Point3d.Origin - def.Origin);

        var symbol = new List<Entity>();
        var joins = new List<Point3d>();
        foreach (ObjectId id in def)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
                continue;
            if (ent is DBPoint p)
            {
                joins.Add(p.Position.TransformBy(xform)); // _BREAKLINE's connection points
                continue;
            }
            if (ent is AttributeDefinition)
                continue;
            var copy = (Entity)ent.Clone();
            copy.TransformBy(xform);
            copy.LayerId = layer;
            if (copy.Color.IsByBlock || copy.Color.IsByLayer)
                copy.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            if (copy.LinetypeId != ObjectId.Null && copy.Linetype.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase))
                copy.Linetype = "BYLAYER";
            symbol.Add(copy);
        }

        // No POINTs in the block: join at the ends of the symbol along the line.
        if (joins.Count < 2)
        {
            joins.Clear();
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var e in symbol)
            {
                try
                {
                    var ext = e.GeometricExtents;
                    foreach (var c in new[] { ext.MinPoint, ext.MaxPoint, new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0), new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0) })
                    {
                        double t = (c - mid).DotProduct(dir);
                        lo = Math.Min(lo, t);
                        hi = Math.Max(hi, t);
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            if (lo > hi) { lo = -size / 2; hi = size / 2; }
            joins.Add(mid + dir * lo);
            joins.Add(mid + dir * hi);
        }

        var ordered = joins.OrderBy(j => (j - start).DotProduct(dir)).ToList();
        var a = ordered[0];
        var b = ordered[^1];
        var result = new List<Entity>
        {
            new Line(start - dir * extension, a) { LayerId = layer },
            new Line(b, end + dir * extension) { LayerId = layer },
        };
        result.AddRange(symbol);
        return result;
    }
}
