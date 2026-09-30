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

        var kwOpts = new PromptKeywordOptions("\nDifficulty [Easy/Normal/Hard] <Normal>: ") { AllowNone = true };
        kwOpts.Keywords.Add("Easy");
        kwOpts.Keywords.Add("Normal");
        kwOpts.Keywords.Add("Hard");
        kwOpts.Keywords.Default = "Normal";
        var kwRes = ed.GetKeywords(kwOpts);
        if (kwRes.Status == PromptStatus.Cancel) return;
        var difficulty = kwRes.StringResult switch
        {
            "Easy" => Difficulty.Easy,
            "Hard" => Difficulty.Hard,
            _ => Difficulty.Normal,
        };

        var pitchIds = Pitch.Draw(doc.Database, centre);
        ZoomToPitch(ed, centre);

        _game = new GameWindow(doc, centre, difficulty, pitchIds);
        AcApp.ShowModelessDialog(_game);
        _game.Activate();
        ed.WriteMessage("\nKick off! Keep the CAD FIFA window focused to play. Esc quits and removes the pitch.");
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
