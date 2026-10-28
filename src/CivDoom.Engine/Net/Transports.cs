using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CivDoom.Engine;

/// <summary>
/// Carries a multiplayer game's messages. Two kinds: the latest state of each player (only the newest
/// matters, sent many times a second) and events (each one delivered, in order).
/// </summary>
public interface IGameTransport : IDisposable
{
    /// <summary>Replaces this player's published state.</summary>
    void PublishState(string json);

    /// <summary>Queues an event for everyone.</summary>
    void SendEvent(string json);

    /// <summary>Moves what has arrived since the last call into the lists (newest state per player, then events).</summary>
    void Receive(List<string> states, List<string> events);

    /// <summary>Why messages aren't getting through, or null.</summary>
    string? Error { get; }

    /// <summary>Sends anything still queued (called before closing).</summary>
    void Flush();
}

/// <summary>Both ends in one process: for tests and for trying multiplayer on one PC.</summary>
public sealed class MemoryHub
{
    private readonly object _lock = new();
    private readonly List<MemoryTransport> _members = new();

    public MemoryTransport Connect(string id)
    {
        var t = new MemoryTransport(this, id);
        lock (_lock) _members.Add(t);
        return t;
    }

    internal void Broadcast(MemoryTransport from, bool state, string json)
    {
        lock (_lock)
            foreach (MemoryTransport m in _members)
                if (m != from) m.Deliver(from.Id, state, json);
    }

    internal void Leave(MemoryTransport t)
    {
        lock (_lock) _members.Remove(t);
    }
}

public sealed class MemoryTransport : IGameTransport
{
    private readonly MemoryHub _hub;
    private readonly ConcurrentDictionary<string, string> _states = new();
    private readonly ConcurrentQueue<string> _events = new();

    internal MemoryTransport(MemoryHub hub, string id)
    {
        _hub = hub;
        Id = id;
    }

    public string Id { get; }
    public string? Error => null;

    public void PublishState(string json) => _hub.Broadcast(this, true, json);
    public void SendEvent(string json) => _hub.Broadcast(this, false, json);

    internal void Deliver(string from, bool state, string json)
    {
        if (state) _states[from] = json;
        else _events.Enqueue(json);
    }

    public void Receive(List<string> states, List<string> events)
    {
        foreach (string key in _states.Keys.ToList())
            if (_states.TryRemove(key, out string? s)) states.Add(s);
        while (_events.TryDequeue(out string? e)) events.Add(e);
    }

    public void Flush() { }
    public void Dispose() => _hub.Leave(this);
}

/// <summary>
/// Multiplayer over a shared network folder, the same way DoomChat (and TTW Chat) work without a server:
/// <code>
///   doom\&lt;game&gt;\session.json             what the game is, who hosts it, whether it has ended
///   doom\&lt;game&gt;\level.json.gz            the level (for players joining)
///   doom\&lt;game&gt;\&lt;player&gt;\state.json     each player's latest state, replaced several times a second
///   doom\&lt;game&gt;\&lt;player&gt;\events.jsonl   each player's events, appended (nobody writes anyone else's files)
/// </code>
/// A background thread does the file work so a slow network never stalls the game.
/// </summary>
public sealed class FolderTransport : IGameTransport
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(60);

    private readonly string _gameDir, _myDir, _id;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, string> _states = new();
    private readonly ConcurrentQueue<string> _incoming = new();
    private readonly ConcurrentQueue<string> _outgoing = new();
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _stateTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private string? _pendingState;
    private string[] _peers = Array.Empty<string>();
    private DateTime _lastScan = DateTime.MinValue;

    public FolderTransport(string gameDir, string id)
    {
        _gameDir = gameDir;
        _id = id;
        _myDir = Path.Combine(gameDir, id);
        Directory.CreateDirectory(_myDir);
        _thread = new Thread(Run) { IsBackground = true, Name = "CivDoom folder sync" };
        _thread.Start();
    }

    public string? Error { get; private set; }

    public void PublishState(string json)
    {
        lock (_lock) _pendingState = json;
    }

    public void SendEvent(string json) => _outgoing.Enqueue(json);

    public void Receive(List<string> states, List<string> events)
    {
        foreach (string key in _states.Keys.ToList())
            if (_states.TryRemove(key, out string? s)) states.Add(s);
        while (_incoming.TryDequeue(out string? e)) events.Add(e);
    }

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                WriteMine();
                ReadOthers();
                Error = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error = ex is UnauthorizedAccessException ? "No permission to use the game folder" : ex.Message;
            }
            _cts.Token.WaitHandle.WaitOne(PollInterval);
        }
    }

    private void WriteMine()
    {
        string? state;
        lock (_lock) { state = _pendingState; _pendingState = null; }
        if (state != null)
        {
            // Write then swap in, so readers never see half a file.
            string tmp = Path.Combine(_myDir, "state.tmp"), final = Path.Combine(_myDir, "state.json");
            File.WriteAllText(tmp, state);
            Retry(() => File.Move(tmp, final, overwrite: true));
        }
        if (_outgoing.IsEmpty) return;
        var sb = new StringBuilder();
        while (_outgoing.TryDequeue(out string? e)) sb.Append(e).Append('\n');
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        Retry(() =>
        {
            using var fs = new FileStream(Path.Combine(_myDir, "events.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(bytes, 0, bytes.Length);
        });
    }

    private static void Retry(Action a)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { a(); return; }
            catch (IOException) when (attempt < 4) { Thread.Sleep(15); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(15); } // replacing a file someone is reading
        }
    }

    private void ReadOthers()
    {
        if (DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(1))
        {
            _lastScan = DateTime.UtcNow;
            _peers = Directory.GetDirectories(_gameDir).Select(Path.GetFileName).Where(n => n != null && n != _id).ToArray()!;
        }
        foreach (string peer in _peers)
        {
            string dir = Path.Combine(_gameDir, peer);
            ReadState(peer, Path.Combine(dir, "state.json"));
            ReadEvents(Path.Combine(dir, "events.jsonl"));
        }
    }

    private void ReadState(string peer, string file)
    {
        DateTime written;
        try { written = File.GetLastWriteTimeUtc(file); } catch (IOException) { return; }
        if (written.Year < 2000 || (_stateTimes.TryGetValue(peer, out DateTime seen) && seen == written)) return;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            string text = sr.ReadToEnd();
            if (text.Length == 0 || text[^1] != '}') return; // caught mid-write
            _states[peer] = text;
            _stateTimes[peer] = written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads whole lines added since last time (a half-written line waits for the next poll).</summary>
    private void ReadEvents(string file)
    {
        long known = _offsets.TryGetValue(file, out long o) ? o : 0;
        long size;
        try { size = new FileInfo(file).Length; } catch (IOException) { return; }
        if (size <= known) { if (size < known) _offsets[file] = 0; return; }
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(known, SeekOrigin.Begin);
            var buf = new byte[size - known];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            int lastNl = read == 0 ? -1 : Array.LastIndexOf(buf, (byte)'\n', read - 1);
            if (lastNl < 0) return;
            foreach (string line in Encoding.UTF8.GetString(buf, 0, lastNl + 1).Split('\n'))
                if (line.Trim().Length > 0) _incoming.Enqueue(line.Trim());
            _offsets[file] = known + lastNl + 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Flush()
    {
        try { WriteMine(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(1));
        Flush();
    }
}

/// <summary>
/// Multiplayer through the TTW Chat server (instant delivery): players join the chat channel
/// "doom-&lt;game&gt;" and the server relays each message to everyone else in it, without storing anything.
/// </summary>
public sealed class RelayTransport : IGameTransport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Uri _uri;
    private readonly string _channel, _id, _user, _display, _machine;
    private readonly ConcurrentDictionary<string, string> _states = new();
    private readonly ConcurrentQueue<string> _incoming = new();
    private readonly ConcurrentQueue<string> _outEvents = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _cts = new();
    private string? _pendingState;
    private ClientWebSocket? _ws;

    public RelayTransport(string serverUrl, string gameId, string id, string user, string display, string machine)
    {
        _uri = new Uri(serverUrl);
        _channel = "doom-" + gameId;
        _id = id;
        _user = user; _display = display; _machine = machine;
        _ = Task.Run(RunAsync);
        _ = Task.Run(SendLoopAsync);
    }

    public string? Error { get; private set; } = "Connecting...";

    /// <summary>True once connected and in the game's channel.</summary>
    public bool Connected => _ws?.State == WebSocketState.Open && Error == null;

    public void PublishState(string json)
    {
        Interlocked.Exchange(ref _pendingState, json);
        _wake.Release();
    }

    public void SendEvent(string json)
    {
        _outEvents.Enqueue(json);
        _wake.Release();
    }

    public void Receive(List<string> states, List<string> events)
    {
        foreach (string key in _states.Keys.ToList())
            if (_states.TryRemove(key, out string? s)) states.Add(s);
        while (_incoming.TryDequeue(out string? e)) events.Add(e);
    }

    private async Task RunAsync()
    {
        int delay = 1;
        while (!_cts.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await ws.ConnectAsync(_uri, timeout.Token).ConfigureAwait(false);
                }
                _ws = ws;
                await SendRaw(new { type = "hello", user = _user, display = _display, machine = _machine }).ConfigureAwait(false);
                await SendRaw(new { type = "join", channel = _channel }).ConfigureAwait(false);
                Error = null;
                delay = 1;
                _wake.Release();
                await ReceiveLoop(ws).ConfigureAwait(false);
                Error = "Disconnected from the chat server";
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Error = ex is OperationCanceledException ? "Can't reach the chat server" : ex.Message;
            }
            catch { }
            finally { _ws = null; }
            if (_cts.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token).ConfigureAwait(false); } catch { break; }
            delay = Math.Min(delay * 2, 15);
        }
    }

    private async Task ReceiveLoop(ClientWebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);
            try
            {
                using JsonDocument doc = JsonDocument.Parse(ms.ToArray());
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("type", out JsonElement t) && t.GetString() == "relay"
                    && root.TryGetProperty("body", out JsonElement body) && body.GetString() is { } text)
                {
                    string from = root.TryGetProperty("from", out JsonElement f) ? f.GetString() ?? "" : "";
                    if (from == _id) continue;
                    if (root.TryGetProperty("k", out JsonElement k) && k.GetString() == "s") _states[from] = text;
                    else _incoming.Enqueue(text);
                }
            }
            catch (JsonException) { }
        }
    }

    private async Task SendLoopAsync()
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
        if (_ws?.State != WebSocketState.Open || Error != null) return;
        while (_outEvents.TryPeek(out string? e))
        {
            if (!await SendRaw(new { type = "relay", channel = _channel, k = "e", from = _id, body = e }).ConfigureAwait(false)) return;
            _outEvents.TryDequeue(out _);
        }
        if (Interlocked.Exchange(ref _pendingState, null) is { } s)
            await SendRaw(new { type = "relay", channel = _channel, k = "s", from = _id, body = s }).ConfigureAwait(false);
    }

    private async Task<bool> SendRaw(object payload)
    {
        ClientWebSocket? ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return false;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
        finally { _sendLock.Release(); }
    }

    public void Flush()
    {
        try { PumpAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    public void Dispose()
    {
        Flush();
        _cts.Cancel();
        try { _ws?.Abort(); } catch { }
    }
}
