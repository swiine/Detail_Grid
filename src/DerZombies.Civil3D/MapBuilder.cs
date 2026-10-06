using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DerZombies.Core;

namespace DerZombies.Civil3D
{
    /// <summary>
    /// Writes the static castle into model space as ordinary drawing objects on
    /// "DE-*" layers: walls, doors (erased when bought), area names, perk machines,
    /// wall-buys and quest features, inside a plan-sheet style title border.
    /// </summary>
    internal static class MapBuilder
    {
        public const string LayerPrefix = "DE-";
        public const string Walls = "DE-WALLS";
        public const string WallFill = "DE-WALL-FILL";
        public const string Doors = "DE-DOORS";
        public const string Areas = "DE-AREAS";
        public const string Perks = "DE-PERKS";
        public const string WallBuys = "DE-WALLBUYS";
        public const string Features = "DE-FEATURES";
        public const string Sheet = "DE-SHEET";
        public const string Terrain = "DE-TERRAIN";
        public const string Points = "DE-COGO-POINTS";
        /// <summary>Layer for the transient (non-database) game graphics.</summary>
        public const string Game = "DE-GAME";

        /// <summary>Erases everything on DE-* layers in model space (optionally keeping the Civil 3D terrain and points).</summary>
        public static void Clear(Database db, bool includeCivil = true)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                if (!ent.Layer.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!includeCivil && (Is(ent.Layer, Terrain) || Is(ent.Layer, Points))) continue;
                ent.UpgradeOpen();
                ent.Erase();
            }
            tr.Commit();
        }

        /// <summary>Draws the map. Returns the entities belonging to each door so they can be erased when bought.</summary>
        public static Dictionary<char, List<ObjectId>> Build(Database db, GameMap map, Vector3d origin)
        {
            var doorEntities = new Dictionary<char, List<ObjectId>>();
            using var tr = db.TransactionManager.StartTransaction();

            EnsureLayer(db, tr, Walls, 7, LineWeight.LineWeight050);
            EnsureLayer(db, tr, WallFill, 251);
            EnsureLayer(db, tr, Doors, 42);
            EnsureLayer(db, tr, Areas, 254);
            EnsureLayer(db, tr, Perks, 7);
            EnsureLayer(db, tr, WallBuys, 51);
            EnsureLayer(db, tr, Features, 7);
            EnsureLayer(db, tr, Sheet, 8);
            EnsureLayer(db, tr, Terrain, 32);
            EnsureLayer(db, tr, Points, 141);
            EnsureLayer(db, tr, Game, 7);

            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            Point3d P(Vec2 v) => new Point3d(v.X, v.Y, 0) + origin;
            ObjectId Add(Entity e)
            {
                ObjectId id = ms.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
                return id;
            }
            ObjectId Text(Vec2 at, string s, double h, string layer, short color = 256, bool centred = true)
            {
                var t = new DBText();
                t.SetDatabaseDefaults(db);
                t.TextString = s;
                t.Height = h;
                t.Layer = layer;
                t.ColorIndex = color;
                if (centred)
                {
                    t.HorizontalMode = TextHorizontalMode.TextCenter;
                    t.VerticalMode = TextVerticalMode.TextVerticalMid;
                    t.AlignmentPoint = P(at);
                }
                else t.Position = P(at);
                var id = Add(t);
                if (centred) t.AdjustAlignment(db);
                return id;
            }
            ObjectId Rect(Vec2 min, Vec2 max, string layer, short color = 256)
            {
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(min.X + origin.X, min.Y + origin.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(max.X + origin.X, min.Y + origin.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(max.X + origin.X, max.Y + origin.Y), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(min.X + origin.X, max.Y + origin.Y), 0, 0, 0);
                pl.Closed = true;
                pl.Layer = layer;
                pl.ColorIndex = color;
                return Add(pl);
            }
            ObjectId Ring(Vec2 c, double r, string layer, short color = 256)
            {
                var circle = new Circle(P(c), Vector3d.ZAxis, r) { Layer = layer, ColorIndex = color };
                return Add(circle);
            }

            // Wall fill: a solid for every wall cell that borders the playable area.
            double cs = map.Cell;
            for (int r = 0; r < map.Rows; r++)
            for (int c = 0; c < map.Cols; c++)
            {
                if (map.IsFloorOrDoor(c, r) || !TouchesFloor(map, c, r)) continue;
                var ctr = map.CellCenter(c, r);
                var solid = new Solid(
                    P(ctr + new Vec2(-cs / 2, -cs / 2)), P(ctr + new Vec2(cs / 2, -cs / 2)),
                    P(ctr + new Vec2(-cs / 2, cs / 2)), P(ctr + new Vec2(cs / 2, cs / 2)))
                { Layer = WallFill };
                Add(solid);
            }

            foreach (var (a, b) in map.WallSegments())
                Add(new Line(P(a), P(b)) { Layer = Walls });

            // Doors: a crossed-out rectangle plus its price, all erased when bought.
            foreach (var door in map.Doors.Values)
            {
                var (min, max) = map.DoorBounds(door);
                var ids = new List<ObjectId>
                {
                    Rect(min, max, Doors),
                    Add(new Line(P(min), P(max)) { Layer = Doors }),
                    Add(new Line(P(new Vec2(min.X, max.Y)), P(new Vec2(max.X, min.Y))) { Layer = Doors }),
                    Text(door.Center, door.Cost.ToString(), 2.2, Doors, 2),
                };
                doorEntities[door.Id] = ids;
            }

            foreach (var area in map.Areas.Values)
                Text(area.LabelPoint, area.Name.ToUpperInvariant(), 4, Areas);

            // Points of interest.
            foreach (var f in MapData.Features)
            {
                var c = map.CellCenter(f.Col, f.Row);
                switch (f.Kind)
                {
                    case FeatureKind.Perk:
                    {
                        var perk = Enum.Parse<Perk>(f.Key);
                        short col = DerZombies.Core.Perks.Color(perk);
                        Rect(c - new Vec2(1.8, 1.8), c + new Vec2(1.8, 1.8), Perks, col);
                        Text(c + new Vec2(0, 3.2), DerZombies.Core.Perks.DisplayName(perk), 1.6, Perks, col);
                        Text(c - new Vec2(0, 3.2), DerZombies.Core.Perks.Cost(perk).ToString(), 1.4, Perks, col);
                        break;
                    }
                    case FeatureKind.WallBuy:
                    {
                        var def = WeaponCatalog.Get(f.Key);
                        Rect(c - new Vec2(2.2, 0.8), c + new Vec2(2.2, 0.8), WallBuys);
                        Text(c + new Vec2(0, 2.2), def.Name, 1.5, WallBuys);
                        Text(c - new Vec2(0, 2.2), def.WallCost.ToString(), 1.3, WallBuys);
                        break;
                    }
                    case FeatureKind.BoxLocation:
                        Rect(c - new Vec2(3.2, 1.7), c + new Vec2(3.2, 1.7), Features, 8);
                        break;
                    case FeatureKind.PowerSwitch:
                        Rect(c - new Vec2(1.5, 2.5), c + new Vec2(1.5, 2.5), Features, 1);
                        Text(c - new Vec2(0, 4), "POWER", 1.6, Features, 1);
                        break;
                    case FeatureKind.LandingPad:
                        Ring(c, 3.5, Features, 8);
                        Text(c - new Vec2(0, 5), "LANDING PAD", 1.4, Features, 8);
                        break;
                    case FeatureKind.PackAPunch:
                        Rect(c - new Vec2(2.5, 2.5), c + new Vec2(2.5, 2.5), Features, 6);
                        Text(c - new Vec2(0, 4.2), "PACK-A-PUNCH", 1.6, Features, 6);
                        break;
                    case FeatureKind.Gondola:
                        Rect(c - new Vec2(2.5, 1.5), c + new Vec2(2.5, 1.5), Features, 4);
                        Text(c - new Vec2(0, 3.5), "GONDOLA", 1.5, Features, 4);
                        break;
                    case FeatureKind.Dragon:
                        Ring(c, 2.5, Features, 30);
                        Text(c + new Vec2(0, 4), f.Key.ToUpperInvariant(), 1.5, Features, 30);
                        break;
                    case FeatureKind.BowPedestal:
                        Rect(c - new Vec2(1.5, 1.5), c + new Vec2(1.5, 1.5), Features, 30);
                        Text(c - new Vec2(0, 3.2), "BOW PEDESTAL", 1.4, Features, 30);
                        break;
                    case FeatureKind.BowAltar:
                        Ring(c, 2, Features, 6);
                        Text(c - new Vec2(0, 3.5), f.Key.ToUpperInvariant() + " ALTAR", 1.4, Features, 6);
                        break;
                }
            }

            // Plan-sheet border and title block.
            var sheetMin = new Vec2(-20, -55);
            var sheetMax = new Vec2(map.Width + 95, map.Height + 60);
            Rect(sheetMin, sheetMax, Sheet);
            Rect(new Vec2(map.Width + 5, -55), new Vec2(map.Width + 95, -15), Sheet);
            Text(new Vec2(map.Width + 8, -22), "DER EISENDRACHE", 4.5, Sheet, 1, centred: false);
            Text(new Vec2(map.Width + 8, -29), "CASTLE SITE PLAN - ZOMBIES", 2.5, Sheet, 7, centred: false);
            Text(new Vec2(map.Width + 8, -35), $"1 GRID CELL = {map.Cell:0} UNITS", 2, Sheet, 8, centred: false);
            Text(new Vec2(map.Width + 8, -40), "SHEET Z-01 OF 01   CIVIL 3D 2026", 2, Sheet, 8, centred: false);
            Text(new Vec2(map.Width + 8, -45), "COMMANDS: DERZOMBIES / DERZQUIT", 2, Sheet, 8, centred: false);

            // North arrow.
            var na = new Vec2(map.Width + 80, -35);
            var arrow = new Polyline();
            arrow.AddVertexAt(0, new Point2d(na.X + origin.X, na.Y + 8 + origin.Y), 0, 0, 0);
            arrow.AddVertexAt(1, new Point2d(na.X - 3 + origin.X, na.Y - 4 + origin.Y), 0, 0, 0);
            arrow.AddVertexAt(2, new Point2d(na.X + origin.X, na.Y - 1 + origin.Y), 0, 0, 0);
            arrow.AddVertexAt(3, new Point2d(na.X + 3 + origin.X, na.Y - 4 + origin.Y), 0, 0, 0);
            arrow.Closed = true;
            arrow.Layer = Sheet;
            Add(arrow);
            Text(na + new Vec2(0, 11), "N", 3, Sheet);

            tr.Commit();
            return doorEntities;
        }

        public static void EraseDoor(Database db, IEnumerable<ObjectId> ids)
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
            }
            tr.Commit();
        }

        private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool TouchesFloor(GameMap map, int c, int r)
        {
            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
                if (map.IsFloorOrDoor(c + dc, r + dr)) return true;
            return false;
        }

        internal static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci, LineWeight lw = LineWeight.ByLineWeightDefault)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, aci),
                LineWeight = lw,
            };
            var id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }
    }
}
