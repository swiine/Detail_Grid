using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using CoWork.Core;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace CoWork
{
    /// <summary>
    /// CoWork for Civil 3D objects, stage one: COGO points (matched by point number) and point groups (matched by
    /// name). Everything that touches Civil 3D's own API is in here and only runs inside Civil 3D, so CoWork still
    /// loads in plain AutoCAD. Alignments, surfaces, corridors and the like don't sync yet.
    /// </summary>
    internal static class CivilSync
    {
        private const string PointType = "Autodesk.Civil.DatabaseServices.CogoPoint";
        private const string GroupType = "Autodesk.Civil.DatabaseServices.PointGroup";

        /// <summary>Running inside Civil 3D (its API is loaded).</summary>
        public static bool Available => AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AeccDbMgd");

        // Checked by type name, so nothing of Civil 3D's is loaded to answer.
        public static bool IsCivil(DBObject o) => o.GetType().Namespace?.StartsWith("Autodesk.Civil", StringComparison.Ordinal) == true;
        public static bool IsPoint(DBObject o) => o.GetType().FullName == PointType;
        public static bool IsGroup(DBObject o) => o.GetType().FullName == GroupType;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static uint PointNumber(DBObject o) => ((CogoPoint)o).PointNumber;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LivePoint ReadPoint(DBObject o)
        {
            var p = (CogoPoint)o;
            return new LivePoint(p.PointNumber, p.Easting, p.Northing, p.Elevation, p.RawDescription ?? "", p.PointName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ObjectId FindPoint(Database db, uint number)
        {
            CogoPointCollection cogo = CivilDocument.GetCivilDocument(db).CogoPoints;
            return cogo.Contains(number) ? cogo.GetPointByPointNumber(number) : ObjectId.Null;
        }

        /// <summary>Adds or updates a point so it matches the one from the other PC.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ObjectId ApplyPoint(Database db, Transaction tr, LivePoint lp)
        {
            CogoPointCollection cogo = CivilDocument.GetCivilDocument(db).CogoPoints;
            ObjectId id;
            if (cogo.Contains(lp.Number)) id = cogo.GetPointByPointNumber(lp.Number);
            else
            {
                id = cogo.Add(new Point3d(lp.Easting, lp.Northing, lp.Elevation), lp.RawDescription, false);
                var added = (CogoPoint)tr.GetObject(id, OpenMode.ForWrite);
                if (added.PointNumber != lp.Number) added.PointNumber = lp.Number;
            }
            var p = (CogoPoint)tr.GetObject(id, OpenMode.ForWrite);
            if (p.Easting != lp.Easting) p.Easting = lp.Easting;
            if (p.Northing != lp.Northing) p.Northing = lp.Northing;
            if (p.Elevation != lp.Elevation) p.Elevation = lp.Elevation;
            if (p.RawDescription != lp.RawDescription) p.RawDescription = lp.RawDescription;
            if (lp.Name != null && p.PointName != lp.Name) p.PointName = lp.Name;
            return id;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ErasePoint(Database db, Transaction tr, uint number)
        {
            ObjectId id = FindPoint(db, number);
            if (!id.IsNull && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string GroupName(DBObject o) => ((PointGroup)o).Name;

        /// <summary>A point group's description and include/exclude rules (null for "_All Points", which isn't shared).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LivePointGroup? ReadGroup(DBObject o)
        {
            var g = (PointGroup)o;
            if (g.IsAllPointsGroup) return null;
            if (g.GetQuery() is StandardPointGroupQuery q)
                return new LivePointGroup(g.Name, g.Description, q.IncludeAllPoints, q.IncludeNumbers, q.ExcludeNumbers,
                    q.IncludeRawDescriptions, q.ExcludeRawDescriptions, q.IncludeFullDescriptions, q.ExcludeFullDescriptions,
                    q.IncludeNames, q.ExcludeNames, q.IncludeElevations, q.ExcludeElevations);
            // Custom (query builder) groups: share the name and description; the rules stay as each PC has them.
            return new LivePointGroup(g.Name, g.Description, false, null, null, null, null, null, null, null, null, null, null);
        }

        private static ObjectId GroupByName(PointGroupCollection groups, Transaction tr, string name)
        {
            foreach (ObjectId id in groups)
                if (tr.GetObject(id, OpenMode.ForRead) is PointGroup g && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
            return ObjectId.Null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ApplyGroup(Database db, Transaction tr, LivePointGroup lg)
        {
            PointGroupCollection groups = CivilDocument.GetCivilDocument(db).PointGroups;
            ObjectId id = GroupByName(groups, tr, lg.Name);
            if (id.IsNull) id = groups.Add(lg.Name);
            var g = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
            if (g.IsAllPointsGroup) return;
            if (ReadGroup(g) is { } now && now with { Name = lg.Name } == lg) return; // already the same: leave it alone
            g.UpgradeOpen();
            if ((g.Description ?? "") != (lg.Description ?? "")) g.Description = lg.Description ?? "";
            bool hasRules = lg.IncludeAll || new[]
            {
                lg.IncludeNumbers, lg.ExcludeNumbers, lg.IncludeRawDescriptions, lg.ExcludeRawDescriptions, lg.IncludeFullDescriptions,
                lg.ExcludeFullDescriptions, lg.IncludeNames, lg.ExcludeNames, lg.IncludeElevations, lg.ExcludeElevations,
            }.Any(x => !string.IsNullOrEmpty(x));
            if (hasRules || g.GetQuery() is StandardPointGroupQuery)
                g.SetQuery(new StandardPointGroupQuery
                {
                    IncludeAllPoints = lg.IncludeAll,
                    IncludeNumbers = lg.IncludeNumbers ?? "", ExcludeNumbers = lg.ExcludeNumbers ?? "",
                    IncludeRawDescriptions = lg.IncludeRawDescriptions ?? "", ExcludeRawDescriptions = lg.ExcludeRawDescriptions ?? "",
                    IncludeFullDescriptions = lg.IncludeFullDescriptions ?? "", ExcludeFullDescriptions = lg.ExcludeFullDescriptions ?? "",
                    IncludeNames = lg.IncludeNames ?? "", ExcludeNames = lg.ExcludeNames ?? "",
                    IncludeElevations = lg.IncludeElevations ?? "", ExcludeElevations = lg.ExcludeElevations ?? "",
                });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void EraseGroup(Database db, Transaction tr, string name)
        {
            PointGroupCollection groups = CivilDocument.GetCivilDocument(db).PointGroups;
            ObjectId id = GroupByName(groups, tr, name);
            if (id.IsNull || ((PointGroup)tr.GetObject(id, OpenMode.ForRead)).IsAllPointsGroup) return;
            tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        // ------------------------------------------------------------------ pipe networks

        private const string PipeType = "Autodesk.Civil.DatabaseServices.Pipe";
        private const string StructureType = "Autodesk.Civil.DatabaseServices.Structure";
        private const string SampleLineType = "Autodesk.Civil.DatabaseServices.SampleLine";

        public static bool IsPipePart(DBObject o) => o.GetType().FullName is PipeType or StructureType;
        public static bool IsSampleLine(DBObject o) => o.GetType().FullName == SampleLineType;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LivePipePart? ReadPipePart(DBObject o)
        {
            if (o is Pipe p)
                return new LivePipePart(p.NetworkName, p.Name, true, p.StartPoint.X, p.StartPoint.Y, p.StartPoint.Z, p.EndPoint.X, p.EndPoint.Y, p.EndPoint.Z);
            if (o is Structure s)
                return new LivePipePart(s.NetworkName, s.Name, false, s.Location.X, s.Location.Y, s.Location.Z, 0, 0, 0, s.RimElevation, s.SumpElevation);
            return null;
        }

        /// <summary>Moves an existing pipe or structure to match (parts are found by network and name; new parts aren't added).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool ApplyPipePart(Database db, Transaction tr, LivePipePart lp)
        {
            foreach (ObjectId nid in CivilDocument.GetCivilDocument(db).GetPipeNetworkIds())
            {
                if (tr.GetObject(nid, OpenMode.ForRead) is not Network net || !string.Equals(net.Name, lp.Network, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (ObjectId id in lp.IsPipe ? net.GetPipeIds() : net.GetStructureIds())
                {
                    DBObject o = tr.GetObject(id, OpenMode.ForRead);
                    if (o is Pipe p && lp.IsPipe && p.Name == lp.Name)
                    {
                        var start = new Point3d(lp.X1, lp.Y1, lp.Z1);
                        var end = new Point3d(lp.X2, lp.Y2, lp.Z2);
                        if (p.StartPoint.DistanceTo(start) < 1e-6 && p.EndPoint.DistanceTo(end) < 1e-6) return true;
                        p.UpgradeOpen();
                        p.StartPoint = start;
                        p.EndPoint = end;
                        return true;
                    }
                    if (o is Structure s && !lp.IsPipe && s.Name == lp.Name)
                    {
                        s.UpgradeOpen();
                        var at = new Point3d(lp.X1, lp.Y1, lp.Z1);
                        if (s.Location.DistanceTo(at) > 1e-6) s.Location = at;
                        if (Math.Abs(s.RimElevation - lp.Rim) > 1e-6) s.RimElevation = lp.Rim;
                        if (Math.Abs(s.SumpElevation - lp.Sump) > 1e-6) s.SumpElevation = lp.Sump;
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ sample lines

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LiveSampleLine? ReadSampleLine(Transaction tr, DBObject o)
        {
            if (o is not SampleLine sl || tr.GetObject(sl.GroupId, OpenMode.ForRead) is not SampleLineGroup g) return null;
            var vertices = new List<double[]>();
            foreach (SampleLineVertex v in sl.Vertices) vertices.Add(new[] { v.Location.X, v.Location.Y, v.Location.Z });
            return new LiveSampleLine(g.Name, sl.Name, sl.Station, vertices);
        }

        /// <summary>Moves an existing sample line (by group and name) to match: its station and vertices. Sections follow by themselves.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool ApplySampleLine(Database db, Transaction tr, LiveSampleLine ls)
        {
            foreach (ObjectId aid in CivilDocument.GetCivilDocument(db).GetAlignmentIds())
            {
                if (tr.GetObject(aid, OpenMode.ForRead) is not Alignment al) continue;
                foreach (ObjectId gid in al.GetSampleLineGroupIds())
                {
                    if (tr.GetObject(gid, OpenMode.ForRead) is not SampleLineGroup g || g.Name != ls.Group) continue;
                    foreach (ObjectId sid in g.GetSampleLineIds())
                    {
                        if (tr.GetObject(sid, OpenMode.ForRead) is not SampleLine sl || sl.Name != ls.Name) continue;
                        sl.UpgradeOpen();
                        if (Math.Abs(sl.Station - ls.Station) > 1e-6) sl.Station = ls.Station;
                        if (ls.Vertices is { } vs && vs.Count == sl.Vertices.Count)
                            for (int i = 0; i < vs.Count; i++)
                            {
                                var at = new Point3d(vs[i][0], vs[i][1], vs[i][2]);
                                if (sl.Vertices[i].Location.DistanceTo(at) > 1e-6) sl.Vertices[i].Location = at;
                            }
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Brings every point group's list of points up to date after points or rules changed.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateGroups(Database db)
        {
            try { CivilDocument.GetCivilDocument(db).PointGroups.UpdateAllPointGroups(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }
    }
}
