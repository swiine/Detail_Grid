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

    private readonly IGameTransport _transport;
    private readonly List<string> _states = new(), _events = new();
    private readonly long _joinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly string? _hostId;
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
            ? $"Hosting a co-op game ({gameId}). Others join with CIVDOOMJOIN."
            : $"Joined {game.Level.Name}. Monsters are shared - work together!");
    }

    public Game Game { get; }
    public string GameId { get; }
    public NetRole Role { get; }

    /// <summary>A connection problem to show, or null when all is well.</summary>
    public string? Problem => _transport.Error;

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
            Game.ApplyEvent(e);
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

    /// <summary>Raised once the session has closed (an embedded server shuts down with it).</summary>
    public event Action? Ended;
}
