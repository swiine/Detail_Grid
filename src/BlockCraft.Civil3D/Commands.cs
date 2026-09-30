using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using BlockCraft.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(BlockCraft.Civil3D.PluginEntry))]
[assembly: CommandClass(typeof(BlockCraft.Civil3D.Commands))]

namespace BlockCraft.Civil3D;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nBlockCraft loaded. Type BLOCKCRAFT for the list of commands.\n");
    }

    public void Terminate()
    {
    }
}

/// <summary>
/// BlockCraft commands. The world is built in model space as block references, so it can be edited
/// with BlockCraft's own commands or with ordinary drafting commands followed by BCSYNC.
/// </summary>
public sealed class Commands
{
    private const int ConfirmAbove = 120_000;
    private const int ConfirmMissingAbove = 2_000;

    private static BlockType _placeType = BlockType.Stone;
    private static string _placeDirection = "Auto";

    private static Document Doc => AcApp.DocumentManager.MdiActiveDocument;

    [CommandMethod("BLOCKCRAFT")]
    public void Help()
    {
        Doc.Editor.WriteMessage(
            "\nBlockCraft commands" +
            "\n  BCNEW        Generate a procedural block world in model space" +
            "\n  BCSURFACE    Generate a block world from a Civil 3D surface (alignments become roads)" +
            "\n  BCBREAK      Dig out selected blocks (reveals the blocks underneath)" +
            "\n  BCPLACE      Build new blocks onto picked blocks [Type/Direction]" +
            "\n  BCSYNC       Read ERASE/COPY/MOVE/ARRAY edits of BC_* blocks back into the world" +
            "\n  BCPLAY       Swap into play mode right in the viewport (Esc swaps back to drafting)" +
            "\n  BCTOSURFACE  Create a Civil 3D TIN surface from the top of the block terrain" +
            "\n  BCCLEAR      Remove the block world from the drawing\n");
    }

    // ------------------------------------------------------------------ generating

    [CommandMethod("BCNEW")]
    public void NewProcedural()
    {
        var ed = Doc.Editor;
        if (AskReplaceExisting() is not { } replace) return;

        var seedRes = ed.GetInteger(new PromptIntegerOptions("\nWorld seed <random>: ") { AllowNone = true, AllowNegative = true, AllowZero = true });
        if (seedRes.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        int seed = seedRes.Status == PromptStatus.OK ? seedRes.Value : Environment.TickCount;

        var sizeRes = ed.GetInteger(new PromptIntegerOptions("\nWorld size in blocks (16-256) <96>: ")
        {
            AllowNone = true, DefaultValue = 96, UseDefaultValue = true, LowerLimit = 16, UpperLimit = 256,
        });
        if (sizeRes.Status != PromptStatus.OK) return;

        var cellRes = ed.GetDouble(new PromptDoubleOptions("\nBlock size in drawing units <1>: ")
        {
            AllowNone = true, AllowNegative = false, AllowZero = false, DefaultValue = 1, UseDefaultValue = true,
        });
        if (cellRes.Status != PromptStatus.OK) return;

        var ptRes = ed.GetPoint(new PromptPointOptions("\nSouth-west corner of the world <0,0,0>: ") { AllowNone = true });
        if (ptRes.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        Point3d corner = ptRes.Status == PromptStatus.OK
            ? ptRes.Value.TransformBy(ed.CurrentUserCoordinateSystem)
            : Point3d.Origin;

        int size = sizeRes.Value;
        double cell = cellRes.Value;
        var mapping = new WorldMapping(corner.X, corner.Y, corner.Z, cell, cell);
        var world = TerrainGenerator.Procedural(size, 64, size, seed, mapping);
        Build(world, replace);
    }

    [CommandMethod("BCSURFACE")]
    public void NewFromSurface()
    {
        var ed = Doc.Editor;
        if (AskReplaceExisting() is not { } replace) return;

        World? world;
        try
        {
            world = SurfaceWorldBuilder.Prompt(Doc);
        }
        catch (FileNotFoundException)
        {
            // AeccDbMgd is missing: this is plain AutoCAD rather than Civil 3D.
            ed.WriteMessage("\nBCSURFACE needs Civil 3D. Use BCNEW for a procedural world.");
            return;
        }

        if (world != null)
            Build(world, replace);
    }

    /// <summary>null = cancelled, true = replace the existing world, false = there is none.</summary>
    private static bool? AskReplaceExisting()
    {
        bool exists;
        using (var tr = Doc.Database.TransactionManager.StartOpenCloseTransaction())
        {
            var nod = (DBDictionary)tr.GetObject(Doc.Database.NamedObjectsDictionaryId, OpenMode.ForRead);
            exists = nod.Contains(DrawingWorld.DictionaryKey);
        }
        if (!exists) return false;

        return AskYesNo("\nThis drawing already has a BlockCraft world. Replace it? [Yes/No] <No>: ", false) ? true : null;
    }

    private static void Build(World world, bool replace, bool zoomToWorld = true)
    {
        var doc = Doc;
        var ed = doc.Editor;
        var voxels = world.DrawnVoxels().ToList();
        if (voxels.Count > ConfirmAbove &&
            !AskYesNo($"\nThis will draw {voxels.Count:N0} blocks, which can make the drawing slow. Continue? [Yes/No] <No>: ", false))
            return;

        const int ticksPer = 500;
        var meter = new ProgressMeter();
        meter.SetLimit(voxels.Count / ticksPer + 1);
        meter.Start($"BlockCraft: drawing {voxels.Count:N0} blocks");
        int drawn = 0;
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            int removed = replace ? DrawingWorld.Clear(doc.Database, tr) : 0;
            var dw = DrawingWorld.Create(doc.Database, world);
            dw.DrawAll(tr, voxels, () => { if (++drawn % ticksPer == 0) meter.MeterProgress(); });
            dw.Save(tr);
            tr.Commit();
            if (removed > 0) ed.WriteMessage($"\nRemoved the previous world ({removed:N0} blocks).");
        }
        finally
        {
            meter.Stop();
        }

        ed.WriteMessage($"\nDrew {voxels.Count:N0} blocks for a {world.SizeX} x {world.SizeY} x {world.SizeZ} world: {world.Source}." +
                        "\nEdit with BCBREAK / BCPLACE, or ERASE/COPY/MOVE the BC_* blocks and run BCSYNC. BCWALK to walk around.");
        if (zoomToWorld)
            doc.SendStringToExecute("._-VIEW _SWISO ._ZOOM _E ", true, false, false);
    }

    // ------------------------------------------------------------------ editing

    [CommandMethod("BCBREAK")]
    public void Break()
    {
        var doc = Doc;
        var ed = doc.Editor;

        var filter = new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Start, "INSERT"),
            new TypedValue((int)DxfCode.BlockName, DrawingWorld.BlockPrefix + "*"),
        });
        var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect blocks to dig out: " }, filter);
        if (sel.Status != PromptStatus.OK) return;

        using var tr = doc.Database.TransactionManager.StartTransaction();
        if (LoadWorld(tr) is not { } dw) return;

        var changed = new List<(int, int, int)>();
        int bedrock = 0, outside = 0;
        foreach (ObjectId id in sel.Value.GetObjectIds())
        {
            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var (x, y, z) = dw.VoxelOf(br);
            if (!dw.World.InBounds(x, y, z)) { outside++; continue; }
            if (!Blocks.Info(dw.World.Get(x, y, z)).Breakable) { bedrock++; continue; }
            dw.World.Set(x, y, z, BlockType.Air);
            changed.Add((x, y, z));
        }

        dw.Reconcile(tr, changed);
        dw.Save(tr);
        tr.Commit();

        ed.WriteMessage($"\nDug out {changed.Count} block(s); {dw.Inserted} newly exposed block(s) drawn.");
        if (bedrock > 0) ed.WriteMessage($" {bedrock} bedrock block(s) can't be broken.");
        if (outside > 0) ed.WriteMessage($" {outside} block(s) are outside the world and were left alone.");
    }

    [CommandMethod("BCPLACE")]
    public void Place()
    {
        var doc = Doc;
        var ed = doc.Editor;

        DrawingWorld? dw;
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            dw = LoadWorld(tr);
            tr.Commit();
        }
        if (dw == null) return;

        int placed = 0;
        while (true)
        {
            var peo = new PromptEntityOptions("");
            peo.SetMessageAndKeywords(
                $"\nSelect a block to build on ({Blocks.Info(_placeType).Name}, {_placeDirection}) or [Type/Direction] <done>: ",
                "Type Direction");
            peo.AllowNone = true;
            peo.SetRejectMessage("\nSelect a BlockCraft block.");
            peo.AddAllowedClass(typeof(BlockReference), true);

            var res = ed.GetEntity(peo);
            if (res.Status == PromptStatus.Keyword)
            {
                if (res.StringResult == "Type") AskBlockType();
                else AskDirection();
                continue;
            }
            if (res.Status != PromptStatus.OK) break;

            using var tr = doc.Database.TransactionManager.StartTransaction();
            var br = (BlockReference)tr.GetObject(res.ObjectId, OpenMode.ForRead);
            if (DrawingWorld.TypeOfBlockName(br.Name) == null)
            {
                ed.WriteMessage("\nThat is not a BlockCraft block.");
                continue;
            }

            var v = dw.VoxelOf(br);
            if (ChooseFace(dw.World, v, ed) is not { } target)
            {
                ed.WriteMessage("\nNo free side to build on in that direction.");
                continue;
            }

            dw.World.Set(target.X, target.Y, target.Z, _placeType);
            dw.Reconcile(tr, new[] { target });
            tr.Commit();
            placed++;
            ed.UpdateScreen();
        }

        if (placed > 0)
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            dw.Save(tr);
            tr.Commit();
        }
        ed.WriteMessage($"\nPlaced {placed} block(s).");
    }

    private static readonly (string Name, int DX, int DY, int DZ, Vector3d Normal)[] Faces =
    {
        ("Top", 0, 1, 0, Vector3d.ZAxis),
        ("Bottom", 0, -1, 0, -Vector3d.ZAxis),
        ("North", 0, 0, 1, Vector3d.YAxis),
        ("South", 0, 0, -1, -Vector3d.YAxis),
        ("East", 1, 0, 0, Vector3d.XAxis),
        ("West", -1, 0, 0, -Vector3d.XAxis),
    };

    /// <summary>
    /// The empty voxel next to <paramref name="v"/> to build in. "Auto" picks the open face that
    /// looks most towards the viewer (ties go to the top face).
    /// </summary>
    private static (int X, int Y, int Z)? ChooseFace(World world, (int X, int Y, int Z) v, Editor ed)
    {
        Vector3d toViewer;
        using (var view = ed.GetCurrentView())
            toViewer = view.ViewDirection.GetNormal();

        (int X, int Y, int Z)? best = null;
        double bestScore = double.MinValue;
        foreach (var f in Faces)
        {
            if (_placeDirection != "Auto" && f.Name != _placeDirection) continue;

            var t = (v.X + f.DX, v.Y + f.DY, v.Z + f.DZ);
            if (!world.InBounds(t.Item1, t.Item2, t.Item3)) continue;
            var existing = world.Get(t.Item1, t.Item2, t.Item3);
            if (existing is not (BlockType.Air or BlockType.Water)) continue;

            double score = f.Normal.DotProduct(toViewer) + (f.Name == "Top" ? 0.01 : 0);
            if (score > bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    private static void AskBlockType()
    {
        var names = Blocks.Placeable.Select(b => Blocks.Info(b).Name).ToList();
        var res = Doc.Editor.GetString(new PromptStringOptions(
            $"\nBlock type [{string.Join("/", names)}] <{Blocks.Info(_placeType).Name}>: ") { AllowSpaces = false });
        if (res.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(res.StringResult)) return;

        string input = res.StringResult.Trim();
        var matches = Blocks.Placeable.Where(b => Blocks.Info(b).Name.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToList();
        var exact = matches.Where(b => Blocks.Info(b).Name.Equals(input, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) matches = exact;

        if (matches.Count == 1) _placeType = matches[0];
        else Doc.Editor.WriteMessage(matches.Count == 0 ? "\nUnknown block type." : "\nAmbiguous; type more letters.");
    }

    private static void AskDirection()
    {
        var pko = new PromptKeywordOptions("");
        pko.SetMessageAndKeywords($"\nBuild direction [Auto/Top/Bottom/North/South/East/West] <{_placeDirection}>: ",
            "Auto Top Bottom North South East West");
        pko.AllowNone = true;
        var res = Doc.Editor.GetKeywords(pko);
        if (res.Status == PromptStatus.OK) _placeDirection = res.StringResult;
    }

    [CommandMethod("BCSYNC")]
    public void Sync()
    {
        var doc = Doc;
        var ed = doc.Editor;
        using var tr = doc.Database.TransactionManager.StartTransaction();
        if (LoadWorld(tr) is not { } dw) return;
        var world = dw.World;
        var index = dw.Index(tr);

        // Visible blocks with no block reference were erased (or moved away) by the user.
        var missing = world.DrawnVoxels().Where(v => !index.ContainsKey(v)).ToList();
        if (missing.Count > ConfirmMissingAbove &&
            !AskYesNo($"\n{missing.Count:N0} visible blocks are missing from the drawing and will be dug out. Continue? [Yes/No] <No>: ", false))
            return;

        var changed = new List<(int, int, int)>();
        foreach (var (x, y, z) in missing)
        {
            if (!Blocks.Info(world.Get(x, y, z)).Breakable) continue;
            world.Set(x, y, z, BlockType.Air);
            changed.Add((x, y, z));
        }
        int dug = changed.Count;

        // Block references where the world has something else were copied, moved or arrayed there.
        int outside = 0;
        foreach (var (v, refs) in index)
        {
            if (!world.InBounds(v.X, v.Y, v.Z)) { outside += refs.Count; continue; }
            BlockType type = refs[^1].Type;
            if (world.Get(v.X, v.Y, v.Z) == type) continue;
            world.Set(v.X, v.Y, v.Z, type);
            changed.Add(v);
        }
        int added = changed.Count - dug;

        dw.Reconcile(tr, changed);
        dw.Save(tr);
        tr.Commit();

        ed.WriteMessage($"\nSynced: {dug} block(s) dug out, {added} block(s) added, {dw.Inserted} newly exposed block(s) drawn, {dw.Erased} duplicate/replaced reference(s) removed.");
        if (outside > 0)
            ed.WriteMessage($"\n{outside} BC_* block(s) are outside the world's extents and were ignored.");
    }

    // ------------------------------------------------------------------ exploring

    [CommandMethod("BCPLAY")]
    public void Play()
    {
        var doc = Doc;
        var ed = doc.Editor;

        if (ViewportPlay.Active != null)
        {
            ViewportPlay.Active.Stop();
            return;
        }

        DrawingWorld? dw;
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            dw = DrawingWorld.Load(doc.Database, tr);
            tr.Commit();
        }

        if (dw == null)
        {
            if (!AskYesNo("\nThis drawing has no BlockCraft world. Generate a random one here? [Yes/No] <Yes>: ", true))
                return;
            int seed = Environment.TickCount;
            var world = TerrainGenerator.Procedural(96, 64, 96, seed, new WorldMapping(0, 0, 0, 1, 1));
            Build(world, replace: false, zoomToWorld: false);

            using var tr = doc.Database.TransactionManager.StartTransaction();
            dw = DrawingWorld.Load(doc.Database, tr);
            tr.Commit();
            if (dw == null) return;
        }

        ViewportPlay.Start(doc, dw);
    }

    // ------------------------------------------------------------------ output / cleanup

    [CommandMethod("BCTOSURFACE")]
    public void ToSurface()
    {
        var doc = Doc;
        var ed = doc.Editor;

        var nameRes = ed.GetString(new PromptStringOptions("\nSurface name <BlockCraft Terrain>: ") { AllowSpaces = true });
        if (nameRes.Status != PromptStatus.OK) return;
        string name = string.IsNullOrWhiteSpace(nameRes.StringResult) ? "BlockCraft Terrain" : nameRes.StringResult.Trim();

        using var tr = doc.Database.TransactionManager.StartTransaction();
        if (LoadWorld(tr) is not { } dw) return;
        try
        {
            var (_, points) = CivilSurfaceWriter.Create(doc.Database, tr, dw.World, name);
            tr.Commit();
            ed.WriteMessage($"\nCreated TIN surface \"{name}\" from {points:N0} points (top of each block column; trees and water ignored).");
        }
        catch (FileNotFoundException)
        {
            ed.WriteMessage("\nBCTOSURFACE needs Civil 3D.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nCould not create the surface: {ex.Message}");
        }
    }

    [CommandMethod("BCCLEAR")]
    public void Clear()
    {
        if (!AskYesNo("\nRemove the BlockCraft world and all BC_* blocks from this drawing? [Yes/No] <No>: ", false))
            return;

        using var tr = Doc.Database.TransactionManager.StartTransaction();
        int erased = DrawingWorld.Clear(Doc.Database, tr);
        tr.Commit();
        Doc.Editor.WriteMessage($"\nRemoved {erased:N0} block(s). The BC_* block definitions can be removed with PURGE.");
    }

    // ------------------------------------------------------------------ helpers

    private static DrawingWorld? LoadWorld(Transaction tr)
    {
        var dw = DrawingWorld.Load(Doc.Database, tr);
        if (dw == null)
            Doc.Editor.WriteMessage("\nThis drawing has no BlockCraft world. Create one with BCNEW or BCSURFACE.");
        return dw;
    }

    private static bool AskYesNo(string message, bool defaultYes)
    {
        var pko = new PromptKeywordOptions("");
        pko.SetMessageAndKeywords(message, "Yes No");
        pko.Keywords.Default = defaultYes ? "Yes" : "No";
        pko.AllowNone = true;
        var res = Doc.Editor.GetKeywords(pko);
        return res.Status switch
        {
            PromptStatus.OK => res.StringResult == "Yes",
            PromptStatus.None => defaultYes,
            _ => false,
        };
    }
}
