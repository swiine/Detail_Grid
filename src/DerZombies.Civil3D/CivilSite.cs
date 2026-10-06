using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using DerZombies.Core;

namespace DerZombies.Civil3D
{
    /// <summary>
    /// The Civil 3D part of the castle: a TIN surface for the mountain the castle
    /// sits on (contoured around the walls) and survey COGO points on every machine.
    /// Kept apart from the AutoCAD-only code so the game still runs if this fails.
    /// </summary>
    internal static class CivilSite
    {
        public const string SurfaceName = "DE - Eisendrache Mountain";
        public const double CastleElevation = 300.0;

        public static string Build(Database db, GameMap map, Vector3d origin)
        {
            var civDoc = CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();

            // ---- Mountain surface: a plateau under the castle falling away on every side.
            ObjectId surfaceId = TinSurface.Create(db, UniqueSurfaceName(civDoc, tr));
            var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForWrite);
            surface.Layer = MapBuilder.Terrain;

            var contourStyle = civDoc.Styles.SurfaceStyles
                .Cast<ObjectId>()
                .Select(id => (id, name: ((StyleBase)tr.GetObject(id, OpenMode.ForRead)).Name))
                .FirstOrDefault(s => s.name.IndexOf("contour", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!contourStyle.id.IsNull) surface.StyleId = contourStyle.id;

            var rng = new Random(115);
            var pts = new Point3dCollection();
            const double step = 10, apron = 90;
            for (double x = -apron; x <= map.Width + apron; x += step)
            for (double y = -apron; y <= map.Height + apron; y += step)
            {
                // Distance outside the castle footprint.
                double dx = Math.Max(0, Math.Max(-x, x - map.Width));
                double dy = Math.Max(0, Math.Max(-y, y - map.Height));
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 4) continue; // no data inside the walls
                double z = CastleElevation - 1.6 * d - 0.004 * d * d + (rng.NextDouble() - 0.5) * 6;
                pts.Add(new Point3d(x, y, z) + origin);
            }
            // Rim of the plateau right around the walls, so contours hug the castle.
            for (double x = -4; x <= map.Width + 4; x += step)
            {
                pts.Add(new Point3d(x, -4, CastleElevation) + origin);
                pts.Add(new Point3d(x, map.Height + 4, CastleElevation) + origin);
            }
            for (double y = 6; y < map.Height; y += step)
            {
                pts.Add(new Point3d(-4, y, CastleElevation) + origin);
                pts.Add(new Point3d(map.Width + 4, y, CastleElevation) + origin);
            }
            surface.AddVertices(pts);

            // Hide the triangles that would span the castle courtyards: a boundary
            // polyline (kept on the terrain layer) used as a Hide boundary.
            try
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var outline = new Polyline();
                outline.AddVertexAt(0, new Point2d(origin.X - 3, origin.Y - 3), 0, 0, 0);
                outline.AddVertexAt(1, new Point2d(origin.X + map.Width + 3, origin.Y - 3), 0, 0, 0);
                outline.AddVertexAt(2, new Point2d(origin.X + map.Width + 3, origin.Y + map.Height + 3), 0, 0, 0);
                outline.AddVertexAt(3, new Point2d(origin.X - 3, origin.Y + map.Height + 3), 0, 0, 0);
                outline.Closed = true;
                outline.Elevation = CastleElevation;
                outline.Layer = MapBuilder.Terrain;
                ObjectId outlineId = ms.AppendEntity(outline);
                tr.AddNewlyCreatedDBObject(outline, true);
                surface.BoundariesDefinition.AddBoundaries(new ObjectIdCollection { outlineId }, 0.1, SurfaceBoundaryType.Hide, true);
                surface.Rebuild();
            }
            catch (Exception)
            {
                // Cosmetic only.
            }

            // ---- COGO points on every machine, as if the castle had been surveyed.
            int count = 0;
            foreach (var f in MapData.Features)
            {
                string? desc = f.Kind switch
                {
                    FeatureKind.Perk => "PERK " + Perks.DisplayName(Enum.Parse<Perk>(f.Key)).ToUpperInvariant(),
                    FeatureKind.PackAPunch => "PACK-A-PUNCH",
                    FeatureKind.PowerSwitch => "POWER SWITCH",
                    FeatureKind.LandingPad => "LANDING PAD " + f.Key.ToUpperInvariant(),
                    FeatureKind.BowPedestal => "BOW PEDESTAL",
                    FeatureKind.Dragon => f.Key.ToUpperInvariant(),
                    _ => null,
                };
                if (desc == null) continue;
                var c = map.CellCenter(f.Col, f.Row);
                ObjectId pid = civDoc.CogoPoints.Add(new Point3d(c.X, c.Y, CastleElevation) + origin, desc, true);
                var cogo = (CogoPoint)tr.GetObject(pid, OpenMode.ForWrite);
                cogo.Layer = MapBuilder.Points;
                count++;
            }

            tr.Commit();
            return $"Civil 3D: built surface \"{surface.Name}\" ({pts.Count} points) and {count} COGO points.";
        }

        private static string UniqueSurfaceName(CivilDocument civDoc, Transaction tr)
        {
            var existing = civDoc.GetSurfaceIds()
                .Cast<ObjectId>()
                .Where(id => !id.IsErased)
                .Select(id => tr.GetObject(id, OpenMode.ForRead))
                .OfType<Autodesk.Civil.DatabaseServices.Surface>()
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string name = SurfaceName;
            for (int i = 2; existing.Contains(name); i++) name = $"{SurfaceName} ({i})";
            return name;
        }
    }
}
