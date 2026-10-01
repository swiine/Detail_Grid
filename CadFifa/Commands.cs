using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CadFifa.Commands))]

namespace CadFifa;

public class Commands
{
    static GameWindow? _game;

    internal static void GameClosed() => _game = null;

    /// <summary>FIFA: draw a pitch in the current drawing and kick off a match.</summary>
    [CommandMethod("FIFA")]
    public void Fifa()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;

        if (_game != null)
        {
            ed.WriteMessage("\nA match is already running. Press Esc in the FIFA window to quit it.");
            _game.Activate();
            return;
        }

        var ptOpts = new PromptPointOptions("\nPitch centre point <0,0>: ") { AllowNone = true };
        var ptRes = ed.GetPoint(ptOpts);
        if (ptRes.Status == PromptStatus.Cancel) return;
        var centre = ptRes.Status == PromptStatus.OK ? ptRes.Value.TransformBy(ed.CurrentUserCoordinateSystem) : Point3d.Origin;

        var mode = AskKeyword(ed, "\nMode [Solo/Versus/Coop] <Solo>: ", "Solo", "Solo", "Versus", "Coop") switch
        {
            null => (GameMode?)null,
            "Versus" => GameMode.Versus,
            "Coop" => GameMode.Coop,
            _ => GameMode.Solo,
        };
        if (mode == null) return;

        // Head-to-head has no CPU side, so difficulty only matters against the computer.
        var difficulty = Difficulty.Normal;
        if (mode != GameMode.Versus)
        {
            var answer = AskKeyword(ed, "\nDifficulty [Easy/Normal/Hard] <Normal>: ", "Normal", "Easy", "Normal", "Hard");
            if (answer == null) return;
            difficulty = answer == "Easy" ? Difficulty.Easy : answer == "Hard" ? Difficulty.Hard : Difficulty.Normal;
        }

        var pitchIds = Pitch.Draw(doc.Database, centre);
        ZoomToPitch(ed, centre);

        _game = new GameWindow(doc, centre, mode.Value, difficulty, pitchIds);
        AcApp.ShowModelessDialog(_game);
        _game.Activate();
        ed.WriteMessage("\nKick off! Keep the CAD FIFA window focused to play. Esc quits and removes the pitch.");
    }

    /// <returns>The chosen keyword, or null if the user cancelled.</returns>
    static string? AskKeyword(Editor ed, string message, string defaultKeyword, params string[] keywords)
    {
        var opts = new PromptKeywordOptions(message) { AllowNone = true };
        foreach (var k in keywords) opts.Keywords.Add(k);
        opts.Keywords.Default = defaultKeyword;
        var res = ed.GetKeywords(opts);
        if (res.Status == PromptStatus.Cancel) return null;
        return string.IsNullOrEmpty(res.StringResult) ? defaultKeyword : res.StringResult;
    }

    static void ZoomToPitch(Editor ed, Point3d centre)
    {
        // Plan view in WCS: world XY maps straight to the view's DCS.
        using var view = ed.GetCurrentView();
        view.ViewDirection = Vector3d.ZAxis;
        view.Target = Point3d.Origin;
        view.ViewTwist = 0;
        view.CenterPoint = new Point2d(centre.X, centre.Y + 1.5);
        view.Height = 2 * Match.HalfWidth + 22;
        view.Width = 2 * Match.HalfLength + 16;
        ed.SetCurrentView(view);
    }
}
