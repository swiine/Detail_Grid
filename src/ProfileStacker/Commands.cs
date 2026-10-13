using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using ProfileStacker.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ProfileStacker.Commands))]
[assembly: ExtensionApplication(typeof(ProfileStacker.PluginEntry))]

namespace ProfileStacker;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\nTTW Profile View Stacker loaded. Type TTWPROFSTACK and pick a long profile view to split it onto sheets.\n");
    }

    public void Terminate() { }
}

public sealed class Commands
{
    /// <summary>
    /// Splits a long profile view into stacked views that each fill a profile viewport of the sheet
    /// template, and makes the sheet layouts with every viewport set to its view.
    /// </summary>
    [CommandMethod("TTWPROFSTACK", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ProfileStack()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        var viewId = PickProfileView(ed);
        if (viewId.IsNull)
            return;

        try
        {
            SourceView source;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                source = SourceView.Read(tr, viewId);
                tr.Commit();
            }
            if (source.IsSplit)
                ed.WriteMessage("\nThis profile view is split; the new views use the full height instead of splitting.");

            var settings = StackerSettings.Load(StackerSettings.DefaultPath);
            settings.StartStation = source.StationStart;
            settings.EndStation = source.StationEnd;
            settings.MinStation = source.AlignmentStart;
            settings.MaxStation = source.AlignmentEnd;
            settings.StationPlusDigits = IsImperial(db) ? 2 : 3;

            StackPlan plan;
            SheetTemplate? template;
            using (var form = new StackerForm(source, settings))
            {
                if (AcApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK || form.Plan is null)
                    return;
                plan = form.Plan;
                template = form.Template;
            }

            using (template)
            {
                TrySave(settings, ed);
                var result = StackBuilder.Build(db, source, plan, settings, settings.CreateLayouts ? template : null);
                ed.WriteMessage($"\nProfile View Stacker: {result.Views} profile view{(result.Views == 1 ? "" : "s")}"
                                + (result.Layouts > 0 ? $" and {result.Layouts} sheet layout{(result.Layouts == 1 ? "" : "s")}" : "")
                                + " created.");
                foreach (var note in plan.Warnings.Concat(result.Notes))
                    ed.WriteMessage($"\n  {note}");
            }
            ed.Regen();
        }
        catch (PlanException ex)
        {
            ed.WriteMessage($"\nProfile View Stacker: {ex.Message}");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            ed.WriteMessage($"\nProfile View Stacker failed: {ex.ErrorStatus} {ex.Message}");
        }
    }

    private static ObjectId PickProfileView(Editor ed)
    {
        var implied = ed.SelectImplied();
        if (implied.Status == PromptStatus.OK)
        {
            var picked = implied.Value.GetObjectIds()
                .FirstOrDefault(id => id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(ProfileView))));
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            if (!picked.IsNull)
                return picked;
        }

        var options = new PromptEntityOptions("\nSelect the long profile view to stack onto sheets: ");
        options.SetRejectMessage("\nThat isn't a profile view.");
        options.AddAllowedClass(typeof(ProfileView), false);
        var result = ed.GetEntity(options);
        return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
    }

    private static bool IsImperial(Database db) =>
        db.Insunits is UnitsValue.Feet or UnitsValue.Inches or UnitsValue.Miles or UnitsValue.Yards or UnitsValue.USSurveyFeet;

    private static void TrySave(StackerSettings settings, Editor ed)
    {
        try { settings.Save(StackerSettings.DefaultPath); }
        catch (IOException ex) { ed.WriteMessage($"\nCouldn't save the settings: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { ed.WriteMessage($"\nCouldn't save the settings: {ex.Message}"); }
    }
}
