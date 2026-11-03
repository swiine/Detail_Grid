using CoWork.Core;
using Xunit;

namespace CivDoom.Engine.Tests;

public class BackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cw-bak-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void FindsTheJobNumberAndTheCivilFolder()
    {
        Backups.Where w = Backups.Locate(@"\\ttw\dfs\ttwcad\jobs\24\241796\civil\sheets\0\0\C-ROAD.dwg");
        Assert.Equal("241796", w.Job);
        Assert.True(w.InCivil);
        Assert.Equal(@"\\ttw\dfs\ttwcad\jobs\24\241796\civil\_backup", w.Root);
        Assert.Equal(@"\\ttw\dfs\ttwcad\jobs\24\241796\civil\_backup\C-ROAD", Backups.FolderFor(@"\\ttw\dfs\ttwcad\jobs\24\241796\civil\sheets\0\0\C-ROAD.dwg"));

        // A drawing straight in the civil folder, a capitalised Civil, and a mapped drive.
        Assert.Equal(@"\\ttw\dfs\ttwcad\jobs\24\241796\civil\_backup", Backups.Locate(@"\\ttw\dfs\ttwcad\jobs\24\241796\civil\EG.dwg").Root);
        Assert.Equal(@"J:\jobs\25\250012\Civil\_backup", Backups.Locate(@"J:\jobs\25\250012\Civil\Design\Model.dwg").Root);
    }

    [Fact]
    public void WithoutAJobOrCivilFolderBackupsGoNextToTheDrawing()
    {
        // "24" (2 digits) and "1234567" (7 digits) aren't job numbers.
        Backups.Where none = Backups.Locate(@"\\ttw\dfs\ttwcad\jobs\24\1234567\civil\x.dwg");
        Assert.Null(none.Job);
        Assert.Equal(@"\\ttw\dfs\ttwcad\jobs\24\1234567\civil\_backup", none.Root);

        Backups.Where noCivil = Backups.Locate(@"\\ttw\dfs\ttwcad\jobs\24\241796\survey\points.dwg");
        Assert.Equal("241796", noCivil.Job);
        Assert.False(noCivil.InCivil);
        Assert.Equal(@"\\ttw\dfs\ttwcad\jobs\24\241796\survey\_backup", noCivil.Root);
    }

    private string Job()
    {
        string sheets = Path.Combine(_root, "jobs", "24", "241796", "civil", "sheets", "0", "0");
        Directory.CreateDirectory(sheets);
        string dwg = Path.Combine(sheets, "C-ROAD.dwg");
        File.WriteAllText(dwg, "version 1");
        return dwg;
    }

    [Fact]
    public void MakesListsPrunesAndRestoresCopies()
    {
        string dwg = Job();
        var t = new DateTime(2026, 11, 3, 9, 15, 0);
        string first = Backups.Make(dwg, "session start", "Corey", t);
        Assert.StartsWith(Path.Combine(_root, "jobs", "24", "241796", "civil", "_backup", "C-ROAD"), first);
        Assert.EndsWith("2026-11-03 0915 - session start - Corey.dwg", first);

        File.WriteAllText(dwg, "version 2");
        Backups.Make(dwg, "every 30 minutes", "Corey", t.AddMinutes(30));
        Backups.Make(dwg, "before hand-over", "Daniel V", t.AddMinutes(30)); // same minute: gets "(2)"

        List<Backups.Copy> all = Backups.List(dwg);
        Assert.Equal(3, all.Count);
        Assert.Equal(t, all[^1].Time);
        Assert.Equal("session start", all[^1].Reason);
        Assert.Contains(all, c => c.Who == "Daniel V" && c.Reason == "before hand-over");

        // Restore the 9:15 copy: the current drawing is kept first.
        File.WriteAllText(dwg, "broken");
        string kept = Backups.Restore(dwg, first, "Corey", t.AddHours(1));
        Assert.Equal("version 1", File.ReadAllText(dwg));
        Assert.Equal("broken", File.ReadAllText(kept));
        Assert.Contains("before restore", kept);

        Backups.Prune(dwg, 2);
        Assert.Equal(2, Backups.List(dwg).Count);
        Assert.Contains("before restore", Backups.List(dwg)[0].File); // newest kept
    }

    [Fact]
    public void WontRestoreOverADrawingSomeoneHasOpen()
    {
        string dwg = Job();
        string copy = Backups.Make(dwg, "session start", "Corey", DateTime.Now);
        File.WriteAllText(Path.ChangeExtension(dwg, ".dwl"), "lock");
        Assert.Throws<IOException>(() => Backups.Restore(dwg, copy, "Corey", DateTime.Now));
    }
}
