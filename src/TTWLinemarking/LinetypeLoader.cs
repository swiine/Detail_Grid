using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using TTWLinemarking.Core;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace TTWLinemarking
{
    internal static class LinetypeLoader
    {
        // Makes sure each code exists in the drawing with the pattern for the current scale.
        //
        // A code that's missing gets loaded. A code that's already there but was loaded at a
        // different scale (pattern length doesn't match) gets replaced, so switching a drawing from
        // 1:500 to 1:250 doesn't leave old dash lengths behind. Database.LoadLineTypeFile refuses
        // to overwrite an existing linetype, so codes are loaded into a scratch database and
        // cloned across with Replace.
        //
        // Returns the codes that could not be loaded. Call with the document locked and no
        // transaction open.
        public static List<string> Ensure(Database db, IEnumerable<string> codes, string linPath, int scale, Editor ed)
        {
            var missing = new List<string>();
            var stale = new List<string>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                foreach (string code in codes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!ltt.Has(code))
                    {
                        missing.Add(code);
                        continue;
                    }
                    var ltr = (LinetypeTableRecord)tr.GetObject(ltt[code], OpenMode.ForRead);
                    double expected = Catalogue.ExpectedPatternLength(Catalogue.Find(code), scale);
                    if (Math.Abs(ltr.PatternLength - expected) > 1e-6 * Math.Max(1.0, expected))
                        stale.Add(code);
                }
                tr.Commit();
            }

            var failed = new List<string>();
            if (missing.Count == 0 && stale.Count == 0) return failed;

            using (var scratch = new Database(true, true))
            {
                var loaded = new List<string>();
                foreach (string code in missing.Concat(stale))
                {
                    try
                    {
                        scratch.LoadLineTypeFile(code, linPath);
                        loaded.Add(code);
                    }
                    catch (AcException ex)
                    {
                        failed.Add(code);
                        ed.WriteMessage($"\nCould not load linetype \"{code}\" from {linPath}: {ex.Message}");
                    }
                }

                if (loaded.Count > 0)
                {
                    var ids = new ObjectIdCollection();
                    using (var tr = scratch.TransactionManager.StartTransaction())
                    {
                        var ltt = (LinetypeTable)tr.GetObject(scratch.LinetypeTableId, OpenMode.ForRead);
                        foreach (string code in loaded) ids.Add(ltt[code]);
                        tr.Commit();
                    }
                    scratch.WblockCloneObjects(ids, db.LinetypeTableId, new IdMapping(), DuplicateRecordCloning.Replace, false);
                }
            }

            var updated = stale.Except(failed).ToList();
            if (updated.Count > 0)
                ed.WriteMessage($"\nUpdated {string.Join(", ", updated)} to the 1:{scale} pattern (was loaded at a different scale).");
            return failed;
        }
    }
}
