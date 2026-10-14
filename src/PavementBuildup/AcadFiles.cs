using Autodesk.AutoCAD.Windows;

namespace PavementBuildup;

internal static class AcadFiles
{
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
