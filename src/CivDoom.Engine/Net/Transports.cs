using System.Collections.Concurrent;

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
