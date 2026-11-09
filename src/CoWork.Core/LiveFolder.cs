using System.Text.Json;

namespace CoWork.Core;

/// <summary>A multiplayer CoWork session: one drawing that several people edit at once.</summary>
/// <param name="HostSince">When the current host took over (hosting can be handed to a guest).</param>
/// <remarks>Server: the session's own server ("PC:port") that the host's CoWork started, when there's no office server.</remarks>
public sealed record LiveSession(string Id, string Drawing, string HostId, string HostName, DateTimeOffset Started, bool Ended = false,
    DateTimeOffset? HostSince = null, string? Server = null, string? Presenter = null);

/// <summary>The host's say over the guests: who may only watch, and who has been removed (by person id).</summary>
public sealed record LiveRoles(string[] Viewers, string[] Kicked)
{
    public static readonly LiveRoles None = new(Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>Someone in a CoWork session: who they are, where their crosshair is, and what they're holding.</summary>
/// <param name="Colour">Their crosshair colour ("#RRGGBB"); objects they're editing show in it too.</param>
/// <param name="Editing">Objects they have selected or are changing (shared id -> when they grabbed it).</param>
/// <param name="Watching">They joined to watch only (or the host made them view-only).</param>
/// <remarks>ViewX/ViewY/ViewH: the centre and height of their view, so others can follow it.</remarks>
public sealed record LivePerson(string Id, string Name, string User, string Machine, bool Host, double X, double Y, DateTimeOffset Seen,
    string? Colour = null, Dictionary<string, DateTimeOffset>? Editing = null, bool Watching = false,
    double ViewX = 0, double ViewY = 0, double ViewH = 0);

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

    /// <summary>Your colour: the one you picked (any "#RRGGBB", from the swatches or the colour wheel), or one chosen
    /// from your login (so it's the same every time).</summary>
    public static string For(string user, string? chosen)
    {
        if (IsValid(chosen)) return chosen!.ToUpperInvariant();
        int h = 0;
        foreach (char c in user.ToLowerInvariant()) h = (h * 31 + c) & 0x7fffffff;
        return Choices[h % Choices.Length].Hex;
    }

    public static bool IsValid(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);

    /// <summary>A colour from hue (0-360), saturation and brightness (0-1), as "#RRGGBB".</summary>
    public static string FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        static int B(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{B(r + m):X2}{B(g + m):X2}{B(b + m):X2}";
    }

    /// <summary>Hue, saturation and brightness of "#RRGGBB" (to put the wheel's marker in the right place).</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(string hex)
    {
        int rgb = Convert.ToInt32(hex.TrimStart('#'), 16);
        double r = (rgb >> 16 & 255) / 255.0, g = (rgb >> 8 & 255) / 255.0, b = (rgb & 255) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }
}

/// <summary>
/// One set of changes someone made (after a command finishes): the shared ids of the objects added or changed
/// (their copies are in the matching .dwg) and of those erased.
/// </summary>
/// It can also carry layer settings, Civil 3D COGO points (by point number) and point groups (by name).
public sealed record LiveBatch(long Seq, string From, DateTimeOffset Time, List<string> Changed, List<string> Erased, List<string> Layers,
    List<LiveLayer>? LayerStates = null, List<LivePoint>? Points = null, List<uint>? ErasedPoints = null,
    List<LivePointGroup>? Groups = null, List<string>? ErasedGroups = null,
    List<string>? Records = null, Dictionary<string, string>? Layouts = null, List<LivePipePart>? Pipes = null, List<LiveSampleLine>? SampleLines = null)
{
    /// <summary>Whether there's anything in it.</summary>
    public bool IsEmpty => Changed.Count == 0 && Erased.Count == 0 && (LayerStates?.Count ?? 0) == 0 && (Points?.Count ?? 0) == 0
                           && (ErasedPoints?.Count ?? 0) == 0 && (Groups?.Count ?? 0) == 0 && (ErasedGroups?.Count ?? 0) == 0
                           && (Records?.Count ?? 0) == 0 && (Pipes?.Count ?? 0) == 0 && (SampleLines?.Count ?? 0) == 0;

    /// <summary>Records are "kind:name": text, dim, ltype or block (its copy is in the side drawing, replacing yours).</summary>
    public static string Record(string kind, string name) => kind + ":" + name;

    public static (string Kind, string Name) SplitRecord(string r)
    {
        int i = r.IndexOf(':');
        return i > 0 ? (r[..i], r[(i + 1)..]) : ("", r);
    }
}

/// <summary>A layer's settings. Colour is an AutoCAD colour number ("7") or a true colour ("#RRGGBB").</summary>
public sealed record LiveLayer(string Name, string Colour, bool Off, bool Frozen, bool Locked, string Linetype, int LineWeight,
    bool Plottable, string? Description);

/// <summary>
/// A pipe or structure in a Civil 3D pipe network, matched by network name and part name. Pipes: start (X1..) and end
/// (X2..). Structures: position (X1..), rim and sump elevations.
/// </summary>
public sealed record LivePipePart(string Network, string Name, bool IsPipe, double X1, double Y1, double Z1, double X2, double Y2, double Z2,
    double Rim = 0, double Sump = 0);

/// <summary>A Civil 3D sample line, matched by its group and name: station and its vertices (x, y, z each).</summary>
public sealed record LiveSampleLine(string Group, string Name, double Station, List<double[]>? Vertices);

/// <summary>A Civil 3D COGO point, matched on every PC by its point number.</summary>
public sealed record LivePoint(uint Number, double Easting, double Northing, double Elevation, string RawDescription, string? Name);

/// <summary>
/// A Civil 3D point group, matched by name: its description and which points it takes (the same fields as the
/// Include / Exclude tabs of its properties).
/// </summary>
public sealed record LivePointGroup(string Name, string? Description, bool IncludeAll,
    string? IncludeNumbers, string? ExcludeNumbers, string? IncludeRawDescriptions, string? ExcludeRawDescriptions,
    string? IncludeFullDescriptions, string? ExcludeFullDescriptions, string? IncludeNames, string? ExcludeNames,
    string? IncludeElevations, string? ExcludeElevations);

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

    /// <summary>A batch as sent through the CoWork server.</summary>
    public static string BatchJson(LiveBatch b) => JsonSerializer.Serialize(b, Json);

    public static LiveBatch? BatchFromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LiveBatch>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Someone's here.json as sent through the CoWork server.</summary>
    public static string PersonJson(LivePerson p) => JsonSerializer.Serialize(p, Json);

    public static LivePerson? PersonFromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LivePerson>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Server mode: everyone in the session (from the shared folder) with the newest crosshair/locks the server passed
    /// on. Someone the server has heard from but whose here.json isn't there yet counts too (just joined); stale ones
    /// (no sign for <see cref="PresenceWindow"/>) don't.
    /// </summary>
    public static List<LivePerson> Merge(IEnumerable<LivePerson> fromFolder, IEnumerable<LivePerson> fromServer, string myId, DateTimeOffset now)
    {
        var byId = new Dictionary<string, LivePerson>();
        foreach (LivePerson p in fromFolder) byId[p.Id] = p;
        foreach (LivePerson p in fromServer)
        {
            if (p.Id == myId || now - p.Seen >= PresenceWindow) continue;
            if (!byId.TryGetValue(p.Id, out LivePerson? f) || p.Seen > f.Seen) byId[p.Id] = p;
        }
        return byId.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

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

    /// <summary>The host's CoWork writes here.json at least every 5 seconds; after this long without one, they're gone.</summary>
    public static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// When the host was last seen: the newest of what was seen before and their here.json now. A missing or
    /// half-written file for a moment doesn't reset it - only real silence counts.
    /// </summary>
    public static DateTimeOffset HostLastSeen(IEnumerable<LivePerson> others, DateTimeOffset seenBefore)
    {
        DateTimeOffset now = others.Where(p => p.Host).Select(p => p.Seen).DefaultIfEmpty(seenBefore).Max();
        return now > seenBefore ? now : seenBefore;
    }

    /// <summary>
    /// Whether a guest has lost the host: no sign of them for <see cref="HostTimeout"/> since they were last seen
    /// (they closed Civil 3D, crashed or dropped off the network). Guests start the clock when they join, and
    /// again (with extra grace) when hosting is handed over.
    /// </summary>
    public static bool HostGone(DateTimeOffset lastSeen, DateTimeOffset now) => now - lastSeen > HostTimeout;

    public static void WriteRoles(string sessionDir, LiveRoles roles)
    {
        Directory.CreateDirectory(sessionDir);
        WriteAtomic(Path.Combine(sessionDir, "roles.json"), JsonSerializer.Serialize(roles, Json));
    }

    public static LiveRoles ReadRoles(string sessionDir) => Read<LiveRoles>(Path.Combine(sessionDir, "roles.json")) ?? LiveRoles.None;

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
