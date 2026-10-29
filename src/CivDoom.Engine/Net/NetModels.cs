namespace CivDoom.Engine;

/// <summary>Whether this game is on its own, runs the world for other players, or follows a host.</summary>
public enum NetRole
{
    /// <summary>Single player.</summary>
    Solo,

    /// <summary>Runs the monsters, doors and pickups for everyone and sends them out.</summary>
    Host,

    /// <summary>Plays its own character; the monsters are the host's, shown as they move.</summary>
    Client,
}

/// <summary>Another player in a multiplayer game, as last reported over the network.</summary>
public sealed class RemotePlayer
{
    public RemotePlayer(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; set; }

    /// <summary>Where they're drawn: eased towards <see cref="NetPosition"/> each frame.</summary>
    public Vec2 Position;
    public double Z;
    public double Angle;
    public int Health = Player.MaxHealth;
    public bool Dead;
    public bool Finished;
    public string? WeaponId;
    public bool Firing;
    public int Color = 0x40C0FF;

    /// <summary>Latest reported position and how fast they're moving (monsters lead their shots with it).</summary>
    public Vec2 NetPosition;
    public double NetZ;
    public Vec2 Velocity;

    /// <summary>Game time of the last update from them (they're dropped after a while without one).</summary>
    public double LastHeard;
    public long LastSeq = -1;
    public double RunPhase;

    /// <summary>Their own rockets and flames in flight (drawn only; their computer works out what they hit).</summary>
    public List<NetProjectile> Projectiles { get; } = new();

    public bool Alive => !Dead && !Finished && Health > 0;
}

/// <summary>Something flying that another computer is simulating, drawn as-is.</summary>
public readonly record struct NetProjectile(Vec2 Position, double Z, double Size, SpriteImage Sprite);

/// <summary>A short-lived explosion on the host's screen, copied to clients.</summary>
public readonly record struct NetEffect(Vec2 Position, double Z, double Size, double Age, double Duration);

/// <summary>
/// One-off messages between players (reliable and in order, unlike the regular state updates, which only
/// keep the latest). <see cref="To"/> = null means everyone.
/// </summary>
public sealed class NetEvent
{
    public const string DamageKind = "dmg";     // client -> host: I hit monster Index for Amount (from X,Y)
    public const string TakeKind = "take";      // client -> host: I picked up pickup Index
    public const string DoorKind = "door";      // client -> host: I opened the Key door
    public const string NoiseKind = "noise";    // client -> host: gunfire at X,Y heard Amount away
    public const string HurtKind = "hurt";      // host -> To: you took Amount damage from X,Y (Text = what)
    public const string SayKind = "say";        // anyone -> all: a line for the message feed
    public const string JoinKind = "join";      // anyone -> all: I've joined (Text = name)
    public const string LeaveKind = "leave";    // anyone -> all: I've left
    public const string EndKind = "end";        // host -> all: the host has closed the game

    public long Seq { get; set; }
    public string From { get; set; } = "";
    public string? To { get; set; }

    /// <summary>When it was sent (Unix ms): a joining player skips events from before they arrived.</summary>
    public long Ts { get; set; }
    public string Kind { get; set; } = "";
    public int Index { get; set; }
    public int Amount { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public KeyColor Key { get; set; }
    public string? Text { get; set; }
}

/// <summary>What each player sends out many times a second: where they are and what they're doing.</summary>
public sealed class PlayerState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long Seq { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double A { get; set; }
    public int Hp { get; set; }
    public bool Dead { get; set; }
    public bool Won { get; set; }
    public string? W { get; set; }
    public bool Fire { get; set; }

    /// <summary>Their own projectiles: x, y, z, size each, with the weapon ids in <see cref="Pw"/>.</summary>
    public double[][]? P { get; set; }
    public string[]? Pw { get; set; }

    /// <summary>The host also sends the world (monsters, doors, pickups...).</summary>
    public WorldState? World { get; set; }
}

/// <summary>The host's world, sent with its <see cref="PlayerState"/>.</summary>
public sealed class WorldState
{
    /// <summary>Monsters, in level order: x, y, z, health, state, enraged (0/1), pain (0/1).</summary>
    public double[][] M { get; set; } = Array.Empty<double[]>();

    /// <summary>Monster fireballs: x, y, z, size, with the monster design ids in <see cref="Ps"/>.</summary>
    public double[][] P { get; set; } = Array.Empty<double[]>();
    public string[] Ps { get; set; } = Array.Empty<string>();

    /// <summary>Explosions: x, y, z, size, age, duration.</summary>
    public double[][] Fx { get; set; } = Array.Empty<double[]>();

    /// <summary>'1' for each pickup that's been taken, in level order.</summary>
    public string Taken { get; set; } = "";

    public KeyColor[] Doors { get; set; } = Array.Empty<KeyColor>();
    public KeyColor[] Keys { get; set; } = Array.Empty<KeyColor>();
    public bool Gates { get; set; }

    /// <summary>Kills per player id.</summary>
    public Dictionary<string, int> Kills { get; set; } = new();
}
