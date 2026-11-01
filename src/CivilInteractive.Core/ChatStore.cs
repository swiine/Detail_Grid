using System.Text;
using System.Text.Json;

namespace CivilInteractive.Core;

/// <summary>
/// CivilInteractive's messages, kept in a shared folder:
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
    public ChatMessage Post(string room, string text, string? drawing = null, MessageButton? button = null)
    {
        if (!Rooms.IsValid(room)) throw new ArgumentException("Bad room name", nameof(room));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var msg = new ChatMessage(Guid.NewGuid().ToString("N"), room, User, Name, now, text.Trim(), drawing, button);
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
        return all.GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Time).TakeLast(max).ToList();
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

    /// <summary>Tells others this person is here, and in which rooms.</summary>
    public void SayOnline(IEnumerable<string> rooms)
    {
        Directory.CreateDirectory(OnlineDir);
        var me = new OnlinePerson(User, Name, Machine, rooms.ToArray(), DateTimeOffset.UtcNow);
        string tmp = Path.Combine(OnlineDir, _me + ".tmp"), file = Path.Combine(OnlineDir, _me + ".json");
        File.WriteAllText(tmp, JsonSerializer.Serialize(me, Json));
        File.Move(tmp, file, overwrite: true);
    }

    public void SayOffline()
    {
        try { File.Delete(Path.Combine(OnlineDir, _me + ".json")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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
                if (JsonSerializer.Deserialize<OnlinePerson>(fs, Json) is { } p && DateTimeOffset.UtcNow - p.Seen < window) result.Add(p);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
