using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using BlockCraft.Core;
using BlockCraft.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(BlockCraft.Civil3D.PluginEntry))]
[assembly: CommandClass(typeof(BlockCraft.Civil3D.Commands))]

namespace BlockCraft.Civil3D;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nBlockCraft loaded. Commands: BLOCKCRAFT, BLOCKCRAFTSURFACE, BLOCKCRAFTRESUME, BLOCKCRAFTEXPORT.\n");
    }

    public void Terminate()
    {
    }
}

public sealed class Commands
{
    /// <summary>The most recent game, kept for BLOCKCRAFTRESUME and BLOCKCRAFTEXPORT.</summary>
    internal static Game? LastGame { get; private set; }

    [CommandMethod("BLOCKCRAFT")]
    public void PlayProcedural()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;

        var opts = new PromptIntegerOptions("\nWorld seed <random>: ") { AllowNone = true, AllowNegative = true, AllowZero = true };
        var res = ed.GetInteger(opts);
        if (res.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        int seed = res.Status == PromptStatus.OK ? res.Value : Environment.TickCount;

        var world = TerrainGenerator.Procedural(seed: seed);
        Play(new Game(world), $"BlockCraft - seed {seed}");
    }

    [CommandMethod("BLOCKCRAFTSURFACE")]
    public void PlaySurface()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
        World? world;
        try
        {
            world = SurfaceWorldBuilder.Prompt(AcApp.DocumentManager.MdiActiveDocument);
        }
        catch (System.IO.FileNotFoundException)
        {
            // AeccDbMgd is missing: this is plain AutoCAD rather than Civil 3D.
            ed.WriteMessage("\nBLOCKCRAFTSURFACE needs Civil 3D. Use BLOCKCRAFT for a procedural world.");
            return;
        }

        if (world != null)
            Play(new Game(world), $"BlockCraft - {world.Source}");
    }

    [CommandMethod("BLOCKCRAFTRESUME")]
    public void Resume()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
        if (LastGame == null)
        {
            ed.WriteMessage("\nNo BlockCraft world to resume. Start one with BLOCKCRAFT or BLOCKCRAFTSURFACE.");
            return;
        }
        Play(LastGame, $"BlockCraft - {LastGame.World.Source}");
    }

    [CommandMethod("BLOCKCRAFTEXPORT")]
    public void Export()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (LastGame == null)
        {
            doc.Editor.WriteMessage("\nNothing to export yet. Play with BLOCKCRAFT or BLOCKCRAFTSURFACE first.");
            return;
        }
        BlockExporter.Export(doc, LastGame.World);
    }

    private static void Play(Game game, string title)
    {
        LastGame = game;
        using (var form = new GameForm(game, title))
            AcApp.ShowModalDialog(form);

        var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
        int placed = game.World.PlacedBlocks().Count();
        ed.WriteMessage($"\nBlockCraft closed: {game.BlocksPlaced} placed, {game.BlocksBroken} broken.");
        if (placed > 0)
            ed.WriteMessage($" Run BLOCKCRAFTEXPORT to bring {placed} placed block(s) into the drawing as 3D solids.");
    }
}
