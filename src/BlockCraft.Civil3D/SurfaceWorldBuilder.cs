using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using BlockCraft.Core;
using Alignment = Autodesk.Civil.DatabaseServices.Alignment;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace BlockCraft.Civil3D;

/// <summary>Turns a Civil 3D surface (and optionally its alignments) into a voxel world.</summary>
internal static class SurfaceWorldBuilder
{
    private const int MaxColumns = 512;
    private const int MaxLayers = 256;
    private const int FloorLayers = 6;
    private const int SkyLayers = 32;

    public static World? Prompt(Document doc)
    {
        Editor ed = doc.Editor;

        var peo = new PromptEntityOptions("\nSelect a surface to play on: ");
        peo.SetRejectMessage("\nThat is not a Civil 3D surface.");
        peo.AddAllowedClass(typeof(CivilSurface), false);
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return null;

        using var tr = doc.Database.TransactionManager.StartTransaction();
        var surface = (CivilSurface)tr.GetObject(per.ObjectId, OpenMode.ForRead);
        var ext = surface.GeometricExtents;
        double width = ext.MaxPoint.X - ext.MinPoint.X;
        double depth = ext.MaxPoint.Y - ext.MinPoint.Y;
        if (width <= 0 || depth <= 0)
        {
            ed.WriteMessage("\nThe surface has no extents (is it empty?).");
            return null;
        }

        double suggested = NiceNumber(Math.Max(width, depth) / 192);
        var pdo = new PromptDoubleOptions($"\nBlock size in drawing units <{suggested}>: ")
        {
            AllowNegative = false, AllowZero = false, AllowNone = true, DefaultValue = suggested, UseDefaultValue = true,
        };
        var cellRes = ed.GetDouble(pdo);
        if (cellRes.Status != PromptStatus.OK) return null;
        double cell = cellRes.Value;

        int nx = (int)Math.Ceiling(width / cell), nz = (int)Math.Ceiling(depth / cell);
        if (nx > MaxColumns || nz > MaxColumns)
        {
            cell = NiceNumber(Math.Max(width, depth) / MaxColumns * 1.01);
            nx = (int)Math.Ceiling(width / cell);
            nz = (int)Math.Ceiling(depth / cell);
            ed.WriteMessage($"\nThat would be too many blocks; using a block size of {cell} instead.");
        }

        var pxo = new PromptDoubleOptions("\nVertical exaggeration <1>: ")
        {
            AllowNegative = false, AllowZero = false, AllowNone = true, DefaultValue = 1, UseDefaultValue = true,
        };
        var exRes = ed.GetDouble(pxo);
        if (exRes.Status != PromptStatus.OK) return null;
        double cellHeight = cell / exRes.Value;

        var pko = new PromptKeywordOptions("\nPave alignments as roads? [Yes/No] <Yes>: ");
        pko.Keywords.Add("Yes");
        pko.Keywords.Add("No");
        pko.Keywords.Default = "Yes";
        pko.AllowNone = true;
        var roadRes = ed.GetKeywords(pko);
        if (roadRes.Status is not (PromptStatus.OK or PromptStatus.None)) return null;
        bool roads = roadRes.Status == PromptStatus.None || roadRes.StringResult == "Yes";

        // Sample the surface at the centre of every block column.
        double[,] elev = new double[nx, nz];
        double min = double.MaxValue, max = double.MinValue;
        var meter = new ProgressMeter();
        meter.SetLimit(nx);
        meter.Start($"BlockCraft: sampling {nx * nz:N0} points on {surface.Name}");
        try
        {
            for (int i = 0; i < nx; i++)
            {
                for (int k = 0; k < nz; k++)
                {
                    double x = ext.MinPoint.X + (i + 0.5) * cell;
                    double y = ext.MinPoint.Y + (k + 0.5) * cell;
                    double z = SampleElevation(surface, x, y);
                    elev[i, k] = z;
                    if (double.IsNaN(z)) continue;
                    min = Math.Min(min, z);
                    max = Math.Max(max, z);
                }
                meter.MeterProgress();
            }
        }
        finally
        {
            meter.Stop();
        }

        if (min > max)
        {
            ed.WriteMessage("\nNo points could be sampled on the surface.");
            return null;
        }

        int reliefLayers = (int)Math.Ceiling((max - min) / cellHeight);
        if (reliefLayers + FloorLayers + SkyLayers > MaxLayers)
        {
            cellHeight = (max - min) / (MaxLayers - FloorLayers - SkyLayers);
            reliefLayers = (int)Math.Ceiling((max - min) / cellHeight);
            ed.WriteMessage($"\nRelief is too tall for the world; vertical scale reduced to {cell / cellHeight:0.##}x.");
        }

        double baseElevation = min - FloorLayers * cellHeight;
        var mapping = new WorldMapping(ext.MinPoint.X, ext.MinPoint.Y, baseElevation, cell, cellHeight);

        var layers = new double[nx, nz];
        for (int i = 0; i < nx; i++)
        for (int k = 0; k < nz; k++)
            layers[i, k] = double.IsNaN(elev[i, k]) ? double.NaN : mapping.ElevationToLayer(elev[i, k]);

        HashSet<(int, int)>? roadCells = null;
        if (roads)
        {
            roadCells = CollectRoadCells(tr, ed, mapping, nx, nz);
            ed.WriteMessage($"\nPaved {roadCells.Count:N0} road blocks from alignments.");
        }

        int sizeY = Math.Clamp(reliefLayers + FloorLayers + SkyLayers, 48, MaxLayers);
        var world = TerrainGenerator.FromHeightField(layers, sizeY, mapping, roadCells);
        world.Source = $"{surface.Name} ({nx}x{nz} blocks, {cell:0.###} units/block)";
        tr.Commit();

        ed.WriteMessage($"\nBuilt a {nx} x {nz} x {sizeY} world from surface \"{surface.Name}\".");
        return world;
    }

    private static double SampleElevation(CivilSurface surface, double x, double y)
    {
        try
        {
            return surface.FindElevationAtXY(x, y);
        }
        catch (System.Exception)
        {
            return double.NaN; // outside the surface boundary or in a hole
        }
    }

    private static HashSet<(int, int)> CollectRoadCells(Transaction tr, Editor ed, WorldMapping mapping, int nx, int nz)
    {
        var cells = new HashSet<(int, int)>();
        var pdo = new PromptDoubleOptions($"\nRoad half-width <{mapping.CellSize * 2:0.###}>: ")
        {
            AllowNegative = false, AllowZero = false, AllowNone = true, DefaultValue = mapping.CellSize * 2, UseDefaultValue = true,
        };
        var res = ed.GetDouble(pdo);
        double halfWidth = res.Status == PromptStatus.OK ? res.Value : mapping.CellSize * 2;

        double step = mapping.CellSize / 2;
        foreach (ObjectId id in CivilApplication.ActiveDocument.GetAlignmentIds())
        {
            try
            {
                var alignment = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                double start = alignment.StartingStation, end = alignment.EndingStation;
                for (double sta = start; sta <= end; sta += step)
                {
                    for (double off = -halfWidth; off <= halfWidth; off += step)
                    {
                        double east = 0, north = 0;
                        alignment.PointLocation(sta, off, ref east, ref north);
                        int i = (int)Math.Floor((east - mapping.OriginX) / mapping.CellSize);
                        int k = (int)Math.Floor((north - mapping.OriginY) / mapping.CellSize);
                        if ((uint)i < (uint)nx && (uint)k < (uint)nz)
                            cells.Add((i, k));
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSkipped an alignment: {ex.Message}");
            }
        }
        return cells;
    }

    /// <summary>Rounds up to 1, 2 or 5 times a power of ten, so prompts show tidy values.</summary>
    internal static double NiceNumber(double value)
    {
        if (value <= 0) return 1;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double f = value / pow;
        double nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
        return nice * pow;
    }
}
