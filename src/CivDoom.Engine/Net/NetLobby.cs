using System.Text.Json;

namespace CivDoom.Engine;

/// <summary>
/// Where the CivDOOM server is. The office default comes from civdoom.server.json next to the plugin; each
/// person can override it (CIVDOOMNET), which is saved in %APPDATA%\CivDoom\multiplayer.json.
/// </summary>
public sealed class NetConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>"PCNAME" or "PCNAME:port" of the PC running CivDoom.Server, or blank if none is set up.</summary>
    public string? Server { get; set; }

    /// <summary>Name shown to other players (blank = the Windows login).</summary>
    public string? DisplayName { get; set; }

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Environment.UserName : DisplayName!.Trim();

    public bool HasServer => !string.IsNullOrWhiteSpace(Server);

    public static string UserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivDoom", "multiplayer.json");

    /// <summary>The office default (civdoom.server.json next to the plugin), then this user's own settings.</summary>
    public static NetConfig Load(string? defaultsFile)
    {
        NetConfig cfg = (defaultsFile != null ? Read(defaultsFile) : null) ?? new NetConfig();
        if (Read(UserFile) is { } u)
        {
            if (!string.IsNullOrWhiteSpace(u.Server)) cfg.Server = u.Server;
            if (!string.IsNullOrWhiteSpace(u.DisplayName)) cfg.DisplayName = u.DisplayName;
        }
        return cfg;
    }

    /// <summary>Saves this user's own settings.</summary>
    public void SaveForUser()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
        File.WriteAllText(UserFile, JsonSerializer.Serialize(this, Json));
    }

    private static NetConfig? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<NetConfig>(File.ReadAllText(path), Json) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Splits "PC" / "PC:5390" into host and port.</summary>
    public static (string Host, int Port) ParseServer(string server)
    {
        string s = server.Trim();
        int colon = s.LastIndexOf(':');
        if (colon > 0 && int.TryParse(s[(colon + 1)..], out int port) && port is > 0 and < 65536) return (s[..colon], port);
        return (s, GameServer.DefaultPort);
    }
}

/// <summary>A game someone is hosting.</summary>
public sealed record GameListing(string Id, string HostName, string LevelName, int Players, DateTimeOffset Started);

/// <summary>Hosting, finding and joining co-op games on a CivDOOM server.</summary>
public static class NetLobby
{
    /// <summary>A player id unique to this computer and Civil 3D window.</summary>
    public static string NewPlayerId()
    {
        string user = new(Environment.UserName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        if (user.Length == 0) user = "player";
        if (user.Length > 24) user = user[..24];
        return $"{user}-{Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 2).ToLowerInvariant()}";
    }

    private static ServerConnection Connect(string server, string id, string name)
    {
        (string host, int port) = NetConfig.ParseServer(server);
        return ServerConnection.Connect(host, port, id, name);
    }

    /// <summary>Starts hosting <paramref name="game"/> (already built from the level) on the server.</summary>
    public static NetSession Host(string server, string name, Game game, int seed)
    {
        string me = NewPlayerId();
        ServerConnection conn = Connect(server, me, name);
        try
        {
            string id = conn.Host(game.Level.Name, LevelCodec.ToBytes(LevelCodec.Pack(game, seed)));
            return new NetSession(game, conn, NetRole.Host, id, me, name);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>The games running on the server right now.</summary>
    public static List<GameListing> ListGames(string server)
    {
        using ServerConnection conn = Connect(server, NewPlayerId(), Environment.UserName);
        return conn.ListGames();
    }

    /// <summary>Joins a game: fetches the host's level and returns the session with this player's game.</summary>
    public static NetSession Join(string server, string name, string gameId, GameContent content, int spawnIndex = -1)
    {
        string me = NewPlayerId();
        ServerConnection conn = Connect(server, me, name);
        try
        {
            (byte[] level, string hostId) = conn.Join(gameId.Trim().ToLowerInvariant());
            Game game = LevelCodec.CreateGame(LevelCodec.FromBytes(level), content);
            game.SpawnIndex = spawnIndex >= 0 ? spawnIndex : 1 + (Math.Abs(me.GetHashCode()) % 6);
            game.MoveToSpawn();
            return new NetSession(game, conn, NetRole.Client, gameId, me, name, hostId);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }
}
