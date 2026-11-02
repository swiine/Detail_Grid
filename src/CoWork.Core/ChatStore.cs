using System.Text;
using System.Text.Json;

namespace CoWork.Core;

/// <summary>
/// CoWork's messages, kept in a shared folder:
/// <code>
///   rooms\&lt;room&gt;\&lt;person&gt;\&lt;yyyy-MM&gt;.jsonl   that person's messages in that room, one per line (appended)
///   online\&lt;person&gt;.json                       "I'm here" note, rewritten every 20 seconds
/// </code>
/// Everyone writes only their own files, so nobody ever overwrites anyone else; reading picks up what others
/// appended since last time. Thread-safe.
/// </summary>
public sealed class ChatStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _lock = new();
    private readonly Dictionary<string, long> _read = new(StringComparer.OrdinalIgnoreCase); // file -> bytes read
    private readonly string _folder, _me;

    public ChatStore(string folder, string user, string name, string machine)
    {
        _folder = folder;
        User = user;
        Name = name;
        Machine = machine;
        _me = FileName(user);
    }

    public string User { get; }
    public string Name { get; set; }
    public string Machine { get; }

    private string RoomDir(string room) => Path.Combine(_folder, "rooms", room);
    private string OnlineDir => Path.Combine(_folder, "online");

    private static string FileName(string user)
    {
        string s = new(user.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        return s.Length == 0 ? "_" : s;
    }

    private static string Month(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM");

    /// <summary>Adds a message (to this person's own file for the room and month).</summary>
    public ChatMessage Post(string room, string text, string? drawing = null, MessageButton? button = null) =>
        Append(new ChatMessage(Guid.NewGuid().ToString("N"), room, User, Name, DateTimeOffset.UtcNow, text.Trim(), drawing, button));

    /// <summary>Changes the text of one of your own messages (others see "(edited)").</summary>
    public ChatMessage Edit(string room, string messageId, string text) =>
        Append(new ChatMessage(Guid.NewGuid().ToString("N"), room, User, Name, DateTimeOffset.UtcNow, text.Trim(), Action: "edit", Ref: messageId));

    /// <summary>Removes one of your own messages.</summary>
    public ChatMessage Delete(string room, string messageId) =>
        Append(new ChatMessage(Guid.NewGuid().ToString("N"), room, User, Name, DateTimeOffset.UtcNow, "", Action: "delete", Ref: messageId));

    private string PinFile(string room) => Path.Combine(RoomDir(room), "_pins", _me + ".jsonl");

    /// <summary>Pins a message to the top of the room for everyone (or unpins it).</summary>
    public PinEvent Pin(string room, ChatMessage m, bool pinned)
    {
        if (!Rooms.IsValid(room)) throw new ArgumentException("Bad room name", nameof(room));
        var e = new PinEvent(m.Id, pinned, Name, DateTimeOffset.UtcNow, m.Name, m.Text, m.Time);
        string f = PinFile(room);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.AppendAllText(f, JsonSerializer.Serialize(e, Json) + "\n");
        return e;
    }

    /// <summary>What's pinned in a room now (everyone's pins and unpins, latest wins).</summary>
    public List<PinEvent> Pins(string room)
    {
        string dir = Path.Combine(RoomDir(room), "_pins");
        var events = new List<PinEvent>();
        if (!Directory.Exists(dir)) return events;
        foreach (string f in Directory.GetFiles(dir, "*.jsonl"))
        {
            string[] lines;
            try { lines = File.ReadAllLines(f); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string line in lines)
                try { if (line.Trim().Length > 0 && JsonSerializer.Deserialize<PinEvent>(line, Json) is { } e) events.Add(e); }
                catch (JsonException) { }
        }
        return ChatView.Pinned(events);
    }

    private ChatMessage Append(ChatMessage msg)
    {
        if (!Rooms.IsValid(msg.Room)) throw new ArgumentException("Bad room name", nameof(msg));
        DateTimeOffset now = msg.Time;
        string room = msg.Room;
        string dir = Path.Combine(RoomDir(room), _me);
        Directory.CreateDirectory(dir);
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, Json) + "\n");
        string file = Path.Combine(dir, Month(now) + ".jsonl");
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                fs.Write(line, 0, line.Length);
                return msg;
            }
            catch (IOException) when (attempt < 5) { Thread.Sleep(150); }
        }
    }

    /// <summary>The latest messages in a room (this month and last), oldest first. Later <see cref="ReadNew"/> calls
    /// continue from here.</summary>
    public List<ChatMessage> History(string room, int max = 200)
    {
        var all = new List<ChatMessage>();
        foreach (string file in RoomFiles(room))
        {
            long end = ReadLines(file, 0, all);
            lock (_lock) _read[file] = end;
        }
        // Edits and deletes are applied here; later ones arrive through ReadNew.
        return ChatView.Build(all).TakeLast(max).ToList();
    }

    /// <summary>Messages added to a room since the last call (or since <see cref="History"/>).</summary>
    public List<ChatMessage> ReadNew(string room)
    {
        var fresh = new List<ChatMessage>();
        foreach (string file in RoomFiles(room))
        {
            long from;
            lock (_lock) from = _read.TryGetValue(file, out long r) ? r : 0;
            long size;
            try { size = new FileInfo(file).Length; } catch (IOException) { continue; }
            if (size == from) continue;
            if (size < from) from = 0; // the file was replaced
            long end = ReadLines(file, from, fresh);
            lock (_lock) _read[file] = end;
        }
        return fresh.OrderBy(m => m.Time).ToList();
    }

    /// <summary>This month's and last month's files for everyone in the room.</summary>
    private IEnumerable<string> RoomFiles(string room)
    {
        string dir = RoomDir(room);
        if (!Directory.Exists(dir)) yield break;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string[] months = { Month(now.AddMonths(-1)), Month(now) };
        foreach (string person in Directory.GetDirectories(dir))
            foreach (string m in months)
            {
                string f = Path.Combine(person, m + ".jsonl");
                if (File.Exists(f)) yield return f;
            }
    }

    /// <summary>Reads whole lines from <paramref name="from"/>; a half-written last line waits for next time.</summary>
    private static long ReadLines(string file, long from, List<ChatMessage> into)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (from > fs.Length) from = 0;
            fs.Seek(from, SeekOrigin.Begin);
            var buf = new byte[fs.Length - from];
            int got = 0;
            while (got < buf.Length)
            {
                int n = fs.Read(buf, got, buf.Length - got);
                if (n <= 0) break;
                got += n;
            }
            int last = got == 0 ? -1 : Array.LastIndexOf(buf, (byte)'\n', got - 1);
            if (last < 0) return from;
            foreach (string line in Encoding.UTF8.GetString(buf, 0, last + 1).Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                try { if (JsonSerializer.Deserialize<ChatMessage>(line, Json) is { } m) into.Add(m); }
                catch (JsonException) { }
            }
            return from + last + 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return from; }
    }

    /// <summary>Private rooms someone has started with this person (so they show up without being opened first).</summary>
    public List<string> PrivateRoomsForMe()
    {
        string dir = Path.Combine(_folder, "rooms");
        if (!Directory.Exists(dir)) return new();
        return Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
            .Where(r => Rooms.IsPrivate(r) && Rooms.OtherPerson(r, User) != null).ToList();
    }

    /// <summary>Tells others this person is here, in which rooms, and what they're working on.</summary>
    public void SayOnline(IEnumerable<string> rooms, string? drawing = null, string? liveSession = null, string? email = null, string? teamsId = null,
        IEnumerable<string>? openDrawings = null, DateTimeOffset? busyUntil = null)
    {
        Directory.CreateDirectory(OnlineDir);
        var me = new OnlinePerson(User, Name, Machine, rooms.ToArray(), DateTimeOffset.UtcNow, drawing, liveSession, email, teamsId,
            OpenDrawings: openDrawings?.ToArray(), BusyUntil: busyUntil);
        string tmp = Path.Combine(OnlineDir, _me + ".tmp"), file = Path.Combine(OnlineDir, _me + ".json");
        File.WriteAllText(tmp, JsonSerializer.Serialize(me, Json));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>Marks this person as gone (keeping the note, for "last seen").</summary>
    public void SayOffline()
    {
        try
        {
            string file = Path.Combine(OnlineDir, _me + ".json");
            OnlinePerson? last = File.Exists(file) ? JsonSerializer.Deserialize<OnlinePerson>(File.ReadAllText(file), Json) : null;
            var gone = (last ?? new OnlinePerson(User, Name, Machine, Array.Empty<string>(), DateTimeOffset.UtcNow))
                with { Rooms = Array.Empty<string>(), Seen = DateTimeOffset.UtcNow, Drawing = null, LiveSession = null, OpenDrawings = null, Left = true };
            string tmp = Path.Combine(OnlineDir, _me + ".tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(gone, Json));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    /// <summary>Everyone's last "I'm here" note, however old (for "last seen").</summary>
    public List<OnlinePerson> LastSeen() => Online(TimeSpan.MaxValue);

    /// <summary>Everyone who has said they're here within <paramref name="window"/>.</summary>
    public List<OnlinePerson> Online(TimeSpan window)
    {
        var result = new List<OnlinePerson>();
        if (!Directory.Exists(OnlineDir)) return result;
        foreach (string f in Directory.GetFiles(OnlineDir, "*.json"))
        {
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (JsonSerializer.Deserialize<OnlinePerson>(fs, Json) is not { } p) continue;
                if (window == TimeSpan.MaxValue || (!p.Left && DateTimeOffset.UtcNow - p.Seen < window)) result.Add(p);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
