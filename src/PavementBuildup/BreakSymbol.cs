using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

internal static class BreakSymbol
{
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

    public static List<Entity> Build(Transaction tr, ObjectId blockId, Point3d start, Point3d end, double length, double extension, ObjectId layer)
        => Build(tr, blockId, start, end, length, extension, layer, out _);

    public static List<Entity> Build(Transaction tr, ObjectId blockId, Point3d start, Point3d end, double length, double extension, ObjectId layer, out List<Pt> profile)
    {
        var dir = (end - start).GetNormal();
        var mid = start + (end - start) / 2;
        var def = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
        var toBase = Matrix3d.Displacement(Point3d.Origin - def.Origin);

        var geometry = new List<Entity>();
        var points = new List<Point3d>();
        Extents3d? box = null;
        foreach (ObjectId id in def)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent || ent is AttributeDefinition)
                continue;
            if (ent is DBPoint p)
            {
                points.Add(p.Position.TransformBy(toBase));
                continue;
            }
            geometry.Add(ent);
            try
            {
                var ext = ent.GeometricExtents;
                ext.TransformBy(toBase);
                if (box is { } b) { b.AddExtents(ext); box = b; } else box = ext;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
        }
        if (box is null && points.Count < 2)
        {
            profile = new List<Pt> { new(start.X, start.Y), new(end.X, end.Y) };
            return new List<Entity> { new Line(start, end) { LayerId = layer } };
        }

        Vector3d native;
        if (points.Count >= 2 && points[0].DistanceTo(points[1]) > 1e-9)
            native = (points[1] - points[0]).GetNormal();
        else
        {
            var e = box!.Value;
            native = (e.MaxPoint.X - e.MinPoint.X) >= (e.MaxPoint.Y - e.MinPoint.Y) ? Vector3d.XAxis : Vector3d.YAxis;
        }

        var corners = new List<Point3d>(points);
        if (box is { } bx)
            corners.AddRange(new[] { bx.MinPoint, bx.MaxPoint, new Point3d(bx.MinPoint.X, bx.MaxPoint.Y, 0), new Point3d(bx.MaxPoint.X, bx.MinPoint.Y, 0) });
        double lo = corners.Min(c => c.GetAsVector().DotProduct(native));
        double hi = corners.Max(c => c.GetAsVector().DotProduct(native));
        double nativeLength = Math.Max(hi - lo, 1e-9);
        var centre = points.Count >= 2
            ? points[0] + (points[1] - points[0]) / 2
            : box!.Value.MinPoint + (box.Value.MaxPoint - box.Value.MinPoint) / 2;

        double angle = Math.Atan2(dir.Y, dir.X) - Math.Atan2(native.Y, native.X);
        var xform = Matrix3d.Displacement(mid - Point3d.Origin)
                    * Matrix3d.Rotation(angle, Vector3d.ZAxis, Point3d.Origin)
                    * Matrix3d.Scaling(length / nativeLength, Point3d.Origin)
                    * Matrix3d.Displacement(Point3d.Origin - centre)
                    * toBase;

        var result = new List<Entity>();
        foreach (var ent in geometry)
        {
            var copy = (Entity)ent.Clone();
            copy.TransformBy(xform);
            copy.LayerId = layer;
            if (copy.Color.IsByBlock)
                copy.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            result.Add(copy);
        }

        List<Point3d> joins;
        if (points.Count >= 2)
            joins = points.Take(2).Select(p => p.TransformBy(xform * toBase.Inverse())).ToList();
        else
            joins = new List<Point3d> { mid - dir * (length / 2), mid + dir * (length / 2) };

        var ordered = joins.OrderBy(j => (j - start).DotProduct(dir)).ToList();
        result.Insert(0, new Line(start - dir * extension, ordered[0]) { LayerId = layer });
        result.Insert(1, new Line(ordered[^1], end + dir * extension) { LayerId = layer });

        double ta = (ordered[0] - start).DotProduct(dir), tb = (ordered[^1] - start).DotProduct(dir);
        var vertices = new List<Point3d>();
        foreach (var e in result.Skip(2))
        {
            switch (e)
            {
                case Line l: vertices.Add(l.StartPoint); vertices.Add(l.EndPoint); break;
                case Polyline pl: for (int i = 0; i < pl.NumberOfVertices; i++) vertices.Add(pl.GetPoint3dAt(i)); break;
                case Arc a: vertices.Add(a.StartPoint); vertices.Add(a.EndPoint); break;
            }
        }
        profile = new List<Pt> { new(start.X, start.Y), new(ordered[0].X, ordered[0].Y) };
        profile.AddRange(vertices
            .Select(v => (T: (v - start).DotProduct(dir), V: v))
            .Where(x => x.T > ta + 1e-9 && x.T < tb - 1e-9)
            .OrderBy(x => x.T)
            .Select(x => new Pt(x.V.X, x.V.Y)));
        profile.Add(new Pt(ordered[^1].X, ordered[^1].Y));
        profile.Add(new Pt(end.X, end.Y));
        return result;
    }
}
