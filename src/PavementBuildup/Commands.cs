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
    private const string App = "Civil 3D";

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
                MenuChoice.Joints => PaveJoints(),
                MenuChoice.Usage => PaveUsage(),
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

        if (Place(doc, form.Result, form.ResultSettings, form.ResultStandard))
            UsageLog.Record(UsageTools.DetailPlaced, App);
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
        if (Place(doc, form.Result, form.ResultSettings, form.ResultStandard))
            UsageLog.Record(UsageTools.DetailFromNote, App);
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
        UsageLog.Record(UsageTools.DetailEdited, App);
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
        UsageLog.Record(UsageTools.StandardSaved, App);
        doc.Editor.WriteMessage($"\nCAD standard \"{form.Result.Name}\" saved to {form.ResultPath}.");
        return false;
    }

    private bool PaveJoints()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;
        var ed = doc.Editor;
        var db = doc.Database;

        var pick = new PromptEntityOptions("\nSelect the slab boundary (closed polyline or hatch), Esc = back to menu: ");
        pick.SetRejectMessage("\nPick a closed polyline, circle or hatch.");
        foreach (var type in new[] { typeof(Polyline), typeof(Polyline2d), typeof(Hatch), typeof(Circle), typeof(Spline), typeof(Ellipse) })
            pick.AddAllowedClass(type, false);
        var boundaryPick = ed.GetEntity(pick);
        if (boundaryPick.Status != PromptStatus.OK)
            return true;

        var pitOptions = new PromptSelectionOptions { MessageForAdding = "\nSelect pits / columns / blockouts inside the slab (Enter for none): " };
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,CIRCLE,HATCH,SPLINE,ELLIPSE") });
        var pits = ed.GetSelection(pitOptions, filter);
        if (pits.Status == PromptStatus.Cancel)
            return true;

        List<Pt> outer;
        var holes = new List<IReadOnlyList<Pt>>();
        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var loops = JointDrawer.Loops(tr, boundaryPick.ObjectId);
            if (loops.Count == 0)
            {
                ed.WriteMessage("\nThat boundary isn't closed. Close the polyline (PEDIT > Close) or pick a hatch.");
                return false;
            }
            loops = loops.OrderByDescending(JointDrawer.Area).ToList();
            outer = loops[0];
            holes.AddRange(loops.Skip(1));
            if (pits.Status == PromptStatus.OK)
                foreach (var id in pits.Value.GetObjectIds().Where(id => id != boundaryPick.ObjectId))
                    holes.AddRange(JointDrawer.Loops(tr, id));
            tr.Commit();
        }

        var file = Store.Load();
        var standard = LoadStandard(ed, file.Settings) ?? CadStandard.CreateDefault();
        double unitsPerMetre;
        try
        {
            unitsPerMetre = DetailDrawer.ResolveUnitsPerMm(db, file.Settings, standard) * 1000;
        }
        catch (ArgumentException ex)
        {
            ed.WriteMessage("\n" + ex.Message);
            return false;
        }

        using var form = new JointForm(outer, holes, unitsPerMetre);
        var answer = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
        if (IsBack(answer))
            return true;
        if (answer != System.Windows.Forms.DialogResult.OK || form.Result is not { } result)
            return false;

        Point3d? notePoint = null;
        if (form.PlaceNote)
        {
            var at = ed.GetPoint(new PromptPointOptions("\nPick where the joint note goes (Enter to skip): ") { AllowNone = true });
            if (at.Status == PromptStatus.OK)
                notePoint = at.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        }

        var warnings = new List<string>();
        var imported = StandardResources.Import(db, standard, warnings);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            JointDrawer.Draw(db, tr, result, standard, unitsPerMetre, form.PlanScale, notePoint,
                JointDrawer.Note(result, form.Rules, form.ThicknessMm, form.Kind));
            tr.Commit();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            ed.WriteMessage($"\nCould not draw the joints ({ex.ErrorStatus}): {ex.Message}");
            return false;
        }

        ReportImports(ed, imported);
        UsageLog.Record(UsageTools.JointsDrawn, App, detail: $"{result.Joints.Count} joints, {result.AreaM2:0} m2");
        ed.WriteMessage($"\nDrew {result.Joints.Count} sawcut joint(s): {result.Panels} panels, max {result.SpacingM:0.00} m ({form.Rules.Name}), "
                        + $"sawcut {result.SawcutLengthM:0.0} m x {result.SawcutDepthMm} mm deep, isolation joint {result.PerimeterM:0.0} m.");
        foreach (var w in result.Warnings)
            ed.WriteMessage("\nCheck: " + w);
        return false;
    }

    private bool PaveUsage()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
            return false;
        var events = UsageLog.Read(new[] { UsageLog.FilePath });
        var summary = UsageReport.Summarise(events, TimeSaved.LoadCompany());
        var path = Path.Combine(Path.GetDirectoryName(UsageLog.FilePath)!, "TTW Pavement - time saved.html");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, UsageReport.Html(summary, $"TTW Pavement - time saved ({Environment.UserName})"));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            doc.Editor.WriteMessage($"\nTime saved: {summary.Hours:0.#} hours over {summary.Uses} uses. Report: {path}" +
                                    "\nFor the whole team, use the standalone app (Design tab > Time saved) and add everyone's usage.jsonl.");
        }
        catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            doc.Editor.WriteMessage("\nCould not make the time saved report: " + ex.Message);
        }
        return true;
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
        if (Place(doc, buildup, file.Settings, standard))
            UsageLog.Record(UsageTools.QuickPlace, App);
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

    private static bool Place(Document doc, Buildup buildup, DetailSettings settings, CadStandard standard)
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
            return false;
        }
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

        var pt = ed.GetPoint("\nPick top-left corner of the pavement detail: ");
        if (pt.Status != PromptStatus.OK)
            return false;

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
            return false;
        }

        ReportImports(ed, imported);
        foreach (var w in warnings)
            ed.WriteMessage($"\nWarning (standard \"{standard.Name}\"): {w}");

        ed.WriteMessage($"\nDrew \"{buildup.Name}\": {buildup.Layers.Count} layer(s), total depth {buildup.TotalThicknessMm:0.#}mm at 1:{settings.ScaleDenominator:0.##}. Edit it later: TTWPVMT > Edit a detail.");
        return true;
    }
}
