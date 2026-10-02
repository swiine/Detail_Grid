using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TTWLinemarking.Core;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace TTWLinemarking
{
    internal sealed class ApplyResult
    {
        public int Applied;     // polylines styled (single line) or centrelines offset (pair)
        public int Locked;      // on a locked layer, left alone
        public int Unsupported; // 3D polylines etc. - can't carry a width
        public int Failed;      // offset geometry failed (self-intersecting, zero length...)
    }

    // All drawing changes happen here. Callers open the transaction and commit it.
    internal static class Applicator
    {
        // TTW's paint colours as true colour, not ACI swatches. White paint stays ByLayer.
        public static AcColor ColorFor(Paint paint)
        {
            switch (paint)
            {
                case Paint.Red: return AcColor.FromRgb(184, 29, 19);
                case Paint.Yellow: return AcColor.FromRgb(255, 191, 0);
                default: return AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
            }
        }

        public static ApplyResult ApplySingle(Transaction tr, IEnumerable<ObjectId> ids, LinemarkingItem item)
        {
            var result = new ApplyResult();
            foreach (ObjectId id in ids)
            {
                var ent = OpenForWrite(tr, id, result);
                if (ent == null) continue;
                if (Style(ent, item)) result.Applied++;
                else result.Unsupported++;
            }
            return result;
        }

        // The selected polyline is an unpainted centreline reference. Two new lines are created
        // symmetrically either side of it, and the reference moves to Defpoints (still visible,
        // never plots). sidePoint picks which side gets item.Code; the other side gets the partner.
        // For identical pairs (BB/BB1) sidePoint is null and ignored.
        public static ApplyResult ApplyPair(Transaction tr, IEnumerable<ObjectId> ids, LinemarkingItem item, Point3d? sidePoint)
        {
            var result = new ApplyResult();
            var partner = Catalogue.Find(item.Pair.PartnerCode);
            double half = OffsetMath.HalfSpacing(item);

            foreach (ObjectId id in ids)
            {
                var ent = OpenForWrite(tr, id, result);
                if (ent == null) continue;
                if (!(ent is Polyline || ent is Polyline2d))
                {
                    result.Unsupported++;
                    continue;
                }
                var centreline = (Curve)ent;

                DBObjectCollection plus = null, minus = null;
                try
                {
                    plus = centreline.GetOffsetCurves(half);
                    minus = centreline.GetOffsetCurves(-half);
                }
                catch (AcException)
                {
                    DisposeAll(plus);
                    DisposeAll(minus);
                    result.Failed++;
                    continue;
                }
                if (plus.Count == 0 || minus.Count == 0)
                {
                    DisposeAll(plus);
                    DisposeAll(minus);
                    result.Failed++;
                    continue;
                }

                bool plusIsPrimary = sidePoint == null || Distance(plus, sidePoint.Value) <= Distance(minus, sidePoint.Value);
                var owner = (BlockTableRecord)tr.GetObject(centreline.OwnerId, OpenMode.ForWrite);
                AddAll(tr, owner, plusIsPrimary ? plus : minus, item);
                AddAll(tr, owner, plusIsPrimary ? minus : plus, partner);

                centreline.Layer = Catalogue.ReferenceLayer;
                result.Applied++;
            }
            return result;
        }

        public static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = name };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        // Layer, linetype, colour, width and linetype generation. Returns false (and changes
        // nothing) for entities that can't carry a constant width.
        private static bool Style(Entity ent, LinemarkingItem item)
        {
            switch (ent)
            {
                case Polyline pl:
                    SetCommon(pl, item);
                    pl.ConstantWidth = item.Width;
                    pl.Plinegen = true;
                    return true;
                case Polyline2d p2:
                    SetCommon(p2, item);
                    p2.ConstantWidth = item.Width;
                    p2.LinetypeGenerationOn = true;
                    return true;
                default:
                    return false;
            }
        }

        private static void SetCommon(Entity ent, LinemarkingItem item)
        {
            ent.Layer = Catalogue.Layer;
            ent.Linetype = item.Code;
            ent.Color = ColorFor(item.Paint);
        }

        private static Entity OpenForWrite(Transaction tr, ObjectId id, ApplyResult result)
        {
            try
            {
                return tr.GetObject(id, OpenMode.ForWrite) as Entity;
            }
            catch (AcException ex) when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.OnLockedLayer)
            {
                result.Locked++;
                return null;
            }
        }

        private static void AddAll(Transaction tr, BlockTableRecord owner, DBObjectCollection curves, LinemarkingItem item)
        {
            foreach (DBObject obj in curves)
            {
                // Added to the drawing before styling: a heavy Polyline2d's vertices must be
                // database-resident before its width can be set.
                if (obj is Polyline || obj is Polyline2d)
                {
                    var ent = (Entity)obj;
                    owner.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                    Style(ent, item);
                }
                else
                {
                    obj.Dispose();
                }
            }
        }

        private static double Distance(DBObjectCollection curves, Point3d p)
        {
            double best = double.MaxValue;
            foreach (DBObject obj in curves)
            {
                if (obj is Curve c)
                {
                    double d = c.GetClosestPointTo(p, false).DistanceTo(p);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static void DisposeAll(DBObjectCollection objs)
        {
            if (objs == null) return;
            foreach (DBObject o in objs) o.Dispose();
        }
    }
}
