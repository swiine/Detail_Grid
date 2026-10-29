namespace CivDoom.Engine;

/// <summary>
/// Multiplayer (co-op): the host's game runs the monsters, doors and pickups for everyone; each player's own
/// game moves their character and works out what their shots hit. <see cref="NetSession"/> carries the
/// updates; this half of <see cref="Game"/> produces and applies them.
/// </summary>
public sealed partial class Game
{
    private static readonly int[] PlayerColors = { 0x40C0FF, 0xFF6A3A, 0x60E060, 0xE060E0, 0xF0D040, 0x40F0D0, 0xFF5080, 0xA080FF };

    /// <summary>Solo, host or client (set by <see cref="NetSession"/>).</summary>
    public NetRole Role { get; internal set; } = NetRole.Solo;

    /// <summary>This player's id and name in a multiplayer game.</summary>
    public string LocalId { get; internal set; } = "me";
    public string LocalName { get; internal set; } = "You";

    /// <summary>The other players.</summary>
    public List<RemotePlayer> Others { get; } = new();

    /// <summary>The host's monster fireballs and explosions (on a client).</summary>
    public List<NetProjectile> NetProjectiles { get; } = new();
    public List<NetEffect> NetEffects { get; } = new();

    /// <summary>Kills per player id (multiplayer scoreboard).</summary>
    public Dictionary<string, int> PlayerKills { get; } = new();

    /// <summary>Events waiting for <see cref="NetSession"/> to send.</summary>
    internal List<NetEvent> Outbox { get; } = new();

    /// <summary>A colour for each player, the same on every computer.</summary>
    public static int ColorFor(string id)
    {
        int h = 0;
        foreach (char c in id) h = (h * 31 + c) & 0x7fffffff;
        return PlayerColors[h % PlayerColors.Length];
    }

    private readonly Dictionary<int, double> _myHits = new();

    private void SendEvent(NetEvent e)
    {
        if (Role == NetRole.Solo) return;
        Outbox.Add(e);
    }

    /// <summary>Says something here and on everyone else's screen.</summary>
    private void Announce(string text)
    {
        Say(text);
        SendEvent(new NetEvent { Kind = NetEvent.SayKind, Text = text });
    }

    private void Credit(string byId) => PlayerKills[byId] = PlayerKills.TryGetValue(byId, out int n) ? n + 1 : 1;

    // ---------------------------------------------------------------- who the monsters hunt

    /// <summary>A player monsters can hunt: you, or someone else in a multiplayer game.</summary>
    private readonly record struct Target(Vec2 Position, double Z, Vec2 Velocity, RemotePlayer? Remote, bool Alive);

    private Target LocalTarget => new(Player.Position, Player.Z, _playerVelocity, null, State == GameState.Playing && Player.Health > 0);

    /// <summary>Every player still in the fight.</summary>
    private IEnumerable<Target> Targets()
    {
        if (LocalTarget.Alive) yield return LocalTarget;
        foreach (RemotePlayer o in Others)
            if (o.Alive) yield return new Target(o.Position, o.Z, o.Velocity, o, true);
    }

    /// <summary>The nearest player this monster can see, or else the nearest one.</summary>
    private Target ChooseTarget(Enemy e)
    {
        if (Others.Count == 0) return LocalTarget;
        Target best = LocalTarget;
        double bestScore = double.PositiveInfinity;
        foreach (Target t in Targets())
        {
            double d = Vec2.Distance(e.Position, t.Position);
            bool seen = d < SightRange && Level.Index.HasLineOfSight(e.Position, t.Position);
            double score = d + (seen ? 0 : 1000);
            if (score < bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    private void HurtTarget(Target t, int damage, Vec2 from, string? source)
    {
        if (t.Remote is { } r) HurtRemote(r, damage, from, source);
        else HurtPlayer(damage, from, source);
    }

    private void HurtRemote(RemotePlayer r, int damage, Vec2 from, string? source)
    {
        r.Health = Math.Max(0, r.Health - damage); // until their next update says otherwise
        SendEvent(new NetEvent { Kind = NetEvent.HurtKind, To = r.Id, Amount = damage, X = from.X, Y = from.Y, Text = source });
    }

    // ---------------------------------------------------------------- every frame

    /// <summary>Other players glide to their latest reported position; silent ones are dropped.</summary>
    private void UpdateRemotePlayers(double dt)
    {
        for (int i = Others.Count - 1; i >= 0; i--)
        {
            RemotePlayer o = Others[i];
            if (Time - o.LastHeard > 12)
            {
                Others.RemoveAt(i);
                Say($"{o.Name} lost connection.");
                continue;
            }
            Vec2 before = o.Position;
            o.Position = Ease(o.Position, o.NetPosition, dt);
            o.Z += (o.NetZ - o.Z) * Math.Min(1, dt * 12);
            o.RunPhase += Vec2.Distance(before, o.Position) * 6;
        }
    }

    private static Vec2 Ease(Vec2 from, Vec2 to, double dt)
    {
        Vec2 gap = to - from;
        if (gap.LengthSquared > 4) return to; // too far behind (or respawned): jump there
        return from + gap * Math.Min(1, dt * 12);
    }

    /// <summary>On a client: monsters move smoothly to where the host says they are.</summary>
    private void UpdatePuppets(double dt)
    {
        UpdateRemotePlayers(dt);
        foreach (Enemy e in Enemies)
        {
            e.StateTime += dt;
            e.PainTime = Math.Max(0, e.PainTime - dt);
            if (!e.NetSeen) continue;
            Vec2 before = e.Position;
            e.Position = Ease(e.Position, e.NetPosition, dt);
            e.Z += (e.NetZ - e.Z) * Math.Min(1, dt * 12);
            if (Vec2.Distance(before, e.Position) > 1e-4) e.WalkPhase += dt * 6;
        }
        foreach (NetProjectile _ in NetProjectiles) { } // drawn where the host last put them
    }

    // ---------------------------------------------------------------- sending

    /// <summary>Your character, for everyone else (the host adds the world).</summary>
    public PlayerState CapturePlayer(long seq)
    {
        Player p = Player;
        var own = Projectiles.Where(pr => pr.FromPlayer && pr.Alive).ToList();
        var state = new PlayerState
        {
            Id = LocalId,
            Name = LocalName,
            Seq = seq,
            X = Math.Round(p.Position.X, 3),
            Y = Math.Round(p.Position.Y, 3),
            Z = Math.Round(p.Z, 3),
            A = Math.Round(p.Angle, 3),
            Hp = p.Health,
            Dead = State == GameState.Dead,
            Won = State is GameState.Exiting or GameState.Won,
            W = p.Weapon.Id,
            Fire = p.MuzzleFlashTime > 0,
            P = own.Count == 0 ? null : own.Select(pr => new[] { R(pr.Position.X), R(pr.Position.Y), R(pr.Z), R(pr.Size) }).ToArray(),
            Pw = own.Count == 0 ? null : own.Select(pr => pr.SourceId ?? "").ToArray(),
        };
        if (Role == NetRole.Host) state.World = CaptureWorld();
        return state;
    }

    private static double R(double v) => Math.Round(v, 3);

    /// <summary>The host's world: monsters, fireballs, explosions, pickups, doors, keys, gates and kills.</summary>
    public WorldState CaptureWorld()
    {
        var keys = new HashSet<KeyColor>(Player.Keys);
        return new WorldState
        {
            M = Enemies.Select(e => new[]
            {
                R(e.Position.X), R(e.Position.Y), R(e.Z), e.Health, (int)e.State, e.Enraged ? 1 : 0, e.PainTime > 0 ? 1 : 0,
            }).ToArray(),
            P = Projectiles.Where(p => !p.FromPlayer && p.Alive).Select(p => new[] { R(p.Position.X), R(p.Position.Y), R(p.Z), R(p.Size) }).ToArray(),
            Ps = Projectiles.Where(p => !p.FromPlayer && p.Alive).Select(p => p.SourceId ?? "").ToArray(),
            Fx = Effects.Select(f => new[] { R(f.Position.X), R(f.Position.Y), R(f.Z), R(f.Size), R(f.Age), R(f.Duration) }).ToArray(),
            Taken = new string(Pickups.Select(p => p.Taken ? '1' : '0').ToArray()),
            Doors = _doors.Where(d => d.IsOpen).Select(d => d.Key).Distinct().ToArray(),
            Keys = keys.ToArray(),
            Gates = Level.Gates.Any(g => g.IsOpen),
            Kills = new Dictionary<string, int>(PlayerKills),
        };
    }

    // ---------------------------------------------------------------- receiving

    /// <summary>Another player's latest update (and, from the host, the world).</summary>
    public void ApplyPlayer(PlayerState s)
    {
        if (s.Id == LocalId) return;
        RemotePlayer? o = Others.FirstOrDefault(x => x.Id == s.Id);
        bool isNew = o == null;
        if (o == null)
        {
            o = new RemotePlayer(s.Id, s.Name) { Color = ColorFor(s.Id), Position = new Vec2(s.X, s.Y), Z = s.Z };
            Others.Add(o);
        }
        if (s.Seq <= o.LastSeq) return; // out of date
        double since = Time - o.LastHeard;
        Vec2 pos = new(s.X, s.Y);
        if (!isNew && since > 1e-3 && since < 1) o.Velocity = (pos - o.NetPosition) / since;
        o.LastSeq = s.Seq;
        o.LastHeard = Time;
        o.Name = s.Name;
        o.NetPosition = pos;
        o.NetZ = s.Z;
        o.Angle = s.A;
        bool wasDead = o.Dead;
        o.Health = s.Hp;
        o.Dead = s.Dead;
        o.Finished = s.Won;
        o.WeaponId = s.W;
        o.Firing = s.Fire;
        if (o.Dead && !wasDead && !isNew && Role == NetRole.Host) Announce($"{o.Name} was killed.");
        o.Projectiles.Clear();
        if (s.P != null)
            for (int i = 0; i < s.P.Length; i++)
            {
                double[] v = s.P[i];
                if (v.Length < 4) continue;
                SpriteImage sprite = Weapons.Find(s.Pw is { } ids && i < ids.Length ? ids[i] : null)?.Projectile ?? Art.FireballSprite;
                o.Projectiles.Add(new NetProjectile(new Vec2(v[0], v[1]), v[2], v[3], sprite));
            }
        if (s.World != null && Role == NetRole.Client) ApplyWorld(s.World);
    }

    /// <summary>On a client: the host's monsters, fireballs, pickups, doors, keys and gates.</summary>
    public void ApplyWorld(WorldState w)
    {
        for (int i = 0; i < Enemies.Count && i < w.M.Length; i++)
        {
            Enemy e = Enemies[i];
            double[] m = w.M[i];
            if (m.Length < 7) continue;
            e.NetPosition = new Vec2(m[0], m[1]);
            e.NetZ = m[2];
            if (!e.NetSeen) { e.Position = e.NetPosition; e.Z = e.NetZ; e.NetSeen = true; }
            bool wasAlive = e.IsAlive;
            e.Health = (int)m[3];
            var state = (EnemyState)(int)m[4];
            if (state != e.State) { e.State = state; e.StateTime = 0; }
            e.Enraged = m[5] > 0;
            if (m[6] > 0) e.PainTime = Math.Max(e.PainTime, 0.1);
            if (wasAlive && !e.IsAlive && _myHits.TryGetValue(i, out double hit) && Time - hit < 1.5) LastKillTime = Time;
        }

        NetProjectiles.Clear();
        for (int i = 0; i < w.P.Length; i++)
        {
            double[] v = w.P[i];
            if (v.Length < 4) continue;
            MonsterDesign? d = Monsters.Find(i < w.Ps.Length ? w.Ps[i] : null);
            SpriteImage sprite = d?.ProjectileColor != null ? d.ProjectileSprite : Art.FireballSprite;
            NetProjectiles.Add(new NetProjectile(new Vec2(v[0], v[1]), v[2], v[3], sprite));
        }
        NetEffects.Clear();
        foreach (double[] f in w.Fx)
            if (f.Length >= 6) NetEffects.Add(new NetEffect(new Vec2(f[0], f[1]), f[2], f[3], f[4], f[5]));

        for (int i = 0; i < Pickups.Count && i < w.Taken.Length; i++)
            if (w.Taken[i] == '1') Pickups[i].Taken = true;
        foreach (KeyColor k in w.Keys) Player.Keys.Add(k); // keycards are shared by the team
        foreach (KeyColor k in w.Doors) OpenDoors(k, Player.Position);
        if (w.Gates)
            foreach (Wall g in Level.Gates) g.IsOpen = true;
        OpenGatesIfDue();
        PlayerKills.Clear();
        foreach ((string id, int n) in w.Kills) PlayerKills[id] = n;
    }

    /// <summary>An event from another player.</summary>
    public void ApplyEvent(NetEvent e)
    {
        if (e.From == LocalId || (e.To != null && e.To != LocalId)) return;
        RemotePlayer? from = Others.FirstOrDefault(o => o.Id == e.From);
        switch (e.Kind)
        {
            case NetEvent.DamageKind when Role == NetRole.Host && e.Index >= 0 && e.Index < Enemies.Count:
                ApplyDamage(Enemies[e.Index], Math.Clamp(e.Amount, 0, 10_000), new Vec2(e.X, e.Y), e.From);
                break;
            case NetEvent.TakeKind when Role == NetRole.Host && e.Index >= 0 && e.Index < Pickups.Count:
            {
                Pickup pk = Pickups[e.Index];
                pk.Taken = true;
                if (pk.Kind == PickupKind.Key)
                {
                    Player.Keys.Add(pk.Key);
                    Announce($"{from?.Name ?? "Someone"} found the {Keycards.Name(pk.Key)} keycard.");
                }
                break;
            }
            case NetEvent.DoorKind when Role == NetRole.Host:
                Player.Keys.Add(e.Key);
                OpenDoors(e.Key, from?.Position ?? Player.Position);
                Announce($"{from?.Name ?? "Someone"} opened the {Keycards.Name(e.Key)} door.");
                break;
            case NetEvent.NoiseKind when Role == NetRole.Host:
                Alert(new Vec2(e.X, e.Y), Math.Clamp(e.Amount, 1, 12));
                break;
            case NetEvent.HurtKind:
                HurtPlayer(Math.Clamp(e.Amount, 0, 1000), new Vec2(e.X, e.Y), e.Text);
                break;
            case NetEvent.SayKind when !string.IsNullOrWhiteSpace(e.Text):
                Say(e.Text.Length > 120 ? e.Text[..120] : e.Text);
                break;
            case NetEvent.JoinKind:
                Say($"{e.Text ?? "Someone"} joined the game.");
                break;
            case NetEvent.LeaveKind:
                if (from != null) Others.Remove(from);
                Say($"{from?.Name ?? e.Text ?? "Someone"} left the game.");
                break;
            case NetEvent.EndKind when Role == NetRole.Client:
                HostLeft = true;
                Say("The host ended the game.");
                break;
        }
    }

    /// <summary>On a client: the host has gone, so the monsters have stopped.</summary>
    public bool HostLeft { get; internal set; }

    /// <summary>Which spawn point this player uses (0 = the level start), so players don't start inside each other.</summary>
    public int SpawnIndex { get; set; }

    /// <summary>The start position for this player.</summary>
    public Vec2 SpawnPoint()
    {
        Vec2 start = Level.PlayerStart;
        if (SpawnIndex <= 0) return start;
        double a = Level.PlayerAngle + Math.PI / 2 + (SpawnIndex - 1) * Math.PI / 3;
        Vec2 wanted = start + Vec2.FromAngle(a) * (0.45 + 0.25 * ((SpawnIndex - 1) / 6));
        if (Level.Index.SegmentCrossesWall(start, wanted)) return start;
        return LevelBuilder.FindClearSpot(Level.Index, wanted, PlayerRadius * 1.5);
    }

    /// <summary>Puts this player at their spawn point (used when joining a multiplayer game).</summary>
    public void MoveToSpawn()
    {
        Player.Position = SpawnPoint();
        Player.Z = Player.ViewZ = Level.Terrain.FloorAt(Player.Position);
    }

    /// <summary>Multiplayer: come back to life at the start, keeping your weapons, while the game goes on.</summary>
    public void Respawn()
    {
        if (State != GameState.Dead) return;
        Player p = Player;
        p.Health = Player.MaxHealth;
        p.DamageFlash = 0;
        p.VelocityZ = 0;
        p.OnGround = true;
        MoveToSpawn();
        p.Angle = Level.PlayerAngle;
        KilledBy = null;
        State = GameState.Playing;
        Announce($"{LocalName} is back in the fight.");
    }
}
