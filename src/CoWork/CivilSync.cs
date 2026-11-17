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
    /// loads in plain AutoCAD. Also pipe networks, sample lines, alignments and layout profiles (corridors rebuild from
    /// them on each PC). Surfaces and corridor designs themselves don't sync yet.
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

        // ------------------------------------------------------------------ alignments and profiles

        private const string AlignmentType = "Autodesk.Civil.DatabaseServices.Alignment";
        private const string ProfileType = "Autodesk.Civil.DatabaseServices.Profile";
        private const string CorridorType = "Autodesk.Civil.DatabaseServices.Corridor";

        public static bool IsAlignment(DBObject o) => o.GetType().FullName == AlignmentType;
        public static bool IsProfile(DBObject o) => o.GetType().FullName == ProfileType;
        /// <summary>Corridors rebuild on each PC from the shared alignments and profiles, so they aren't sent themselves.</summary>
        public static bool IsCorridor(DBObject o) => o.GetType().FullName == CorridorType;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string AlignmentName(DBObject o) => ((Alignment)o).Name;

        /// <summary>"alignment|profile" for a profile (its alignment may already be gone: then just "|profile").</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ProfileKey(Transaction tr, DBObject o)
        {
            var p = (Profile)o;
            string align = "";
            try { if (!p.AlignmentId.IsNull && !p.AlignmentId.IsErased && tr.GetObject(p.AlignmentId, OpenMode.ForRead) is Alignment a) align = a.Name; }
            catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            return LiveBatch.ProfileKey(align, p.Name);
        }

        private static string? LayerName(Transaction tr, Autodesk.AutoCAD.DatabaseServices.Entity e)
        {
            try { return ((LayerTableRecord)tr.GetObject(e.LayerId, OpenMode.ForRead)).Name; } catch (System.Exception) { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LiveAlignment? ReadAlignment(Transaction tr, DBObject o)
        {
            if (o is not Alignment al) return null;
            var parts = new List<LiveAlignmentPart>();
            AlignmentEntityCollection ents = al.Entities;
            for (int i = 0; i < ents.Count; i++)
            {
                AlignmentEntity e = ents.GetEntityByOrder(i);
                for (int j = 0; j < e.SubEntityCount; j++)
                {
                    AlignmentSubEntity se = e[j];
                    switch (se)
                    {
                        case AlignmentSubEntityLine l:
                            parts.Add(new LiveAlignmentPart("L", l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y));
                            break;
                        case AlignmentSubEntityArc a:
                            parts.Add(new LiveAlignmentPart("A", a.StartPoint.X, a.StartPoint.Y, a.EndPoint.X, a.EndPoint.Y, a.Radius, a.Clockwise, a.GreaterThan180));
                            break;
                        case AlignmentSubEntitySpiral sp:
                            parts.Add(new LiveAlignmentPart("S", sp.StartPoint.X, sp.StartPoint.Y, sp.EndPoint.X, sp.EndPoint.Y,
                                Px: sp.SPIPoint.X, Py: sp.SPIPoint.Y, Spiral: sp.SpiralDefinition.ToString()));
                            break;
                    }
                }
            }
            return new LiveAlignment(al.Name, LayerName(tr, al), al.StyleName, al.StartingStation, parts);
        }

        private static ObjectId AlignmentByName(Database db, Transaction tr, string name)
        {
            foreach (ObjectId id in CivilDocument.GetCivilDocument(db).GetAlignmentIds())
                if (tr.GetObject(id, OpenMode.ForRead) is Alignment a && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
            return ObjectId.Null;
        }

        private static ObjectId StyleOr(ObjectId wanted, Func<ObjectId> first) => !wanted.IsNull ? wanted : first();

        /// <summary>
        /// Makes an alignment (found by name, added if it isn't here) match: rebuilt piece by piece as fixed lines, arcs
        /// and spirals. Returns its id, or null if nothing needed changing.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ObjectId? ApplyAlignment(Database db, Transaction tr, LiveAlignment la)
        {
            CivilDocument civ = CivilDocument.GetCivilDocument(db);
            ObjectId id = AlignmentByName(db, tr, la.Name);
            if (id.IsNull)
            {
                ObjectId style = ObjectId.Null, labels = ObjectId.Null;
                try { if (la.Style != null && civ.Styles.AlignmentStyles.Contains(la.Style)) style = civ.Styles.AlignmentStyles[la.Style]; } catch (System.Exception) { }
                style = StyleOr(style, () => civ.Styles.AlignmentStyles[0]);
                try { labels = civ.Styles.LabelSetStyles.AlignmentLabelSetStyles[0]; } catch (System.Exception) { }
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ObjectId layer = la.Layer != null && layers.Has(la.Layer) ? layers[la.Layer] : db.Clayer;
                id = Alignment.Create(civ, la.Name, ObjectId.Null, layer, style, labels);
            }
            var al = (Alignment)tr.GetObject(id, OpenMode.ForRead);
            if (ReadAlignment(tr, al) is { } now && now.SameAs(la)) return null;
            al.UpgradeOpen();
            AlignmentEntityCollection ents = al.Entities;
            ents.Clear();
            int prev = -1;
            foreach (LiveAlignmentPart p in la.Parts)
            {
                var start = new Point3d(p.X1, p.Y1, 0);
                var end = new Point3d(p.X2, p.Y2, 0);
                AlignmentEntity added = p.Kind switch
                {
                    "L" => prev < 0 ? ents.AddFixedLine(start, end) : ents.AddFixedLine(prev, start, end),
                    "A" => prev < 0 ? ents.AddFixedCurve(start, end, p.Radius, p.Clockwise) : ents.AddFixedCurve(prev, start, end, p.Radius, p.Clockwise, p.Large),
                    "S" when prev >= 0 => ents.AddFixedSpiral(prev, start, new Point3d(p.Px, p.Py, 0), end,
                        Enum.TryParse(p.Spiral, out Autodesk.Civil.SpiralType t) ? t : Autodesk.Civil.SpiralType.Clothoid),
                    _ => ents.AddFixedLine(start, end), // a spiral can't start an alignment on its own: keep the run going
                };
                prev = added.EntityId;
            }
            if (Math.Abs(al.StartingStation - la.StartStation) > 1e-3)
                try { al.ReferencePointStation = la.StartStation; } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            return id;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void EraseAlignment(Database db, Transaction tr, string name)
        {
            ObjectId id = AlignmentByName(db, tr, name);
            if (!id.IsNull && !id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        /// <summary>A layout (design) profile's PVIs; null for surface and other profiles, which aren't sent.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LiveProfile? ReadProfile(Transaction tr, DBObject o)
        {
            if (o is not Profile p || p.ProfileType != Autodesk.Civil.DatabaseServices.ProfileType.FG) return null;
            if (p.AlignmentId.IsNull || tr.GetObject(p.AlignmentId, OpenMode.ForRead) is not Alignment al) return null;
            var pvis = new List<LivePvi>();
            foreach (ProfilePVI v in p.PVIs)
            {
                LivePvi pvi = new(v.Station, v.Elevation);
                try
                {
                    switch (v.VerticalCurve)
                    {
                        case ProfileParabolaSymmetric sym: pvi = pvi with { Curve = "sym", Length1 = sym.Length }; break;
                        case ProfileParabolaAsymmetric asym: pvi = pvi with { Curve = "asym", Length1 = asym.AsymmetricLength1, Length2 = asym.AsymmetricLength2 }; break;
                        case ProfileCircular arc: pvi = pvi with { Curve = "arc", Length1 = arc.Radius }; break;
                    }
                }
                catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); } // a PVI with no curve
                pvis.Add(pvi);
            }
            return new LiveProfile(al.Name, p.Name, LayerName(tr, p), p.StyleName, pvis);
        }

        private static ObjectId ProfileByName(Transaction tr, Alignment al, string name)
        {
            foreach (ObjectId id in al.GetProfileIds())
                if (tr.GetObject(id, OpenMode.ForRead) is Profile p && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
            return ObjectId.Null;
        }

        /// <summary>
        /// Makes a layout profile (found by alignment and name, added if missing) match: its PVIs and vertical curves
        /// are laid out again. Returns its id, or null if nothing needed changing or its alignment isn't here.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ObjectId? ApplyProfile(Database db, Transaction tr, LiveProfile lp)
        {
            CivilDocument civ = CivilDocument.GetCivilDocument(db);
            ObjectId alId = AlignmentByName(db, tr, lp.Alignment);
            if (alId.IsNull) return null;
            var al = (Alignment)tr.GetObject(alId, OpenMode.ForRead);
            ObjectId id = ProfileByName(tr, al, lp.Name);
            if (id.IsNull)
            {
                ObjectId style = ObjectId.Null, labels = ObjectId.Null;
                try { if (lp.Style != null && civ.Styles.ProfileStyles.Contains(lp.Style)) style = civ.Styles.ProfileStyles[lp.Style]; } catch (System.Exception) { }
                style = StyleOr(style, () => civ.Styles.ProfileStyles[0]);
                try { labels = civ.Styles.LabelSetStyles.ProfileLabelSetStyles[0]; } catch (System.Exception) { }
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ObjectId layer = lp.Layer != null && layers.Has(lp.Layer) ? layers[lp.Layer] : db.Clayer;
                id = Profile.CreateByLayout(lp.Name, alId, layer, style, labels);
            }
            var p = (Profile)tr.GetObject(id, OpenMode.ForRead);
            if (p.ProfileType != Autodesk.Civil.DatabaseServices.ProfileType.FG) return null; // same name as a surface profile: leave it
            if (ReadProfile(tr, p) is { } now && now.SameAs(lp)) return null;
            p.UpgradeOpen();
            p.Entities.Clear();
            foreach (LivePvi v in lp.Pvis.OrderBy(v => v.Station))
                switch (v.Curve)
                {
                    case "sym" when v.Length1 > 0: p.PVIs.AddPVISymParabola(v.Station, v.Elevation, v.Length1); break;
                    case "asym" when v.Length1 > 0 && v.Length2 > 0: p.PVIs.AddPVIAsymParabola(v.Station, v.Elevation, v.Length1, v.Length2); break;
                    case "arc" when v.Length1 > 0: p.PVIs.AddPVIArc(v.Station, v.Elevation, v.Length1); break;
                    default: p.PVIs.AddPVI(v.Station, v.Elevation); break;
                }
            return id;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void EraseProfile(Database db, Transaction tr, string key)
        {
            int bar = key.IndexOf('|');
            if (bar < 0) return;
            ObjectId alId = AlignmentByName(db, tr, key[..bar]);
            if (alId.IsNull) return;
            ObjectId id = ProfileByName(tr, (Alignment)tr.GetObject(alId, OpenMode.ForRead), key[(bar + 1)..]);
            if (!id.IsNull && !id.IsErased && ((Profile)tr.GetObject(id, OpenMode.ForRead)).ProfileType == Autodesk.Civil.DatabaseServices.ProfileType.FG)
                tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        /// <summary>
        /// Rebuilds the corridors that follow any of these alignments or profiles, so they catch up with someone
        /// else's change. Returns how many were rebuilt.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int RebuildCorridors(Database db, ICollection<ObjectId> changed)
        {
            if (changed.Count == 0) return 0;
            int rebuilt = 0;
            using Transaction tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId cid in CivilDocument.GetCivilDocument(db).CorridorCollection)
            {
                try
                {
                    if (tr.GetObject(cid, OpenMode.ForRead) is not Corridor c) continue;
                    bool follows = false;
                    foreach (Baseline b in c.Baselines)
                        if (changed.Contains(b.AlignmentId) || changed.Contains(b.ProfileId)) { follows = true; break; }
                    if (!follows) continue;
                    c.UpgradeOpen();
                    c.Rebuild();
                    rebuilt++;
                }
                catch (System.Exception ex) { CoWork.Core.Log.Error("Rebuilding a corridor", ex); }
            }
            tr.Commit();
            return rebuilt;
        }

        /// <summary>Brings every point group's list of points up to date after points or rules changed.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateGroups(Database db)
        {
            try { CivilDocument.GetCivilDocument(db).PointGroups.UpdateAllPointGroups(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }
    }
}
