using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoWork.Core;

/// <summary>
/// The optional CoWork server (CoWork-Server.exe). Without it, CoWork works through the shared folder only. With it,
/// everyone's CoWork connects to it and:
/// <list type="bullet">
/// <item>gets told the moment something changes (a chat message, someone coming online, a CoWork batch) instead of
/// re-reading the shared folder every fraction of a second;</item>
/// <item>sees other people's crosshairs and locks live, passed straight through the server;</item>
/// <item>has its licence checked when it connects (and again if you revoke it while they're on);</item>
/// <item>gets updates from it (the files are signed, so the server can't change them).</item>
/// </list>
/// The shared folder stays the record of everything (chats, drawings, sessions), so if the server is off or
/// unreachable CoWork carries on exactly as before. The server stores nothing itself.
/// <para>
/// Protocol: plain TCP, one JSON object per line, field <c>t</c> says what it is. Client: <c>hello</c> (user, name,
/// machine, domain, licence, version) then <c>poke</c> (topic), <c>here</c> (session, person); also, without hello,
/// <c>latest</c> and <c>file</c> (version, name) for updates. Server: <c>welcome</c>, <c>denied</c> (why), <c>poke</c>,
/// <c>here</c>, <c>online</c> (users), <c>latest</c> (version), <c>file</c> (name, data base64) and <c>error</c>.
/// </para>
/// </summary>
public sealed class CoWorkServer : IDisposable
{
    public const int DefaultPort = 5391;
    private const int MaxLine = 48 * 1024 * 1024; // a batch of drawing changes can be several MB
    /// <summary>Biggest batch the server passes on (bigger ones go through the shared folder only).</summary>
    public const int MaxBatch = 32 * 1024 * 1024;
    private const int MaxFile = 64 * 1024 * 1024;

    private readonly TcpListener _listener;
    private readonly Action<string> _log;
    private readonly string? _dataFolder;
    private readonly string? _releaseFolder;
    private readonly string _publicKey;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Client, byte> _clients = new();
    private Timer? _recheck;

    /// <summary>The clock used for licence expiry (tests set it).</summary>
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Today);

    private sealed class Client
    {
        public Client(TcpClient tcp)
        {
            Tcp = tcp;
            Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
        }

        public TcpClient Tcp { get; }
        public StreamWriter Writer { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string User = "", Name = "", Machine = "", Domain = "", Licence = "", Version = "";
        public volatile string Session = ""; // the CoWork session this PC is in (batches only go to its members)
        public LicenseInfo? Info;
        public volatile bool In; // said hello with a good licence
    }

    /// <param name="dataFolder">The CoWork-data folder, to read your revoke lists from (optional).</param>
    /// <param name="releaseFolder">The release folder your admin tool publishes to, to hand out updates (optional).</param>
    public CoWorkServer(int port = DefaultPort, string? dataFolder = null, string? releaseFolder = null, Action<string>? log = null,
        IPAddress? address = null, string publicKey = Licensing.PublicKey)
    {
        if (address == null || address.Equals(IPAddress.Any))
        {
            // Both IPv4 and IPv6, so a guest whose PC looks the host up as either can connect (IPv4 only where IPv6 is off).
            TcpListener? both = null;
            if (Socket.OSSupportsIPv6)
                try
                {
                    both = new TcpListener(IPAddress.IPv6Any, port);
                    both.Server.DualMode = true;
                }
                catch (Exception ex) when (ex is SocketException or NotSupportedException) { both = null; }
            _listener = both ?? new TcpListener(IPAddress.Any, port);
        }
        else _listener = new TcpListener(address, port);
        _dataFolder = string.IsNullOrWhiteSpace(dataFolder) ? null : dataFolder;
        _releaseFolder = string.IsNullOrWhiteSpace(releaseFolder) ? null : releaseFolder;
        _log = log ?? (_ => { });
        _publicKey = publicKey;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Who's connected (people whose licence was accepted).</summary>
    public string[] Online => _clients.Keys.Where(c => c.In).Select(c => c.User).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u).ToArray();

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop();
        _recheck = new Timer(_ => RecheckLicences(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception) when (_cts.IsCancellationRequested) { return; }
            catch (SocketException) { continue; }
            tcp.NoDelay = true;
            var c = new Client(tcp);
            _clients[c] = 0;
            _ = Serve(c);
        }
    }

    private async Task Serve(Client c)
    {
        try
        {
            using var reader = new StreamReader(c.Tcp.GetStream(), new UTF8Encoding(false));
            while (!_cts.IsCancellationRequested)
            {
                string? line = await ReadLine(reader);
                if (line == null) break;
                JsonObject? msg;
                try { msg = JsonNode.Parse(line) as JsonObject; } catch (JsonException) { msg = null; }
                if (msg == null) continue;
                await Handle(c, msg);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException) { }
        finally
        {
            _clients.TryRemove(c, out _);
            try { c.Tcp.Dispose(); } catch (Exception) { }
            if (c.In)
            {
                _log($"{c.Name} ({c.User}) left");
                await Broadcast(OnlineMessage(), null);
            }
        }
    }

    /// <summary>One line, read in big chunks (a batch of drawing changes can be MBs), refusing absurdly long ones.</summary>
    private static async Task<string?> ReadLine(StreamReader reader)
    {
        string? line = await reader.ReadLineAsync();
        if (line != null && line.Length > MaxLine) throw new InvalidOperationException("line too long");
        return line;
    }

    private static string S(JsonObject m, string key) => m[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    private async Task Handle(Client c, JsonObject m)
    {
        switch (S(m, "t"))
        {
            case "hello":
                c.User = S(m, "user"); c.Name = S(m, "name"); c.Machine = S(m, "machine"); c.Domain = S(m, "domain");
                c.Licence = S(m, "licence"); c.Version = S(m, "version");
                if (CheckLicence(c) is { } why)
                {
                    _log($"Refused {c.User} on {c.Machine}: {why}");
                    await Send(c, new JsonObject { ["t"] = "denied", ["why"] = why });
                    c.Tcp.Close();
                    return;
                }
                c.In = true;
                _log($"{c.Name} ({c.User}) on {c.Machine} joined - CoWork {c.Version}");
                await Send(c, new JsonObject { ["t"] = "welcome", ["server"] = typeof(CoWorkServer).Assembly.GetName().Version?.ToString() ?? "" });
                await Broadcast(OnlineMessage(), null);
                return;
            case "poke" when c.In:
                await Broadcast(new JsonObject { ["t"] = "poke", ["topic"] = S(m, "topic"), ["from"] = c.User }, c);
                return;
            case "session" when c.In:
                c.Session = S(m, "id");
                return;
            case "batch" when c.In:
                // A set of drawing changes: only to the others in the same session.
                if (S(m, "session").Length == 0) return;
                await BroadcastTo(m.ToJsonString(), c, x => x.Session == S(m, "session"));
                return;
            case "typing" when c.In:
                await Broadcast(new JsonObject { ["t"] = "typing", ["room"] = S(m, "room"), ["user"] = c.User, ["name"] = c.Name }, c);
                return;
            case "here" when c.In:
                await Broadcast(new JsonObject { ["t"] = "here", ["session"] = S(m, "session"), ["person"] = m["person"]?.DeepClone() }, c);
                return;
            case "latest":
                await Send(c, new JsonObject { ["t"] = "latest", ["version"] = LatestVersion() ?? "" });
                return;
            case "file":
                await SendFile(c, S(m, "version"), S(m, "name"));
                return;
        }
    }

    // ------------------------------------------------------------------ licences

    private string? CheckLicence(Client c)
    {
        var (state, info) = Licensing.Check(c.Licence, c.User, c.Domain, c.Machine, Today(), _publicKey);
        if (state == LicenseState.Valid && info != null && Revoked().Revokes(info)) state = LicenseState.Revoked;
        c.Info = info;
        return state switch
        {
            LicenseState.Valid => null,
            LicenseState.Missing => "no licence",
            LicenseState.Revoked => "licence revoked",
            LicenseState.Expired => "licence expired",
            LicenseState.NotForThisUser => "licence isn't for this login",
            LicenseState.NotForThisPc => "licence isn't for this PC",
            _ => "licence isn't genuine",
        };
    }

    private Licensing.RevokedSet Revoked()
    {
        if (_dataFolder == null) return Licensing.RevokedSet.None;
        var texts = new List<string?>();
        try
        {
            string dir = Licensing.Folder(_dataFolder);
            if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir, "revoked*.key")) texts.Add(File.ReadAllText(f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Licensing.RevokedAll(texts, _publicKey);
    }

    /// <summary>Every minute: anyone whose licence has been revoked (or has run out) since they connected is let go.</summary>
    public void RecheckLicences()
    {
        foreach (Client c in _clients.Keys.Where(c => c.In).ToArray())
            if (CheckLicence(c) is { } why)
            {
                _log($"Disconnected {c.User}: {why}");
                c.In = false;
                _ = Send(c, new JsonObject { ["t"] = "denied", ["why"] = why }).ContinueWith(_ => c.Tcp.Close());
            }
    }

    // ------------------------------------------------------------------ updates

    private string? LatestVersion()
    {
        try
        {
            string f = _releaseFolder == null ? "" : Path.Combine(_releaseFolder, "latest.txt");
            return File.Exists(f) && System.Version.TryParse(File.ReadAllText(f).Trim(), out System.Version? v) ? v.ToString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A file from a published release - only names that release's manifest lists (or the manifest itself).</summary>
    private async Task SendFile(Client c, string version, string name)
    {
        string? path = ReleaseFile(version, name);
        if (path == null) { await Send(c, new JsonObject { ["t"] = "error", ["why"] = "no such file" }); return; }
        byte[] data = await File.ReadAllBytesAsync(path);
        await Send(c, new JsonObject { ["t"] = "file", ["name"] = name, ["data"] = Convert.ToBase64String(data) });
    }

    private string? ReleaseFile(string version, string name)
    {
        if (_releaseFolder == null || !System.Version.TryParse(version, out System.Version? v) || v.ToString() != version) return null;
        if (name.Length == 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.Contains("..")) return null;
        string dir = Path.Combine(_releaseFolder, version);
        string manifest = Path.Combine(dir, "manifest.json");
        try
        {
            if (!File.Exists(manifest)) return null;
            bool listed = name is "manifest.json" or "manifest.sig";
            if (!listed)
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    if (string.Equals(p.Name, "files", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement f in p.Value.EnumerateArray())
                            foreach (JsonProperty fp in f.EnumerateObject())
                                if (string.Equals(fp.Name, "name", StringComparison.OrdinalIgnoreCase) && string.Equals(fp.Value.GetString(), name, StringComparison.Ordinal))
                                    listed = true;
            }
            string path = Path.Combine(dir, name);
            return listed && File.Exists(path) && new FileInfo(path).Length <= MaxFile ? path : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    // ------------------------------------------------------------------ sending

    private JsonObject OnlineMessage() => new() { ["t"] = "online", ["users"] = new JsonArray(Online.Select(u => (JsonNode)u!).ToArray()) };

    private async Task BroadcastTo(string line, Client except, Func<Client, bool> who)
    {
        foreach (Client c in _clients.Keys)
            if (c != except && c.In && who(c)) await SendLine(c, line);
    }

    private async Task Broadcast(JsonObject msg, Client? except)
    {
        string line = msg.ToJsonString();
        foreach (Client c in _clients.Keys)
            if (c != except && c.In) await SendLine(c, line);
    }

    private static Task Send(Client c, JsonObject msg) => SendLine(c, msg.ToJsonString());

    private static async Task SendLine(Client c, string line)
    {
        try
        {
            await c.WriteLock.WaitAsync();
            try
            {
                await c.Writer.WriteLineAsync(line);
                await c.Writer.FlushAsync();
            }
            finally { c.WriteLock.Release(); }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            try { c.Tcp.Close(); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _recheck?.Dispose();
        try { _listener.Stop(); } catch (Exception) { }
        foreach (Client c in _clients.Keys) try { c.Tcp.Dispose(); } catch (Exception) { }
    }
}
