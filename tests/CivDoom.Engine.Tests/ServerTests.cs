using System.Net;
using System.Security.Cryptography;
using CoWork.Core;
using CoWork.Loader;
using Xunit;

namespace CivDoom.Engine.Tests;

/// <summary>The CoWork server: licences checked on connect, pokes and crosshairs passed on, updates served.</summary>
public class ServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cw-srv-" + Guid.NewGuid().ToString("N"));
    private readonly string _pub, _priv;

    public ServerTests() => (_pub, _priv) = Licensing.NewKeyPair();

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private CoWorkServer NewServer(string? data = null, string? release = null)
    {
        var s = new CoWorkServer(0, data, release, address: IPAddress.Loopback, publicKey: _pub);
        s.Start();
        return s;
    }

    private string Licence(string id, string user) => Licensing.Issue(new LicenseInfo(id, user, new[] { user }), _priv);

    private static ServerClient Client(CoWorkServer s, string user, string licence) =>
        new($"127.0.0.1:{s.Port}", () => new ServerClient.Hello(user, user, "PC", "OFFICE", licence, "1.3.0"));

    private static async Task<T> Within<T>(TaskCompletionSource<T> tcs, int ms = 5000)
    {
        Task done = await Task.WhenAny(tcs.Task, Task.Delay(ms));
        Assert.True(done == tcs.Task, "timed out");
        return await tcs.Task;
    }

    private static async Task Until(Func<bool> ok, int ms = 5000)
    {
        for (int i = 0; i < ms / 20 && !ok(); i++) await Task.Delay(20);
        Assert.True(ok(), "timed out");
    }

    [Fact]
    public async Task LicensedPeopleArePassedEachOthersChangesAndCrosshairs()
    {
        using CoWorkServer server = NewServer();
        using ServerClient a = Client(server, "coreyy", Licence("l1", "coreyy"));
        using ServerClient b = Client(server, "danielv", Licence("l2", "danielv"));
        var poked = new TaskCompletionSource<string>();
        var here = new TaskCompletionSource<(string, string)>();
        b.Poked += t => poked.TrySetResult(t);
        b.Here += (s, p) => here.TrySetResult((s, p));
        a.Start(); b.Start();
        await Until(() => a.Connected && b.Connected);
        await Until(() => server.Online.Length == 2);
        Assert.Equal(new[] { "coreyy", "danielv" }, server.Online);

        a.Poke("chat:lobby");
        Assert.Equal("chat:lobby", await Within(poked));

        a.SendHere("abc123", """{"id":"p1","x":10.5,"y":20}""");
        var (session, person) = await Within(here);
        Assert.Equal("abc123", session);
        Assert.Contains("10.5", person);
    }

    [Fact]
    public async Task NoLicenceOrSomeoneElsesIsRefused()
    {
        using CoWorkServer server = NewServer();
        using ServerClient none = Client(server, "eve", "");
        using ServerClient borrowed = Client(server, "eve", Licence("l1", "coreyy"));
        var why = new TaskCompletionSource<string>();
        var why2 = new TaskCompletionSource<string>();
        none.Refused += w => why.TrySetResult(w);
        borrowed.Refused += w => why2.TrySetResult(w);
        none.Start(); borrowed.Start();
        Assert.Equal("no licence", await Within(why));
        Assert.Equal("licence isn't for this login", await Within(why2));
        Assert.False(none.Connected);
        Assert.Empty(server.Online);
    }

    [Fact]
    public async Task RevokingALicenceDisconnectsThemWithin_aMinute()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(Licensing.Folder(data));
        using CoWorkServer server = NewServer(data);
        using ServerClient c = Client(server, "sam", Licence("s1", "sam"));
        var why = new TaskCompletionSource<string>();
        c.Refused += w => why.TrySetResult(w);
        c.Start();
        await Until(() => c.Connected);

        File.WriteAllText(Licensing.RevokedFile(data), Licensing.SignRevoked(new Licensing.RevokedList(1, new[] { "s1" }), _priv));
        server.RecheckLicences(); // runs every minute on its own
        Assert.Equal("licence revoked", await Within(why));
        await Until(() => !c.Connected);
    }

    private byte[] Sign(byte[] data)
    {
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(_priv), out _);
        return ec.SignData(data, HashAlgorithmName.SHA256);
    }

    [Fact]
    public void UpdatesComeFromTheServerAndAreStillChecked()
    {
        string build = Path.Combine(_root, "build");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, "CoWork.dll"), "build 1.3.0");
        File.WriteAllText(Path.Combine(build, "CoWork.Core.dll"), "core 1.3.0");
        string release = Path.Combine(_root, "release"), cache = Path.Combine(_root, "cache"), download = Path.Combine(_root, "download");
        Updater.Publish(build, release, "1.3.0", Sign);
        File.WriteAllText(Path.Combine(release, "1.3.0", "secret.txt"), "not in the manifest");
        using CoWorkServer server = NewServer(release: release);

        string? got = Updater.FetchFromServer($"127.0.0.1:{server.Port}", download, cache);
        Assert.Equal(download, got);
        Assert.False(File.Exists(Path.Combine(download, "1.3.0", "secret.txt"))); // only what the manifest lists is served
        Updater.Choice? choice = Updater.Choose(got, cache, Path.Combine(_root, "none"), _pub);
        Assert.Equal("1.3.0", choice?.Version);
        Assert.Equal("build 1.3.0", File.ReadAllText(Path.Combine(choice!.Folder, "CoWork.dll")));

        // A server that changes a file can't get it run: the signature check fails and nothing is used.
        File.WriteAllText(Path.Combine(release, "1.3.0", "CoWork.dll"), "evil");
        string cache2 = Path.Combine(_root, "cache2"), download2 = Path.Combine(_root, "download2");
        Assert.Null(Updater.Choose(Updater.FetchFromServer($"127.0.0.1:{server.Port}", download2, cache2), cache2, Path.Combine(_root, "none"), _pub));

        // No server: null, and the loader falls back to the release folder.
        Assert.Null(Updater.FetchFromServer("127.0.0.1:1", download, cache, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void LiveCrosshairsFromTheServerWinWhenNewer()
    {
        var t = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        LivePerson P(string id, double x, int s) => new(id, id, id, "PC", false, x, 0, t.AddSeconds(s));
        var folder = new[] { P("a", 1, 0), P("b", 1, 5) };
        var server = new[] { P("a", 2, 3), P("b", 9, 4), P("c", 3, 6), P("me", 0, 6), P("old", 0, -60) };
        List<LivePerson> all = LiveFolder.Merge(folder, server, "me", t.AddSeconds(7));
        Assert.Equal(new[] { "a", "b", "c" }, all.Select(p => p.Id));
        Assert.Equal(2, all[0].X); // newer from the server
        Assert.Equal(1, all[1].X); // the folder's was newer
        LivePerson? back = LiveFolder.PersonFromJson(LiveFolder.PersonJson(all[2]));
        Assert.Equal(all[2], back with { Editing = null });
    }

    [Fact]
    public void ServerAddressesParse()
    {
        Assert.Equal(("office-pc", 5391), ServerClient.Parse("office-pc"));
        Assert.Equal(("office-pc", 6000), ServerClient.Parse(" office-pc:6000 "));
    }
}
