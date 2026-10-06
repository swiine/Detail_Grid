using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace PavementBuildup;

/// <summary>Reads the plain text of spec notes (MText / Text) the user selects in the drawing.</summary>
internal static class NoteReader
{
    /// <summary>Asks for text objects and returns their text top to bottom, or null if cancelled / nothing selected.</summary>
    public static string? Pick(Editor ed)
    {
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "MTEXT,TEXT") });
        var opts = new PromptSelectionOptions { MessageForAdding = "\nSelect the pavement note (MText or text): " };
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
                    pieces.Add((m.Location.Y, m.Location.X, m.Text)); // .Text = contents without formatting codes
                    break;
                case DBText t:
                    pieces.Add((t.Position.Y, t.Position.X, t.TextString));
                    break;
            }
        }
        tr.Commit();

        // Several single-line texts: read them like lines of one note, top to bottom, left to right.
        var text = string.Join("\n", pieces.OrderByDescending(p => p.Y).ThenBy(p => p.X).Select(p => p.Text.Trim()));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
