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
    public void CoreAndLoaderShareOnePublicKey() => Assert.Equal(Licensing.PublicKey, Updater.PublicKey);
}
