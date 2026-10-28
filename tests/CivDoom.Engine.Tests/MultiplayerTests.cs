using CivDoom.Engine;

namespace CivDoom.Engine.Tests;

public class MultiplayerTests
{
    // Host at the left, a monster in the middle, a key, a red door, then the finish.
    private static readonly string[] Map =
    {
        "####################",
        "#P....E.......(.).F#",
        "#.................. #",
        "####################",
    };

    private static (NetSession Host, NetSession Client) Pair(string[] map, MonsterSet? monsters = null)
    {
        var hub = new MemoryHub();
        var hostGame = new Game(AsciiMap.Parse("t", map), 7, monsters ?? WeaponTests.Dummies());
        var host = new NetSession(hostGame, hub.Connect("host"), NetRole.Host, "g1", "host", "Hana");
        byte[] level = LevelCodec.ToBytes(LevelCodec.Pack(hostGame, 7));
        var content = new GameContent(monsters ?? WeaponTests.Dummies(), WeaponSet.BuiltIn, ThemeSet.BuiltIn, PlayerDesign.BuiltIn);
        Game clientGame = LevelCodec.CreateGame(LevelCodec.FromBytes(level), content);
        clientGame.SpawnIndex = 1;
        clientGame.MoveToSpawn();
        var client = new NetSession(clientGame, hub.Connect("client"), NetRole.Client, "g1", "client", "Cal", "host");
        return (host, client);
    }

    private static void Run(NetSession a, NetSession b, double seconds, GameInput? aInput = null, GameInput? bInput = null)
    {
        for (int i = 0; i < seconds * 60; i++)
        {
            a.Game.Update(1 / 60.0, aInput ?? new GameInput());
            a.Tick(1 / 60.0);
            b.Game.Update(1 / 60.0, bInput ?? new GameInput());
            b.Tick(1 / 60.0);
        }
    }

    [Fact]
    public void PlayersSeeEachOther()
    {
        (NetSession host, NetSession client) = Pair(Map);
        Run(host, client, 0.5);
        RemotePlayer cal = Assert.Single(host.Game.Others);
        Assert.Equal("Cal", cal.Name);
        Assert.True(Vec2.Distance(cal.Position, client.Game.Player.Position) < 0.05);
        Assert.Equal("Hana", Assert.Single(client.Game.Others).Name);
        Assert.NotEqual(host.Game.Player.Position, client.Game.Player.Position); // spawned side by side
        Assert.Contains(host.Game.MessageLog, m => m.Text.Contains("Cal joined"));
    }

    [Fact]
    public void ClientShotsDamageTheHostsMonsters()
    {
        (NetSession host, NetSession client) = Pair(Map);
        Run(host, client, 0.3);
        Enemy hostMonster = host.Game.Enemies[0];
        int before = hostMonster.Health;
        Game c = client.Game;
        c.Player.Angle = Math.Atan2(hostMonster.Position.Y - c.Player.Position.Y, hostMonster.Position.X - c.Player.Position.X);
        Run(host, client, 0.6, bInput: new GameInput { Fire = true });
        Run(host, client, 0.2); // let the host's next update arrive
        Assert.True(hostMonster.Health < before, "the host's monster wasn't hurt");
        Assert.Equal(hostMonster.Health, client.Game.Enemies[0].Health); // and the client sees the new health
    }

    [Fact]
    public void KillsAreCreditedToWhoeverLandedThem()
    {
        (NetSession host, NetSession client) = Pair(Map);
        Run(host, client, 0.3);
        Enemy m = host.Game.Enemies[0];
        m.Health = 1;
        Game c = client.Game;
        c.Player.Angle = Math.Atan2(m.Position.Y - c.Player.Position.Y, m.Position.X - c.Player.Position.X);
        Run(host, client, 0.6, bInput: new GameInput { Fire = true });
        Assert.False(m.IsAlive);
        Assert.False(client.Game.Enemies[0].IsAlive);
        Assert.Equal(1, client.Game.PlayerKills["client"]);
        Assert.True(client.Game.LastKillTime > 0, "no kill marker for the client");
    }

    [Fact]
    public void MonstersHuntTheNearestPlayer()
    {
        // A monster near the client's end of the corridor, far from the host.
        string[] map =
        {
            "##############################",
            "#P..........................E#",
            "##############################",
        };
        var monsters = new MonsterSet(new[]
        {
            MonsterFile.Parse("proxy", "name = Biter\nhealth = 500\nspeed = 3\nmelee damage = 5-5\nattack delay = 60\nbehavior = rusher\ndodge = 0\n[colors]\nx = 808080\n[idle]\nxx\nxx", new List<string>()),
        });
        (NetSession host, NetSession client) = Pair(map, monsters);
        client.Game.Player.Position = new Vec2(25.5, 1.5); // the client stands near the monster
        Run(host, client, 3);
        Assert.True(client.Game.Player.Health < Player.MaxHealth, "the monster didn't go for the nearer (remote) player");
        Assert.Equal(Player.MaxHealth, host.Game.Player.Health);
        Assert.Contains(client.Game.DamageEvents, d => d.Amount == 5);
    }

    [Fact]
    public void PickupsKeysAndDoorsAreShared()
    {
        (NetSession host, NetSession client) = Pair(Map);
        Run(host, client, 0.3);
        // The client walks to the key and through the door; the host never touches either.
        Game c = client.Game;
        host.Game.Enemies.ForEach(e => e.Health = 0);
        foreach (Enemy e in host.Game.Enemies) e.State = EnemyState.Dead;
        c.Player.Position = new Vec2(13.5, 2.5);
        c.Player.Angle = 0;
        Run(host, client, 2.5, bInput: new GameInput { Forward = true });
        Assert.Contains(KeyColor.Red, c.Player.Keys);
        Pickup key = host.Game.Pickups.Single(p => p.Kind == PickupKind.Key);
        Assert.True(key.Taken, "the host doesn't know the key was taken");
        Assert.Contains(KeyColor.Red, host.Game.Player.Keys); // keycards are shared
        Assert.All(host.Game.Level.Doors, d => Assert.True(d.IsOpen));
        Assert.Contains(host.Game.MessageLog, m => m.Text.Contains("Cal opened the RED door"));
    }

    [Fact]
    public void DeadPlayersRespawnAndTheGameGoesOn()
    {
        (NetSession host, NetSession client) = Pair(Map);
        Run(host, client, 0.3);
        client.Game.Player.Health = 1;
        Assert.True(client.Game.Player.Health > 0);
        host.Game.Others[0].Health = 1;
        typeof(Game).GetMethod("HurtRemote", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(host.Game, new object?[] { host.Game.Others[0], 50, Vec2.Zero, "a test" });
        Run(host, client, 0.3);
        Assert.Equal(GameState.Dead, client.Game.State);
        Assert.Equal("a test", client.Game.KilledBy);
        Assert.False(host.Game.Others[0].Alive);

        client.Game.Respawn();
        Run(host, client, 0.3);
        Assert.Equal(GameState.Playing, client.Game.State);
        Assert.True(host.Game.Others[0].Alive);
    }

    [Fact]
    public void HostLeavingEndsTheGameForClients()
    {
        (NetSession host, NetSession client) = Pair(Map);
        Run(host, client, 0.3);
        host.Dispose();
        client.Game.Update(1 / 60.0, new GameInput());
        client.Tick(1 / 60.0);
        Assert.True(client.Game.HostLeft);

        (NetSession host2, NetSession client2) = Pair(Map);
        Run(host2, client2, 0.3);
        client2.Dispose();
        host2.Game.Update(1 / 60.0, new GameInput());
        host2.Tick(1 / 60.0);
        Assert.Empty(host2.Game.Others);
        Assert.Contains(host2.Game.MessageLog, m => m.Text.Contains("Cal left"));
    }

    [Fact]
    public void SettingsComeFromDoomChatDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civdoom-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string defaults = Path.Combine(dir, "doomchat.defaults.json");
            File.WriteAllText(defaults, "{ \"chatFolder\": \"\\\\\\\\server\\\\share\\\\DoomChat-data\", \"serverUrl\": \"ws://x:5280/ws\" }");
            NetConfig cfg = NetConfig.Load(new[] { Path.Combine(dir, "missing.json"), defaults });
            if (!File.Exists(NetConfig.UserFile))
            {
                Assert.Equal(@"\\server\share\DoomChat-data", cfg.Folder);
                Assert.Equal(Path.Combine(@"\\server\share\DoomChat-data", "doom"), cfg.GamesFolder);
            }
            Assert.Equal("ws://x:5280/ws", cfg.ServerUrl);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LevelPackageRebuildsTheSameLevel()
    {
        Level level = BuiltInLevels.DetailGrid();
        var game = new Game(level, 3);
        LevelPackage p = LevelCodec.FromBytes(LevelCodec.ToBytes(LevelCodec.Pack(game, 3)));
        Game copy = LevelCodec.CreateGame(p, GameContent.BuiltIn);
        Assert.Equal(level.Walls.Count, copy.Level.Walls.Count);
        Assert.Equal(level.Doors.Count(), copy.Level.Doors.Count());
        Assert.Equal(level.Gates.Count(), copy.Level.Gates.Count());
        Assert.Equal(level.Terrain.Platforms.Count, copy.Level.Terrain.Platforms.Count);
        Assert.Equal(game.Theme.Id, copy.Theme.Id);
        Assert.Equal(game.Enemies.Select(e => e.Design.Id), copy.Enemies.Select(e => e.Design.Id));
        Assert.Equal(game.Pickups.Select(x => x.Weapon?.Id), copy.Pickups.Select(x => x.Weapon?.Id));
        Assert.Equal(game.Pickups.Select(x => x.Key), copy.Pickups.Select(x => x.Key));
        Assert.Equal(level.Exit, copy.Level.Exit);
    }

    [Fact]
    public void WorksOverASharedFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "civdoom-mp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cfg = new NetConfig { Folder = root, DisplayName = "Hana" };
            var hostGame = new Game(AsciiMap.Parse("t", Map), 5, WeaponTests.Dummies());
            using NetSession host = NetLobby.Host(cfg, hostGame, 5);

            // Let the host write its state so the game shows up as running.
            for (int i = 0; i < 20; i++) { host.Game.Update(0.016, new GameInput()); host.Tick(0.05); Thread.Sleep(20); }
            GameListing listing = Assert.Single(NetLobby.ListGames(cfg));
            Assert.Equal(host.GameId, listing.Id);
            Assert.Equal("Hana", listing.HostName);

            var joinCfg = new NetConfig { Folder = root, DisplayName = "Cal" };
            var content = new GameContent(WeaponTests.Dummies(), WeaponSet.BuiltIn, ThemeSet.BuiltIn, PlayerDesign.BuiltIn);
            using NetSession client = NetLobby.Join(joinCfg, host.GameId, content);
            Assert.Equal(hostGame.Level.Walls.Count, client.Game.Level.Walls.Count);

            Enemy m = host.Game.Enemies[0];
            int before = m.Health;
            Game c = client.Game;
            DateTime until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && (host.Game.Others.Count == 0 || m.Health == before))
            {
                c.Player.Angle = Math.Atan2(m.Position.Y - c.Player.Position.Y, m.Position.X - c.Player.Position.X);
                host.Game.Update(0.02, new GameInput());
                host.Tick(0.02);
                c.Update(0.02, new GameInput { Fire = c.Others.Count > 0 });
                client.Tick(0.02);
                Thread.Sleep(20);
            }
            Assert.Equal("Cal", Assert.Single(host.Game.Others).Name);
            Assert.True(m.Health < before, "the client's shots didn't reach the host through the folder");
            Assert.True(File.Exists(Path.Combine(root, "doom", host.GameId, "level.json.gz")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void InvitesAreDoomChatLobbyMessages()
    {
        string root = Path.Combine(Path.GetTempPath(), "civdoom-chat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cfg = new NetConfig { Folder = root, DisplayName = "Hana" };
            using NetSession host = NetLobby.Host(cfg, new Game(AsciiMap.Parse("t", Map), 5), 5);
            string channel = NetLobby.PostInvite(cfg, host);
            Assert.Equal(NetLobby.LobbyChannel, channel);
            string file = Directory.GetFiles(Path.Combine(root, "channels", NetLobby.LobbyChannel), "*.jsonl").Single();
            string line = File.ReadAllLines(file).Single();
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            string text = doc.RootElement.GetProperty("text").GetString()!;
            Assert.StartsWith(NetLobby.InvitePrefix, text);
            Assert.Equal(host.GameId, NetLobby.GameIdFromInvite(text));
            Assert.Equal(NetLobby.LobbyChannel, doc.RootElement.GetProperty("channel").GetString());
            Assert.True(doc.RootElement.GetProperty("id").GetInt64() > 0);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
