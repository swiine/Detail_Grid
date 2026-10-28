using System.Text.Json;

namespace CivDoom.Engine;

/// <summary>
/// Connects a <see cref="Game"/> to the other players: call <see cref="Tick"/> after each
/// <see cref="Game.Update"/>. Sends this player's state 15 times a second (the host's includes the world)
/// and applies what arrives from everyone else.
/// </summary>
public sealed class NetSession : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>State updates per second.</summary>
    public const double SendRate = 15;

    /// <summary>Level chunk size for the chat server (its messages are capped at 256 KB).</summary>
    private const int ChunkSize = 120_000;

    private readonly IGameTransport _transport;
    private readonly List<string> _states = new(), _events = new();
    private readonly long _joinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly string? _hostId;
    private byte[]? _levelBytes;
    private double _sendTimer, _hostSilence;
    private long _stateSeq, _eventSeq;
    private bool _disposed;

    public NetSession(Game game, IGameTransport transport, NetRole role, string gameId, string playerId, string playerName, string? hostId = null)
    {
        Game = game;
        GameId = gameId;
        Role = role;
        _transport = transport;
        _hostId = hostId;
        game.Role = role;
        game.LocalId = playerId;
        game.LocalName = playerName;
        SendEvent(new NetEvent { Kind = NetEvent.JoinKind, Text = playerName });
        game.Say(role == NetRole.Host
            ? $"Hosting a co-op game ({gameId}). Others can join from TTW Chat or with CIVDOOMJOIN."
            : $"Joined {game.Level.Name}. Monsters are shared - work together!");
    }

    public Game Game { get; }
    public string GameId { get; }
    public NetRole Role { get; }

    /// <summary>A connection problem to show, or null when all is well.</summary>
    public string? Problem => _transport.Error;

    /// <summary>The host: keep the level ready for players who join through the chat server.</summary>
    internal void ServeLevel(byte[] levelBytes) => _levelBytes = levelBytes;

    public void Tick(double dt)
    {
        if (_disposed) return;
        _states.Clear();
        _events.Clear();
        _transport.Receive(_states, _events);
        foreach (string json in _states)
        {
            PlayerState? s = Parse<PlayerState>(json);
            if (s == null) continue;
            if (s.Id == _hostId) _hostSilence = 0;
            Game.ApplyPlayer(s);
        }
        foreach (string json in _events)
        {
            NetEvent? e = Parse<NetEvent>(json);
            if (e == null || e.Ts < _joinedAt - 5000) continue; // from before we arrived
            if (e.Kind == NetEvent.NeedLevelKind && Role == NetRole.Host) SendLevel(e.From);
            else if (e.Kind != NetEvent.LevelKind) Game.ApplyEvent(e);
        }

        if (Role == NetRole.Client && !Game.HostLeft)
        {
            _hostSilence += dt;
            if (_hostSilence > 15)
            {
                Game.HostLeft = true;
                Game.Say("Lost the host - the game has stopped. Press Esc to leave.");
            }
        }

        foreach (NetEvent e in Game.Outbox) SendEvent(e);
        Game.Outbox.Clear();

        _sendTimer += dt;
        if (_sendTimer >= 1 / SendRate)
        {
            _sendTimer = 0;
            _transport.PublishState(JsonSerializer.Serialize(Game.CapturePlayer(++_stateSeq), Json));
        }
    }

    private void SendEvent(NetEvent e)
    {
        e.Seq = ++_eventSeq;
        e.From = Game.LocalId;
        e.Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _transport.SendEvent(JsonSerializer.Serialize(e, Json));
    }

    /// <summary>Sends the level, in pieces, to a player who joined through the chat server.</summary>
    private void SendLevel(string to)
    {
        if (_levelBytes == null) return;
        string b64 = Convert.ToBase64String(_levelBytes);
        int parts = (b64.Length + ChunkSize - 1) / ChunkSize;
        for (int i = 0; i < parts; i++)
            SendEvent(new NetEvent { Kind = NetEvent.LevelKind, To = to, Index = i, Amount = parts, Text = b64.Substring(i * ChunkSize, Math.Min(ChunkSize, b64.Length - i * ChunkSize)) });
    }

    private static T? Parse<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Says goodbye (the host's leaving ends the game for everyone).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SendEvent(new NetEvent { Kind = Role == NetRole.Host ? NetEvent.EndKind : NetEvent.LeaveKind, Text = Game.LocalName });
        _transport.Flush();
        _transport.Dispose();
        Ended?.Invoke();
    }

    /// <summary>Raised once the session has closed (the lobby marks a hosted game as ended).</summary>
    internal event Action? Ended;
}
