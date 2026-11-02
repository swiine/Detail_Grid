using System.Text.Json;

namespace CoWork.Core;

/// <summary>A multiplayer CoWork session: one drawing that several people edit at once.</summary>
public sealed record LiveSession(string Id, string Drawing, string HostId, string HostName, DateTimeOffset Started, bool Ended = false);

/// <summary>Someone in a CoWork session: who they are, where their crosshair is, and what they're holding.</summary>
/// <param name="Colour">Their crosshair colour ("#RRGGBB"); objects they're editing show in it too.</param>
/// <param name="Editing">Objects they have selected or are changing (shared id -> when they grabbed it).</param>
public sealed record LivePerson(string Id, string Name, string User, string Machine, bool Host, double X, double Y, DateTimeOffset Seen,
    string? Colour = null, Dictionary<string, DateTimeOffset>? Editing = null);

/// <summary>
/// Lock while editing: an object someone has selected (or is changing in a command) is theirs until they let go.
/// If two people grab the same object before seeing each other, whoever grabbed it first keeps it (ties go to the
/// lower person id), and every PC works this out the same way.
/// </summary>
public static class LiveLocks
{
    /// <summary>A grab only counts while that person's here.json is this fresh (so a crashed PC lets go).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(15);

    /// <summary>Who else holds each object: everything others hold that you don't hold first.</summary>
    public static Dictionary<string, LivePerson> HeldByOthers(IEnumerable<LivePerson> others, string myId,
        IReadOnlyDictionary<string, DateTimeOffset> mine, DateTimeOffset now)
    {
        var held = new Dictionary<string, LivePerson>();
        var since = new Dictionary<string, DateTimeOffset>();
        foreach (LivePerson p in others)
        {
            if (p.Editing == null || now - p.Seen > Fresh) continue;
            foreach (var (id, t) in p.Editing)
            {
                if (mine.TryGetValue(id, out DateTimeOffset myT) && Beats(myT, myId, t, p.Id)) continue;
                if (since.TryGetValue(id, out DateTimeOffset otherT) && !Beats(t, p.Id, otherT, held[id].Id)) continue;
                held[id] = p;
                since[id] = t;
            }
        }
        return held;
    }

    private static bool Beats(DateTimeOffset a, string aId, DateTimeOffset b, string bId) =>
        a < b || (a == b && string.CompareOrdinal(aId, bId) < 0);
}

/// <summary>Crosshair colours people can pick (others see their cursor, and what they're editing, in it).</summary>
public static class CrosshairColours
{
    public static readonly (string Name, string Hex)[] Choices =
    {
        ("Green", "#3CC85A"), ("Blue", "#3C9BF0"), ("Orange", "#F5A030"), ("Purple", "#B46EF0"),
        ("Pink", "#F0648C"), ("Yellow", "#F0D23C"), ("Cyan", "#3CDCDC"), ("Red", "#E64646"),
    };

    /// <summary>Your colour: the one you picked, or one chosen from your login (so it's the same every time).</summary>
    public static string For(string user, string? chosen)
    {
        if (chosen != null && Choices.Any(c => string.Equals(c.Hex, chosen, StringComparison.OrdinalIgnoreCase))) return chosen.ToUpperInvariant();
        int h = 0;
        foreach (char c in user.ToLowerInvariant()) h = (h * 31 + c) & 0x7fffffff;
        return Choices[h % Choices.Length].Hex;
    }
}

/// <summary>
/// One set of changes someone made (after a command finishes): the shared ids of the objects added or changed
/// (their copies are in the matching .dwg) and of those erased.
/// </summary>
public sealed record LiveBatch(long Seq, string From, DateTimeOffset Time, List<string> Changed, List<string> Erased, List<string> Layers);

/// <summary>
/// The files of CoWork sessions, kept in the CoWork folder:
/// <code>
///   live\&lt;session&gt;\session.json                     which drawing, who hosts, whether it has ended
///   live\&lt;session&gt;\&lt;person&gt;\here.json              that person's name and crosshair, rewritten several times a second
///   live\&lt;session&gt;\&lt;person&gt;\000001.dwg + .json    their changes, numbered; the .json is written last
/// </code>
/// As with the chat, each person writes only their own files.
/// </summary>
public static class LiveFolder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>People are shown as here while their here.json is this fresh.</summary>
    public static readonly TimeSpan PresenceWindow = TimeSpan.FromSeconds(30);

    public static string Root(string dataFolder) => Path.Combine(dataFolder, "live");

    public static string SessionDir(string dataFolder, string sessionId) => Path.Combine(Root(dataFolder), sessionId);

    public static string NewSessionId() => Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 4).ToLowerInvariant();

    /// <summary>A participant id unique to this person, PC and Civil 3D window.</summary>
    public static string NewPersonId(string user) =>
        $"{new string(user.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())}-{Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 2).ToLowerInvariant()}";

    public static void WriteSession(string dataFolder, LiveSession s)
    {
        string dir = SessionDir(dataFolder, s.Id);
        Directory.CreateDirectory(dir);
        WriteAtomic(Path.Combine(dir, "session.json"), JsonSerializer.Serialize(s, Json));
    }

    public static LiveSession? ReadSession(string dataFolder, string sessionId) =>
        Read<LiveSession>(Path.Combine(SessionDir(dataFolder, sessionId), "session.json"));

    /// <summary>Sessions that haven't ended and whose host is still around.</summary>
    public static List<LiveSession> ActiveSessions(string dataFolder)
    {
        var result = new List<LiveSession>();
        string root = Root(dataFolder);
        if (!Directory.Exists(root)) return result;
        foreach (string dir in Directory.GetDirectories(root))
        {
            LiveSession? s = Read<LiveSession>(Path.Combine(dir, "session.json"));
            if (s == null || s.Ended) continue;
            LivePerson? host = Read<LivePerson>(Path.Combine(dir, s.HostId, "here.json"));
            if (host != null && DateTimeOffset.UtcNow - host.Seen < PresenceWindow) result.Add(s);
        }
        return result.OrderByDescending(s => s.Started).ToList();
    }

    public static string PersonDir(string dataFolder, string sessionId, string personId) =>
        Path.Combine(SessionDir(dataFolder, sessionId), personId);

    public static void WriteHere(string personDir, LivePerson me)
    {
        Directory.CreateDirectory(personDir);
        WriteAtomic(Path.Combine(personDir, "here.json"), JsonSerializer.Serialize(me, Json));
    }

    /// <summary>Everyone else in the session who is still around.</summary>
    public static List<LivePerson> Others(string sessionDir, string myId)
    {
        var result = new List<LivePerson>();
        if (!Directory.Exists(sessionDir)) return result;
        foreach (string dir in Directory.GetDirectories(sessionDir))
        {
            if (Path.GetFileName(dir) == myId) continue;
            if (Read<LivePerson>(Path.Combine(dir, "here.json")) is { } p && DateTimeOffset.UtcNow - p.Seen < PresenceWindow) result.Add(p);
        }
        return result;
    }

    public static string BatchName(long seq) => seq.ToString("D6");

    /// <summary>Records a batch (its .dwg, if any, must already be in place: the .json marks it complete).</summary>
    public static void WriteBatch(string personDir, LiveBatch b)
    {
        Directory.CreateDirectory(personDir);
        WriteAtomic(Path.Combine(personDir, BatchName(b.Seq) + ".json"), JsonSerializer.Serialize(b, Json));
    }

    /// <summary>A person's complete batches after <paramref name="afterSeq"/>, in order.</summary>
    public static List<LiveBatch> BatchesAfter(string personDir, long afterSeq)
    {
        var result = new List<LiveBatch>();
        if (!Directory.Exists(personDir)) return result;
        foreach (string f in Directory.GetFiles(personDir, "??????.json"))
        {
            if (!long.TryParse(Path.GetFileNameWithoutExtension(f), out long seq) || seq <= afterSeq) continue;
            if (Read<LiveBatch>(f) is { } b) result.Add(b);
        }
        return result.OrderBy(b => b.Seq).ToList();
    }

    /// <summary>Everyone else's folders in the session (to read their batches).</summary>
    public static IEnumerable<string> OtherPeopleDirs(string sessionDir, string myId) =>
        Directory.Exists(sessionDir) ? Directory.GetDirectories(sessionDir).Where(d => Path.GetFileName(d) != myId) : Array.Empty<string>();

    private static void WriteAtomic(string file, string text)
    {
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, text);
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, file, overwrite: true); return; }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5) { Thread.Sleep(30); }
        }
    }

    private static T? Read<T>(string file) where T : class
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(fs, Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}
