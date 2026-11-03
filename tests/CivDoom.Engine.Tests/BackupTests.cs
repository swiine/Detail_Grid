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

public class DraftedScriptTests
{
    [Fact]
    public void TheTimelineRunsInOrder()
    {
        Assert.True(DraftedScript.StartA > DraftedScript.Zoom);
        Assert.True(DraftedScript.StartC > DraftedScript.StartA + 6 * DraftedScript.Seg);
        Assert.True(DraftedScript.PickA > DraftedScript.DrawnAt);
        Assert.True(DraftedScript.Final > DraftedScript.PickC + DraftedScript.HatchSweep);
        Assert.Equal(16, DraftedScript.Picks().Count);
        Assert.Equal(DraftedScript.BracketA[0], DraftedScript.BracketA[^1]); // closed
        Assert.Equal("Command: PL", DraftedScript.CommandLine(0.2).Now);
        Assert.StartsWith("Specify next point", DraftedScript.CommandLine(DraftedScript.StartA + 0.5).Now);
        Assert.Equal("Command: _CoWork", DraftedScript.CommandLine(DraftedScript.Final + 0.1).Above);
    }

    [Fact]
    public void TheSoundHasAClickOnEveryPick()
    {
        byte[] wav = DraftedScript.Wav();
        const int rate = 22050;
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        short Sample(int i) => BitConverter.ToInt16(wav, 44 + i * 2);
        double Loudness(double t)
        {
            int s0 = (int)(t * rate); double m = 0;
            for (int i = s0; i < s0 + rate / 100; i++) m = Math.Max(m, Math.Abs(Sample(i)));
            return m;
        }
        foreach (double t in DraftedScript.Picks()) Assert.True(Loudness(t) > 2000, $"no click at {t:0.00}s");
        Assert.True(Loudness(DraftedScript.Final + 0.3) > 1000);   // the chime rings
        Assert.True(Loudness(0.02) < 200);                          // silence before the first key
    }
}
