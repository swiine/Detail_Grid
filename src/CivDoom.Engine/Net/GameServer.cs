using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CivDoom.Engine;

/// <summary>
/// The CivDOOM game server: a small lobby and relay for co-op games. Run it on any PC everyone can reach
/// (CivDoom.Server.exe), or let a host run one inside Civil 3D. It keeps a list of games, hands each joining
/// player the host's level, and passes every player's updates on to the others in the same game. It never
/// runs the game itself (the host's Civil 3D does) and stores nothing on disk.
/// <para>
/// Protocol: plain TCP, one JSON object per line. A client says <c>hello</c>, then <c>host</c> / <c>list</c> /
/// <c>join</c>; in a game it sends <c>s</c> (its latest state) and <c>e</c> (events), which go to everyone else
/// in the game as <c>{t, from, body}</c>. When the host leaves, the others get <c>ended</c>.
/// </para>
/// </summary>
public sealed class GameServer : IDisposable
{
    /// <summary>The port CivDOOM uses unless told otherwise.</summary>
    public const int DefaultPort = 5390;

    /// <summary>Longest message accepted (levels from big drawings are a few MB).</summary>
    public const int MaxLine = 64 * 1024 * 1024;

    private const int MaxGames = 100;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TcpListener _listener;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Client, byte> _clients = new();
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

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
        public string Id = "";
        public string Name = "";
        public Room? Room;
        public bool Played; // hosted or joined a game (only those are logged)
    }

    private sealed class Room
    {
        public string Id = "";
        public string HostId = "";
        public string HostName = "";
        public string Level = "";
        public string Data = ""; // the level, base64 (gzipped JSON)
        public DateTimeOffset Started = DateTimeOffset.UtcNow;
        public readonly List<Client> Members = new();
    }

    public GameServer(int port = DefaultPort, Action<string>? log = null, IPAddress? address = null)
    {
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _log = log ?? (_ => { });
    }

    /// <summary>The port actually listened on (useful when started with port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int GameCount => _rooms.Count;
    public int ClientCount => _clients.Count;

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _log($"CivDOOM server listening on port {Port}.");
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
            catch { break; }
            tcp.NoDelay = true;
            var c = new Client(tcp);
            _clients[c] = 0;
            _ = Task.Run(() => RunClient(c));
        }
    }

    private async Task RunClient(Client c)
    {
        string endpoint = c.Tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            var reader = new LineReader(c.Tcp.GetStream(), MaxLine);
            while (!_cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;
                try { await Handle(c, line).ConfigureAwait(false); }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    await Send(c, new { t = "error", msg = "Bad message" }).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException) { }
        finally
        {
            _clients.TryRemove(c, out _);
            await Leave(c).ConfigureAwait(false);
            try { c.Tcp.Close(); } catch (SocketException) { }
            if (c.Played) _log($"{c.Name} ({endpoint}) disconnected.");
        }
    }

    private static string Str(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String) return "";
        string s = v.GetString()!.Trim();
        return s.Length > max ? s[..max] : s;
    }

    private async Task Handle(Client c, string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        string type = Str(root, "t", 16);

        if (type == "hello")
        {
            c.Id = Str(root, "id", 64);
            c.Name = Str(root, "name", 64) is { Length: > 0 } n ? n : c.Id;
            if (c.Id.Length == 0) { await Send(c, new { t = "error", msg = "No player id" }).ConfigureAwait(false); return; }
            await Send(c, new { t = "welcome", version = 1 }).ConfigureAwait(false);
            return;
        }
        if (c.Id.Length == 0) return; // must say hello first

        switch (type)
        {
            case "list":
                await Send(c, new
                {
                    t = "games",
                    games = _rooms.Values.OrderByDescending(r => r.Started).Select(r => new
                    {
                        id = r.Id, host = r.HostName, level = r.Level, players = r.Members.Count, started = r.Started,
                    }).ToArray(),
                }).ConfigureAwait(false);
                break;

            case "host":
            {
                if (_rooms.Count >= MaxGames) { await Send(c, new { t = "error", msg = "The server has too many games running" }).ConfigureAwait(false); return; }
                await Leave(c).ConfigureAwait(false);
                var room = new Room
                {
                    Id = NewGameId(),
                    HostId = c.Id,
                    HostName = c.Name,
                    Level = Str(root, "level", 120),
                    Data = root.GetProperty("data").GetString() ?? "",
                };
                Convert.FromBase64String(room.Data); // reject garbage early
                lock (room.Members) room.Members.Add(c);
                c.Room = room;
                c.Played = true;
                _rooms[room.Id] = room;
                _log($"{c.Name} is hosting game {room.Id}: {room.Level}.");
                await Send(c, new { t = "hosted", game = room.Id }).ConfigureAwait(false);
                break;
            }

            case "join":
            {
                string id = Str(root, "game", 16).ToLowerInvariant();
                if (!_rooms.TryGetValue(id, out Room? room))
                {
                    await Send(c, new { t = "error", msg = $"No game {id} on this server (it may have ended)" }).ConfigureAwait(false);
                    return;
                }
                await Leave(c).ConfigureAwait(false);
                lock (room.Members) room.Members.Add(c);
                c.Room = room;
                c.Played = true;
                _log($"{c.Name} joined game {room.Id}.");
                await Send(c, new { t = "joined", game = room.Id, host = room.HostId, data = room.Data }).ConfigureAwait(false);
                break;
            }

            case "s":
            case "e":
            {
                Room? room = c.Room;
                if (room == null || !root.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.String) return;
                byte[] bytes = Encode(new { t = type, from = c.Id, body = body.GetString() });
                Client[] others;
                lock (room.Members) others = room.Members.Where(m => m != c).ToArray();
                await Task.WhenAll(others.Select(o => SendRaw(o, bytes))).ConfigureAwait(false);
                break;
            }

            case "leave":
                await Leave(c).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Takes a player out of their game; if they were hosting, the game ends for everyone.</summary>
    private async Task Leave(Client c)
    {
        Room? room = c.Room;
        if (room == null) return;
        c.Room = null;
        Client[] rest;
        lock (room.Members)
        {
            room.Members.Remove(c);
            rest = room.Members.ToArray();
        }
        if (room.HostId != c.Id) return;
        _rooms.TryRemove(room.Id, out _);
        _log($"Game {room.Id} ended (the host left).");
        byte[] ended = Encode(new { t = "ended", game = room.Id });
        foreach (Client o in rest) o.Room = null;
        await Task.WhenAll(rest.Select(o => SendRaw(o, ended))).ConfigureAwait(false);
    }

    private static string NewGameId() => Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 3).ToLowerInvariant();

    private static byte[] Encode(object o) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o, Json) + "\n");

    private static Task Send(Client c, object o) => SendRaw(c, Encode(o));

    private static async Task SendRaw(Client c, byte[] bytes)
    {
        await c.WriteLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await c.Writer.BaseStream.WriteAsync(bytes).ConfigureAwait(false);
            await c.Writer.BaseStream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
        finally { c.WriteLock.Release(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        foreach (Client c in _clients.Keys)
            try { c.Tcp.Close(); } catch (SocketException) { }
    }
}
