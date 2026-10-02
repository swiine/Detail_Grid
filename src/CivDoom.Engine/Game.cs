namespace CivDoom.Engine;

/// <summary>Per-frame input, filled in by whatever window hosts the game.</summary>
public sealed class GameInput
{
    public bool Forward, Back, StrafeLeft, StrafeRight, TurnLeft, TurnRight, Fire, Run;

    /// <summary>Radians to turn this frame from mouse movement (positive = turn left / counter-clockwise).</summary>
    public double MouseTurn;

    /// <summary>Weapon slot key pressed this frame (1-9), or 0.</summary>
    public int SelectSlot;

    /// <summary>+1 / -1 to cycle to the next / previous weapon (mouse wheel), or 0.</summary>
    public int CycleWeapon;

    public void Clear()
    {
        Forward = Back = StrafeLeft = StrafeRight = TurnLeft = TurnRight = Fire = Run = false;
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
    public int Health;
    public EnemyState State = EnemyState.Idle;
    public double AttackCooldown = 1.0;
    public double StateTime;
    public double PainTime;
    public double WalkPhase;
    public Vec2 LastKnownPlayer;
    public Vec2 Detour;
    public double DetourTime;

    public double Radius => Design.Radius;
    public double Speed => Design.Speed;

    public bool IsAlive => State != EnemyState.Dead;
}

public sealed class Projectile
{
    public Vec2 Position;
    public Vec2 Velocity;
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
        Enemies = CreateEnemies(level);
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
            result.Add(new Enemy(s, d));
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
            TryTake(pickup);
            if (pickup.Taken) p.PickupFlash = 1;
        }
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
                Projectiles.Add(new Projectile
                {
                    FromPlayer = true,
                    Position = p.Position + dir * (PlayerRadius + 0.02),
                    Velocity = dir * w.ProjectileSpeed,
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
                        Vec2 toPlayer = p.Position - e.Position;
                        double aim = Math.Atan2(toPlayer.Y, toPlayer.X);
                        MonsterDesign d = e.Design;
                        for (int k = 0; k < d.Shots; k++)
                        {
                            // Fan multiple shots evenly across the spread, centred on the player.
                            double offset = d.Shots == 1 ? 0 : (k / (double)(d.Shots - 1) - 0.5) * d.ShotSpread * Math.PI / 180;
                            Vec2 dir = Vec2.FromAngle(aim + offset);
                            Projectiles.Add(new Projectile
                            {
                                Position = e.Position + dir * (e.Radius + 0.05),
                                Velocity = dir * d.FireballSpeed,
                                Damage = _rng.Next(d.DamageMin, d.DamageMax + 1),
                                Sprite = d.ProjectileColor != null ? d.ProjectileSprite : null,
                                Origin = e.Position,
                                SourceName = d.Name,
                                Size = d.Boss ? 0.26 : 0.18,
                            });
                        }
                    }
                    e.State = EnemyState.Chase;
                    e.StateTime = 0;
                    e.AttackCooldown = e.Design.AttackDelay + _rng.NextDouble();
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
            // The small margin catches a shot that ends exactly on a wall line (it would slip through next frame).
            if (len > 0 && Level.Index.CastRay(pr.Position, step / len, len + 1e-4) is { } wallHit)
            {
                // Explode just in front of the wall so splash isn't blocked by it.
                pr.Position += step / len * Math.Max(0, wallHit.Distance - 0.05);
                Explode(pr, null);
                continue;
            }
            pr.Position += step;
            pr.RangeLeft -= len;

            if (pr.FromPlayer)
            {
                Enemy? hit = Enemies.FirstOrDefault(e => e.IsAlive && Vec2.Distance(e.Position, pr.Position) < e.Radius + pr.Size * 0.5);
                if (hit != null) Explode(pr, hit);
            }
            else if (State == GameState.Playing && Vec2.Distance(pr.Position, Player.Position) < PlayerRadius + 0.08)
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
            Effects.Add(new Effect { Position = pr.Position, Size = pr.SplashRadius * 0.8, Duration = 0.35 });
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
