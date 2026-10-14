using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace PavementBuildup;

internal static class NoteReader
{
    public static string? Pick(Editor ed, string prompt = "\nSelect the pavement note (MText or text): ")
    {
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "MTEXT,TEXT") });
        var opts = new PromptSelectionOptions { MessageForAdding = prompt };
        var res = ed.GetSelection(opts, filter);
        if (res.Status != PromptStatus.OK || res.Value.Count == 0)
            return null;

        using var tr = ed.Document.Database.TransactionManager.StartOpenCloseTransaction();
        var pieces = new List<(double Y, double X, string Text)>();
        foreach (var id in res.Value.GetObjectIds())
        {
            switch (tr.GetObject(id, OpenMode.ForRead))
            {
                case MText m:
                    pieces.Add((m.Location.Y, m.Location.X, m.Text));
                    break;
                case DBText t:
                    pieces.Add((t.Position.Y, t.Position.X, t.TextString));
                    break;
            }
        }
        tr.Commit();

        var text = string.Join("\n", pieces.OrderByDescending(p => p.Y).ThenBy(p => p.X).Select(p => p.Text.Trim()));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
