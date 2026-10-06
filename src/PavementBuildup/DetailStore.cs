using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>An existing detail in the drawing that can be edited.</summary>
internal sealed record DetailTarget(bool IsBlock, ObjectId ContainerId, DetailRecord Record, Matrix3d Placement, int Copies);

/// <summary>Stores a <see cref="DetailRecord"/> in an object's extension dictionary and finds details from a picked entity.</summary>
internal static class DetailStore
{
    public static void Write(Transaction tr, DBObject owner, DetailRecord record)
    {
        if (owner.ExtensionDictionary.IsNull)
        {
            if (!owner.IsWriteEnabled) owner.UpgradeOpen();
            owner.CreateExtensionDictionary();
        }
        var dict = (DBDictionary)tr.GetObject(owner.ExtensionDictionary, OpenMode.ForWrite);
        var data = new ResultBuffer(record.ToChunks().Select(c => new TypedValue((int)DxfCode.Text, c)).ToArray());

        if (dict.Contains(DetailRecord.XrecordKey))
        {
            var existing = (Xrecord)tr.GetObject(dict.GetAt(DetailRecord.XrecordKey), OpenMode.ForWrite);
            existing.Data = data;
        }
        else
        {
            var xrec = new Xrecord { Data = data };
            dict.SetAt(DetailRecord.XrecordKey, xrec);
            tr.AddNewlyCreatedDBObject(xrec, true);
        }
    }

    public static DetailRecord? Read(Transaction tr, DBObject owner)
    {
        if (owner.ExtensionDictionary.IsNull)
            return null;
        var dict = (DBDictionary)tr.GetObject(owner.ExtensionDictionary, OpenMode.ForRead);
        if (!dict.Contains(DetailRecord.XrecordKey))
            return null;
        var xrec = (Xrecord)tr.GetObject(dict.GetAt(DetailRecord.XrecordKey), OpenMode.ForRead);
        var chunks = xrec.Data?.AsArray().Where(v => v.TypeCode == (int)DxfCode.Text).Select(v => (string)v.Value) ?? Enumerable.Empty<string>();
        return DetailRecord.FromChunks(chunks);
    }

    /// <summary>
    /// The detail a picked entity belongs to: a block reference to a detail block, or any entity in a
    /// detail group. Null when the entity is not part of a detail drawn by this plugin.
    /// </summary>
    public static DetailTarget? Find(Transaction tr, ObjectId picked)
    {
        var ent = (Entity)tr.GetObject(picked, OpenMode.ForRead);

        if (ent is BlockReference br)
        {
            var defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            var def = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
            if (Read(tr, def) is { } record)
                return new DetailTarget(true, defId, record, br.BlockTransform, def.GetBlockReferenceIds(true, false).Count);
        }

        var reactors = ent.GetPersistentReactorIds();
        if (reactors is null)
            return null;
        foreach (ObjectId id in reactors)
        {
            if (id.IsErased || !id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Group))))
                continue;
            var group = (Group)tr.GetObject(id, OpenMode.ForRead);
            if (Read(tr, group) is { } record)
                return new DetailTarget(false, id, record, CurrentPlacement(tr, ent.Database, record), 1);
        }
        return null;
    }

    /// <summary>
    /// Placement of a loose (group) detail now: the stored matrix, moved/rotated to follow the surface
    /// line if the user has moved or rotated the detail since it was drawn.
    /// </summary>
    private static Matrix3d CurrentPlacement(Transaction tr, Database db, DetailRecord record)
    {
        var stored = record.Placement is { Length: 16 } p ? new Matrix3d(p) : Matrix3d.Identity;
        if (string.IsNullOrEmpty(record.AnchorHandle))
            return stored;

        try
        {
            var handle = new Handle(Convert.ToInt64(record.AnchorHandle, 16));
            if (!db.TryGetObjectId(handle, out var anchorId) || anchorId.IsErased)
                return stored;
            if (tr.GetObject(anchorId, OpenMode.ForRead) is not Line line || line.Length <= 0)
                return stored;

            var cs = stored.CoordinateSystem3d;
            var newX = (line.EndPoint - line.StartPoint).GetNormal();
            var z = cs.Zaxis.GetNormal();
            if (newX.IsParallelTo(z))
                return stored;
            var newY = z.CrossProduct(newX).GetNormal();
            var delta = Matrix3d.AlignCoordinateSystem(
                cs.Origin, cs.Xaxis.GetNormal(), cs.Yaxis.GetNormal(), z,
                line.StartPoint, newX, newY, z);
            return delta * stored;
        }
        catch (FormatException)
        {
            return stored;
        }
    }
}
