using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CivDoom.Engine;
using CivDoom.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CivDoom.Civil3D.Commands))]
[assembly: ExtensionApplication(typeof(CivDoom.Civil3D.Plugin))]

namespace CivDoom.Civil3D;

public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nCivDOOM loaded. Type CIVDOOM to turn this drawing into a level.\n");
    }

    public void Terminate()
    {
    }
}

public sealed class Commands
{
    /// <summary>
    /// CIVDOOM: turns the current drawing's linework into a Doom-style level and drops you into it.
    /// </summary>
    [CommandMethod("CIVDOOM", CommandFlags.Modal)]
    public void CivDoom()
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var sourceOpts = new PromptKeywordOptions("\nLevel source [Drawing/Selection/Demo] <Drawing>: ", "Drawing Selection Demo")
        {
            AllowNone = true,
        };
        PromptResult source = ed.GetKeywords(sourceOpts);
        if (source.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        string mode = string.IsNullOrEmpty(source.StringResult) ? "Drawing" : source.StringResult;

        if (mode == "Demo")
        {
            Play(BuiltInLevels.DetailGrid);
            return;
        }

        ObjectId[] ids;
        if (mode == "Selection")
        {
            PromptSelectionResult sel = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect linework to use as walls (and POINTs/COGO points as enemies): ",
            });
            if (sel.Status != PromptStatus.OK) return;
            ids = sel.Value.GetObjectIds();
        }
        else
        {
            ids = ModelSpaceIds(db);
        }

        var heightOpts = new PromptDoubleOptions("\nWall height in drawing units")
        {
            DefaultValue = DefaultWallHeight(db),
            UseDefaultValue = true,
            AllowNegative = false,
            AllowZero = false,
        };
        PromptDoubleResult height = ed.GetDouble(heightOpts);
        if (height.Status != PromptStatus.OK) return;
        double wallHeight = height.Value;

        Matrix3d ucs = ed.CurrentUserCoordinateSystem;
        PromptPointResult startPick = ed.GetPoint(new PromptPointOptions("\nPick player start <middle of drawing>: ")
        {
            AllowNone = true,
        });
        if (startPick.Status is not (PromptStatus.OK or PromptStatus.None)) return;

        Point3d? start = null;
        double angle = 0;
        if (startPick.Status == PromptStatus.OK)
        {
            start = startPick.Value.TransformBy(ucs);
            PromptPointResult facePick = ed.GetPoint(new PromptPointOptions("\nPick a point to face <East>: ")
            {
                AllowNone = true,
                BasePoint = startPick.Value,
                UseBasePoint = true,
                UseDashedLine = true,
            });
            if (facePick.Status == PromptStatus.Cancel) return;
            if (facePick.Status == PromptStatus.OK)
            {
                Point3d face = facePick.Value.TransformBy(ucs);
                angle = Math.Atan2(face.Y - start.Value.Y, face.X - start.Value.X);
            }
        }

        DrawingGeometry geometry;
        int skipped;
        using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var extractor = new DrawingExtractor(tr, wallHeight, visibleLayersOnly: mode == "Drawing");
            extractor.AddObjects(ids);
            geometry = extractor.Geometry;
            skipped = extractor.SkippedEntities;
            tr.Commit();
        }

        if (geometry.Segments.Count == 0)
        {
            ed.WriteMessage("\nNo linework found to build walls from. Try CIVDOOM > Demo for the built-in level.");
            return;
        }

        if (start is { } s) geometry.PlayerStart = new Vec2(s.X, s.Y);
        geometry.PlayerAngle = angle;

        ed.WriteMessage($"\nBuilt {geometry.Segments.Count:N0} wall segments" +
                        (geometry.EnemyPoints.Count > 0 ? $", {geometry.EnemyPoints.Count} enemy points" : ", enemies auto-placed") +
                        (skipped > 0 ? $" ({skipped:N0} unsupported objects ignored)." : "."));
        if (geometry.Segments.Count >= LevelBuilder.MaxWalls)
            ed.WriteMessage($"\nThat's a lot of linework: capped at {LevelBuilder.MaxWalls:N0} segments. Use Selection to pick an area.");

        string name = Path.GetFileNameWithoutExtension(doc.Name);
        int seed = Environment.TickCount;
        Play(() => LevelBuilder.FromDrawing(geometry, wallHeight, seed++, name));
    }

    private static void Play(Func<Level> levelFactory)
    {
        using var form = new GameForm(levelFactory, "CivDOOM");
        AcApp.ShowModalDialog(form);
    }

    private static ObjectId[] ModelSpaceIds(Database db)
    {
        using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        ObjectId[] ids = ms.Cast<ObjectId>().ToArray();
        tr.Commit();
        return ids;
    }

    /// <summary>Guess a sensible storey height from the drawing units.</summary>
    private static double DefaultWallHeight(Database db)
    {
        switch (db.Insunits)
        {
            case UnitsValue.Inches: return 120;
            case UnitsValue.Feet: return 10;
            case UnitsValue.Millimeters: return 3000;
            case UnitsValue.Centimeters: return 300;
            case UnitsValue.Meters: return 3;
        }

        // Unknown units: aim for the drawing being roughly 60 wall-heights across.
        // (An empty drawing has inverted extents, which yields no diagonal.)
        Point3d min = db.Extmin, max = db.Extmax;
        double diag = max.X > min.X && max.Y > min.Y ? min.DistanceTo(max) : 0;
        return diag > 0 ? Math.Round(diag / 60, 2) : 10;
    }
}
