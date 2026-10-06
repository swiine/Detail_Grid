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
        ed?.WriteMessage("\nPavement Build-up loaded. Commands: PAVEBUILDUP (dialog), PAVEQUICK (command line), PAVESTANDARD (CAD standard).\n");
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
            var layers = BuildupParser.Parse(input.StringResult, errors);
            foreach (var e in errors)
                ed.WriteMessage("\n" + e);
            if (errors.Count > 0 || layers.Count == 0)
                return;

            var name = ed.GetString(new PromptStringOptions("\nBuild-up name <PAVEMENT BUILD-UP>: ") { AllowSpaces = true });
            if (name.Status != PromptStatus.OK)
                return;
            buildup = new Buildup
            {
                Name = string.IsNullOrWhiteSpace(name.StringResult) ? "PAVEMENT BUILD-UP" : name.StringResult.Trim(),
                Layers = layers,
            };
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

    private static void Place(Document doc, Buildup buildup, DetailSettings settings, CadStandard standard)
    {
        var ed = doc.Editor;
        var db = doc.Database;

        double unitsPerMm = DetailDrawer.ResolveUnitsPerMm(db, settings);
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

        List<string> warnings;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var drawer = new DetailDrawer(db, tr, settings, standard);
            drawer.Draw(buildup, geometry, placement);
            warnings = drawer.Warnings;
            tr.Commit();
        }

        foreach (var w in warnings)
            ed.WriteMessage($"\nWarning (standard \"{standard.Name}\"): {w}");

        ed.WriteMessage($"\nDrew \"{buildup.Name}\": {buildup.Layers.Count} layer(s), total depth {buildup.TotalThicknessMm:0.#}mm at 1:{settings.ScaleDenominator:0.##}.");
    }
}
