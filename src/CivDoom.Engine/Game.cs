namespace CivDoom.Engine;

/// <summary>Per-frame input, filled in by whatever window hosts the game.</summary>
public sealed class GameInput
{
    public bool Forward, Back, StrafeLeft, StrafeRight, TurnLeft, TurnRight, Fire, Run;

    /// <summary>Radians to turn this frame from mouse movement (positive = turn left / counter-clockwise).</summary>
    public double MouseTurn;

    public void Clear()
    {
        Forward = Back = StrafeLeft = StrafeRight = TurnLeft = TurnRight = Fire = Run = false;
        MouseTurn = 0;
    }
}

public enum GameState
{
    Playing,
    Dead,
    Won,
}

public enum EnemyState
{
    Idle,
    Chase,
    Attack,
    Dead,
}

public sealed class Player
{
    public const int MaxHealth = 100;
    public const int MaxAmmo = 200;

    public Vec2 Position;
    public double Angle;
    public int Health = MaxHealth;
    public int Ammo = 50;
    public double FireCooldown;
    public double MuzzleFlashTime;
    public double DamageFlash;
    public double PickupFlash;
    public double BobPhase;
    public double BobAmount;

    public Vec2 Direction => Vec2.FromAngle(Angle);
}

public sealed class Enemy
{
    public Enemy(EnemySpawn spawn)
    {
        Kind = spawn.Kind;
        Position = spawn.Position;
        Health = Kind == EnemyKind.Brute ? 180 : 50;
    }

    public EnemyKind Kind { get; }
    public Vec2 Position;
    public int Health;
    public EnemyState State = EnemyState.Idle;
    public double AttackCooldown = 1.0;
    public double StateTime;
    public double PainTime;
    public double WalkPhase;
    public Vec2 LastKnownPlayer;
    public Vec2 Detour;
    public double DetourTime;

    public double Radius => Kind == EnemyKind.Brute ? 0.2 : 0.14;
    public double Speed => Kind == EnemyKind.Brute ? 0.9 : 1.6;

    /// <summary>Sprite height in world units (walls are 1.0).</summary>
    public double Height => Kind == EnemyKind.Brute ? 0.85 : 0.6;

    public bool IsAlive => State != EnemyState.Dead;
}

public sealed class Projectile
{
    public Vec2 Position;
    public Vec2 Velocity;
    public int Damage;
    public bool Alive = true;
}

public sealed class Pickup
{
    public Pickup(PickupSpawn spawn)
    {
        Kind = spawn.Kind;
        Position = spawn.Position;
    }

    public PickupKind Kind { get; }
    public Vec2 Position { get; }
    public bool Taken;
}

/// <summary>The game simulation. Knows nothing about windows or drawing.</summary>
public sealed class Game
{
    public const double PlayerRadius = LevelBuilder.PlayerRadius;
    public const double WalkSpeed = 2.2;
    public const double RunSpeed = 3.8;
    public const double TurnSpeed = 2.6;
    public const double SightRange = 14;

    private readonly Random _rng;
    private readonly List<string> _messages = new();
    private double _messageTime;

    public Game(Level level, int seed = 1234)
    {
        Level = level;
        _rng = new Random(seed);
        Player = new Player { Position = level.PlayerStart, Angle = level.PlayerAngle };
        Enemies = level.Enemies.Select(s => new Enemy(s)).ToList();
        Pickups = level.Pickups.Select(s => new Pickup(s)).ToList();
        Say($"{level.Name} - {Enemies.Count} hostiles detected");
    }

    public Level Level { get; }
    public Player Player { get; }
    public List<Enemy> Enemies { get; }
    public List<Projectile> Projectiles { get; } = new();
    public List<Pickup> Pickups { get; }
    public GameState State { get; private set; } = GameState.Playing;
    public double Time { get; private set; }
    public int Kills => Enemies.Count(e => !e.IsAlive);

    /// <summary>The most recent status message, or null once it has faded.</summary>
    public string? Message => _messageTime > 0 && _messages.Count > 0 ? _messages[^1] : null;

    public void Say(string message)
    {
        _messages.Add(message);
        _messageTime = 3;
    }

    public void Update(double dt, GameInput input)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        Time += dt;
        _messageTime -= dt;

        Player p = Player;
        p.FireCooldown = Math.Max(0, p.FireCooldown - dt);
        p.MuzzleFlashTime = Math.Max(0, p.MuzzleFlashTime - dt);
        p.DamageFlash = Math.Max(0, p.DamageFlash - dt * 1.5);
        p.PickupFlash = Math.Max(0, p.PickupFlash - dt * 2);

        if (State == GameState.Playing)
        {
            UpdatePlayer(dt, input);
        }

        foreach (Enemy e in Enemies) UpdateEnemy(e, dt);
        UpdateProjectiles(dt);

        if (State == GameState.Playing && Enemies.Count > 0 && Enemies.All(e => !e.IsAlive))
        {
            State = GameState.Won;
            Say("Drawing purged. Nice work.");
        }
    }

    private void UpdatePlayer(double dt, GameInput input)
    {
        Player p = Player;
        double turn = input.MouseTurn;
        if (input.TurnLeft) turn += TurnSpeed * dt;
        if (input.TurnRight) turn -= TurnSpeed * dt;
        p.Angle = NormalizeAngle(p.Angle + turn);

        Vec2 fwd = p.Direction, right = fwd.PerpRight();
        Vec2 wish = Vec2.Zero;
        if (input.Forward) wish += fwd;
        if (input.Back) wish -= fwd;
        if (input.StrafeRight) wish += right;
        if (input.StrafeLeft) wish -= right;

        double speed = input.Run ? RunSpeed : WalkSpeed;
        if (wish.LengthSquared > 0)
        {
            Vec2 before = p.Position;
            p.Position = Move(p.Position, wish.Normalized() * speed * dt, PlayerRadius);
            double moved = Vec2.Distance(before, p.Position);
            p.BobPhase += moved * 9;
            p.BobAmount = Math.Min(1, p.BobAmount + dt * 4);
        }
        else
        {
            p.BobAmount = Math.Max(0, p.BobAmount - dt * 4);
        }

        if (input.Fire && p.FireCooldown <= 0)
        {
            if (p.Ammo > 0) FireHitscan();
            else if (p.FireCooldown <= 0)
            {
                Say("Out of ammo!");
                p.FireCooldown = 0.5;
            }
        }

        foreach (Pickup pickup in Pickups)
        {
            if (pickup.Taken || Vec2.Distance(pickup.Position, p.Position) > PlayerRadius + 0.2) continue;
            if (pickup.Kind == PickupKind.Health && p.Health < Player.MaxHealth)
            {
                p.Health = Math.Min(Player.MaxHealth, p.Health + 25);
                pickup.Taken = true;
                Say("Picked up a medkit.");
            }
            else if (pickup.Kind == PickupKind.Ammo && p.Ammo < Player.MaxAmmo)
            {
                p.Ammo = Math.Min(Player.MaxAmmo, p.Ammo + 20);
                pickup.Taken = true;
                Say("Picked up a box of bullets.");
            }
            if (pickup.Taken) p.PickupFlash = 1;
        }
    }

    private void FireHitscan()
    {
        Player p = Player;
        p.Ammo--;
        p.FireCooldown = 0.28;
        p.MuzzleFlashTime = 0.09;

        // A touch of spread so it doesn't feel like a laser.
        double angle = p.Angle + (_rng.NextDouble() - 0.5) * 0.03;
        Vec2 dir = Vec2.FromAngle(angle);
        double wallDist = Level.Index.CastRay(p.Position, dir, 64)?.Distance ?? 64;

        Enemy? target = null;
        double best = wallDist;
        foreach (Enemy e in Enemies)
        {
            if (!e.IsAlive) continue;
            Vec2 rel = e.Position - p.Position;
            double along = Vec2.Dot(rel, dir);
            if (along <= 0 || along >= best) continue;
            double off = Math.Abs(Vec2.Cross(dir, rel));
            if (off > e.Radius + 0.05) continue;
            best = along;
            target = e;
        }

        if (target == null) return;
        int damage = 12 + _rng.Next(0, 14);
        Damage(target, damage);
    }

    private void Damage(Enemy e, int damage)
    {
        e.Health -= damage;
        e.PainTime = 0.15;
        e.LastKnownPlayer = Player.Position;
        if (e.State == EnemyState.Idle) e.State = EnemyState.Chase;
        if (e.Health <= 0)
        {
            e.State = EnemyState.Dead;
            e.StateTime = 0;
            int left = Enemies.Count(x => x.IsAlive);
            Say(left == 0 ? "All hostiles eliminated!" : $"{(e.Kind == EnemyKind.Brute ? "Brute" : "Imp")} down. {left} left.");
        }
    }

    private void UpdateEnemy(Enemy e, double dt)
    {
        e.StateTime += dt;
        e.PainTime = Math.Max(0, e.PainTime - dt);
        if (!e.IsAlive) return;

        Player p = Player;
        double dist = Vec2.Distance(e.Position, p.Position);
        bool canSee = State == GameState.Playing && dist < SightRange && Level.Index.HasLineOfSight(e.Position, p.Position);
        if (canSee) e.LastKnownPlayer = p.Position;

        switch (e.State)
        {
            case EnemyState.Idle:
                if (canSee)
                {
                    e.State = EnemyState.Chase;
                    e.StateTime = 0;
                }
                break;

            case EnemyState.Chase:
            {
                e.AttackCooldown -= dt;
                if (canSee && e.AttackCooldown <= 0 && dist < 9)
                {
                    e.State = EnemyState.Attack;
                    e.StateTime = 0;
                    break;
                }
                if (e.PainTime > 0) break; // flinch

                Vec2 target = e.LastKnownPlayer;
                Vec2 toTarget = target - e.Position;
                if (toTarget.Length < e.Radius + PlayerRadius + 0.1) break;

                Vec2 dir = toTarget.Normalized();
                if (e.DetourTime > 0)
                {
                    e.DetourTime -= dt;
                    dir = (dir * 0.3 + e.Detour).Normalized();
                }

                Vec2 before = e.Position;
                Vec2 step = dir * e.Speed * dt;
                e.Position = Move(e.Position, step, e.Radius);
                e.WalkPhase += dt * 6;

                // Stuck on a wall corner? Side-step for a moment.
                if (Vec2.Distance(before, e.Position) < step.Length * 0.3 && e.DetourTime <= 0)
                {
                    e.Detour = _rng.Next(2) == 0 ? dir.PerpRight() : -dir.PerpRight();
                    e.DetourTime = 0.6;
                }
                break;
            }

            case EnemyState.Attack:
                if (e.StateTime >= 0.45)
                {
                    if (State == GameState.Playing && Level.Index.HasLineOfSight(e.Position, p.Position))
                    {
                        Vec2 dir = (p.Position - e.Position).Normalized();
                        double speed = e.Kind == EnemyKind.Brute ? 3.5 : 4.5;
                        Projectiles.Add(new Projectile
                        {
                            Position = e.Position + dir * (e.Radius + 0.05),
                            Velocity = dir * speed,
                            Damage = e.Kind == EnemyKind.Brute ? 20 + _rng.Next(10) : 6 + _rng.Next(8),
                        });
                    }
                    e.State = EnemyState.Chase;
                    e.StateTime = 0;
                    e.AttackCooldown = (e.Kind == EnemyKind.Brute ? 2.2 : 1.4) + _rng.NextDouble();
                }
                break;
        }
    }

    private void UpdateProjectiles(double dt)
    {
        foreach (Projectile pr in Projectiles)
        {
            Vec2 step = pr.Velocity * dt;
            double len = step.Length;
            if (len > 0 && Level.Index.CastRay(pr.Position, step / len, len) != null)
            {
                pr.Alive = false;
                continue;
            }
            pr.Position += step;

            if (State == GameState.Playing && Vec2.Distance(pr.Position, Player.Position) < PlayerRadius + 0.08)
            {
                pr.Alive = false;
                HurtPlayer(pr.Damage);
            }
            else if (Vec2.Distance(pr.Position, Player.Position) > 80)
            {
                pr.Alive = false;
            }
        }
        Projectiles.RemoveAll(pr => !pr.Alive);
    }

    private void HurtPlayer(int damage)
    {
        Player.Health = Math.Max(0, Player.Health - damage);
        Player.DamageFlash = Math.Min(1, Player.DamageFlash + damage / 25.0);
        if (Player.Health == 0 && State == GameState.Playing)
        {
            State = GameState.Dead;
            Say("You died. Press Enter to try again.");
        }
    }

    /// <summary>
    /// Moves a circle through the level, sliding along walls. The move is split into sub-steps
    /// no longer than half the radius so fast movers can't tunnel through thin lines.
    /// </summary>
    public Vec2 Move(Vec2 from, Vec2 delta, double radius)
    {
        double len = delta.Length;
        if (len < 1e-12) return from;
        int steps = Math.Max(1, (int)Math.Ceiling(len / (radius * 0.5)));
        Vec2 step = delta / steps;
        Vec2 pos = from;
        for (int i = 0; i < steps; i++)
        {
            Vec2 next = Resolve(pos + step, pos, radius);
            if (Level.Index.SegmentCrossesWall(pos, next)) break;
            pos = next;
        }
        return pos;
    }

    private Vec2 Resolve(Vec2 p, Vec2 previous, double radius)
    {
        Vec2 r = new(radius, radius);
        for (int iter = 0; iter < 4; iter++)
        {
            bool pushed = false;
            foreach (Wall w in Level.Index.Query(p - r, p + r))
            {
                Vec2 q = w.ClosestPoint(p);
                Vec2 away = p - q;
                double d = away.Length;
                if (d >= radius) continue;
                Vec2 n;
                if (d > 1e-9) n = away / d;
                else
                {
                    // Exactly on the line: push back to the side we came from.
                    n = w.Normal;
                    if (Vec2.Dot(previous - w.A, n) < 0) n = -n;
                }
                p += n * (radius - d + 1e-6);
                pushed = true;
            }
            if (!pushed) break;
        }
        return p;
    }

    private static double NormalizeAngle(double a)
    {
        a %= 2 * Math.PI;
        return a < 0 ? a + 2 * Math.PI : a;
    }
}
