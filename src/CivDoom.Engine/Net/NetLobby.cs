using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CivDoom.Engine;

/// <summary>
/// Where multiplayer messages go. By default the same as TTW Chat: its shared folder (games live in a
/// "doom" folder beside the chat's "channels"), or its server if TTW Chat is set to use one.
/// </summary>
public sealed class NetConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>TTW Chat's shared folder (games go in its "doom" sub-folder), or null to use the server.</summary>
    public string? Folder { get; set; }

    /// <summary>TTW Chat server address, e.g. ws://TTW-CAD01:5280/ws.</summary>
    public string? ServerUrl { get; set; }

    /// <summary>Name shown to other players (blank = TTW Chat display name, then the Windows login).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Regex on the drawing path whose group "p" is the TTW Chat project channel for invites.</summary>
    public string ProjectPattern { get; set; } = @"\\jobs\\\d{2}\\(?<p>\d{5,7})(?=\\|$)";

    /// <summary>Where these settings came from (for messages).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Source { get; set; } = "defaults";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesFolder => !string.IsNullOrWhiteSpace(Folder);

    [System.Text.Json.Serialization.JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Environment.UserName : DisplayName!.Trim();

    /// <summary>The folder holding the games, e.g. A:\...\TTWChat\doom.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string GamesFolder => Path.Combine(Folder ?? "", "doom");

    /// <summary>CivDOOM's own override (CIVDOOMNET), if any.</summary>
    public static string OverrideFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivDoom", "multiplayer.json");

    private static string TtwUserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TTW", "Chat", "settings.json");

    /// <summary>
    /// TTW Chat's office defaults: chat.defaults.json next to a loaded TTWChat.dll, or in the TTWChat bundle
    /// in either ApplicationPlugins folder.
    /// </summary>
    private static IEnumerable<string> TtwDefaultsFiles()
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? loc = null;
            try { if (a.GetName().Name == "TTWChat") loc = a.Location; } catch (NotSupportedException) { }
            if (!string.IsNullOrEmpty(loc)) yield return Path.Combine(Path.GetDirectoryName(loc)!, "chat.defaults.json");
        }
        foreach (Environment.SpecialFolder f in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData })
            yield return Path.Combine(Environment.GetFolderPath(f), "Autodesk", "ApplicationPlugins", "TTWChat.bundle", "Contents", "chat.defaults.json");
    }

    private sealed class TtwSettings
    {
        public string? ChatFolder { get; set; }
        public string? ServerUrl { get; set; }
        public string? DisplayName { get; set; }
        public string? ProjectPattern { get; set; }
    }

    /// <summary>TTW Chat's settings (office defaults, then the user's own), then CivDOOM's override on top.</summary>
    public static NetConfig Load(IEnumerable<string>? extraDefaults = null)
    {
        var cfg = new NetConfig();
        foreach (string f in (extraDefaults ?? Array.Empty<string>()).Concat(TtwDefaultsFiles()))
        {
            if (Read<TtwSettings>(f) is not { } d) continue;
            cfg.Folder = d.ChatFolder;
            cfg.ServerUrl = d.ServerUrl;
            if (!string.IsNullOrWhiteSpace(d.ProjectPattern)) cfg.ProjectPattern = d.ProjectPattern!;
            cfg.Source = "TTW Chat";
            break;
        }
        if (Read<TtwSettings>(TtwUserFile) is { } u)
        {
            if (u.ChatFolder != null) cfg.Folder = u.ChatFolder; // "" = that user chose the chat server
            if (!string.IsNullOrWhiteSpace(u.ServerUrl)) cfg.ServerUrl = u.ServerUrl;
            if (!string.IsNullOrWhiteSpace(u.DisplayName)) cfg.DisplayName = u.DisplayName;
            if (!string.IsNullOrWhiteSpace(u.ProjectPattern)) cfg.ProjectPattern = u.ProjectPattern!;
            cfg.Source = "TTW Chat";
        }
        if (Read<NetConfig>(OverrideFile) is { } o)
        {
            if (o.Folder != null) cfg.Folder = o.Folder;
            if (!string.IsNullOrWhiteSpace(o.ServerUrl)) cfg.ServerUrl = o.ServerUrl;
            if (!string.IsNullOrWhiteSpace(o.DisplayName)) cfg.DisplayName = o.DisplayName;
            cfg.Source = "CIVDOOMNET";
        }
        return cfg;
    }

    /// <summary>Saves this as CivDOOM's override (TTW Chat's own settings are never changed).</summary>
    public void SaveOverride()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OverrideFile)!);
        File.WriteAllText(OverrideFile, JsonSerializer.Serialize(this, Json));
    }

    private static T? Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public string Describe() => UsesFolder ? $"shared folder {GamesFolder}" : $"chat server {ServerUrl}";
}

/// <summary>A game someone is hosting.</summary>
public sealed record GameListing(string Id, string HostName, string LevelName, int Players, DateTimeOffset Started);

/// <summary>Hosting, finding and joining co-op games, and inviting people through TTW Chat.</summary>
public static class NetLobby
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Prefix of the chat message TTW Chat turns into a "Join game" button.</summary>
    public const string InvitePrefix = "[Doom] ";

    private static readonly Regex InviteId = new(@"\(game (?<id>[a-z0-9]{4,16})\)", RegexOptions.IgnoreCase);

    public static string? GameIdFromInvite(string text) => InviteId.Match(text) is { Success: true } m ? m.Groups["id"].Value.ToLowerInvariant() : null;

    public static string NewGameId() => Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 3).ToLowerInvariant();

    /// <summary>A player id unique to this computer and Civil 3D window.</summary>
    public static string NewPlayerId() =>
        $"{Safe(Environment.UserName)}-{Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 2).ToLowerInvariant()}";

    private static string Safe(string s)
    {
        string r = new(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return r.Length == 0 ? "player" : r.Length > 24 ? r[..24] : r;
    }

    private sealed class SessionFile
    {
        public string Id { get; set; } = "";
        public string Host { get; set; } = "";
        public string HostName { get; set; } = "";
        public string Level { get; set; } = "";
        public string Mode { get; set; } = "coop";
        public DateTimeOffset Started { get; set; }
        public bool Ended { get; set; }
    }

    /// <summary>
    /// Starts hosting <paramref name="game"/> (already built from the level) and returns its session. Players
    /// join with the returned session's <see cref="NetSession.GameId"/>.
    /// </summary>
    public static NetSession Host(NetConfig cfg, Game game, int seed, IGameTransport? transport = null)
    {
        string id = NewGameId(), me = NewPlayerId();
        byte[] level = LevelCodec.ToBytes(LevelCodec.Pack(game, seed));
        string? dir = null;
        if (transport == null)
        {
            if (cfg.UsesFolder)
            {
                CleanUp(cfg);
                dir = Path.Combine(cfg.GamesFolder, id);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "level.json.gz"), level);
                WriteSession(dir, new SessionFile { Id = id, Host = me, HostName = cfg.Name, Level = game.Level.Name, Started = DateTimeOffset.UtcNow });
                transport = new FolderTransport(dir, me);
            }
            else
            {
                transport = new RelayTransport(RequireServer(cfg), id, me, Environment.UserName, cfg.Name, Environment.MachineName);
            }
        }
        var session = new NetSession(game, transport, NetRole.Host, id, me, cfg.Name);
        session.ServeLevel(level);
        if (dir != null)
            session.Ended += () =>
            {
                try { WriteSession(dir, new SessionFile { Id = id, Host = me, HostName = cfg.Name, Level = game.Level.Name, Started = DateTimeOffset.UtcNow, Ended = true }); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            };
        return session;
    }

    /// <summary>
    /// Housekeeping: removes game folders whose game ended (or went quiet) more than a day ago. Only folders
    /// with a session.json inside the "doom" folder are ever touched.
    /// </summary>
    private static void CleanUp(NetConfig cfg)
    {
        if (!Directory.Exists(cfg.GamesFolder)) return;
        foreach (string dir in Directory.GetDirectories(cfg.GamesFolder))
        {
            try
            {
                string session = Path.Combine(dir, "session.json");
                if (!File.Exists(session)) continue;
                DateTime last = Directory.GetDirectories(dir).Select(p => File.GetLastWriteTimeUtc(Path.Combine(p, "state.json")))
                    .Append(File.GetLastWriteTimeUtc(session)).Max();
                if (DateTime.UtcNow - last > TimeSpan.FromDays(1)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string RequireServer(NetConfig cfg) =>
        Uri.TryCreate(cfg.ServerUrl, UriKind.Absolute, out Uri? u) && (u.Scheme == "ws" || u.Scheme == "wss")
            ? cfg.ServerUrl!
            : throw new InvalidOperationException("No TTW Chat folder or server is set up. Run CIVDOOMNET (or CHATFOLDER / CHATSERVER in TTW Chat).");

    private static void WriteSession(string dir, SessionFile s) =>
        File.WriteAllText(Path.Combine(dir, "session.json"), JsonSerializer.Serialize(s, Json));

    /// <summary>Games being hosted right now (shared-folder mode; with the chat server, use the invite in TTW Chat).</summary>
    public static List<GameListing> ListGames(NetConfig cfg)
    {
        var result = new List<GameListing>();
        if (!cfg.UsesFolder || !Directory.Exists(cfg.GamesFolder)) return result;
        foreach (string dir in Directory.GetDirectories(cfg.GamesFolder))
        {
            try
            {
                var s = JsonSerializer.Deserialize<SessionFile>(File.ReadAllText(Path.Combine(dir, "session.json")), Json);
                if (s == null || s.Ended) continue;
                // The host's state file is rewritten many times a second while the game runs.
                DateTime hostSeen = File.GetLastWriteTimeUtc(Path.Combine(dir, s.Host, "state.json"));
                if (DateTime.UtcNow - hostSeen > TimeSpan.FromSeconds(20)) continue;
                int players = Directory.GetDirectories(dir).Count(p =>
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(p, "state.json")) < TimeSpan.FromSeconds(20));
                result.Add(new GameListing(s.Id, s.HostName, s.Level, players, s.Started));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(g => g.Started).ToList();
    }

    /// <summary>
    /// Joins a game: fetches the level (from the shared folder, or from the host through the chat server)
    /// and returns the session with this player's game, ready to play.
    /// </summary>
    public static NetSession Join(NetConfig cfg, string gameId, GameContent content, int spawnIndex = -1, TimeSpan? timeout = null)
    {
        gameId = gameId.Trim().ToLowerInvariant();
        string me = NewPlayerId();
        LevelPackage package;
        IGameTransport transport;
        string? hostId = null;
        if (cfg.UsesFolder)
        {
            string dir = Path.Combine(cfg.GamesFolder, gameId);
            string sessionFile = Path.Combine(dir, "session.json");
            if (!File.Exists(sessionFile)) throw new InvalidOperationException($"No game {gameId} in {cfg.GamesFolder}.");
            var s = JsonSerializer.Deserialize<SessionFile>(File.ReadAllText(sessionFile), Json);
            if (s == null || s.Ended) throw new InvalidOperationException("That game has ended.");
            hostId = s.Host;
            package = LevelCodec.FromBytes(File.ReadAllBytes(Path.Combine(dir, "level.json.gz")));
            transport = new FolderTransport(dir, me);
        }
        else
        {
            var relay = new RelayTransport(RequireServer(cfg), gameId, me, Environment.UserName, cfg.Name, Environment.MachineName);
            try
            {
                package = FetchLevel(relay, me, timeout ?? TimeSpan.FromSeconds(20), out hostId);
            }
            catch
            {
                relay.Dispose();
                throw;
            }
            transport = relay;
        }

        Game game = LevelCodec.CreateGame(package, content);
        game.SpawnIndex = spawnIndex >= 0 ? spawnIndex : 1 + (Math.Abs(me.GetHashCode()) % 6);
        game.MoveToSpawn();
        return new NetSession(game, transport, NetRole.Client, gameId, me, cfg.Name, hostId);
    }

    /// <summary>Asks the host for the level through the chat server and waits for all its pieces.</summary>
    private static LevelPackage FetchLevel(RelayTransport relay, string me, TimeSpan timeout, out string? hostId)
    {
        DateTime until = DateTime.UtcNow + timeout;
        while (!relay.Connected)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException(relay.Error ?? "Can't reach the chat server.");
            Thread.Sleep(50);
        }
        relay.SendEvent(JsonSerializer.Serialize(new NetEvent { Kind = NetEvent.NeedLevelKind, From = me, Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, Json));
        var parts = new Dictionary<int, string>();
        int total = -1;
        hostId = null;
        var states = new List<string>();
        var events = new List<string>();
        while (total < 0 || parts.Count < total)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The host didn't send the level - is the game still running?");
            relay.Receive(states, events);
            foreach (string json in events)
            {
                NetEvent? e = JsonSerializer.Deserialize<NetEvent>(json, Json);
                if (e is not { Kind: NetEvent.LevelKind } || e.To != me || e.Text == null) continue;
                total = e.Amount;
                hostId = e.From;
                parts[e.Index] = e.Text;
            }
            events.Clear();
            states.Clear();
            Thread.Sleep(30);
        }
        string b64 = string.Concat(Enumerable.Range(0, total).Select(i => parts[i]));
        return LevelCodec.FromBytes(Convert.FromBase64String(b64));
    }

    // ---------------------------------------------------------------- TTW Chat invites

    /// <summary>The TTW Chat channel for a drawing (its project), or "general".</summary>
    public static string ChannelFor(NetConfig cfg, string? drawingPath)
    {
        if (!string.IsNullOrEmpty(drawingPath))
        {
            try
            {
                Match m = Regex.Match(drawingPath, cfg.ProjectPattern, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string v = Regex.Replace((m.Groups["p"].Success ? m.Groups["p"].Value : m.Value).ToLowerInvariant(), "[^a-z0-9_.-]", "");
                    if (v.Length > 0) return v;
                }
            }
            catch (ArgumentException) { }
        }
        return "general";
    }

    public static string InviteText(NetConfig cfg, NetSession s) =>
        $"{InvitePrefix}{cfg.Name} is hosting CivDOOM co-op: {s.Game.Level.Name} - click Join game to play (game {s.GameId}).";

    /// <summary>
    /// Posts the invite as a TTW Chat message: appended to this user's file in the chat folder, exactly as
    /// TTW Chat writes messages, or sent through the chat server. Returns the channel it went to.
    /// </summary>
    public static string PostInvite(NetConfig cfg, NetSession s, string? drawingPath)
    {
        string channel = ChannelFor(cfg, drawingPath);
        string text = InviteText(cfg, s);
        if (cfg.UsesFolder)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var msg = new { id = now.UtcTicks, channel, user = Environment.UserName, display = cfg.Name, ts = now, text, drawing = (string?)null };
            string dir = Path.Combine(cfg.Folder!, "channels", channel);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, ChatFileName(Environment.UserName) + ".jsonl");
            byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, Json) + "\n");
            using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(line, 0, line.Length);
        }
        else
        {
            SendChatMessage(RequireServer(cfg), cfg.Name, channel, text).GetAwaiter().GetResult();
        }
        return channel;
    }

    /// <summary>The file name TTW Chat uses for a user (lower case, invalid characters replaced).</summary>
    private static string ChatFileName(string user)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        string r = new(user.ToLowerInvariant().Select(c => bad.Contains(c) || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c).ToArray());
        return r.Length == 0 ? "_" : r;
    }

    private static async Task SendChatMessage(string url, string display, string channel, string text)
    {
        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ws.ConnectAsync(new Uri(url), cts.Token).ConfigureAwait(false);
        async Task Send(object o)
        {
            byte[] b = JsonSerializer.SerializeToUtf8Bytes(o, Json);
            await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
        }
        await Send(new { type = "hello", user = Environment.UserName, display, machine = Environment.MachineName }).ConfigureAwait(false);
        await Send(new { type = "join", channel }).ConfigureAwait(false);
        await Send(new { type = "msg", channel, text }).ConfigureAwait(false);
        await Task.Delay(300, cts.Token).ConfigureAwait(false);
        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token).ConfigureAwait(false); } catch (WebSocketException) { }
    }
}
