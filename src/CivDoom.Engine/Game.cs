namespace CivDoom.Engine;

/// <summary>Per-frame input, filled in by whatever window hosts the game.</summary>
public sealed class GameInput
{
    public bool Forward, Back, StrafeLeft, StrafeRight, TurnLeft, TurnRight, Fire, Run, Jump;

    /// <summary>Radians to turn this frame from mouse movement (positive = turn left / counter-clockwise).</summary>
    public double MouseTurn;

    /// <summary>Weapon slot key pressed this frame (1-9), or 0.</summary>
    public int SelectSlot;

    /// <summary>+1 / -1 to cycle to the next / previous weapon (mouse wheel), or 0.</summary>
    public int CycleWeapon;

    public void Clear()
    {
        Forward = Back = StrafeLeft = StrafeRight = TurnLeft = TurnRight = Fire = Run = Jump = false;
        MouseTurn = 0;
        SelectSlot = CycleWeapon = 0;
    }
}

public enum GameState
{
    Playing,
    Dead,

    /// <summary>Crossed the finish line: the camera pulls back and you watch yourself run off the map.</summary>
    Exiting,

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

    public Vec2 Position;
    public double Angle;
    public int Health = MaxHealth;

    /// <summary>Height of your feet above the ground (0 unless you're on a platform or in the air).</summary>
    public double Z;
    public double VelocityZ;
    public bool OnGround = true;

    /// <summary><see cref="Z"/> smoothed for the camera, so stepping up stairs doesn't jolt the view.</summary>
    public double ViewZ;

    /// <summary>Ammo carried, by ammo type ("bullets", "shells", ...).</summary>
    public Dictionary<string, int> Ammo { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Weapons carried, in slot order.</summary>
    public List<WeaponDesign> Weapons { get; } = new();

    public WeaponDesign Weapon = null!;

    public int AmmoOf(string type) => Ammo.TryGetValue(type, out int n) ? n : 0;

    /// <summary>Shots left in <paramref name="w"/>, or null if it never runs out.</summary>
    public int? ShotsLeft(WeaponDesign w) => w.UsesAmmo ? AmmoOf(w.AmmoType) : null;

    public bool CanFire(WeaponDesign w) => !w.UsesAmmo || AmmoOf(w.AmmoType) >= w.AmmoPerShot;

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
    public Enemy(EnemySpawn spawn, MonsterDesign design)
    {
        Design = design;
        Position = spawn.Position;
        Health = design.Health;
    }

    /// <summary>Stats and pictures. Replaced in place when the monster files are reloaded.</summary>
    public MonsterDesign Design { get; set; }
    public Vec2 Position;

    /// <summary>Height of its feet (the floor it stands on; fliers float <see cref="MonsterDesign.FloatHeight"/> above that).</summary>
    public double Z;
    public int Health;
    public EnemyState State = EnemyState.Idle;
    public double AttackCooldown = 1.0;
    public double StateTime;
    public double PainTime;
    public double WalkPhase;
    public Vec2 LastKnownPlayer;
    public Vec2 Detour;
    public double DetourTime;

    /// <summary>+1 / -1: which way it circles you.</summary>
    public int StrafeDirection = 1;
    public double StrafeSwitchTime;

    /// <summary>Side-step from a dodge, and how long it lasts.</summary>
    public Vec2 DodgeVelocity;
    public double DodgeTime;

    /// <summary>Time left falling back when badly hurt (only once).</summary>
    public double RetreatTime;
    public bool HasRetreated;

    public double MeleeCooldown;

    /// <summary>Bosses get angrier at half health: faster attacks, an extra shot.</summary>
    public bool Enraged;

    /// <summary>Random per-monster number, for varied movement.</summary>
    public int Seed;

    public double Radius => Design.Radius;
    public double Speed => Design.Speed;

    public bool IsAlive => State != EnemyState.Dead;
}

public sealed class Projectile
{
    public Vec2 Position;
    public Vec2 Velocity;

    /// <summary>Height above the ground, and how fast that changes (shots aim up and down at targets on platforms).</summary>
    public double Z = 0.3;
    public double VelocityZ;
    public int Damage;
    public bool Alive = true;

    /// <summary>True for the player's rockets/flames (hurt monsters), false for monster fireballs (hurt you).</summary>
    public bool FromPlayer;

    public double SplashRadius;
    public int SplashDamage;

    /// <summary>Distance left before it fizzles out (infinite by default).</summary>
    public double RangeLeft = double.PositiveInfinity;

    public double Size = 0.18;

    /// <summary>Picture to draw, or null for the monster fireball.</summary>
    public SpriteImage? Sprite;

    /// <summary>Where it was fired from and who fired it (for the HUD's damage arrows and death screen).</summary>
    public Vec2 Origin;
    public string? SourceName;
}

/// <summary>A short-lived visual, e.g. an explosion.</summary>
public sealed class Effect
{
    public Vec2 Position;
    public double Z;
    public double Age;
    public double Duration = 0.3;
    public double Size = 0.5;
}

public sealed class Pickup
{
    public Pickup(PickupSpawn spawn, WeaponDesign? weapon = null)
    {
        Kind = weapon == null && spawn.Kind == PickupKind.Weapon ? PickupKind.Ammo : spawn.Kind;
        Position = spawn.Position;
        Weapon = weapon;
    }

    public PickupKind Kind { get; }
    public Vec2 Position { get; }

    /// <summary>The weapon lying here, for <see cref="PickupKind.Weapon"/>.</summary>
    public WeaponDesign? Weapon { get; set; }

    public bool Taken;
}

public readonly record struct HudMessage(string Text, double Time);

public readonly record struct DamageEvent(Vec2 From, double Time, int Amount);

/// <summary>The game simulation. Knows nothing about windows or drawing.</summary>
public sealed class Game
{
    public const double PlayerRadius = LevelBuilder.PlayerRadius;
    public const double WalkSpeed = 2.2;
    public const double RunSpeed = 3.8;
    public const double TurnSpeed = 2.6;
    public const double SightRange = 14;
    public const double Gravity = 12;

    /// <summary>Lifts your feet about 0.57 wall heights: enough for a ledge a bit over half a wall high.</summary>
    public const double JumpSpeed = 3.7;

    private readonly Random _rng;
    private readonly List<string> _messages = new();
    private double _messageTime;

    public Game(Level level, int seed, GameContent content)
        : this(level, seed, content.Monsters, content.Weapons, content.Themes, content.Player)
    {
    }

    public Game(Level level, int seed = 1234, MonsterSet? monsters = null, WeaponSet? weapons = null,
                ThemeSet? themes = null, PlayerDesign? player = null)
    {
        Level = level;
        _rng = new Random(seed);
        Monsters = monsters ?? MonsterSet.BuiltIn;
        Weapons = weapons ?? WeaponSet.BuiltIn;
        Themes = themes ?? ThemeSet.BuiltIn;
        Theme = Themes.Resolve(level.ThemeId, _rng);
        PlayerLook = player ?? PlayerDesign.BuiltIn;
        Player = new Player { Position = level.PlayerStart, Angle = level.PlayerAngle };
        Player.Z = Player.ViewZ = level.Terrain.FloorAt(level.PlayerStart);
        Enemies = CreateEnemies(level);
        foreach (Enemy e in Enemies) e.Z = level.Terrain.FloorAt(e.Position);
        if (level.Exit is { } ex)
            FinalBoss = Enemies.Where(e => e.Design.Boss).OrderBy(e => Vec2.Distance(e.Position, ex)).FirstOrDefault();
        foreach (Wall g in level.Gates) g.IsOpen = false; // levels can be replayed
        OpenGatesIfDue();
        Pickups = CreatePickups(level.Pickups);
        GiveStartingWeapons();
        Say($"{level.Name} ({Theme.Name}) - {Enemies.Count} hostiles detected");
        ReportWarnings(Monsters.Warnings);
        ReportWarnings(Weapons.Warnings);
        ReportWarnings(Themes.Warnings);
        ReportWarnings(PlayerLook.Warnings);
    }

    public ThemeSet Themes { get; private set; }

    /// <summary>The look of this level (sky, skyline, walls, floor).</summary>
    public ThemeDesign Theme { get; private set; }

    /// <summary>Your character's pictures, for the finish-line cutscene.</summary>
    public PlayerDesign PlayerLook { get; private set; }

    /// <summary>Swaps in freshly loaded themes and player pictures (F5).</summary>
    public void ReloadLook(ThemeSet themes, PlayerDesign player)
    {
        Themes = themes;
        Theme = themes.Find(Theme.Id) ?? themes.Resolve(Level.ThemeId, _rng);
        PlayerLook = player;
        ReportWarnings(themes.Warnings);
        ReportWarnings(player.Warnings);
    }

    // ---- Stats for the level-complete card ----
    public int DamageTaken { get; private set; }
    public int BossesKilled => Enemies.Count(e => e.Design.Boss && !e.IsAlive);
    public int BossCount => Enemies.Count(e => e.Design.Boss);

    /// <summary>Seconds it took to reach the finish (frozen once you cross it).</summary>
    public double CompletionTime { get; private set; }

    // ---- Finish-line cutscene ----
    public const double CutsceneLength = 3.2;
    private double _cutsceneTime;
    private Vec2 _runDirection, _cameraPosition;
    private double _cameraAngle;

    /// <summary>Where the view is from: your eyes, or the pulled-back camera during the cutscene.</summary>
    public (Vec2 Position, double Angle) Camera =>
        State == GameState.Exiting || (State == GameState.Won && _cutsceneTime > 0)
            ? (_cameraPosition, _cameraAngle)
            : (Player.Position, Player.Angle);

    /// <summary>Height of the camera (eye level).</summary>
    public double CameraZ =>
        (State == GameState.Exiting || (State == GameState.Won && _cutsceneTime > 0)
            ? Level.Terrain.FloorAt(_cameraPosition)
            : Player.ViewZ) + Renderer.EyeHeight;

    /// <summary>True while your character should be drawn running (the cutscene).</summary>
    public bool ShowPlayerCharacter => State == GameState.Exiting;

    /// <summary>The running frame to show (alternates while running).</summary>
    public SpriteImage PlayerFrame => ((int)(_cutsceneTime * 8) & 1) == 0 ? PlayerLook.Run : PlayerLook.Run2;

    /// <summary>0 = clear, 1 = black: fades out at the end of the cutscene.</summary>
    public double Fade => State == GameState.Won && _cutsceneTime > 0 ? 1 : Math.Clamp((_cutsceneTime - (CutsceneLength - 1.0)) / 1.0, 0, 1);

    private void StartExitCutscene(Vec2 exit)
    {
        State = GameState.Exiting;
        CompletionTime = Time;
        Projectiles.Clear();
        Vec2 dir = exit - Player.Position;
        _runDirection = dir.LengthSquared > 1e-4 ? dir.Normalized() : Player.Direction;
        Player.Position = exit;
        Player.Angle = Math.Atan2(_runDirection.Y, _runDirection.X);

        // Camera a little behind the finish line (not inside a wall), looking the way you run.
        double back = 1.8;
        if (Level.Index.CastRay(exit, -_runDirection, back) is { } hit) back = Math.Max(0.3, hit.Distance - 0.2);
        _cameraPosition = exit - _runDirection * back;
        _cameraAngle = Player.Angle;
        Say("You crossed the finish line!");
    }

    private void UpdateCutscene(double dt)
    {
        _cutsceneTime += dt;
        // Sprint away, straight through whatever is in the way: you're leaving the map.
        Player.Position += _runDirection * RunSpeed * 1.2 * dt;
        Player.BobPhase += dt * 12;
        if (_cutsceneTime >= CutsceneLength)
        {
            State = GameState.Won;
            Say("LEVEL COMPLETE");
        }
    }

    public MonsterSet Monsters { get; private set; }
    public WeaponSet Weapons { get; private set; }
    public List<Effect> Effects { get; } = new();

    /// <summary>Random-boss spawns get different bosses where possible (each fight is a new one).</summary>
    private List<Enemy> CreateEnemies(Level level)
    {
        List<MonsterDesign> bossDeck = Monsters.Bosses.OrderBy(_ => _rng.Next()).ToList();
        int dealt = 0;
        var result = new List<Enemy>();
        foreach (EnemySpawn s in level.Enemies)
        {
            MonsterDesign d = s.Kind == MonsterSet.RandomBoss && bossDeck.Count > 0
                ? bossDeck[dealt++ % bossDeck.Count]
                : Monsters.Resolve(s.Kind, _rng);
            result.Add(new Enemy(s, d) { Seed = _rng.Next(1000) });
        }
        return result;
    }

    private void GiveStartingWeapons()
    {
        List<WeaponDesign> start = Weapons.Designs.Where(w => w.StartWith).ToList();
        if (start.Count == 0) start.Add(Weapons.Designs[0]);
        foreach (WeaponDesign w in start)
        {
            Player.Weapons.Add(w);
            if (w.UsesAmmo) Player.Ammo[w.AmmoType] = Math.Min(w.MaxAmmo, Math.Max(Player.AmmoOf(w.AmmoType), w.StartAmmo));
        }
        // Hold the best starting weapon that can actually fire.
        Player.Weapon = start.LastOrDefault(Player.CanFire) ?? start[^1];
    }

    /// <summary>Weapon pickups name a weapon by id, or leave it open to be dealt out round-robin.</summary>
    private List<Pickup> CreatePickups(IReadOnlyList<PickupSpawn> spawns)
    {
        List<WeaponDesign> dealable = Weapons.Designs.Where(w => !w.StartWith).ToList();
        int next = 0;
        var result = new List<Pickup>();
        foreach (PickupSpawn s in spawns)
        {
            WeaponDesign? weapon = null;
            if (s.Kind == PickupKind.Weapon)
            {
                weapon = Weapons.Find(s.Weapon);
                if (weapon == null && dealable.Count > 0) weapon = dealable[next++ % dealable.Count];
            }
            result.Add(new Pickup(s, weapon));
        }
        return result;
    }

    /// <summary>Swaps in freshly loaded weapon designs without restarting.</summary>
    public void ReloadWeapons(WeaponSet weapons)
    {
        Weapons = weapons;
        WeaponDesign Swap(WeaponDesign old) => weapons.Find(old.Id) ?? old;
        for (int i = 0; i < Player.Weapons.Count; i++) Player.Weapons[i] = Swap(Player.Weapons[i]);
        Player.Weapons.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : string.CompareOrdinal(a.Id, b.Id));
        Player.Weapon = Swap(Player.Weapon);
        foreach (Pickup pk in Pickups)
            if (pk.Weapon != null) pk.Weapon = Swap(pk.Weapon);
        Say($"Reloaded {weapons.Designs.Count} weapon design{(weapons.Designs.Count == 1 ? "" : "s")}.");
        ReportWarnings(weapons.Warnings);
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

    /// <summary>
    /// Swaps in freshly loaded monster designs without restarting. Monsters keep their position and
    /// damage taken; a monster whose file was removed becomes a random one from the new set.
    /// </summary>
    public void ReloadMonsters(MonsterSet monsters)
    {
        Monsters = monsters;
        foreach (Enemy e in Enemies)
        {
            MonsterDesign old = e.Design;
            MonsterDesign next = monsters.Resolve(old.Id, _rng);
            if (e.IsAlive) e.Health = Math.Max(1, next.Health - (old.Health - e.Health));
            e.Design = next;
        }
        Say($"Reloaded {monsters.Designs.Count} monster design{(monsters.Designs.Count == 1 ? "" : "s")}.");
        ReportWarnings(monsters.Warnings);
    }

    private void ReportWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0) return;
        string more = warnings.Count > 1 ? $" (+{warnings.Count - 1} more)" : "";
        Say(warnings[0] + more);
    }

    public void Say(string message)
    {
        _messages.Add(message);
        _messageTime = 3;
        _log.Add(new HudMessage(message, Time));
        if (_log.Count > 32) _log.RemoveAt(0);
    }

    // ---- Events the HUD reacts to ----
    private readonly List<HudMessage> _log = new();
    private readonly List<DamageEvent> _damageEvents = new();

    /// <summary>Recent messages, newest last, with the time they were said.</summary>
    public IReadOnlyList<HudMessage> MessageLog => _log;

    /// <summary>Recent hits on the player: where they came from (for directional damage arrows).</summary>
    public IReadOnlyList<DamageEvent> DamageEvents => _damageEvents;

    /// <summary>When you last hurt / killed a monster (hit markers), fired, or changed weapon.</summary>
    public double LastHitTime { get; private set; } = double.NegativeInfinity;
    public double LastKillTime { get; private set; } = double.NegativeInfinity;
    public double LastShotTime { get; private set; } = double.NegativeInfinity;
    public double WeaponSwitchTime { get; private set; } = double.NegativeInfinity;

    /// <summary>What killed you, for the death screen.</summary>
    public string? KilledBy { get; private set; }

    /// <summary>
    /// Where the objective is right now (open finish line, the nearest boss in the way, or the nearest monster
    /// on a kill-everything level), for the compass and radar.
    /// </summary>
    public (Vec2 Position, string Label)? ObjectiveTarget
    {
        get
        {
            Vec2 me = Player.Position;
            if (Level.Exit is { } exit && ExitOpen) return (exit, "FINISH");
            Enemy? boss = BlockingBosses.OrderBy(b => Vec2.Distance(b.Position, me)).FirstOrDefault();
            if (boss != null) return (boss.Position, boss.Design.Name.ToUpperInvariant());
            if (Level.Exit is { } ex) return (ex, "FINISH");
            Enemy? any = Enemies.Where(e => e.IsAlive).OrderBy(e => Vec2.Distance(e.Position, me)).FirstOrDefault();
            return any != null ? (any.Position, "HOSTILE") : null;
        }
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

        if (_nav == null && Enemies.Count > 0) _ = Nav; // build pathfinding up front, not mid-fight
        _navRefresh -= dt;
        if (_navRefresh <= 0 && State == GameState.Playing && Enemies.Any(e => e.State != EnemyState.Idle && e.IsAlive))
        {
            Nav.SetTarget(Player.Position);
            _navRefresh = 0.25;
        }
        foreach (Enemy e in Enemies) UpdateEnemy(e, dt);
        UpdateProjectiles(dt);
        foreach (Effect fx in Effects) fx.Age += dt;
        Effects.RemoveAll(fx => fx.Age >= fx.Duration);

        if (State == GameState.Exiting)
        {
            UpdateCutscene(dt);
            return;
        }
        if (State != GameState.Playing) return;
        if (Level.Exit is { } exit)
        {
            // Finish-line level: you win by reaching the exit once the boss is down.
            _lockedNagCooldown -= dt;
            if (Vec2.Distance(Player.Position, exit) < ExitRadius + PlayerRadius)
            {
                if (ExitOpen)
                {
                    StartExitCutscene(exit);
                }
                else if (_lockedNagCooldown <= 0)
                {
                    _lockedNagCooldown = 2.5;
                    Say(Objective + " first!");
                }
            }
        }
        else if (Enemies.Count > 0 && Enemies.All(e => !e.IsAlive))
        {
            State = GameState.Won;
            Say("Drawing purged. Nice work.");
        }
    }

    /// <summary>Radius of the finish-line pad, in wall heights.</summary>
    public const double ExitRadius = 0.45;

    private double _lockedNagCooldown;

    private IEnumerable<Enemy> AliveBosses => Enemies.Where(e => e.IsAlive && e.Design.Boss);

    /// <summary>The boss whose death opens the gate under <see cref="GateRule.FinalBoss"/>: the one nearest the finish.</summary>
    public Enemy? FinalBoss { get; private set; }

    /// <summary>The bosses still standing between you and the finish, under the level's gate rule.</summary>
    private IEnumerable<Enemy> BlockingBosses => Level.GateRule == GateRule.FinalBoss
        ? (FinalBoss is { IsAlive: true } f ? new[] { f } : Array.Empty<Enemy>())
        : AliveBosses;

    /// <summary>The gates and finish line open once the gate rule is met (always, if there are no bosses).</summary>
    public bool ExitOpen => !BlockingBosses.Any();

    private bool _gatesOpened;

    private void OpenGatesIfDue()
    {
        if (_gatesOpened || !ExitOpen) return;
        _gatesOpened = true;
        bool anyGates = false;
        foreach (Wall g in Level.Gates)
        {
            g.IsOpen = true;
            anyGates = true;
        }
        if (anyGates) _nav?.Rebuild();
        if (anyGates || Level.Exit != null) Say(anyGates ? "The gate is open! Get to the finish line." : "The finish line is open!");
    }

    /// <summary>What the player should be doing right now, for the HUD.</summary>
    public string Objective
    {
        get
        {
            if (Level.Exit == null && !Level.Gates.Any()) return $"Clear the drawing: {Enemies.Count(e => e.IsAlive)} hostiles left";
            List<Enemy> blocking = BlockingBosses.ToList();
            if (blocking.Count == 0) return "Reach the finish line!";
            if (Level.GateRule == GateRule.FinalBoss) return $"Defeat the final boss, the {blocking[0].Design.Name}, to open the gate";
            return blocking.Count == 1
                ? $"Defeat the {blocking[0].Design.Name} to open the gate"
                : $"Defeat all bosses to open the gate ({blocking.Count} left)";
        }
    }

    /// <summary>The boss to show a health bar for: the first one that has noticed you.</summary>
    public Enemy? ActiveBoss => AliveBosses.FirstOrDefault(e => e.State != EnemyState.Idle);

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
        _playerVelocity = Vec2.Zero;
        if (input.Jump && p.OnGround)
        {
            p.VelocityZ = JumpSpeed;
            p.OnGround = false;
        }
        if (wish.LengthSquared > 0)
        {
            Vec2 before = p.Position;
            p.Position = Move(p.Position, wish.Normalized() * speed * dt, PlayerRadius, p.Z);
            if (dt > 0) _playerVelocity = (p.Position - before) / dt;
            double moved = Vec2.Distance(before, p.Position);
            p.BobPhase += moved * 9;
            p.BobAmount = Math.Min(1, p.BobAmount + dt * 4);
        }
        else
        {
            p.BobAmount = Math.Max(0, p.BobAmount - dt * 4);
        }
        UpdatePlayerHeight(dt);

        if (input.SelectSlot > 0) SelectSlot(input.SelectSlot);
        if (input.CycleWeapon != 0) CycleWeapon(Math.Sign(input.CycleWeapon));

        if (input.Fire && p.FireCooldown <= 0)
        {
            if (p.CanFire(p.Weapon)) Fire(p.Weapon);
            else
            {
                Say($"Out of {p.Weapon.AmmoType}!");
                p.FireCooldown = 0.4;
                SwitchToBestWeapon();
            }
        }

        foreach (Pickup pickup in Pickups)
        {
            if (pickup.Taken || Vec2.Distance(pickup.Position, p.Position) > PlayerRadius + 0.2) continue;
            if (Math.Abs(Level.Terrain.FloorAt(pickup.Position) - p.Z) > 0.3) continue; // it's up on a ledge (or down below)
            TryTake(pickup);
            if (pickup.Taken) p.PickupFlash = 1;
        }
    }

    /// <summary>Gravity, landing, walking off ledges and stepping up stairs.</summary>
    private void UpdatePlayerHeight(double dt)
    {
        Player p = Player;
        double floor = Level.Terrain.FloorAt(p.Position);
        if (p.OnGround)
        {
            if (floor >= p.Z - Terrain.StepHeight) p.Z = floor; // walk up or down a step
            else p.OnGround = false;                            // walked off a ledge
        }
        if (!p.OnGround)
        {
            p.VelocityZ -= Gravity * dt;
            p.Z += p.VelocityZ * dt;
            if (p.Z <= floor)
            {
                if (p.VelocityZ < -5) p.BobAmount = 1; // a hard landing jolts the gun
                p.Z = floor;
                p.VelocityZ = 0;
                p.OnGround = true;
            }
        }
        // The camera follows instantly in the air, but eases up steps.
        p.ViewZ = !p.OnGround || p.ViewZ > p.Z ? p.Z : Math.Min(p.Z, p.ViewZ + dt * 2.5);
    }

    private void TryTake(Pickup pickup)
    {
        Player p = Player;
        switch (pickup.Kind)
        {
            case PickupKind.Health when p.Health < Player.MaxHealth:
                p.Health = Math.Min(Player.MaxHealth, p.Health + 25);
                pickup.Taken = true;
                Say("Picked up a medkit.");
                break;

            case PickupKind.Ammo:
            {
                // Ammo for what you're holding, or for the first carried gun that isn't full.
                WeaponDesign? w = new[] { p.Weapon }.Concat(p.Weapons)
                    .FirstOrDefault(x => x.UsesAmmo && x.BoxAmmo > 0 && p.AmmoOf(x.AmmoType) < x.MaxAmmo);
                if (w == null) break;
                int got = AddAmmo(w, w.BoxAmmo);
                pickup.Taken = true;
                Say($"Picked up {got} {w.AmmoType}.");
                break;
            }

            case PickupKind.Weapon when pickup.Weapon is { } w:
            {
                bool isNew = !p.Weapons.Contains(w);
                if (!isNew && (!w.UsesAmmo || p.AmmoOf(w.AmmoType) >= w.MaxAmmo)) break;
                int got = w.UsesAmmo ? AddAmmo(w, w.PickupAmmo) : 0;
                pickup.Taken = true;
                if (isNew)
                {
                    p.Weapons.Add(w);
                    p.Weapons.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : string.CompareOrdinal(a.Id, b.Id));
                    p.Weapon = w;
                    WeaponSwitchTime = Time;
                    p.FireCooldown = Math.Max(p.FireCooldown, 0.2);
                    Say($"You got the {w.Name}! (key {w.Slot})");
                }
                else
                {
                    Say($"Picked up {got} {w.AmmoType}.");
                }
                break;
            }
        }
    }

    private int AddAmmo(WeaponDesign w, int amount)
    {
        int before = Player.AmmoOf(w.AmmoType);
        int after = Math.Min(w.MaxAmmo, before + amount);
        Player.Ammo[w.AmmoType] = after;
        return after - before;
    }

    private void SelectSlot(int slot)
    {
        List<WeaponDesign> inSlot = Player.Weapons.Where(w => w.Slot == slot).ToList();
        if (inSlot.Count == 0) return;
        int i = inSlot.IndexOf(Player.Weapon);
        Equip(inSlot[(i + 1) % inSlot.Count]); // pressing the key again cycles a shared slot
    }

    private void CycleWeapon(int dir)
    {
        List<WeaponDesign> list = Player.Weapons;
        int i = list.IndexOf(Player.Weapon);
        Equip(list[((i + dir) % list.Count + list.Count) % list.Count]);
    }

    private void SwitchToBestWeapon()
    {
        WeaponDesign? best = Player.Weapons.LastOrDefault(w => w != Player.Weapon && w.UsesAmmo && Player.CanFire(w))
                             ?? Player.Weapons.LastOrDefault(w => w != Player.Weapon && Player.CanFire(w));
        if (best != null) Equip(best);
    }

    private void Equip(WeaponDesign w)
    {
        if (w == Player.Weapon) return;
        WeaponSwitchTime = Time;
        Player.Weapon = w;
        Player.FireCooldown = Math.Max(Player.FireCooldown, 0.15);
        Player.MuzzleFlashTime = 0;
    }

    private void Fire(WeaponDesign w)
    {
        LastShotTime = Time;
        Alert(Player.Position, w.IsMelee ? 3 : 8); // gunfire is loud
        MaybeDodge(Player.Direction);
        Player p = Player;
        if (w.UsesAmmo) p.Ammo[w.AmmoType] = p.AmmoOf(w.AmmoType) - w.AmmoPerShot;
        p.FireCooldown = w.FireDelay;
        p.MuzzleFlashTime = Math.Min(0.15, w.FireDelay * 0.6);

        if (w.IsMelee)
        {
            Swing(w);
            return;
        }

        for (int i = 0; i < w.Pellets; i++)
        {
            double angle = p.Angle + (_rng.NextDouble() - 0.5) * w.Spread * Math.PI / 180;
            int damage = _rng.Next(w.DamageMin, w.DamageMax + 1);
            if (w.ProjectileSpeed > 0)
            {
                Vec2 dir = Vec2.FromAngle(angle);
                double z = p.Z + 0.36;
                Projectiles.Add(new Projectile
                {
                    FromPlayer = true,
                    Position = p.Position + dir * (PlayerRadius + 0.02),
                    Velocity = dir * w.ProjectileSpeed,
                    Z = z,
                    VelocityZ = AutoAimClimb(dir, z, w.ProjectileSpeed),
                    Damage = damage,
                    SplashRadius = w.SplashRadius,
                    SplashDamage = w.SplashDamage,
                    RangeLeft = w.ProjectileRange > 0 ? w.ProjectileRange : double.PositiveInfinity,
                    Size = w.ProjectileSize,
                    Sprite = w.Projectile,
                });
            }
            else
            {
                Hitscan(angle, damage);
            }
        }
    }

    /// <summary>
    /// Vertical auto-aim (there's no looking up or down): a shot heads for the middle of the monster
    /// nearest your aim line, so you can hit things on ledges above or below you.
    /// </summary>
    private double AutoAimClimb(Vec2 dir, double fromZ, double speed)
    {
        if (Level.Terrain.IsFlat) return 0;
        Enemy? target = null;
        double best = double.PositiveInfinity;
        foreach (Enemy e in Enemies)
        {
            if (!e.IsAlive) continue;
            Vec2 rel = e.Position - Player.Position;
            double along = Vec2.Dot(rel, dir);
            if (along <= 0 || along >= best || Math.Abs(Vec2.Cross(dir, rel)) > e.Radius + 0.3) continue;
            if (!Level.Index.HasLineOfSight(Player.Position, e.Position)) continue;
            best = along;
            target = e;
        }
        if (target == null) return 0;
        double targetZ = target.Z + target.Design.FloatHeight + target.Design.Size * 0.5;
        return (targetZ - fromZ) / Math.Max(0.05, best / speed);
    }

    /// <summary>Melee: hits every monster within reach inside the swing arc.</summary>
    private void Swing(WeaponDesign w)
    {
        Player p = Player;
        double halfArc = w.Sweep * Math.PI / 360;
        foreach (Enemy e in Enemies.ToList())
        {
            if (!e.IsAlive) continue;
            Vec2 rel = e.Position - p.Position;
            double dist = rel.Length;
            if (dist - e.Radius > w.Range + PlayerRadius) continue;
            double off = Math.Abs(Math.Atan2(Vec2.Cross(p.Direction, rel), Vec2.Dot(p.Direction, rel)));
            // Close monsters count even slightly outside the arc, since they're wide.
            double allowance = dist > 1e-6 ? Math.Asin(Math.Min(1, e.Radius / dist)) : Math.PI;
            if (off > halfArc + allowance) continue;
            if (!Level.Index.HasLineOfSight(p.Position, e.Position)) continue;
            if (Math.Abs(e.Z - p.Z) > 0.6) continue; // out of reach up on a ledge
            Damage(e, _rng.Next(w.DamageMin, w.DamageMax + 1));
        }
    }

    private void Hitscan(double angle, int damage)
    {
        Player p = Player;
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

        if (target != null) Damage(target, damage);
    }

    private void Damage(Enemy e, int damage)
    {
        if (!e.IsAlive) return;
        e.Health -= damage;
        LastHitTime = Time;
        Wake(e);
        Alert(e.Position, 5);
        if (e.Health <= 0) LastKillTime = Time;
        e.PainTime = 0.15;
        e.LastKnownPlayer = Player.Position;
        if (e.State == EnemyState.Idle) e.State = EnemyState.Chase;
        if (e.Health <= 0)
        {
            e.State = EnemyState.Dead;
            e.StateTime = 0;
            int left = Enemies.Count(x => x.IsAlive);
            Say(e.Design.Boss ? $"The {e.Design.Name} is dead!" : left == 0 ? "All hostiles eliminated!" : $"{e.Design.Name} down. {left} left.");
            OpenGatesIfDue();
        }
    }

    private void UpdateEnemy(Enemy e, double dt)
    {
        e.StateTime += dt;
        e.PainTime = Math.Max(0, e.PainTime - dt);
        e.MeleeCooldown -= dt;
        if (!e.IsAlive) return;

        Player p = Player;
        MonsterDesign d = e.Design;
        // Drop off ledges (fliers glide down).
        double floor = Level.Terrain.FloorAt(e.Position);
        e.Z = e.Z > floor ? Math.Max(floor, e.Z - dt * (d.FloatHeight > 0 ? 1.5 : 5)) : floor;
        double dist = Vec2.Distance(e.Position, p.Position);
        bool canSee = State == GameState.Playing && dist < SightRange && Level.Index.HasLineOfSight(e.Position, p.Position);
        if (canSee) e.LastKnownPlayer = p.Position;

        if (d.Boss && !e.Enraged && e.Health <= d.Health / 2)
        {
            e.Enraged = true;
            Say($"The {d.Name} is enraged!");
        }

        switch (e.State)
        {
            case EnemyState.Idle:
                if (canSee)
                {
                    Wake(e);
                    Alert(e.Position, 5); // it shouts to its friends
                }
                break;

            case EnemyState.Chase:
            {
                if (State != GameState.Playing) break;
                e.AttackCooldown -= dt;

                // Close-range hit.
                double reach = e.Radius + PlayerRadius + 0.15;
                if (d.MeleeMax > 0 && dist < reach && e.MeleeCooldown <= 0 && Math.Abs(e.Z - p.Z) < 0.4)
                {
                    HurtPlayer(_rng.Next(d.MeleeMin, d.MeleeMax + 1), e.Position, d.Name);
                    e.MeleeCooldown = e.Enraged ? 0.6 : 0.9;
                }

                // Ranged attack (rushers that can bite prefer to close in first).
                bool wantsMelee = d.MeleeMax > 0 && d.Behavior == Behavior.Rusher && dist < 3;
                double attackRange = d.Behavior == Behavior.Sniper ? 14 : 9;
                if (canSee && e.AttackCooldown <= 0 && dist < attackRange && !wantsMelee)
                {
                    e.State = EnemyState.Attack;
                    e.StateTime = 0;
                    break;
                }
                if (e.PainTime > 0 && !d.Boss) break; // flinch

                Vec2 move = ChooseMove(e, dist, canSee, dt);
                if (move.LengthSquared < 1e-9) break;

                double speed = e.Speed * (e.RetreatTime > 0 ? 1.2 : 1) * (e.Enraged ? 1.25 : 1);
                Vec2 before = e.Position;
                Vec2 step = move.Normalized() * speed * dt;
                // Walkers can't climb ledges (only stairs); fliers float over them.
                e.Position = Move(e.Position, step, e.Radius, d.FloatHeight > 0 ? double.PositiveInfinity : e.Z);
                e.WalkPhase += dt * 6;

                // Blocked? Circle the other way, and side-step for a moment.
                if (Vec2.Distance(before, e.Position) < step.Length * 0.3)
                {
                    e.StrafeDirection = -e.StrafeDirection;
                    if (e.DetourTime <= 0)
                    {
                        e.Detour = _rng.Next(2) == 0 ? move.Normalized().PerpRight() : -move.Normalized().PerpRight();
                        e.DetourTime = 0.5;
                    }
                }
                break;
            }

            case EnemyState.Attack:
            {
                // Stands still while winding up (your cue to move), then fires.
                double windup = e.Enraged ? 0.32 : 0.45;
                if (e.StateTime < windup) break;
                if (State == GameState.Playing && Level.Index.HasLineOfSight(e.Position, p.Position)) FireAt(e, dist);
                e.State = EnemyState.Chase;
                e.StateTime = 0;
                e.AttackCooldown = d.AttackDelay * (e.Enraged ? 0.65 : 1) + _rng.NextDouble() * 0.6;
                break;
            }
        }
    }

    /// <summary>Where a monster wants to go this frame, by its behaviour.</summary>
    private Vec2 ChooseMove(Enemy e, double dist, bool canSee, double dt)
    {
        MonsterDesign d = e.Design;
        Vec2 toPlayer = Player.Position - e.Position;
        Vec2 dir = toPlayer.Normalized();
        Vec2 perp = dir.PerpRight() * e.StrafeDirection;

        // Spread out instead of stacking on top of each other.
        Vec2 separation = Vec2.Zero;
        foreach (Enemy o in Enemies)
        {
            if (o == e || !o.IsAlive) continue;
            Vec2 away = e.Position - o.Position;
            double gap = away.Length;
            double want = e.Radius + o.Radius + 0.35;
            if (gap > 1e-6 && gap < want) separation += away / gap * ((want - gap) / want);
        }

        if (e.DetourTime > 0)
        {
            e.DetourTime -= dt;
            return dir * 0.3 + e.Detour + separation;
        }
        if (e.DodgeTime > 0)
        {
            e.DodgeTime -= dt;
            return e.DodgeVelocity + separation;
        }

        if (!canSee)
        {
            // Hunt: follow the flow field around walls to where you are.
            if (Nav.NextWaypoint(e.Position) is { } waypoint)
            {
                Vec2 toWaypoint = waypoint - e.Position;
                if (toWaypoint.LengthSquared > 1e-6) return toWaypoint.Normalized() + separation * 0.8;
            }
            Vec2 toLast = e.LastKnownPlayer - e.Position;
            return toLast.Length > 0.3 ? toLast.Normalized() + separation : separation;
        }

        // Badly hurt skirmishers and snipers fall back for a while (once).
        if (!d.Boss && d.Behavior is Behavior.Skirmisher or Behavior.Sniper && !e.HasRetreated && e.Health < d.Health * 0.3)
        {
            e.HasRetreated = true;
            e.RetreatTime = 2.0;
        }
        if (e.RetreatTime > 0)
        {
            e.RetreatTime -= dt;
            return -dir + perp * 0.6 + separation;
        }

        // Change circling direction now and then so they're harder to predict.
        if (Time >= e.StrafeSwitchTime)
        {
            e.StrafeDirection = _rng.Next(2) == 0 ? 1 : -1;
            e.StrafeSwitchTime = Time + 1.2 + _rng.NextDouble() * 1.8;
        }

        double range = d.PreferredRange;
        bool backsOff = d.Behavior is Behavior.Skirmisher or Behavior.Sniper;
        Vec2 radial = dist > range + 0.6 ? dir : dist < range - 0.6 && backsOff ? -dir : Vec2.Zero;
        bool inBand = radial.LengthSquared < 1e-9;

        return d.Behavior switch
        {
            // Zig-zag straight in.
            Behavior.Rusher => (dist > e.Radius + PlayerRadius + 0.05 ? dir : Vec2.Zero)
                               + perp * Math.Sin(Time * 5 + e.Seed) * d.Strafe * 1.4 + separation,
            // Keep coming, barely sidestepping.
            Behavior.Tank => (dist > e.Radius + PlayerRadius + 0.1 ? radial : Vec2.Zero) + perp * d.Strafe * 0.5 + separation,
            // Hold the range band and circle.
            _ => radial + perp * d.Strafe * (inBand ? 1.0 : 0.6) + separation,
        };
    }

    /// <summary>Fires at you, leading a moving target by the monster's aim skill.</summary>
    private void FireAt(Enemy e, double dist)
    {
        MonsterDesign d = e.Design;
        Vec2 target = Player.Position;
        if (d.AimLead > 0 && d.FireballSpeed > 0)
        {
            double flight = dist / d.FireballSpeed;
            Vec2 predicted = target + _playerVelocity * flight * d.AimLead;
            if (Level.Index.HasLineOfSight(e.Position, predicted)) target = predicted;
        }
        Vec2 toTarget = target - e.Position;
        double aim = Math.Atan2(toTarget.Y, toTarget.X);
        double fromZ = e.Z + d.FloatHeight + 0.3;
        double climb = d.FireballSpeed > 0 ? (Player.Z + 0.3 - fromZ) / Math.Max(0.05, toTarget.Length / d.FireballSpeed) : 0;
        int shots = d.Shots + (e.Enraged ? 1 : 0);
        double spread = d.ShotSpread + (e.Enraged && d.Shots == 1 ? 12 : 0);
        for (int k = 0; k < shots; k++)
        {
            // Fan multiple shots evenly across the spread, centred on the aim point.
            double offset = shots == 1 ? 0 : (k / (double)(shots - 1) - 0.5) * spread * Math.PI / 180;
            Vec2 dir = Vec2.FromAngle(aim + offset);
            Projectiles.Add(new Projectile
            {
                Position = e.Position + dir * (e.Radius + 0.05),
                Velocity = dir * d.FireballSpeed,
                Z = fromZ,
                VelocityZ = climb,
                Damage = _rng.Next(d.DamageMin, d.DamageMax + 1),
                Sprite = d.ProjectileColor != null ? d.ProjectileSprite : null,
                Origin = e.Position,
                SourceName = d.Name,
                Size = d.Boss ? 0.26 : 0.18,
            });
        }
    }

    private NavGrid? _nav;
    private double _navRefresh;
    private Vec2 _playerVelocity;

    /// <summary>The level's navigation grid (built on first use).</summary>
    public NavGrid Nav => _nav ??= new NavGrid(Level.Index, Level.Terrain);

    /// <summary>Wakes a monster up and gives it a moment before it shoots.</summary>
    private void Wake(Enemy e)
    {
        if (e.State != EnemyState.Idle) return;
        e.State = EnemyState.Chase;
        e.StateTime = 0;
        e.LastKnownPlayer = Player.Position;
        e.AttackCooldown = Math.Max(e.AttackCooldown, 0.4 + _rng.NextDouble() * 0.6);
    }

    /// <summary>Wakes every sleeping monster within <paramref name="radius"/> (gunfire, a shout, a scream).</summary>
    private void Alert(Vec2 at, double radius)
    {
        foreach (Enemy o in Enemies)
            if (o.IsAlive && o.State == EnemyState.Idle && Vec2.Distance(o.Position, at) < radius) Wake(o);
    }

    /// <summary>Monsters you're aiming at may side-step your next shot.</summary>
    private void MaybeDodge(Vec2 aimDir)
    {
        foreach (Enemy e in Enemies)
        {
            if (!e.IsAlive || e.State == EnemyState.Idle || e.Design.Dodge <= 0 || e.DodgeTime > 0) continue;
            Vec2 rel = e.Position - Player.Position;
            double along = Vec2.Dot(rel, aimDir);
            if (along <= 0 || Math.Abs(Vec2.Cross(aimDir, rel)) > e.Radius + 0.4) continue;
            if (_rng.NextDouble() >= e.Design.Dodge) continue;
            e.DodgeVelocity = aimDir.PerpRight() * (_rng.Next(2) == 0 ? 1 : -1) * 1.6;
            e.DodgeTime = 0.35;
        }
    }

    private void UpdateProjectiles(double dt)
    {
        foreach (Projectile pr in Projectiles)
        {
            Vec2 step = pr.Velocity * dt;
            double len = step.Length;
            // The small margin catches a shot that ends exactly on a wall line (it would slip through next frame).
            if (len > 0 && Level.Index.CastRay(pr.Position, step / len, len + 1e-4) is { } wallHit)
            {
                // Explode just in front of the wall so splash isn't blocked by it.
                pr.Position += step / len * Math.Max(0, wallHit.Distance - 0.05);
                Explode(pr, null);
                continue;
            }
            if (!Level.Terrain.IsFlat && HitsLedge(pr, step, len)) continue;
            pr.Position += step;
            pr.Z += pr.VelocityZ * dt;
            pr.RangeLeft -= len;

            if (pr.FromPlayer)
            {
                Enemy? hit = Enemies.FirstOrDefault(e => e.IsAlive && Vec2.Distance(e.Position, pr.Position) < e.Radius + pr.Size * 0.5
                                                        && pr.Z > e.Z + e.Design.FloatHeight - 0.2 && pr.Z < e.Z + e.Design.FloatHeight + e.Design.Size + 0.2);
                if (hit != null) Explode(pr, hit);
            }
            else if (State == GameState.Playing && Vec2.Distance(pr.Position, Player.Position) < PlayerRadius + 0.08
                     && pr.Z > Player.Z - 0.15 && pr.Z < Player.Z + 0.75)
            {
                pr.Alive = false;
                HurtPlayer(pr.Damage, pr.Origin, pr.SourceName);
            }

            if (pr.Alive && (pr.RangeLeft <= 0 || Vec2.Distance(pr.Position, Player.Position) > 80))
            {
                if (pr.FromPlayer) Explode(pr, null);
                else pr.Alive = false;
            }
        }
        Projectiles.RemoveAll(pr => !pr.Alive);
    }

    /// <summary>Shots hit the side of a platform that's taller than they are, or the floor they dip into.</summary>
    private bool HitsLedge(Projectile pr, Vec2 step, double len)
    {
        Vec2 dir = step / len;
        double speed = Math.Max(1e-9, pr.Velocity.Length);
        foreach (RayHit h in LedgeHits(pr.Position, dir, len))
        {
            double z = pr.Z + pr.VelocityZ * h.Distance / speed;
            if (z >= h.Wall.LedgeHeight || Level.Terrain.FloorAt(pr.Position + dir * (h.Distance + 1e-3)) <= z) continue;
            pr.Position += dir * Math.Max(0, h.Distance - 0.05);
            Explode(pr, null);
            return true;
        }
        if (pr.Z < Level.Terrain.FloorAt(pr.Position) - 0.02)
        {
            Explode(pr, null);
            return true;
        }
        return false;
    }

    private readonly List<RayHit> _ledgeHits = new();

    private List<RayHit> LedgeHits(Vec2 from, Vec2 dir, double len)
    {
        Level.Terrain.Ledges.CastAll(from, dir, len + 1e-4, _ledgeHits);
        return _ledgeHits;
    }

    /// <summary>A player projectile hits: direct damage to <paramref name="direct"/>, then splash around it.</summary>
    private void Explode(Projectile pr, Enemy? direct)
    {
        pr.Alive = false;
        if (!pr.FromPlayer) return;
        if (direct != null) Damage(direct, pr.Damage);
        if (pr.SplashRadius <= 0) return;

        foreach (Enemy e in Enemies)
        {
            if (e == direct || !e.IsAlive) continue;
            double d = Vec2.Distance(e.Position, pr.Position) - e.Radius;
            if (d >= pr.SplashRadius || !Level.Index.HasLineOfSight(pr.Position, e.Position)) continue;
            Damage(e, (int)Math.Round(pr.SplashDamage * (1 - Math.Max(0, d) / pr.SplashRadius)));
        }

        // Rocket too close? You feel it (at half strength).
        double pd = Vec2.Distance(Player.Position, pr.Position);
        if (State == GameState.Playing && pd < pr.SplashRadius && Level.Index.HasLineOfSight(pr.Position, Player.Position))
        {
            int self = (int)Math.Round(pr.SplashDamage * 0.5 * (1 - pd / pr.SplashRadius));
            if (self > 0) HurtPlayer(self, pr.Position, "your own rocket");
        }

        if (pr.SplashRadius >= 0.5)
            Effects.Add(new Effect { Position = pr.Position, Z = pr.Z, Size = pr.SplashRadius * 0.8, Duration = 0.35 });
    }

    private void HurtPlayer(int damage, Vec2 from, string? source)
    {
        _damageEvents.Add(new DamageEvent(from, Time, damage));
        _damageEvents.RemoveAll(d => Time - d.Time > 2);
        if (Player.Health - damage <= 0 && State == GameState.Playing) KilledBy = source;
        DamageTaken += Math.Min(damage, Player.Health);
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
    /// <param name="feetZ">Height of the mover's feet: platform edges more than a step above this block it
    /// like walls (pass +infinity to ignore ledges, e.g. for fliers).</param>
    public Vec2 Move(Vec2 from, Vec2 delta, double radius, double feetZ = 0)
    {
        double len = delta.Length;
        if (len < 1e-12) return from;
        int steps = Math.Max(1, (int)Math.Ceiling(len / (radius * 0.5)));
        Vec2 step = delta / steps;
        Vec2 pos = from;
        bool ledges = !Level.Terrain.IsFlat && double.IsFinite(feetZ);
        double climb = feetZ + Terrain.StepHeight;
        for (int i = 0; i < steps; i++)
        {
            Vec2 next = Resolve(pos + step, pos, radius, ledges ? climb : double.PositiveInfinity);
            if (Level.Index.SegmentCrossesWall(pos, next)) break;
            if (ledges && CrossesLedge(pos, next, climb)) break;
            pos = next;
        }
        return pos;
    }

    private bool CrossesLedge(Vec2 p, Vec2 q, double climb)
    {
        Vec2 d = q - p;
        if (d.LengthSquared < 1e-18) return false;
        Vec2 min = new(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y)), max = new(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y));
        foreach (Wall w in Level.Terrain.Ledges.Query(min, max))
            if (w.LedgeHeight > climb && SpatialIndex.Intersect(p, d, w, out double t, out _) && t <= 1
                && Level.Terrain.FloorAt(p + d * Math.Min(1, t + 1e-3)) > climb)
                return true;
        return false;
    }

    private Vec2 Resolve(Vec2 p, Vec2 previous, double radius, double climb)
    {
        Vec2 r = new(radius, radius);
        for (int iter = 0; iter < 4; iter++)
        {
            bool pushed = false;
            IEnumerable<Wall> nearby = Level.Index.Query(p - r, p + r);
            if (double.IsFinite(climb))
                nearby = nearby.Concat(Level.Terrain.Ledges.Query(p - r, p + r).Where(w => w.LedgeHeight > climb && TooHighBeyond(w, previous, climb)));
            foreach (Wall w in nearby)
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

    /// <summary>
    /// A ledge only blocks if the floor on its far side (from <paramref name="from"/>) is too high to step onto:
    /// the edge of the platform you're standing on doesn't stop you walking off it.
    /// </summary>
    private bool TooHighBeyond(Wall ledge, Vec2 from, double climb)
    {
        Vec2 q = ledge.ClosestPoint(from);
        Vec2 away = q - from;
        double d = away.Length;
        Vec2 n = d > 1e-9 ? away / d : ledge.Normal;
        return Level.Terrain.FloorAt(q + n * 0.01) > climb;
    }

    private static double NormalizeAngle(double a)
    {
        a %= 2 * Math.PI;
        return a < 0 ? a + 2 * Math.PI : a;
    }
}
