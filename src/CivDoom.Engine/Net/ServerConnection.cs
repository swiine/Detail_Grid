using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CivDoom.Engine;

/// <summary>
/// A connection to a <see cref="GameServer"/>: the lobby (host, list, join) and then the game's link to the
/// other players. State updates are sent at most one at a time (newer ones replace older ones still waiting).
/// </summary>
public sealed class ServerConnection : IGameTransport
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, string> _states = new();
    private readonly ConcurrentQueue<string> _events = new();
    private readonly ConcurrentQueue<byte[]> _outEvents = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly BlockingCollection<JsonDocument> _replies = new();
    private string? _pendingState;

    private ServerConnection(TcpClient tcp, string playerId)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        PlayerId = playerId;
        _ = Task.Run(ReadLoop);
        _ = Task.Run(SendLoop);
    }

    public string PlayerId { get; }
    public string? Error { get; private set; }

    /// <summary>Connects and introduces this player. Throws if the server can't be reached.</summary>
    public static ServerConnection Connect(string host, int port, string playerId, string name, TimeSpan? timeout = null)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            if (!tcp.ConnectAsync(host, port).Wait(timeout ?? TimeSpan.FromSeconds(8)))
                throw new TimeoutException($"Can't reach the CivDOOM server at {host}:{port}.");
        }
        catch (AggregateException ex) when (ex.InnerException is SocketException se)
        {
            tcp.Dispose();
            throw new IOException($"Can't reach the CivDOOM server at {host}:{port} ({se.Message}).");
        }
        var c = new ServerConnection(tcp, playerId);
        c.Write(new { t = "hello", id = playerId, name });
        c.Expect("welcome");
        return c;
    }

    // ---------------------------------------------------------------- lobby

    /// <summary>Starts a game on the server with this level; returns its id.</summary>
    public string Host(string levelName, byte[] level)
    {
        Write(new { t = "host", level = levelName, data = Convert.ToBase64String(level) });
        using JsonDocument r = Expect("hosted");
        return r.RootElement.GetProperty("game").GetString()!;
    }

    public List<GameListing> ListGames()
    {
        Write(new { t = "list" });
        using JsonDocument r = Expect("games");
        return r.RootElement.GetProperty("games").EnumerateArray().Select(g => new GameListing(
            g.GetProperty("id").GetString()!, g.GetProperty("host").GetString()!, g.GetProperty("level").GetString()!,
            g.GetProperty("players").GetInt32(), g.GetProperty("started").GetDateTimeOffset())).ToList();
    }

    /// <summary>Joins a game: returns the host's level and the host's player id.</summary>
    public (byte[] Level, string HostId) Join(string gameId)
    {
        Write(new { t = "join", game = gameId });
        using JsonDocument r = Expect("joined", TimeSpan.FromSeconds(30));
        return (Convert.FromBase64String(r.RootElement.GetProperty("data").GetString()!), r.RootElement.GetProperty("host").GetString()!);
    }

    private JsonDocument Expect(string type, TimeSpan? timeout = null)
    {
        if (!_replies.TryTake(out JsonDocument? doc, timeout ?? TimeSpan.FromSeconds(10)))
            throw new TimeoutException(Error ?? "The CivDOOM server didn't answer.");
        string t = doc.RootElement.TryGetProperty("t", out JsonElement te) ? te.GetString() ?? "" : "";
        if (t == type) return doc;
        string msg = doc.RootElement.TryGetProperty("msg", out JsonElement m) ? m.GetString() ?? "" : $"unexpected '{t}'";
        doc.Dispose();
        throw new InvalidOperationException(msg);
    }

    // ---------------------------------------------------------------- game link

    public void PublishState(string json)
    {
        Interlocked.Exchange(ref _pendingState, json);
        _wake.Release();
    }

    public void SendEvent(string json)
    {
        _outEvents.Enqueue(Encode(new { t = "e", body = json }));
        _wake.Release();
    }

    public void Receive(List<string> states, List<string> events)
    {
        foreach (string key in _states.Keys.ToList())
            if (_states.TryRemove(key, out string? s)) states.Add(s);
        while (_events.TryDequeue(out string? e)) events.Add(e);
    }

    private async Task ReadLoop()
    {
        var reader = new LineReader(_stream, GameServer.MaxLine);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
                JsonElement root = doc.RootElement;
                string t = root.TryGetProperty("t", out JsonElement te) ? te.GetString() ?? "" : "";
                switch (t)
                {
                    case "s":
                        _states[root.GetProperty("from").GetString() ?? ""] = root.GetProperty("body").GetString() ?? "";
                        doc.Dispose();
                        break;
                    case "e":
                        _events.Enqueue(root.GetProperty("body").GetString() ?? "");
                        doc.Dispose();
                        break;
                    case "ended":
                        // The host left: tell the game the same way the host would have.
                        _events.Enqueue(JsonSerializer.Serialize(new NetEvent
                        {
                            Kind = NetEvent.EndKind, From = "server", Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        }, GameServer.Json));
                        doc.Dispose();
                        break;
                    default:
                        _replies.Add(doc); // a lobby answer (or an error)
                        break;
                }
            }
            Error = "Disconnected from the CivDOOM server";
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
        {
            if (!_cts.IsCancellationRequested) Error = "Lost the connection to the CivDOOM server";
        }
    }

    private async Task SendLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(250), _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            await PumpAsync().ConfigureAwait(false);
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (_outEvents.TryDequeue(out byte[]? e)) await WriteRaw(e).ConfigureAwait(false);
            if (Interlocked.Exchange(ref _pendingState, null) is { } s) await WriteRaw(Encode(new { t = "s", body = s })).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            Error = "Lost the connection to the CivDOOM server";
        }
    }

    private static byte[] Encode(object o) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o, GameServer.Json) + "\n");

    private void Write(object o) => WriteRaw(Encode(o)).GetAwaiter().GetResult();

    private async Task WriteRaw(byte[] bytes)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    public void Flush()
    {
        try { PumpAsync().Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }

    public void Dispose()
    {
        Flush();
        try { Write(new { t = "leave" }); } catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
        _cts.Cancel();
        try { _tcp.Close(); } catch (SocketException) { }
    }
}
