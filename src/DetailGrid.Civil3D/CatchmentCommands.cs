using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DetailGrid.Hydrology;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using CivSurface = Autodesk.Civil.DatabaseServices.Surface;
using TransientDrawingMode = Autodesk.AutoCAD.GraphicsInterface.TransientDrawingMode;
using TransientManager = Autodesk.AutoCAD.GraphicsInterface.TransientManager;

[assembly: CommandClass(typeof(DetailGrid.Civil3D.CatchmentCommands))]

namespace DetailGrid.Civil3D
{
    /// <summary>
    /// CATCHMENTVIS — pick a surface, then click pour points; the catchment draining to each
    /// click is highlighted immediately as transient graphics and can be kept as a polyline.
    /// </summary>
    public class CatchmentCommands
    {
        private const string LayerName = "C-HYDR-CTCH";
        private const short BoundaryColor = 1;   // red
        private const short FillColor = 4;       // cyan
        private const short FlowPathColor = 5;   // blue
        private const int SnapRadiusCells = 5;
        private const long MaxCells = 16_000_000;

        private static bool _snap = true;

        [CommandMethod("DetailGrid", "CATCHMENTVIS", CommandFlags.Modal)]
        public void CatchmentVisualiser()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var peo = new PromptEntityOptions("\nSelect surface: ");
            peo.SetRejectMessage("\nObject must be a Civil 3D surface.");
            peo.AddAllowedClass(typeof(CivSurface), false);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            FlowModel model;
            string surfaceName;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var surface = (CivSurface)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                surfaceName = surface.Name;
                var ext = SurfaceSampler.Extents(surface);

                var pdo = new PromptDoubleOptions("\nAnalysis cell size")
                {
                    DefaultValue = SurfaceSampler.SuggestCellSize(ext),
                    UseDefaultValue = true,
                    AllowNegative = false,
                    AllowZero = false,
                };
                var pdr = ed.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK) return;

                double w = ext.MaxPoint.X - ext.MinPoint.X, h = ext.MaxPoint.Y - ext.MinPoint.Y;
                long cells = (long)Math.Ceiling(w / pdr.Value) * (long)Math.Ceiling(h / pdr.Value);
                if (cells > MaxCells)
                {
                    ed.WriteMessage($"\nCell size gives {cells:N0} cells (limit {MaxCells:N0}). Use a larger cell size.");
                    return;
                }

                var sw = Stopwatch.StartNew();
                ed.WriteMessage($"\nSampling \"{surfaceName}\" onto {cells:N0} cells...");
                var grid = SurfaceSampler.CreateGrid(ext, pdr.Value);
                SurfaceSampler.Sample(surface, grid);
                model = FlowModel.Build(grid);
                tr.Commit();
                ed.WriteMessage($" ready in {sw.Elapsed.TotalSeconds:F1} s.");
            }

            var units = AreaUnits(db.Insunits);
            using var display = new TransientSet();
            Catchment? current = null;

            while (true)
            {
                var ppo = new PromptPointOptions(
                    $"\nPick pour point [Snap ({(_snap ? "On" : "Off")})/Keep/Exit] <Exit>: ", "Snap Keep Exit")
                {
                    AllowNone = true,
                };
                var ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.Keyword)
                {
                    switch (ppr.StringResult)
                    {
                        case "Snap":
                            _snap = !_snap;
                            ed.WriteMessage($"\nSnap to channel is {(_snap ? "on" : "off")}.");
                            continue;
                        case "Keep":
                            if (current == null) ed.WriteMessage("\nNo catchment to keep yet.");
                            else KeepBoundary(db, current, ed);
                            continue;
                        default:
                            return;
                    }
                }
                if (ppr.Status != PromptStatus.OK) return;

                var pick = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                if (!model.Grid.TryWorldToCell(pick.X, pick.Y, out int row, out int col))
                {
                    ed.WriteMessage("\nPoint is outside the surface extents.");
                    continue;
                }

                int outlet = model.Grid.Index(row, col);
                if (_snap) outlet = model.SnapPourPoint(outlet, SnapRadiusCells);

                var sw = Stopwatch.StartNew();
                var catchment = model.Delineate(outlet);
                if (catchment.CellCount == 0)
                {
                    ed.WriteMessage("\nNo surface data at that point.");
                    continue;
                }

                current = catchment;
                display.Show(BuildGraphics(model, catchment));
                ed.UpdateScreen();

                var (zMin, zMax) = catchment.ElevationRange();
                var (ox, oy) = model.Grid.CellCenter(outlet);
                ed.WriteMessage(
                    $"\nOutlet ({ox:F2}, {oy:F2})  Area {catchment.Area:N1} sq units{units(catchment.Area)}" +
                    $"  Cells {catchment.CellCount:N0}  Relief {zMax - zMin:F2}  [{sw.ElapsedMilliseconds} ms]");
            }
        }

        private static List<Entity> BuildGraphics(FlowModel model, Catchment catchment)
        {
            var ents = new List<Entity>();
            var rings = catchment.BoundaryWorld();

            try
            {
                var hatch = new Hatch();
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                foreach (var ring in rings)
                {
                    var pts = new Point2dCollection();
                    var bulges = new DoubleCollection();
                    foreach (var (x, y) in ring) { pts.Add(new Point2d(x, y)); bulges.Add(0); }
                    hatch.AppendLoop(HatchLoopTypes.Default, pts, bulges);
                }
                hatch.EvaluateHatch(true);
                hatch.ColorIndex = FillColor;
                hatch.Transparency = new Transparency((byte)90);
                ents.Add(hatch);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Fill is cosmetic; the outline below still shows the catchment.
            }

            foreach (var ring in rings)
            {
                var pl = ToPolyline(ring);
                pl.ColorIndex = BoundaryColor;
                pl.ConstantWidth = model.Grid.CellSize * 0.4;
                ents.Add(pl);
            }

            // Downstream flow path from the outlet to where it leaves the surface.
            var path = model.FlowPath(catchment.Outlet);
            if (path.Count > 1)
            {
                var flow = new AcPolyline();
                for (int k = 0; k < path.Count; k++)
                {
                    var (x, y) = model.Grid.CellCenter(path[k]);
                    flow.AddVertexAt(k, new Point2d(x, y), 0, 0, 0);
                }
                flow.ColorIndex = FlowPathColor;
                ents.Add(flow);
            }

            var (ox, oy) = model.Grid.CellCenter(catchment.Outlet);
            ents.Add(new Circle(new Point3d(ox, oy, 0), Vector3d.ZAxis, model.Grid.CellSize * 2) { ColorIndex = BoundaryColor });
            return ents;
        }

        private static AcPolyline ToPolyline(List<(double X, double Y)> ring)
        {
            var pl = new AcPolyline();
            // Ring is closed (last == first); a closed polyline does not repeat the start vertex.
            for (int k = 0; k < ring.Count - 1; k++)
                pl.AddVertexAt(k, new Point2d(ring[k].X, ring[k].Y), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private static void KeepBoundary(Database db, Catchment catchment, Editor ed)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var layerId = EnsureLayer(db, tr);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            int n = 0;
            foreach (var ring in catchment.BoundaryWorld())
            {
                var pl = ToPolyline(ring);
                pl.SetDatabaseDefaults(db);
                pl.LayerId = layerId;
                space.AppendEntity(pl);
                tr.AddNewlyCreatedObject(pl, true);
                n++;
            }
            tr.Commit();
            ed.WriteMessage($"\nKept {n} boundary polyline{(n == 1 ? "" : "s")} on layer {LayerName}.");
        }

        private static ObjectId EnsureLayer(Database db, Transaction tr)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return lt[LayerName];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = LayerName,
                Color = Color.FromColorIndex(ColorMethod.ByAci, BoundaryColor),
            };
            var id = lt.Add(ltr);
            tr.AddNewlyCreatedObject(ltr, true);
            return id;
        }

        private static Func<double, string> AreaUnits(UnitsValue insunits) => insunits switch
        {
            UnitsValue.Meters => a => $" ({a / 10_000:N3} ha)",
            UnitsValue.Feet => a => $" ({a / 43_560:N3} ac)",
            _ => _ => "",
        };

        /// <summary>Owns the transient graphics for the current catchment and clears them on replace or dispose.</summary>
        private sealed class TransientSet : IDisposable
        {
            private readonly List<Entity> _shown = new();

            public void Show(List<Entity> ents)
            {
                Clear();
                var tm = TransientManager.CurrentTransientManager;
                foreach (var e in ents)
                {
                    tm.AddTransient(e, TransientDrawingMode.DirectTopmost, 128, new IntegerCollection());
                    _shown.Add(e);
                }
            }

            public void Clear()
            {
                var tm = TransientManager.CurrentTransientManager;
                foreach (var e in _shown)
                {
                    tm.EraseTransient(e, new IntegerCollection());
                    e.Dispose();
                }
                _shown.Clear();
            }

            public void Dispose() => Clear();
        }
    }
}
