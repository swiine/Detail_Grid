using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TTW.PluginTools.Example.ExampleTools))]

namespace TTW.PluginTools.Example
{
    /// <summary>
    /// How a plugin appears in TTW Plugin Tools - all standard .NET attributes, no launcher reference:
    ///   [CommandMethod("NAME")]      makes it a tool (and a normal typed command)
    ///   [DisplayName("...")]         the name in the list (otherwise the command name)
    ///   [Description("...")]         the text shown when it's picked (or put NAME.txt next to the DLL)
    ///   [Category("...")]            the group heading (otherwise the sub-folder name)
    ///   [ReadOnly(true)]             read-only: anything it changes is thrown away
    ///   [Browsable(false)]           keep a command out of the list
    /// A video called NAME.mp4 (or "Display Name.mp4") next to the DLL adds a Watch tutorial button.
    ///
    /// Use doc.TransactionManager.StartTransaction() (not StartOpenCloseTransaction) so that
    /// the launcher can roll your changes back if the tool fails.
    /// </summary>
    public class ExampleTools
    {
        [CommandMethod("TTWCOUNTBLOCKS")]
        [DisplayName("Count blocks")]
        [Description("Lists every block used in model space and how many times it appears, most used first.\n\nUseful for checking a drawing before issue.")]
        [Category("Audit")]
        [ReadOnly(true)]
        public void CountBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                    {
                        // Dynamic blocks show their real name, not *U123
                        var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                        counts[btr.Name] = counts.TryGetValue(btr.Name, out int n) ? n + 1 : 1;
                    }
                }
                tr.Commit();
            }

            if (counts.Count == 0) { ed.WriteMessage("\nNo blocks in model space."); return; }
            ed.WriteMessage($"\n{counts.Values.Sum()} blocks, {counts.Count} different:");
            foreach (var kv in counts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                ed.WriteMessage($"\n  {kv.Value,5}  {kv.Key}");
        }

        [CommandMethod("TTWMAKELAYER")]
        [DisplayName("Make layer")]
        [Description("Asks for a layer name and creates it if it doesn't exist. Makes it current either way.")]
        [Category("Drafting")]
        public void MakeLayer()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            var answer = ed.GetString(new PromptStringOptions("\nLayer name: ") { AllowSpaces = true });
            if (answer.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(answer.StringResult)) return;
            string name = answer.StringResult.Trim();

            try { SymbolUtilityServices.ValidateSymbolName(name, false); }
            catch { ed.WriteMessage($"\n\"{name}\" isn't a valid layer name."); return; }

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                ObjectId id;
                if (layers.Has(name))
                {
                    id = layers[name];
                    ed.WriteMessage($"\nLayer {name} already exists - made it current.");
                }
                else
                {
                    layers.UpgradeOpen();
                    var layer = new LayerTableRecord { Name = name };
                    id = layers.Add(layer);
                    tr.AddNewlyCreatedDBObject(layer, true);
                    ed.WriteMessage($"\nCreated layer {name} and made it current.");
                }
                doc.Database.Clayer = id;
                tr.Commit();
            }
        }

        [CommandMethod("TTWEXAMPLEREPORT")]
        [DisplayName("Drawing report")]
        [Category("Audit")]
        [ReadOnly(true)]
        // No [Description] here: the text comes from TTWEXAMPLEREPORT.txt next to the DLL
        public void DrawingReport()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                doc.Editor.WriteMessage(
                    $"\n{System.IO.Path.GetFileName(db.Filename)}" +
                    $"\n  Layers:   {layers.Cast<ObjectId>().Count()}" +
                    $"\n  Layouts:  {layouts.Count - 1}" +
                    $"\n  Objects in model space: {ms.Cast<ObjectId>().Count()}" +
                    $"\n  Units:    {db.Insunits}");
                tr.Commit();
            }
        }

        [CommandMethod("TTWROLLBACKDEMO")]
        [DisplayName("Rollback demo")]
        [Description("Draws a circle at 0,0 and then fails on purpose.\n\nThe circle disappears again: when a tool fails, Plugin Tools rolls back everything it did to the drawing. Try it, then delete this command from your own plugins.")]
        [Category("Examples")]
        public void RollbackDemo()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 10);
                ms.AppendEntity(circle);
                tr.AddNewlyCreatedDBObject(circle, true);
                tr.Commit();
            }
            throw new InvalidOperationException("Failing on purpose - the circle should be gone.");
        }

        /// <summary>A helper command that shouldn't show in the list.</summary>
        [CommandMethod("TTWEXAMPLEHIDDEN")]
        [Browsable(false)]
        public void Hidden() => AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nHidden from Plugin Tools.");
    }
}
