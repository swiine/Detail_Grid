using System.Security.Cryptography;
using CoWork.Core;
using CoWork.Loader;
using Xunit;

namespace CivDoom.Engine.Tests;

public class UpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cw-upd-" + Guid.NewGuid().ToString("N"));
    private readonly string _pub, _priv;

    public UpdaterTests() => (_pub, _priv) = Licensing.NewKeyPair();

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private byte[] Sign(byte[] data)
    {
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(_priv), out _);
        return ec.SignData(data, HashAlgorithmName.SHA256);
    }

    private string Build(string name, string version)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "CoWork.dll"), "build " + version);
        File.WriteAllText(Path.Combine(dir, "CoWork.Core.dll"), "core " + version);
        return dir;
    }

    [Fact]
    public void NewReleaseIsCopiedCheckedAndUsed()
    {
        string bundled = Build("bundle-app", "1.0.0");
        Updater.Sign(bundled, "1.0.0", Sign);
        string release = Path.Combine(_root, "release"), cache = Path.Combine(_root, "cache");
        Updater.Publish(Build("new", "1.1.0"), release, "1.1.0", Sign);

        Updater.Choice c = Updater.Choose(release, cache, bundled, _pub)!;
        Assert.Equal("1.1.0", c.Version);
        Assert.StartsWith(cache, c.Folder);                                   // runs from this PC, not the network
        Assert.Equal("build 1.1.0", File.ReadAllText(Path.Combine(c.Folder, "CoWork.dll")));
        Assert.Contains("updated", c.Note);

        // Next start: already have it, nothing copied again.
        Updater.Choice again = Updater.Choose(release, cache, bundled, _pub)!;
        Assert.Equal("1.1.0", again.Version);
        Assert.Equal("", again.Note);
    }

    [Fact]
    public void TamperedOrUnsignedReleasesNeverRun()
    {
        string bundled = Build("bundle-app", "1.0.0");
        Updater.Sign(bundled, "1.0.0", Sign);
        string release = Path.Combine(_root, "release"), cache = Path.Combine(_root, "cache");
        Updater.Publish(Build("new", "1.1.0"), release, "1.1.0", Sign);
        File.WriteAllText(Path.Combine(release, "1.1.0", "CoWork.dll"), "something nasty");   // swapped after signing

        Updater.Choice c = Updater.Choose(release, cache, bundled, _pub)!;
        Assert.Equal("1.0.0", c.Version);                                     // falls back to the installed copy
        Assert.Contains("isn't signed", c.Note);

        // A release signed by someone else's key is ignored too.
        var (_, otherPriv) = Licensing.NewKeyPair();
        Updater.Publish(Build("evil", "9.9.9"), release, "9.9.9", d =>
        {
            using var ec = ECDsa.Create();
            ec.ImportPkcs8PrivateKey(Convert.FromBase64String(otherPriv), out _);
            return ec.SignData(d, HashAlgorithmName.SHA256);
        });
        Assert.Equal("1.0.0", Updater.Choose(release, cache, bundled, _pub)!.Version);
    }

    [Fact]
    public void OfflineUsesNewestCopyOnThisPc()
    {
        string bundled = Build("bundle-app", "1.0.0");
        Updater.Sign(bundled, "1.0.0", Sign);
        string release = Path.Combine(_root, "release"), cache = Path.Combine(_root, "cache");
        Updater.Publish(Build("new", "1.2.0"), release, "1.2.0", Sign);
        Assert.Equal("1.2.0", Updater.Choose(release, cache, bundled, _pub)!.Version);

        Directory.Delete(release, true);                                      // network gone
        Assert.Equal("1.2.0", Updater.Choose(release, cache, bundled, _pub)!.Version);
        Assert.Null(Updater.Choose(null, Path.Combine(_root, "empty"), Path.Combine(_root, "nothing"), _pub)); // nothing signed at all
    }

    [Fact]
    public void TestGroupGetsItFirstThenEveryone()
    {
        string bundled = Build("bundle-app", "1.0.0");
        Updater.Sign(bundled, "1.0.0", Sign);
        string release = Path.Combine(_root, "release"), tester = Path.Combine(_root, "c-tester"), other = Path.Combine(_root, "c-other");
        Updater.Publish(Build("v11", "1.1.0"), release, "1.1.0", Sign);
        Updater.SetTesters(release, new[] { "coreyy", " DanielV " });
        Assert.Equal(new[] { "coreyy", "DanielV" }, Updater.Testers(release));

        Updater.Publish(Build("v12", "1.2.0"), release, "1.2.0", Sign, testGroupOnly: true);
        Assert.Equal(("1.1.0", "1.2.0"), (Updater.Channels(release).Latest, Updater.Channels(release).Test));
        Assert.Equal("1.2.0", Updater.Choose(release, tester, bundled, _pub, "DANIELV")!.Version); // logins aren't case sensitive
        Assert.Equal("1.1.0", Updater.Choose(release, other, bundled, _pub, "mattb")!.Version);

        // Taken out of the group: back to everyone's version, and the test copy is withdrawn.
        Updater.SetTesters(release, new[] { "coreyy" });
        Updater.Choice back = Updater.Choose(release, tester, bundled, _pub, "danielv")!;
        Assert.Equal("1.1.0", back.Version);
        Assert.Contains("withdrawn", back.Note);
        Assert.Null(Updater.Verify(Path.Combine(tester, "1.2.0"), _pub));                       // no loader will run it again

        Assert.Equal("1.2.0", Updater.Promote(release));
        Assert.Null(Updater.Channels(release).Test);
        Assert.Equal("1.2.0", Updater.Choose(release, other, bundled, _pub, "mattb")!.Version);
        Assert.Equal("1.2.0", Updater.Choose(release, tester, bundled, _pub, "danielv")!.Version); // copied again, freshly signed
    }

    [Fact]
    public void RollBackTakesEveryoneBackOneStep()
    {
        string bundled = Build("bundle-app", "1.0.0");
        Updater.Sign(bundled, "1.0.0", Sign);
        string release = Path.Combine(_root, "release"), cache = Path.Combine(_root, "cache");
        Updater.Publish(Build("v11", "1.1.0"), release, "1.1.0", Sign);
        Updater.Publish(Build("v12", "1.2.0"), release, "1.2.0", Sign);
        Assert.Equal("1.2.0", Updater.Choose(release, cache, bundled, _pub, "a")!.Version);

        Assert.Equal(("1.2.0", "1.1.0"), Updater.RollBack(release));
        Assert.Null(Updater.RollBack(release));                                                 // one step only
        Updater.Choice c = Updater.Choose(release, cache, bundled, _pub, "a")!;
        Assert.Equal("1.1.0", c.Version);
        Assert.Null(Updater.Verify(Path.Combine(cache, "1.2.0"), _pub));

        Directory.Delete(release, true);                                                        // offline: stays rolled back
        Assert.Equal("1.1.0", Updater.Choose(release, cache, bundled, _pub, "a")!.Version);
        // A newer version installed by hand from a zip still runs (it's what was asked for).
        string newer = Build("bundle-new", "1.3.0");
        Updater.Sign(newer, "1.3.0", Sign);
        Assert.Equal("1.3.0", Updater.Choose(release, cache, newer, _pub, "a")!.Version);
    }

    [Fact]
    public void EndingATestSendsTheGroupBack()
    {
        string bundled = Build("bundle-app", "1.0.0");
        Updater.Sign(bundled, "1.0.0", Sign);
        string release = Path.Combine(_root, "release"), cache = Path.Combine(_root, "cache");
        Updater.Publish(Build("v11", "1.1.0"), release, "1.1.0", Sign);
        Updater.SetTesters(release, new[] { "coreyy" });
        Updater.Publish(Build("v12", "1.2.0"), release, "1.2.0", Sign, testGroupOnly: true);
        Assert.Equal("1.2.0", Updater.Choose(release, cache, bundled, _pub, "coreyy")!.Version);
        Assert.Equal("1.2.0", Updater.EndTest(release));
        Assert.Equal("1.1.0", Updater.Choose(release, cache, bundled, _pub, "coreyy")!.Version);
        // Publishing to everyone a version at least as new as the test ends the test too.
        Updater.Publish(Build("v13t", "1.3.0"), release, "1.3.0", Sign, testGroupOnly: true);
        Updater.Publish(Build("v13", "1.3.0"), release, "1.3.0", Sign);
        Assert.Null(Updater.Channels(release).Test);
        Assert.Equal("1.1.0", Updater.Channels(release).Previous);
    }

    [Fact]
    public void CoreAndLoaderShareOnePublicKey() => Assert.Equal(Licensing.PublicKey, Updater.PublicKey);
}
