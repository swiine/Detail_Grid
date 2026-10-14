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
        StandardStore.SearchFrom.Add(Path.GetDirectoryName(typeof(Commands).Assembly.Location) ?? "");
        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\nTTW Pavement loaded. Type TTWPVMT for the menu (new detail, edit, from a note, CAD standard), or PAVEQUICK for a quick one at the command line.\n");
    }

    public void Terminate() { }
}

public sealed class Commands
{
    private static readonly PresetStore Store = PresetStore.Default();

    [CommandMethod("TTWPVMT", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void TtwPvmt()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return;

        bool back = true;
        while (back)
        {
            MenuChoice choice;
            using (var menu = new MenuForm())
            {
                if (Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(menu) != System.Windows.Forms.DialogResult.OK)
                    return;
                choice = menu.Choice;
            }

            back = choice switch
            {
                MenuChoice.NewDetail => PaveBuildup(),
                MenuChoice.EditDetail => PaveEdit(),
                MenuChoice.FromNote => PaveText(),
                MenuChoice.Quick => PaveQuick(fromMenu: true),
                MenuChoice.Standard => PaveStandard(),
                _ => false,
            };
        }
    }

    private static bool IsBack(System.Windows.Forms.DialogResult result) => result == System.Windows.Forms.DialogResult.Retry;

    private bool PaveBuildup()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;

        var file = Store.Load();
        using var form = new BuildupForm(file, Store, doc);
        var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        if (IsBack(result))
            return true;
        if (result != System.Windows.Forms.DialogResult.OK)
            return false;

        Place(doc, form.Result, form.ResultSettings, form.ResultStandard);
        return false;
    }

    private bool PaveText()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;
        var ed = doc.Editor;

        var note = NoteReader.Pick(ed, "\nSelect the pavement note (MText or text), Esc = back to menu: ");
        if (note is null)
            return true;

        var file = Store.Load();
        var errors = new List<string>();
        var parsed = Read(ed, Interpret(ed, note, file), errors);
        foreach (var e in errors)
            ed.WriteMessage("\n" + e);
        if (parsed.Layers.Count == 0)
            return false;

        using var form = new BuildupForm(file, Store, doc);
        form.Prefill(parsed.ToBuildup());
        var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        if (IsBack(result))
            return true;
        if (result != System.Windows.Forms.DialogResult.OK)
            return false;
        Place(doc, form.Result, form.ResultSettings, form.ResultStandard);
        return false;
    }

    private bool PaveEdit()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;
        var ed = doc.Editor;
        var db = doc.Database;

        ObjectId picked = ObjectId.Null;
        var implied = ed.SelectImplied();
        if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            picked = implied.Value[0].ObjectId;
        if (picked.IsNull)
        {
            var res = ed.GetEntity("\nSelect a pavement detail to edit (Esc = back to menu): ");
            if (res.Status != PromptStatus.OK)
                return true;
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
            return false;
        }
        if (target is null)
        {
            ed.WriteMessage("\nThat is not a pavement detail drawn by this plugin. (Details drawn by older versions " +
                            "don't carry their build-up — draw them again once and they become editable.)");
            return false;
        }

        var file = Store.Load();
        using var form = new BuildupForm(file, Store, doc, target.Record);
        var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        if (IsBack(result))
            return true;
        if (result != System.Windows.Forms.DialogResult.OK)
            return false;

        var buildup = form.Result;
        var settings = form.ResultSettings;
        settings.CreateBlock = target.IsBlock;
        settings.UnitsOverride = target.Record.Settings.UnitsOverride;
        var standard = form.ResultStandard;
        double unitsPerMm = target.Record.UnitsPerMm;

        DetailGeometry geometry;
        try
        {
            geometry = DetailLayout.Build(buildup, settings, standard, unitsPerMm);
        }
        catch (ArgumentException ex)
        {
            ed.WriteMessage("\n" + ex.Message);
            return false;
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
            return false;
        }

        ReportImports(ed, imported);
        foreach (var w in warnings)
            ed.WriteMessage($"\nWarning (standard \"{standard.Name}\"): {w}");
        ed.Regen();
        ed.WriteMessage(target.Copies > 1
            ? $"\nUpdated \"{buildup.Name}\" — all {target.Copies} copies of this detail changed."
            : $"\nUpdated \"{buildup.Name}\".");
        return false;
    }

    private bool PaveStandard()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;

        var file = Store.Load();
        var path = StandardStore.PathFor(file.Settings);
        if (LoadStandard(doc.Editor, file.Settings) is not { } standard)
        {
            standard = CadStandard.CreateDefault();
        }

        using var form = new StandardForm(standard, path, file.Settings, doc);
        var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        if (IsBack(result))
            return true;
        if (result != System.Windows.Forms.DialogResult.OK)
            return false;

        file.Settings.StandardPath = StandardStore.ToSetting(form.ResultPath);
        Store.Save(file);
        doc.Editor.WriteMessage($"\nCAD standard \"{form.Result.Name}\" saved to {form.ResultPath}.");
        return false;
    }

    [CommandMethod("PAVEQUICK", CommandFlags.Modal)]
    public void PaveQuickCommand() => PaveQuick(fromMenu: false);

    private bool PaveQuick(bool fromMenu)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;
        var ed = doc.Editor;
        var file = Store.Load();

        if (file.Presets.Count > 0)
            ed.WriteMessage("\nSaved presets: " + string.Join(", ", file.Presets.Select(p => p.Name)));

        var input = ed.GetString(new PromptStringOptions(
            "\nEnter build-up top to bottom in mm (e.g. 40 SMA 10 surface; 60 AC 20 binder; 150 Type 1 sub-base) or a preset name" + (fromMenu ? " (Esc = back to menu)" : "") + ": ")
        { AllowSpaces = true });
        if (input.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(input.StringResult))
            return fromMenu && input.Status == PromptStatus.Cancel;

        Buildup buildup;
        if (PresetStore.Find(file, input.StringResult) is { } preset)
        {
            buildup = preset.Clone();
        }
        else
        {
            var errors = new List<string>();
            var parsed = Read(ed, Interpret(ed, input.StringResult, file), errors);
            foreach (var e in errors)
                ed.WriteMessage("\n" + e);
            if (errors.Count > 0 || parsed.Layers.Count == 0)
                return false;

            string defaultName = parsed.Name ?? "PAVEMENT BUILD-UP";
            var name = ed.GetString(new PromptStringOptions($"\nBuild-up name <{defaultName}>: ") { AllowSpaces = true });
            if (name.Status != PromptStatus.OK)
                return false;
            buildup = parsed.ToBuildup(string.IsNullOrWhiteSpace(name.StringResult) ? defaultName : name.StringResult);
        }

        if (LoadStandard(ed, file.Settings) is not { } standard)
            return false;
        Place(doc, buildup, file.Settings, standard);
        return false;
    }

    private static CadStandard? LoadStandard(Editor ed, DetailSettings settings)
    {
        string path = StandardStore.PathFor(settings);
        try
        {
            return StandardStore.Load(path);
        }
        catch (System.Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ed.WriteMessage($"\n{ex.Message}\nFix the file or choose another standard (TTWPVMT > CAD standard).");
            return null;
        }
    }

    [CommandMethod("TTWPVMTAI", CommandFlags.Modal)]
    public void TtwPvmtAi() => PaveAi();

    private bool PaveAi()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;
        var file = Store.Load();
        var standard = LoadStandard(doc.Editor, file.Settings) ?? CadStandard.CreateDefault();
        using var form = new AiSettingsForm(file.Ai, standard);
        form.HideBack();
        var result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        if (result == System.Windows.Forms.DialogResult.OK)
            Store.Save(file);
        return IsBack(result);
    }

    private static BuildupParseResult Read(Editor ed, string text, List<string> errors)
    {
        var read = SmartReader.Read(text, errors);
        foreach (var note in read.Notes)
            ed.WriteMessage("\nRead: " + note);
        return read.Parsed;
    }

    private static string Interpret(Editor ed, string text, PresetFile file)
    {
        if (!AiService.IsReady(file.Ai, out var reason))
        {
            if (file.Ai.Enabled && AiService.HasCredentials)
                ed.WriteMessage($"\n{reason} Reading the text without AI.");
            return text;
        }
        var standard = LoadStandard(ed, file.Settings) ?? CadStandard.CreateDefault();
        try
        {
            ed.WriteMessage("\nInterpreting with AI...");
            var converted = AiService.Interpret(text, file.Ai, standard);
            ed.WriteMessage("\nAI: " + converted.Replace("\n", " "));
            return converted;
        }
        catch (InvalidOperationException ex)
        {
            ed.WriteMessage($"\n{ex.Message} Reading the text without AI.");
            return text;
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
            ed.WriteMessage($"\nCould not draw the detail ({ex.ErrorStatus}): {ex.Message}\n{ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("PavementBuildup"))?.Trim()}");
            return;
        }

        ReportImports(ed, imported);
        foreach (var w in warnings)
            ed.WriteMessage($"\nWarning (standard \"{standard.Name}\"): {w}");

        ed.WriteMessage($"\nDrew \"{buildup.Name}\": {buildup.Layers.Count} layer(s), total depth {buildup.TotalThicknessMm:0.#}mm at 1:{settings.ScaleDenominator:0.##}. Edit it later: TTWPVMT > Edit a detail.");
    }
}
