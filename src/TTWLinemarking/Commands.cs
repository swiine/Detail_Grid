using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using TTWLinemarking.Core;
using TTWLinemarking.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TTWLinemarking
{
    public class Commands
    {
        private const CommandFlags Flags = CommandFlags.Modal | CommandFlags.UsePickSet;

        // Flat code list, the way the original ROAD dialog worked.
        [CommandMethod("ROAD", Flags)]
        public void Road() => Run(startInOverride: true);

        // Pick by purpose ("Give Way Line") instead of by code.
        [CommandMethod("LINEMARKING", Flags)]
        public void Linemarking() => Run(startInOverride: false);

        [CommandMethod("8", Flags)]
        public void LinemarkingShortcut() => Run(startInOverride: false);

        private static void Run(bool startInOverride)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                RunCore(doc, startInOverride);
            }
            catch (System.Exception ex)
            {
                // Otherwise AutoCAD reports the error generically, or not at all, and nothing
                // visibly happens. The full text is what's needed to diagnose it.
                doc.Editor.WriteMessage($"\nTTW Linemarking error - nothing was changed.\n{ex}\n");
                MessageBox.Show($"Something went wrong - nothing was changed.\n\n{ex.GetType().Name}: {ex.Message}\n\nFull details are on the command line (F2).",
                    "TTW Linemarking", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void RunCore(Document doc, bool startInOverride)
        {
            Editor ed = doc.Editor;

            // Grab any pre-selection before the dialog takes focus.
            List<ObjectId> preselected = PolylinesOnly(ed.SelectImplied());

            string cannoscale = AcadApp.GetSystemVariable("CANNOSCALE") as string;
            int? scale = Scales.Parse(cannoscale);
            if (scale == null)
            {
                using (var warning = new UnsupportedScaleForm(cannoscale))
                    AcadApp.ShowModalDialog(warning);
                return;
            }

            string linFile = Scales.LinFileName(scale.Value);
            string linPath = Paths.FindLinFile(linFile);
            if (linPath == null)
            {
                MessageBox.Show(
                    $"Can't find {linFile}.\n\nLooked in:\n" + string.Join("\n", Paths.LinetypeFolders),
                    "TTW Linemarking", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            LinemarkingItem item;
            bool allowMultiple;
            using (var form = new MainForm(startInOverride, cannoscale, linPath))
            {
                if (AcadApp.ShowModalDialog(form) != DialogResult.OK || form.SelectedItem == null) return;
                item = form.SelectedItem;
                allowMultiple = form.AllowMultiple;
            }

            // Prompts happen before the document lock and transaction.
            List<ObjectId> ids = preselected.Count > 0 && (allowMultiple || preselected.Count == 1)
                ? preselected
                : Select(ed, item, allowMultiple);
            if (ids.Count == 0)
            {
                ed.WriteMessage("\nNo polylines selected - nothing was changed. (Lines, arcs and feature lines aren't picked up; use a polyline.)\n");
                return;
            }

            Point3d? sidePoint = null;
            bool pair = item.Pair != null;
            if (pair && item.Pair.PartnerCode != item.Code)
            {
                var ppr = ed.GetPoint(new PromptPointOptions($"\nPick the side for the {item.Code} line ({item.Pair.PartnerCode} goes on the other side): "));
                if (ppr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nSide pick cancelled - nothing was changed.\n");
                    return;
                }
                sidePoint = ppr.Value;
            }

            using (doc.LockDocument())
            {
                var codes = pair ? new[] { item.Code, item.Pair.PartnerCode } : new[] { item.Code };
                var failed = LinetypeLoader.Ensure(doc.Database, codes, linPath, scale.Value, ed);
                if (failed.Count > 0)
                {
                    ed.WriteMessage("\nNothing was changed.\n");
                    return;
                }

                ApplyResult result;
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Applicator.EnsureLayer(tr, doc.Database, Catalogue.Layer);
                    if (pair)
                    {
                        ed.WriteMessage($"\n{item.Code}: drawing {item.Code} + {item.Pair.PartnerCode}, each {OffsetMath.HalfSpacing(item):0.000} either side of the selected centreline.");
                        Applicator.EnsureLayer(tr, doc.Database, Catalogue.ReferenceLayer);
                        result = Applicator.ApplyPair(tr, ids, item, sidePoint);
                    }
                    else
                    {
                        result = Applicator.ApplySingle(tr, ids, item);
                    }
                    tr.Commit();
                }

                Report(ed, item, pair, result);
            }
        }

        private static List<ObjectId> Select(Editor ed, LinemarkingItem item, bool allowMultiple)
        {
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE") });
            string what = item.Pair != null ? "centreline polyline" : "polyline";
            var opts = new PromptSelectionOptions
            {
                MessageForAdding = allowMultiple ? $"\nSelect {what}s for {item.Code}: " : $"\nSelect a {what} for {item.Code}: ",
                SingleOnly = !allowMultiple,
                SinglePickInSpace = !allowMultiple,
            };
            var res = ed.GetSelection(opts, filter);
            return res.Status == PromptStatus.OK ? res.Value.GetObjectIds().ToList() : new List<ObjectId>();
        }

        private static List<ObjectId> PolylinesOnly(PromptSelectionResult implied)
        {
            if (implied.Status != PromptStatus.OK || implied.Value == null) return new List<ObjectId>();
            var lw = RXObject.GetClass(typeof(Polyline));
            var heavy = RXObject.GetClass(typeof(Polyline2d));
            return implied.Value.GetObjectIds()
                .Where(id => id.ObjectClass.IsDerivedFrom(lw) || id.ObjectClass.IsDerivedFrom(heavy))
                .ToList();
        }

        private static void Report(Editor ed, LinemarkingItem item, bool pair, ApplyResult r)
        {
            string colour = item.Paint == Paint.White ? "" : $", {item.Paint.ToString().ToUpperInvariant()}";
            if (pair)
            {
                if (r.Applied > 0)
                    ed.WriteMessage($"\nCreated {item.Code} + {item.Pair.PartnerCode} ({item.Pair.Gap:0.00} clear gap) either side of {r.Applied} centreline(s). Centrelines moved to {Catalogue.ReferenceLayer} (won't plot).");
            }
            else if (r.Applied > 0)
            {
                ed.WriteMessage($"\nApplied {item.Code} to {r.Applied} polyline(s) on {Catalogue.Layer}, width {item.Width:0.00}{colour}.");
            }

            if (r.Locked > 0) ed.WriteMessage($"\n{r.Locked} on a locked layer - skipped.");
            if (r.Unsupported > 0)
                ed.WriteMessage(pair
                    ? $"\n{r.Unsupported} 3D polyline(s) can't be offset - skipped. Use a 2D polyline as the centreline."
                    : $"\n{r.Unsupported} 3D polyline(s) can't take a width - skipped.");
            if (r.Failed > 0) ed.WriteMessage($"\n{r.Failed} couldn't be offset - left unchanged. Reason: {r.FirstError}.");
            if (r.Applied == 0 && r.Locked == 0 && r.Unsupported == 0 && r.Failed == 0)
                ed.WriteMessage("\nNothing was changed.");
            if (item.NeedsManualOffset && r.Applied > 0)
                ed.WriteMessage($"\n{item.Code} is a double line, but its gap isn't confirmed yet - offset the second line by hand.");
            ed.WriteMessage("\n");
        }
    }
}
