using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PavementBuildup.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(PavementBuildup.Commands))]
[assembly: ExtensionApplication(typeof(PavementBuildup.PluginEntry))]

namespace PavementBuildup;

public sealed class PluginEntry : IExtensionApplication
{
    public void Initialize()
    {
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\nPavement Build-up loaded. Commands: PAVEBUILDUP (new detail), PAVEEDIT (edit a detail), PAVETEXT (from a note), PAVEQUICK (command line), PAVESTANDARD (CAD standard).\n");
    }

    public void Terminate() { }
}

public sealed class Commands
{
    private static readonly PresetStore Store = PresetStore.Default();

    /// <summary>Opens the build-up dialog, then asks where to place the detail.</summary>
    [CommandMethod("PAVEBUILDUP", CommandFlags.Modal)]
    public void PaveBuildup()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;

        var file = Store.Load();
        using var form = new BuildupForm(file, Store, doc);
        if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK)
            return;

        Place(doc, form.Result, form.ResultSettings, form.ResultStandard);
    }

    /// <summary>
    /// Converts a spec note already in the drawing ("60mm THICK PAVERS ..., 30mm THICK MORTAR, ...")
    /// into a detail: pick the MText/text, check it in the dialog, place it.
    /// </summary>
    [CommandMethod("PAVETEXT", CommandFlags.Modal)]
    public void PaveText()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;

        var note = NoteReader.Pick(ed);
        if (note is null)
            return;

        var errors = new List<string>();
        var parsed = BuildupParser.ParseBuildup(note, errors);
        foreach (var e in errors)
            ed.WriteMessage("\n" + e);
        if (parsed.Layers.Count == 0)
            return;

        var file = Store.Load();
        using var form = new BuildupForm(file, Store, doc);
        form.Prefill(parsed.ToBuildup());
        if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK)
            return;
        Place(doc, form.Result, form.ResultSettings, form.ResultStandard);
    }

    /// <summary>Edits a detail already in the drawing: pick it, change the build-up/settings, and it is redrawn in place.</summary>
    [CommandMethod("PAVEEDIT", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void PaveEdit()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        // Use a pre-selected detail if there is one, otherwise ask.
        ObjectId picked = ObjectId.Null;
        var implied = ed.SelectImplied();
        if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            picked = implied.Value[0].ObjectId;
        if (picked.IsNull)
        {
            var res = ed.GetEntity("\nSelect a pavement detail to edit: ");
            if (res.Status != PromptStatus.OK)
                return;
            picked = res.ObjectId;
        }

        DetailTarget? target;
        try
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            target = DetailStore.Find(tr, picked);
            tr.Commit();
        }
        catch (System.Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            ed.WriteMessage("\nCould not read this detail's data: " + ex.Message);
            return;
        }
        if (target is null)
        {
            ed.WriteMessage("\nThat is not a pavement detail drawn by this plugin. (Details drawn before PAVEEDIT existed " +
                            "don't carry their build-up — redraw them once with PAVEBUILDUP and they become editable.)");
            return;
        }

        var file = Store.Load();
        using var form = new BuildupForm(file, Store, doc, target.Record);
        if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK)
            return;

        var buildup = form.Result;
        var settings = form.ResultSettings;
        settings.CreateBlock = target.IsBlock;
        settings.UnitsOverride = target.Record.Settings.UnitsOverride;
        var standard = form.ResultStandard;
        double unitsPerMm = target.Record.UnitsPerMm; // keep the size it was drawn at

        DetailGeometry geometry;
        try
        {
            geometry = DetailLayout.Build(buildup, settings, standard, unitsPerMm);
        }
        catch (ArgumentException ex)
        {
            ed.WriteMessage("\n" + ex.Message);
            return;
        }

        var record = new DetailRecord { Buildup = buildup.Clone(), Settings = settings.Clone(), UnitsPerMm = unitsPerMm };
        var warnings = new List<string>();
        var imported = StandardResources.Import(db, standard, warnings);
        var breakBlock = BreakSymbol.Ensure(db, standard, settings, warnings);
        DetailDrawer drawer;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            drawer = new DetailDrawer(db, tr, settings, standard) { BreakBlockId = breakBlock };
            drawer.Redraw(target, buildup, geometry, record);
            warnings.AddRange(drawer.Warnings);
            tr.Commit();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            ed.WriteMessage($"\nCould not update the detail ({ex.ErrorStatus}): {ex.Message}");
            return;
        }

        ReportImports(ed, imported);
        foreach (var w in warnings)
            ed.WriteMessage($"\nWarning (standard \"{standard.Name}\"): {w}");
        ed.Regen();
        ed.WriteMessage(target.Copies > 1
            ? $"\nUpdated \"{buildup.Name}\" — all {target.Copies} copies of this detail changed."
            : $"\nUpdated \"{buildup.Name}\".");
    }

    /// <summary>Edits the CAD standard (layers, hatches, text, labels) that details are drawn to.</summary>
    [CommandMethod("PAVESTANDARD", CommandFlags.Modal)]
    public void PaveStandard()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;

        var file = Store.Load();
        var path = StandardStore.PathFor(file.Settings);
        if (LoadStandard(doc.Editor, file.Settings) is not { } standard)
        {
            // Unreadable or missing company file: start from the built-in standard so it can be fixed or re-saved.
            standard = CadStandard.CreateDefault();
        }

        using var form = new StandardForm(standard, path, file.Settings, doc);
        if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK)
            return;

        file.Settings.StandardPath = string.Equals(form.ResultPath, StandardStore.DefaultPath, StringComparison.OrdinalIgnoreCase) ? "" : form.ResultPath;
        Store.Save(file);
        doc.Editor.WriteMessage($"\nCAD standard \"{form.Result.Name}\" saved to {form.ResultPath}.");
    }

    /// <summary>
    /// Command-line version: type a build-up ("40 SMA surface; 60 AC20 binder; 150 Type 1")
    /// or the name of a saved preset. Uses the last settings from the dialog.
    /// </summary>
    [CommandMethod("PAVEQUICK", CommandFlags.Modal)]
    public void PaveQuick()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;
        var ed = doc.Editor;
        var file = Store.Load();

        if (file.Presets.Count > 0)
            ed.WriteMessage("\nSaved presets: " + string.Join(", ", file.Presets.Select(p => p.Name)));

        var input = ed.GetString(new PromptStringOptions(
            "\nEnter build-up top to bottom in mm (e.g. 40 SMA 10 surface; 60 AC 20 binder; 150 Type 1 sub-base) or a preset name: ")
        { AllowSpaces = true });
        if (input.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(input.StringResult))
            return;

        Buildup buildup;
        if (PresetStore.Find(file, input.StringResult) is { } preset)
        {
            buildup = preset.Clone();
        }
        else
        {
            var errors = new List<string>();
            var parsed = BuildupParser.ParseBuildup(input.StringResult, errors);
            foreach (var e in errors)
                ed.WriteMessage("\n" + e);
            if (errors.Count > 0 || parsed.Layers.Count == 0)
                return;

            string defaultName = parsed.Name ?? "PAVEMENT BUILD-UP";
            var name = ed.GetString(new PromptStringOptions($"\nBuild-up name <{defaultName}>: ") { AllowSpaces = true });
            if (name.Status != PromptStatus.OK)
                return;
            buildup = parsed.ToBuildup(string.IsNullOrWhiteSpace(name.StringResult) ? defaultName : name.StringResult);
        }

        if (LoadStandard(ed, file.Settings) is not { } standard)
            return;
        Place(doc, buildup, file.Settings, standard);
    }

    /// <summary>The standard the settings point at, or null (with the reason written to the command line).</summary>
    private static CadStandard? LoadStandard(Editor ed, DetailSettings settings)
    {
        string path = StandardStore.PathFor(settings);
        try
        {
            return StandardStore.Load(path);
        }
        catch (System.Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ed.WriteMessage($"\n{ex.Message}\nFix the file or choose another standard with PAVESTANDARD.");
            return null;
        }
    }

    private static void ReportImports(Editor ed, List<string> imported)
    {
        if (imported.Count > 0)
            ed.WriteMessage("\nFrom the standard drawing: imported " + string.Join(", ", imported) + ".");
    }

    private static void Place(Document doc, Buildup buildup, DetailSettings settings, CadStandard standard)
    {
        var ed = doc.Editor;
        var db = doc.Database;

        double unitsPerMm;
        try
        {
            unitsPerMm = DetailDrawer.ResolveUnitsPerMm(db, settings, standard);
        }
        catch (ArgumentException ex)
        {
            ed.WriteMessage("\n" + ex.Message);
            return;
        }
        DetailGeometry geometry;
        try
        {
            geometry = DetailLayout.Build(buildup, settings, standard, unitsPerMm);
        }
        catch (ArgumentException ex)
        {
            ed.WriteMessage("\n" + ex.Message);
            return;
        }

        var pt = ed.GetPoint("\nPick top-left corner of the pavement detail: ");
        if (pt.Status != PromptStatus.OK)
            return;

        var placement = ed.CurrentUserCoordinateSystem * Matrix3d.Displacement(pt.Value - Point3d.Origin);

        var warnings = new List<string>();
        var imported = StandardResources.Import(db, standard, warnings);
        var breakBlock = BreakSymbol.Ensure(db, standard, settings, warnings);
        DetailDrawer drawer;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            drawer = new DetailDrawer(db, tr, settings, standard) { BreakBlockId = breakBlock };
            var record = new DetailRecord { Buildup = buildup.Clone(), Settings = settings.Clone(), UnitsPerMm = unitsPerMm };
            drawer.Draw(buildup, geometry, placement, record);
            warnings.AddRange(drawer.Warnings);
            tr.Commit();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            // Nothing is committed, so the drawing is left untouched. Report instead of crashing AutoCAD.
            ed.WriteMessage($"\nCould not draw the detail ({ex.ErrorStatus}): {ex.Message}\n{ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("PavementBuildup"))?.Trim()}");
            return;
        }

        ReportImports(ed, imported);
        foreach (var w in warnings)
            ed.WriteMessage($"\nWarning (standard \"{standard.Name}\"): {w}");

        ed.WriteMessage($"\nDrew \"{buildup.Name}\": {buildup.Layers.Count} layer(s), total depth {buildup.TotalThicknessMm:0.#}mm at 1:{settings.ScaleDenominator:0.##}. Edit it later with PAVEEDIT.");
    }
}
