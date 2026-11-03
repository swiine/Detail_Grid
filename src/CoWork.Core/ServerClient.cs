using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoWork.Core;

/// <summary>
/// CoWork's connection to the CoWork server (when cowork.json names one). Keeps itself connected (retrying every few
/// seconds if the server is off), says hello with your licence, and raises events for what the server passes on.
/// Everything still works through the shared folder while it isn't connected - this only makes things quicker.
/// </summary>
public sealed class ServerClient : IDisposable
{
    public sealed record Hello(string User, string Name, string Machine, string Domain, string Licence, string Version);

    private readonly string _host;
    private readonly int _port;
    private readonly Func<Hello> _hello;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _tcp;
    private StreamWriter? _writer;
    private volatile bool _connected;

    /// <summary>Connected and accepted.</summary>
    public bool Connected => _connected;

    /// <summary>Why the server refused us (licence), or null.</summary>
    public string? Denied { get; private set; }

    /// <summary>The connection came up (true) or went down (false).</summary>
    public event Action<bool>? StateChanged;
    /// <summary>Someone changed something: "chat:&lt;room&gt;", "online", "live:&lt;session&gt;", "pins:&lt;room&gt;".</summary>
    public event Action<string>? Poked;
    /// <summary>Someone's live crosshair/locks (session id, their here.json as JSON).</summary>
    public event Action<string, string>? Here;
    /// <summary>Who's connected to the server now.</summary>
    public event Action<string[]>? Online;
    /// <summary>The server refused or dropped your licence.</summary>
    public event Action<string>? Refused;

    public ServerClient(string address, Func<Hello> hello)
    {
        (_host, _port) = Parse(address);
        _hello = hello;
    }

    /// <summary>"pc-name" or "pc-name:5391" (blank port = the default).</summary>
    public static (string Host, int Port) Parse(string address)
    {
        string a = address.Trim();
        int colon = a.LastIndexOf(':');
        if (colon > 0 && int.TryParse(a[(colon + 1)..], out int port)) return (a[..colon], port);
        return (a, CoWorkServer.DefaultPort);
    }

    public void Start() => _ = Run();

    private async Task Run()
    {
        TimeSpan wait = TimeSpan.FromSeconds(2);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient { NoDelay = true };
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await tcp.ConnectAsync(_host, _port, timeout.Token);
                }
                _tcp = tcp;
                _writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { NewLine = "\n" };
                Hello h = _hello();
                await SendRaw(new JsonObject
                {
                    ["t"] = "hello", ["user"] = h.User, ["name"] = h.Name, ["machine"] = h.Machine, ["domain"] = h.Domain,
                    ["licence"] = h.Licence, ["version"] = h.Version,
                });
                using var reader = new StreamReader(tcp.GetStream(), new UTF8Encoding(false));
                while (!_cts.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(_cts.Token);
                    if (line == null) break;
                    if (Handle(line)) wait = TimeSpan.FromSeconds(2);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
            finally
            {
                _writer = null;
                _tcp = null;
                if (_connected) { _connected = false; StateChanged?.Invoke(false); }
            }
            if (_cts.IsCancellationRequested) return;
            // Refused (licence): try much less often - an approval or new key takes a while anyway.
            TimeSpan pause = Denied != null ? TimeSpan.FromMinutes(1) : wait;
            try { await Task.Delay(pause, _cts.Token); } catch (OperationCanceledException) { return; }
            wait = TimeSpan.FromSeconds(Math.Min(30, wait.TotalSeconds * 2));
        }
    }

    /// <summary>Returns true once welcomed.</summary>
    private bool Handle(string line)
    {
        JsonObject? m;
        try { m = JsonNode.Parse(line) as JsonObject; } catch (JsonException) { return false; }
        if (m == null) return false;
        string S(string k) => m[k] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
        try
        {
            switch (S("t"))
            {
                case "welcome":
                    Denied = null;
                    _connected = true;
                    StateChanged?.Invoke(true);
                    return true;
                case "denied":
                    Denied = S("why");
                    Refused?.Invoke(Denied);
                    return false;
                case "poke":
                    Poked?.Invoke(S("topic"));
                    return false;
                case "here":
                    if (m["person"] is JsonNode p) Here?.Invoke(S("session"), p.ToJsonString());
                    return false;
                case "online":
                    Online?.Invoke(m["users"] is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>());
                    return false;
            }
        }
        catch (Exception ex) { Log.Swallowed(ex); }
        return false;
    }

    /// <summary>Tell everyone something changed (no-op while not connected).</summary>
    public void Poke(string topic) => Send(new JsonObject { ["t"] = "poke", ["topic"] = topic });

    /// <summary>Send your live crosshair/locks (here.json contents) to everyone in the session.</summary>
    public void SendHere(string session, string personJson)
    {
        JsonNode? person;
        try { person = JsonNode.Parse(personJson); } catch (JsonException) { return; }
        Send(new JsonObject { ["t"] = "here", ["session"] = session, ["person"] = person });
    }

    private void Send(JsonObject msg)
    {
        if (!_connected) return;
        _ = SendRaw(msg);
    }

    private async Task SendRaw(JsonObject msg)
    {
        StreamWriter? w = _writer;
        if (w == null) return;
        await _writeLock.WaitAsync();
        try
        {
            await w.WriteLineAsync(msg.ToJsonString());
            await w.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            try { _tcp?.Close(); } catch (Exception) { }
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _tcp?.Close(); } catch (Exception) { }
    }
}
