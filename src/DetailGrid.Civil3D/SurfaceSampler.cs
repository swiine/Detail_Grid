using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DetailGrid.Hydrology;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;
using TinSurface = Autodesk.Civil.DatabaseServices.TinSurface;
using TinSurfaceTriangle = Autodesk.Civil.DatabaseServices.TinSurfaceTriangle;

namespace DetailGrid.Civil3D
{
    /// <summary>Samples a Civil 3D surface onto a regular <see cref="ElevationGrid"/>.</summary>
    internal static class SurfaceSampler
    {
        public static Extents3d Extents(CivSurface surface) => surface.GeometricExtents;

        /// <summary>A round cell size giving roughly <paramref name="targetCells"/> cells over the surface.</summary>
        public static double SuggestCellSize(Extents3d ext, int targetCells = 1_000_000)
        {
            double w = ext.MaxPoint.X - ext.MinPoint.X, h = ext.MaxPoint.Y - ext.MinPoint.Y;
            double raw = Math.Sqrt(Math.Max(w * h, 1e-9) / targetCells);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (double step in new[] { 1, 2, 2.5, 5, 10 })
                if (step * mag >= raw) return step * mag;
            return 10 * mag;
        }

        public static ElevationGrid CreateGrid(Extents3d ext, double cellSize)
        {
            int width = Math.Max(1, (int)Math.Ceiling((ext.MaxPoint.X - ext.MinPoint.X) / cellSize));
            int height = Math.Max(1, (int)Math.Ceiling((ext.MaxPoint.Y - ext.MinPoint.Y) / cellSize));
            return new ElevationGrid(width, height, cellSize, ext.MinPoint.X, ext.MaxPoint.Y);
        }

        /// <summary>
        /// Fills the grid from the surface. TIN surfaces are rasterised triangle by triangle
        /// (fast, linear interpolation identical to the TIN); other surface types fall back to
        /// <see cref="CivSurface.FindElevationAtXY"/> per cell.
        /// </summary>
        public static void Sample(CivSurface surface, ElevationGrid grid)
        {
            if (surface is TinSurface tin) RasteriseTin(tin, grid);
            else SamplePointwise(surface, grid);
        }

        private static void RasteriseTin(TinSurface tin, ElevationGrid grid)
        {
            double cs = grid.CellSize, ox = grid.OriginX, oy = grid.OriginY;
            foreach (TinSurfaceTriangle t in tin.Triangles)
            {
                Point3d a = t.Vertex1.Location, b = t.Vertex2.Location, c = t.Vertex3.Location;
                double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (Math.Abs(det) < 1e-12) continue;

                double minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
                double minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                int c0 = Math.Max(0, (int)Math.Ceiling((minX - ox) / cs - 0.5));
                int c1 = Math.Min(grid.Width - 1, (int)Math.Floor((maxX - ox) / cs - 0.5));
                int r0 = Math.Max(0, (int)Math.Ceiling((oy - maxY) / cs - 0.5));
                int r1 = Math.Min(grid.Height - 1, (int)Math.Floor((oy - minY) / cs - 0.5));

                const double tol = -1e-9;
                for (int r = r0; r <= r1; r++)
                {
                    double y = oy - (r + 0.5) * cs;
                    for (int col = c0; col <= c1; col++)
                    {
                        double x = ox + (col + 0.5) * cs;
                        double l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / det;
                        double l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / det;
                        double l3 = 1 - l1 - l2;
                        if (l1 < tol || l2 < tol || l3 < tol) continue;
                        grid.Z[r * grid.Width + col] = (float)(l1 * a.Z + l2 * b.Z + l3 * c.Z);
                    }
                }
            }
        }

        private static void SamplePointwise(CivSurface surface, ElevationGrid grid)
        {
            for (int i = 0; i < grid.Count; i++)
            {
                var (x, y) = grid.CellCenter(i);
                try { grid.Z[i] = (float)surface.FindElevationAtXY(x, y); }
                catch (Exception) { grid.Z[i] = float.NaN; } // outside the surface or inside a hide boundary
            }
        }
    }
}
