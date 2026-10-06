using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DerZombies.Core;

namespace DerZombies.Civil3D
{
    /// <summary>
    /// The castle as real 3D solids for first-person mode: stone floors per area,
    /// extruded walls, door barricades (erased when bought) and machines you can
    /// walk up to. Everything goes on DE-* layers so DERZCLEAN removes it.
    /// </summary>
    internal static class MapBuilder3D
    {
        public const double WallHeight = 14;
        public const double DoorHeight = 11;

        public const string Floor = "DE-3D-FLOOR";
        public const string Walls = "DE-3D-WALLS";
        public const string Doors = "DE-3D-DOORS";
        public const string Props = "DE-3D-PROPS";

        // Slightly different stone tones per area so you can tell where you are.
        private static readonly Dictionary<char, short> AreaFloorColors = new Dictionary<char, short>
        {
            ['S'] = 252, ['L'] = 253, ['G'] = 251, ['C'] = 254, ['A'] = 33,
            ['B'] = 252, ['U'] = 250, ['M'] = 153, ['R'] = 43, ['T'] = 23,
        };

        public static Dictionary<char, List<ObjectId>> Build(Database db, GameMap map, Vector3d origin)
        {
            var doorEntities = new Dictionary<char, List<ObjectId>>();
            using var tr = db.TransactionManager.StartTransaction();

            MapBuilder.EnsureLayer(db, tr, Floor, 252);
            MapBuilder.EnsureLayer(db, tr, Walls, 8);
            MapBuilder.EnsureLayer(db, tr, Doors, 34);
            MapBuilder.EnsureLayer(db, tr, Props, 7);
            MapBuilder.EnsureLayer(db, tr, MapBuilder.Game, 7);
            MapBuilder.EnsureLayer(db, tr, MapBuilder.Terrain, 32);

            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            ObjectId Add(Entity e, string layer, short color = 256)
            {
                e.Layer = layer;
                e.ColorIndex = color;
                var id = ms.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
                return id;
            }
            ObjectId Box(Vec2 min, Vec2 max, double z0, double z1, string layer, short color = 256) =>
                Add(Solids.Box(db, new Point3d(min.X, min.Y, z0) + origin, new Point3d(max.X, max.Y, z1) + origin), layer, color);
            ObjectId BoxAt(Vec2 c, double sx, double sy, double z0, double z1, string layer, short color = 256) =>
                Box(c - new Vec2(sx / 2, sy / 2), c + new Vec2(sx / 2, sy / 2), z0, z1, layer, color);
            ObjectId Cyl(Vec2 c, double r, double z0, double z1, string layer, short color = 256) =>
                Add(Solids.Cylinder(db, new Point3d(c.X, c.Y, z0) + origin, r, z1 - z0), layer, color);
            ObjectId Ball(Vec2 c, double r, double z, string layer, short color = 256) =>
                Add(Solids.Sphere(db, new Point3d(c.X, c.Y, z) + origin, r), layer, color);

            double cs = map.Cell;

            // Floors: one slab per area (its bounding box), plus a base slab under the doorways.
            Box(new Vec2(0, 0), new Vec2(map.Width, map.Height), -1.2, -0.6, Floor, 250);
            foreach (var area in map.Areas.Values)
            {
                int minC = int.MaxValue, maxC = int.MinValue, minR = int.MaxValue, maxR = int.MinValue;
                foreach (var (c, r) in area.Cells)
                {
                    minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
                    minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                }
                var a = map.CellCenter(minC, maxR) - new Vec2(cs / 2, cs / 2);
                var b = map.CellCenter(maxC, minR) + new Vec2(cs / 2, cs / 2);
                Box(a, b, -0.6, 0, Floor, AreaFloorColors.TryGetValue(area.Id, out var col) ? col : (short)252);
            }

            // Walls: wall cells that border the playable area, merged into runs along each row.
            for (int r = 0; r < map.Rows; r++)
            {
                int start = -1;
                for (int c = 0; c <= map.Cols; c++)
                {
                    bool wall = c < map.Cols && !map.IsFloorOrDoor(c, r) && MapBuilder.TouchesFloor(map, c, r);
                    if (wall && start < 0) start = c;
                    if (!wall && start >= 0)
                    {
                        var a = map.CellCenter(start, r) - new Vec2(cs / 2, cs / 2);
                        var b = map.CellCenter(c - 1, r) + new Vec2(cs / 2, cs / 2);
                        Box(a, b, 0, WallHeight, Walls);
                        start = -1;
                    }
                }
            }

            // Battlements on the outer wall.
            for (int c = 0; c < map.Cols; c += 2)
            {
                BoxAt(map.CellCenter(c, 0), cs * 0.8, cs, WallHeight, WallHeight + 3, Walls);
                BoxAt(map.CellCenter(c, map.Rows - 1), cs * 0.8, cs, WallHeight, WallHeight + 3, Walls);
            }
            for (int r = 1; r < map.Rows - 1; r += 2)
            {
                BoxAt(map.CellCenter(0, r), cs, cs * 0.8, WallHeight, WallHeight + 3, Walls);
                BoxAt(map.CellCenter(map.Cols - 1, r), cs, cs * 0.8, WallHeight, WallHeight + 3, Walls);
            }

            // Doors: a plank barricade filling the doorway.
            foreach (var door in map.Doors.Values)
            {
                var (min, max) = map.DoorBounds(door);
                doorEntities[door.Id] = new List<ObjectId> { Box(min, max, 0, DoorHeight, Doors) };
            }

            // Machines and quest props.
            foreach (var f in MapData.Features)
            {
                var c = map.CellCenter(f.Col, f.Row);
                switch (f.Kind)
                {
                    case FeatureKind.Perk:
                    {
                        short col = Perks.Color(Enum.Parse<Perk>(f.Key));
                        BoxAt(c, 3, 2.4, 0, 7, Props, col);
                        BoxAt(c, 3.4, 2.8, 7, 7.6, Props, 8);
                        break;
                    }
                    case FeatureKind.WallBuy:
                        BoxAt(c, 0.6, 0.6, 0, 3.5, Props, 8);       // stand
                        BoxAt(c, 4, 0.5, 3.5, 5.5, Props, 51);      // chalk board
                        break;
                    case FeatureKind.PowerSwitch:
                        BoxAt(c, 2.5, 1.2, 0, 5.5, Props, 1);
                        break;
                    case FeatureKind.LandingPad:
                        Cyl(c, 3.6, 0, 0.35, Props, 8);
                        break;
                    case FeatureKind.PackAPunch:
                        BoxAt(c, 4, 3.5, 0, 6, Props, 6);
                        BoxAt(c, 4.6, 4.1, 6, 6.6, Props, 250);
                        break;
                    case FeatureKind.Gondola:
                        BoxAt(c, 5, 3, 0, 0.5, Props, 8);
                        BoxAt(c, 4, 2.4, 3, 7, Props, 4);
                        Cyl(c, 0.2, 7, WallHeight + 2, Props, 8);    // cable mast
                        break;
                    case FeatureKind.Dragon:
                        Cyl(c, 2.4, 0, 11, Props, 30);
                        Ball(c, 2.6, 12.5, Props, 30);
                        break;
                    case FeatureKind.BowPedestal:
                        Cyl(c, 1.3, 0, 3, Props, 252);
                        break;
                    case FeatureKind.BowAltar:
                        Cyl(c, 1.7, 0, 2.6, Props, 6);
                        break;
                }
            }

            tr.Commit();
            return doorEntities;
        }
    }

    /// <summary>Solid3d helpers shared by the 3D map and the 3D renderer.</summary>
    internal static class Solids
    {
        public static Solid3d Box(Database db, Point3d min, Point3d max)
        {
            var s = new Solid3d();
            s.SetDatabaseDefaults(db);
            s.CreateBox(Math.Max(0.01, max.X - min.X), Math.Max(0.01, max.Y - min.Y), Math.Max(0.01, max.Z - min.Z));
            s.TransformBy(Matrix3d.Displacement(new Vector3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2)));
            return s;
        }

        /// <summary>Upright cylinder standing on <paramref name="baseCenter"/>.</summary>
        public static Solid3d Cylinder(Database db, Point3d baseCenter, double radius, double height)
        {
            var s = new Solid3d();
            s.SetDatabaseDefaults(db);
            s.CreateFrustum(height, radius, radius, radius);
            s.TransformBy(Matrix3d.Displacement(baseCenter.GetAsVector() + new Vector3d(0, 0, height / 2)));
            return s;
        }

        public static Solid3d Sphere(Database db, Point3d center, double radius)
        {
            var s = new Solid3d();
            s.SetDatabaseDefaults(db);
            s.CreateSphere(radius);
            s.TransformBy(Matrix3d.Displacement(center.GetAsVector()));
            return s;
        }
    }
}
