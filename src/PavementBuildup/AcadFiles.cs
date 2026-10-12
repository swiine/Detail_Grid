using Autodesk.AutoCAD.Windows;

namespace PavementBuildup;

/// <summary>
/// File pickers through AutoCAD's own dialogs (not the plain .NET ones), so focus and the
/// drawing stay with Civil 3D.
/// </summary>
internal static class AcadFiles
{
    /// <param name="extensions">e.g. "json" or "dwg;dwt"</param>
    /// <returns>The chosen file, or null if cancelled.</returns>
    public static string? Open(string title, string extensions, string defaultName = "")
    {
        var dlg = new OpenFileDialog(title, defaultName, extensions, "PavementBuildupOpen",
            (OpenFileDialog.OpenFileDialogFlags)0);
        return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.Filename : null;
    }

    public static string? Save(string title, string extension, string defaultName = "")
    {
        var dlg = new SaveFileDialog(title, defaultName, extension, "PavementBuildupSave",
            (SaveFileDialog.SaveFileDialogFlags)0);
        return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.Filename : null;
    }
}
