using System;
using System.Collections.Generic;
using System.Linq;

namespace DerZombies.Core
{
    /// <summary>Held-state input for one frame. The game does its own press (edge) detection.</summary>
    public sealed class InputState
    {
        public double MoveX;
        public double MoveY;
        /// <summary>World point to aim at (mouse), or null to keep the current aim.</summary>
        public Vec2? AimPoint;
        /// <summary>Absolute facing in radians (first-person mouse look); overrides <see cref="AimPoint"/>.</summary>
        public double? AimAngle;
        /// <summary>-1..1 keyboard aim rotation (counter-clockwise positive).</summary>
        public double AimTurn;
        public bool Fire;
        public bool Reload;
        public bool Interact;
        public bool SwitchWeapon;
        public bool Knife;
        public bool Grenade;

        public InputState Clone() => (InputState)MemberwiseClone();
    }

    public enum GameEventType { DoorOpened, PowerOn, PadActivated, PackAPunchReady, BoxMoved, DragonFed, BowReady, RoundStarted, GameOver }

    public readonly struct GameEvent
    {
        public GameEventType Type { get; }
        public string Data { get; }
        public GameEvent(GameEventType type, string data = "") { Type = type; Data = data; }
        public override string ToString() => $"{Type} {Data}";
    }

    public enum InteractKind { Door, Perk, WallBuy, Box, Power, Pad, PackAPunch, Gondola, BowPedestal, BowAltar }

    public sealed class Interactable
    {
        public InteractKind Kind { get; internal set; }
        public Vec2 Pos { get; internal set; }
        public string Key { get; internal set; } = "";
        public int Index { get; internal set; }
        public Door? Door { get; internal set; }
        public double Radius { get; internal set; } = 5.5;
    }

    public enum BoxState { Idle, Spinning, Offering, Leaving }

    public sealed class Dragon
    {
        public string Name { get; internal set; } = "";
        public Vec2 Pos { get; internal set; }
        public int Souls { get; internal set; }
        public bool Fed => Souls >= SoulsNeeded;
        public const int SoulsNeeded = 8;
        public const double Range = 20;
    }

    /// <summary>
    /// The whole simulation. Call <see cref="Update"/> once per frame with the
    /// elapsed seconds and the current input, then draw from the public state.
    /// </summary>
    public sealed class Game
    {
        public GameMap Map { get; }
        public Player Player { get; } = new Player();
        public List<Zombie> Zombies { get; } = new List<Zombie>();
        public List<Projectile> Projectiles { get; } = new List<Projectile>();
        public List<PowerUp> PowerUps { get; } = new List<PowerUp>();
        public List<Effect> Effects { get; } = new List<Effect>();
        public List<Interactable> Interactables { get; } = new List<Interactable>();
        public List<Dragon> Dragons { get; } = new List<Dragon>();

        public int Round { get; private set; }
        public double Time { get; private set; }
        public bool PowerOn { get; private set; }
        public bool[] PadsActive { get; } = new bool[3];
        public List<(string Name, Vec2 Pos)> Pads { get; } = new List<(string, Vec2)>();
        public bool PackAPunchReady => PowerOn && PadsActive.All(p => p);
        public bool BowReady => Dragons.Count > 0 && Dragons.All(d => d.Fed);
        public bool BowTaken { get; private set; }

        public List<Vec2> BoxLocations { get; } = new List<Vec2>();
        public int BoxIndex { get; private set; }
        public Vec2 BoxPos => BoxLocations[BoxIndex];
        public BoxState BoxState { get; private set; }
        public string BoxDisplay { get; private set; } = "";

        public double InstaKill { get; private set; }
        public double DoublePoints { get; private set; }
        public double FireSale { get; private set; }

        public string Prompt { get; private set; } = "";
        public string CenterMessage { get; private set; } = "";
        public IReadOnlyList<string> Feed => _feed.Select(f => f.Text).ToList();
        public bool IsGameOver { get; private set; }
        public int ZombiesRemaining => Math.Max(0, _toSpawn - _spawned) + Zombies.Count(z => z.Alive);
        public double Intermission => _intermission;

        private readonly Random _rng;
        private readonly List<GameEvent> _events = new List<GameEvent>();
        private readonly List<(string Text, double Life)> _feed = new List<(string, double)>();
        private readonly List<Zone> _zones = new List<Zone>();
        private InputState _prev = new InputState();
        private double _centerTimer;
        private int _toSpawn, _spawned, _dropsThisRound;
        private double _spawnTimer, _intermission;
        private int _nextPanzerRound = 8;
        private bool _panzerPending;
        private double _boxTimer, _boxCycle;
        private int _boxUses;
        private WeaponDef? _boxOffer;
        private double _outOfAmmoCooldown;

        public const int MaxAlive = 24;

        public Game(GameMap? map = null, int? seed = null)
        {
            Map = map ?? GameMap.CreateDefault();
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();

            Map.OpenArea(MapData.StartArea);
            foreach (var door in Map.Doors.Values)
                Interactables.Add(new Interactable { Kind = InteractKind.Door, Pos = door.Center, Door = door, Key = door.Id.ToString(), Radius = 7.5 });

            foreach (var f in MapData.Features)
            {
                var pos = Map.CellCenter(f.Col, f.Row);
                switch (f.Kind)
                {
                    case FeatureKind.PlayerStart: Player.Pos = pos; break;
                    case FeatureKind.Perk: Add(InteractKind.Perk, pos, f.Key); break;
                    case FeatureKind.WallBuy: Add(InteractKind.WallBuy, pos, f.Key); break;
                    case FeatureKind.BoxLocation: BoxLocations.Add(pos); break;
                    case FeatureKind.PowerSwitch: Add(InteractKind.Power, pos, ""); break;
                    case FeatureKind.LandingPad:
                        Add(InteractKind.Pad, pos, f.Key, Pads.Count);
                        Pads.Add((f.Key, pos));
                        break;
                    case FeatureKind.PackAPunch: Add(InteractKind.PackAPunch, pos, ""); break;
                    case FeatureKind.Gondola: Add(InteractKind.Gondola, pos, f.Key, Interactables.Count(i => i.Kind == InteractKind.Gondola)); break;
                    case FeatureKind.Dragon: Dragons.Add(new Dragon { Name = f.Key, Pos = pos }); break;
                    case FeatureKind.BowPedestal: Add(InteractKind.BowPedestal, pos, ""); break;
                    case FeatureKind.BowAltar: Add(InteractKind.BowAltar, pos, f.Key); break;
                }
            }
            Interactables.Add(new Interactable { Kind = InteractKind.Box, Radius = 6 });

            Player.Weapons.Add(new WeaponInstance(WeaponCatalog.MR6));
            Player.AimAngle = Math.PI / 2;
            _intermission = 4;
            Center("DER EISENDRACHE", 4);

            void Add(InteractKind kind, Vec2 pos, string key, int index = 0) =>
                Interactables.Add(new Interactable { Kind = kind, Pos = pos, Key = key, Index = index });
        }

        public List<GameEvent> DrainEvents()
        {
            var e = _events.ToList();
            _events.Clear();
            return e;
        }

        // ================================================================== frame

        public void Update(double dt, InputState input)
        {
            if (IsGameOver) return;
            dt = Math.Clamp(dt, 0, 0.1);
            Time += dt;

            UpdateTimers(dt);
            UpdatePlayer(dt, input);
            UpdateRound(dt);
            Map.UpdateFlowField(Player.Pos);
            UpdateZombies(dt);
            UpdateProjectiles(dt);
            UpdateZones(dt);
            UpdatePowerUps(dt);
            UpdateBox(dt);
            UpdatePrompt(input);

            Zombies.RemoveAll(z => !z.Alive);
            Projectiles.RemoveAll(p => !p.Alive);
            PowerUps.RemoveAll(p => !p.Alive);
            Effects.RemoveAll(e => !e.Alive);
            _prev = input.Clone();
        }

        private bool Pressed(bool now, bool before) => now && !before;

        private void UpdateTimers(double dt)
        {
            InstaKill = Math.Max(0, InstaKill - dt);
            DoublePoints = Math.Max(0, DoublePoints - dt);
            FireSale = Math.Max(0, FireSale - dt);
            _outOfAmmoCooldown -= dt;

            if (_centerTimer > 0 && (_centerTimer -= dt) <= 0) CenterMessage = "";
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                var f = _feed[i];
                if ((f.Life -= dt) <= 0) _feed.RemoveAt(i); else _feed[i] = f;
            }
            foreach (var e in Effects) e.Life -= dt;
        }

        // ================================================================== player

        private void UpdatePlayer(double dt, InputState input)
        {
            var p = Player;
            p.FireCooldown -= dt;
            p.KnifeCooldown -= dt;
            p.WidowCooldown -= dt;
            p.GondolaCooldown -= dt;
            p.Invulnerable = Math.Max(0, p.Invulnerable - dt);
            p.HurtFlash = Math.Max(0, p.HurtFlash - dt);
            p.SinceDamage += dt;
            if (p.SinceDamage > 2.5) p.Health = Math.Min(p.MaxHealth, p.Health + 100 * dt);

            // Movement with per-axis sliding against walls.
            var move = new Vec2(input.MoveX, input.MoveY);
            if (move.LengthSq > 1e-6)
            {
                move = move.Normalized() * p.Speed * dt;
                var nx = new Vec2(p.Pos.X + move.X, p.Pos.Y);
                if (Map.CircleFree(nx, Player.Radius)) p.Pos = nx;
                var ny = new Vec2(p.Pos.X, p.Pos.Y + move.Y);
                if (Map.CircleFree(ny, Player.Radius)) p.Pos = ny;
            }

            if (input.AimAngle.HasValue)
                p.AimAngle = input.AimAngle.Value;
            else if (input.AimPoint.HasValue && (input.AimPoint.Value - p.Pos).Length > 0.5)
                p.AimAngle = (input.AimPoint.Value - p.Pos).Angle;
            p.AimAngle += input.AimTurn * 3.5 * dt;

            // Reload progress.
            var w = p.Current;
            if (p.Reloading)
            {
                p.ReloadTimer -= dt;
                if (p.ReloadTimer <= 0)
                {
                    p.ReloadTimer = 0;
                    int take = Math.Min(w.MagSize - w.Mag, w.Reserve);
                    w.Mag += take;
                    w.Reserve -= take;
                }
            }

            if (Pressed(input.SwitchWeapon, _prev.SwitchWeapon) && p.Weapons.Count > 1)
            {
                p.ReloadTimer = 0;
                p.WeaponIndex = (p.WeaponIndex + 1) % p.Weapons.Count;
                p.FireCooldown = 0.25;
                w = p.Current;
            }

            if (Pressed(input.Reload, _prev.Reload) && !p.Reloading && w.Mag < w.MagSize && w.Reserve > 0)
                StartReload();

            bool trigger = w.Def.Auto ? input.Fire : Pressed(input.Fire, _prev.Fire);
            if (trigger && !p.Reloading && p.FireCooldown <= 0)
            {
                if (w.Mag > 0)
                {
                    FireWeapon(w);
                    w.Mag--;
                    p.FireCooldown = w.Def.FireInterval * (p.Perks.Contains(Perk.DoubleTap) ? 0.75 : 1.0);
                    if (w.Mag == 0 && w.Reserve > 0) StartReload();
                }
                else if (w.Reserve > 0) StartReload();
                else if (_outOfAmmoCooldown <= 0)
                {
                    Say("Out of ammo!");
                    _outOfAmmoCooldown = 1.5;
                }
            }

            if (Pressed(input.Knife, _prev.Knife) && p.KnifeCooldown <= 0) Knife();
            if (Pressed(input.Grenade, _prev.Grenade) && p.Grenades > 0) ThrowGrenade(input);
        }

        private void StartReload()
        {
            var p = Player;
            p.ReloadTimer = p.Current.Def.ReloadTime * (p.Perks.Contains(Perk.SpeedCola) ? 0.5 : 1.0);
        }

        private Vec2 AimDir => Vec2.FromAngle(Player.AimAngle);

        private void FireWeapon(WeaponInstance w)
        {
            var p = Player;
            double dtMul = p.Perks.Contains(Perk.DoubleTap) ? 2.0 : 1.0;
            switch (w.Def.Kind)
            {
                case WeaponKind.Bow:
                    Projectiles.Add(new Projectile
                    {
                        Kind = ProjectileKind.Arrow,
                        Pos = p.Pos + AimDir * 1.5,
                        Vel = AimDir * 90,
                        Variant = w.Bow,
                        Radius = 0.7,
                        Color = BowColor(w.Bow),
                    });
                    break;

                case WeaponKind.RayGun:
                    FireRay(p.Pos, AimDir, w.Damage * dtMul, 1, w.Def.Range, 0, w.Packed ? (short)1 : (short)3, splash: w.Packed ? 7 : 5);
                    break;

                default:
                    for (int i = 0; i < w.Def.Pellets; i++)
                    {
                        double a = p.AimAngle + (_rng.NextDouble() * 2 - 1) * w.Def.Spread;
                        FireRay(p.Pos, Vec2.FromAngle(a), w.Damage * dtMul, w.Def.Pierce + (w.Packed ? 1 : 0),
                            w.Def.Range, w.Def.HeadshotChance, w.Packed ? (short)6 : (short)2, splash: 0);
                    }
                    break;
            }
        }

        private void FireRay(Vec2 origin, Vec2 dir, double damage, int pierce, double range, double headshotChance, short color, double splash)
        {
            double wall = Map.Raycast(origin, dir, range);
            var hits = Zombies
                .Where(z => z.Alive)
                .Select(z => (z, t: Vec2.Dot(z.Pos - origin, dir)))
                .Where(h => h.t > 0 && h.t <= wall + h.z.Radius && Math.Abs(Vec2.Cross(dir, h.z.Pos - origin)) <= h.z.Radius + 0.2)
                .OrderBy(h => h.t)
                .ToList();

            double end = wall;
            int n = 0;
            foreach (var (z, t) in hits)
            {
                bool head = z.Type != ZombieType.Panzer && _rng.NextDouble() < headshotChance;
                DamageZombie(z, head ? damage * 2 : damage, head ? DamageCause.Headshot : DamageCause.Bullet);
                if (++n >= pierce) { end = Math.Min(wall, t); break; }
            }
            AddLine(origin + dir * 1.5, origin + dir * end, color, 0.06);
            if (splash > 0) Explode(origin + dir * end, splash, damage, color, DamageCause.Explosive);
        }

        private void Knife()
        {
            var p = Player;
            p.KnifeCooldown = 0.6;
            var dir = AimDir;
            var target = Zombies
                .Where(z => z.Alive)
                .Select(z => (z, d: Vec2.Distance(z.Pos, p.Pos)))
                .Where(h => h.d < 2.5 + h.z.Radius || (h.d < 4.5 + h.z.Radius && Vec2.Dot((h.z.Pos - p.Pos).Normalized(), dir) > 0.35))
                .OrderBy(h => h.d)
                .Select(h => h.z)
                .FirstOrDefault();
            AddLine(p.Pos + dir * 1.3, p.Pos + dir * 4.5, 7, 0.1);
            if (target != null) DamageZombie(target, 150 + 25 * Round, DamageCause.Knife);
        }

        private void ThrowGrenade(InputState input)
        {
            var p = Player;
            p.Grenades--;
            Projectiles.Add(new Projectile
            {
                Kind = ProjectileKind.Grenade,
                Pos = p.Pos + AimDir * 1.5,
                Vel = AimDir * 45,
                Life = 1.5,
                Radius = 0.6,
                Color = 30,
            });
        }

        private void DamagePlayer(double amount)
        {
            var p = Player;
            if (p.Invulnerable > 0 || IsGameOver) return;
            p.Health -= amount;
            p.SinceDamage = 0;
            p.HurtFlash = 0.3;

            if (p.Perks.Contains(Perk.WidowsWine) && p.WidowCooldown <= 0)
            {
                p.WidowCooldown = 12;
                foreach (var z in Zombies.Where(z => z.Alive && Vec2.Distance(z.Pos, p.Pos) < 15))
                    z.Webbed = 6;
                AddCircle(p.Pos, 15, 6, 0.6, grow: true);
                Say("Widow's Wine!");
            }

            if (p.Health <= 0) Down();
        }

        private void Down()
        {
            var p = Player;
            if (p.Perks.Contains(Perk.QuickRevive) && p.SelfRevivesUsed < 3)
            {
                p.SelfRevivesUsed++;
                p.Perks.Clear();
                while (p.Weapons.Count > p.MaxWeapons)
                {
                    LoseWeapon(p.Weapons.Count - 1);
                }
                p.WeaponIndex = Math.Min(p.WeaponIndex, p.Weapons.Count - 1);
                p.Health = p.MaxHealth;
                p.Invulnerable = 4;
                foreach (var z in Zombies.Where(z => z.Alive && Vec2.Distance(z.Pos, p.Pos) < 12))
                {
                    var pushed = z.Pos + (z.Pos - p.Pos).Normalized() * 8;
                    if (Map.CircleFree(pushed, z.Radius * 0.8)) z.Pos = pushed;
                }
                AddCircle(p.Pos, 12, 4, 0.8, grow: true);
                Center($"SELF REVIVE  ({3 - p.SelfRevivesUsed} left) - perks lost", 3);
                return;
            }

            p.Health = 0;
            IsGameOver = true;
            Center($"GAME OVER - you survived {Round} round{(Round == 1 ? "" : "s")}", 999);
            _events.Add(new GameEvent(GameEventType.GameOver, Round.ToString()));
        }

        // ================================================================== rounds & spawning

        public static int ZombiesForRound(int r) =>
            r <= 4 ? new[] { 6, 8, 13, 18 }[Math.Max(r, 1) - 1] : (int)(24 + (r - 5) * 3 + 0.09 * r * r);

        public static double HealthForRound(int r) =>
            r < 10 ? 150 + 100 * (r - 1) : 950 * Math.Pow(1.1, r - 9);

        private void UpdateRound(double dt)
        {
            if (_intermission > 0)
            {
                _intermission -= dt;
                if (_intermission <= 0) StartRound(Round + 1);
                return;
            }

            if (_spawned >= _toSpawn && !_panzerPending && !Zombies.Any(z => z.Alive))
            {
                Center($"ROUND {Round} SURVIVED", 4);
                _intermission = 10;
                return;
            }

            _spawnTimer -= dt;
            int alive = Zombies.Count(z => z.Alive);
            if (_spawnTimer <= 0 && alive < MaxAlive)
            {
                if (_panzerPending && _spawned >= _toSpawn / 2)
                {
                    if (SpawnZombie(ZombieType.Panzer))
                    {
                        _panzerPending = false;
                        _nextPanzerRound = Round + _rng.Next(4, 7);
                        Center("PANZER SOLDAT INCOMING!", 3);
                    }
                }
                else if (_spawned < _toSpawn && SpawnZombie(RollZombieType()))
                {
                    _spawned++;
                }
                _spawnTimer = Math.Max(0.3, 2.0 * Math.Pow(0.95, Round - 1));
            }
        }

        private void StartRound(int r)
        {
            Round = r;
            _toSpawn = ZombiesForRound(r);
            _spawned = 0;
            _dropsThisRound = 0;
            _spawnTimer = 1.5;
            _panzerPending = r >= _nextPanzerRound;
            if (r > 1) Player.Grenades = Math.Min(4, Player.Grenades + 2);
            Center($"ROUND {r}", 4);
            _events.Add(new GameEvent(GameEventType.RoundStarted, r.ToString()));
        }

        private ZombieType RollZombieType()
        {
            double p = _rng.NextDouble();
            int r = Round;
            if (r <= 2) return ZombieType.Walker;
            if (r <= 4) return p < 0.5 ? ZombieType.Walker : ZombieType.Runner;
            if (r <= 9) return p < 0.15 ? ZombieType.Walker : (r >= 7 && p > 0.85) ? ZombieType.Sprinter : ZombieType.Runner;
            return p < 0.4 ? ZombieType.Runner : ZombieType.Sprinter;
        }

        private bool SpawnZombie(ZombieType type)
        {
            var spawners = Map.Areas.Values
                .Where(a => a.Open)
                .SelectMany(a => a.Spawners)
                .Select(s => (s, d: Map.FlowDistance(s)))
                .Where(x => x.d != GameMap.Unreachable && x.d >= 4)
                .ToList();
            if (spawners.Count == 0) return false;

            var near = spawners.Where(x => x.d <= 22).ToList();
            if (near.Count == 0) near = spawners.OrderBy(x => x.d).Take(3).ToList();
            var pos = near[_rng.Next(near.Count)].s;

            var z = new Zombie { Type = type, Pos = pos };
            switch (type)
            {
                case ZombieType.Panzer:
                    z.MaxHealth = 2500 + 750 * Round; z.Speed = 7; z.Radius = 2.5; break;
                case ZombieType.Sprinter:
                    z.MaxHealth = HealthForRound(Round); z.Speed = 12; z.Radius = 1.2; break;
                case ZombieType.Runner:
                    z.MaxHealth = HealthForRound(Round); z.Speed = 8.5; z.Radius = 1.2; break;
                default:
                    z.MaxHealth = HealthForRound(Round); z.Speed = 4.5; z.Radius = 1.2; break;
            }
            z.Speed *= 0.9 + 0.2 * _rng.NextDouble();
            z.Health = z.MaxHealth;
            Zombies.Add(z);
            return true;
        }

        // ================================================================== zombies

        private void UpdateZombies(double dt)
        {
            var p = Player;
            foreach (var z in Zombies)
            {
                if (!z.Alive) continue;
                z.AttackCooldown -= dt;
                z.FlameCooldown -= dt;
                z.Webbed = Math.Max(0, z.Webbed - dt);
                z.HitFlash = Math.Max(0, z.HitFlash - dt);
                z.StuckTimer = Math.Max(0, z.StuckTimer - dt);

                var toPlayer = p.Pos - z.Pos;
                double dist = toPlayer.Length;
                double reach = z.Radius + Player.Radius + 0.6;

                if (dist <= reach)
                {
                    if (z.AttackCooldown <= 0)
                    {
                        z.AttackCooldown = z.Type == ZombieType.Panzer ? 1.5 : 1.0;
                        DamagePlayer(z.Type == ZombieType.Panzer ? 75 : 50);
                    }
                    continue;
                }

                if (z.Type == ZombieType.Panzer && dist < 14 && z.FlameCooldown <= 0 && Map.HasLineOfSight(z.Pos, p.Pos))
                {
                    z.FlameCooldown = 0.4;
                    AddLine(z.Pos, p.Pos, 30, 0.25);
                    DamagePlayer(15);
                }

                Vec2 goal;
                if (z.StuckTimer <= 0 && dist < 25 && Map.HasLineOfSight(z.Pos, p.Pos)) goal = p.Pos;
                else goal = Map.NextWaypoint(z.Pos) ?? p.Pos;

                double speed = z.Speed * (z.Webbed > 0 ? 0.35 : 1.0);
                var step = (goal - z.Pos).Normalized() * speed * dt;
                var before = z.Pos;
                MoveCircle(z, step);
                if ((z.Pos - before).Length < speed * dt * 0.25) z.StuckTimer = 1.0;
            }

            // Keep zombies from stacking on top of each other.
            var alive = Zombies.Where(z => z.Alive).ToList();
            for (int i = 0; i < alive.Count; i++)
            for (int j = i + 1; j < alive.Count; j++)
            {
                var a = alive[i]; var b = alive[j];
                var d = b.Pos - a.Pos;
                double min = a.Radius + b.Radius, len = d.Length;
                if (len >= min) continue;
                var push = (len < 1e-4 ? new Vec2(1, 0) : d / len) * ((min - len) * 0.5);
                MoveCircle(a, -push);
                MoveCircle(b, push);
            }
        }

        private void MoveCircle(Zombie z, Vec2 step)
        {
            double r = z.Radius * 0.8;
            var nx = new Vec2(z.Pos.X + step.X, z.Pos.Y);
            if (Map.CircleFree(nx, r)) z.Pos = nx;
            var ny = new Vec2(z.Pos.X, z.Pos.Y + step.Y);
            if (Map.CircleFree(ny, r)) z.Pos = ny;
        }

        private enum DamageCause { Bullet, Headshot, Knife, Explosive, Wonder, Nuke }

        private void DamageZombie(Zombie z, double damage, DamageCause cause)
        {
            if (!z.Alive) return;
            if (InstaKill > 0 && z.Type != ZombieType.Panzer) damage = z.Health;
            z.Health -= damage;
            z.HitFlash = 0.1;
            if (z.Health <= 0) KillZombie(z, cause);
            else if (cause == DamageCause.Bullet || cause == DamageCause.Headshot || cause == DamageCause.Knife) AddPoints(10);
        }

        private void KillZombie(Zombie z, DamageCause cause)
        {
            z.Alive = false;
            var p = Player;
            p.Kills++;
            if (cause == DamageCause.Headshot) p.Headshots++;
            if (cause == DamageCause.Wonder) p.BowKills++;

            int pts = z.Type == ZombieType.Panzer ? 500 : cause switch
            {
                DamageCause.Headshot => 100,
                DamageCause.Knife => 130,
                DamageCause.Nuke => 0,
                _ => 60,
            };
            AddPoints(pts);

            // Dragon soul feeding (only one dragon eats each soul).
            var dragon = Dragons.FirstOrDefault(d => !d.Fed && Vec2.Distance(d.Pos, z.Pos) <= Dragon.Range);
            if (dragon != null)
            {
                dragon.Souls++;
                AddLine(z.Pos, dragon.Pos, 30, 0.4);
                if (dragon.Fed)
                {
                    _events.Add(new GameEvent(GameEventType.DragonFed, dragon.Name));
                    Say($"{dragon.Name} is satisfied!");
                    if (BowReady)
                    {
                        _events.Add(new GameEvent(GameEventType.BowReady));
                        Center("THE DRAGONS ARE FED - CLAIM THE BOW (Clock Tower)", 5);
                    }
                }
            }

            if (cause == DamageCause.Nuke) return;
            if (z.Type == ZombieType.Panzer) SpawnPowerUp(z.Pos, PowerUpType.MaxAmmo);
            else if (_dropsThisRound < 4 && _rng.NextDouble() < 0.025)
            {
                _dropsThisRound++;
                var types = (PowerUpType[])Enum.GetValues(typeof(PowerUpType));
                SpawnPowerUp(z.Pos, types[_rng.Next(types.Length)]);
            }
        }

        private void AddPoints(int pts)
        {
            if (DoublePoints > 0) pts *= 2;
            Player.Points += pts;
            Player.TotalPointsEarned += pts;
        }

        private void Explode(Vec2 pos, double radius, double damage, short color, DamageCause cause)
        {
            AddCircle(pos, radius, color, 0.35, grow: true);
            foreach (var z in Zombies.Where(z => z.Alive && Vec2.Distance(z.Pos, pos) <= radius + z.Radius).ToList())
                DamageZombie(z, damage, cause);
        }

        // ================================================================== projectiles & wonder weapon

        private static short BowColor(BowVariant v) => v switch
        {
            BowVariant.Storm => 4,
            BowVariant.Fire => 1,
            BowVariant.Void => 6,
            BowVariant.Wolf => 8,
            _ => 30,
        };

        private double BowDamage => 2000 + 400 * Round;

        private void UpdateProjectiles(double dt)
        {
            foreach (var pr in Projectiles.ToList())
            {
                if (!pr.Alive) continue;
                switch (pr.Kind)
                {
                    case ProjectileKind.Arrow:
                    {
                        var next = pr.Pos + pr.Vel * dt;
                        pr.Travelled += (next - pr.Pos).Length;
                        var hit = Zombies.FirstOrDefault(z => z.Alive && Vec2.Distance(z.Pos, next) <= z.Radius + pr.Radius);
                        if (hit != null) { pr.Alive = false; ArrowImpact(hit.Pos, pr.Variant); }
                        else if (!Map.IsWalkableAt(next) || pr.Travelled > WeaponCatalog.Bow.Range) { pr.Alive = false; ArrowImpact(pr.Pos, pr.Variant); }
                        else pr.Pos = next;
                        break;
                    }
                    case ProjectileKind.Grenade:
                    {
                        var next = pr.Pos + pr.Vel * dt;
                        if (Map.IsWalkableAt(next)) pr.Pos = next; else pr.Vel = Vec2.Zero;
                        pr.Vel *= Math.Exp(-2.5 * dt);
                        pr.Life -= dt;
                        if (pr.Life <= 0)
                        {
                            pr.Alive = false;
                            Explode(pr.Pos, 11, 300 + 150 * Round, 30, DamageCause.Explosive);
                        }
                        break;
                    }
                    case ProjectileKind.Wolf:
                    {
                        pr.Life -= dt;
                        if (pr.Target == null || !pr.Target.Alive)
                            pr.Target = Zombies.Where(z => z.Alive).OrderBy(z => Vec2.Distance(z.Pos, pr.Pos)).FirstOrDefault();
                        if (pr.Target == null)
                        {
                            // Nothing to hunt: circle the player.
                            var orbit = Player.Pos + Vec2.FromAngle(Time * 3) * 4;
                            pr.Pos += (orbit - pr.Pos) * Math.Min(1, 4 * dt);
                        }
                        else
                        {
                            var d = pr.Target.Pos - pr.Pos;
                            if (d.Length <= pr.Target.Radius + 1.2)
                            {
                                DamageZombie(pr.Target, BowDamage * 3, DamageCause.Wonder);
                                AddCircle(pr.Pos, 3, 8, 0.25, grow: true);
                                pr.KillsLeft--;
                                pr.Target = null;
                            }
                            else pr.Pos += d.Normalized() * Math.Min(d.Length, 26 * dt);
                        }
                        if (pr.Life <= 0 || pr.KillsLeft <= 0) pr.Alive = false;
                        break;
                    }
                }
            }
        }

        private void ArrowImpact(Vec2 pos, BowVariant variant)
        {
            double dmg = BowDamage;
            switch (variant)
            {
                case BowVariant.Storm:
                {
                    Explode(pos, 7, dmg * 2, 4, DamageCause.Wonder);
                    var struck = new HashSet<Zombie>();
                    var from = pos;
                    for (int i = 0; i < 8; i++)
                    {
                        var next = Zombies
                            .Where(z => z.Alive && !struck.Contains(z) && Vec2.Distance(z.Pos, from) < 20)
                            .OrderBy(z => Vec2.Distance(z.Pos, from))
                            .FirstOrDefault();
                        if (next == null) break;
                        struck.Add(next);
                        AddLine(from, next.Pos, 4, 0.3);
                        from = next.Pos;
                        DamageZombie(next, 6000 + 600 * Round, DamageCause.Wonder);
                    }
                    break;
                }
                case BowVariant.Fire:
                    Explode(pos, 9, dmg * 2, 1, DamageCause.Wonder);
                    AddZone(false, pos, 12, 5, 1500 + 300 * Round, 1);
                    break;
                case BowVariant.Void:
                    Explode(pos, 5, dmg, 6, DamageCause.Wonder);
                    AddZone(true, pos, 18, 2.5, 0, 6);
                    break;
                case BowVariant.Wolf:
                    Explode(pos, 6, dmg, 8, DamageCause.Wonder);
                    Projectiles.Add(new Projectile { Kind = ProjectileKind.Wolf, Pos = pos, Life = 12, KillsLeft = 6, Radius = 1.0, Color = 7 });
                    break;
                default:
                    Explode(pos, 7, dmg, 30, DamageCause.Wonder);
                    break;
            }
        }

        private void AddZone(bool vortex, Vec2 pos, double radius, double life, double dps, short color)
        {
            var visual = new Effect { Shape = EffectShape.Circle, A = pos, Radius = radius, Color = color, Life = life, MaxLife = life };
            Effects.Add(visual);
            _zones.Add(new Zone { Vortex = vortex, Pos = pos, Radius = radius, Life = life, Dps = dps, Visual = visual });
        }

        private void UpdateZones(double dt)
        {
            foreach (var zone in _zones.ToList())
            {
                zone.Life -= dt;
                foreach (var z in Zombies.Where(z => z.Alive && Vec2.Distance(z.Pos, zone.Pos) <= zone.Radius).ToList())
                {
                    if (zone.Vortex)
                    {
                        var d = zone.Pos - z.Pos;
                        if (d.Length > 0.5) MoveCircle(z, d.Normalized() * Math.Min(d.Length, 20 * dt));
                    }
                    else DamageZombie(z, zone.Dps * dt, DamageCause.Wonder);
                }
                if (zone.Life <= 0)
                {
                    _zones.Remove(zone);
                    if (zone.Vortex)
                    {
                        AddCircle(zone.Pos, 8, 6, 0.4, grow: true);
                        foreach (var z in Zombies.Where(z => z.Alive && Vec2.Distance(z.Pos, zone.Pos) <= 8).ToList())
                            DamageZombie(z, z.Type == ZombieType.Panzer ? BowDamage * 2 : z.Health, DamageCause.Wonder);
                    }
                }
            }
        }

        // ================================================================== power-ups

        private void SpawnPowerUp(Vec2 pos, PowerUpType type) =>
            PowerUps.Add(new PowerUp { Type = type, Pos = pos, Life = 25 });

        private void UpdatePowerUps(double dt)
        {
            foreach (var pu in PowerUps)
            {
                pu.Life -= dt;
                if (pu.Life <= 0) { pu.Alive = false; continue; }
                if (Vec2.Distance(pu.Pos, Player.Pos) > 3.5) continue;

                pu.Alive = false;
                Center(pu.Label + "!", 2.5);
                switch (pu.Type)
                {
                    case PowerUpType.MaxAmmo:
                        foreach (var w in Player.Weapons) w.Reserve = w.MaxReserve;
                        Player.Grenades = 4;
                        break;
                    case PowerUpType.InstaKill: InstaKill = 30; break;
                    case PowerUpType.DoublePoints: DoublePoints = 30; break;
                    case PowerUpType.FireSale: FireSale = 30; break;
                    case PowerUpType.Nuke:
                        AddCircle(Player.Pos, 60, 30, 0.8, grow: true);
                        foreach (var z in Zombies.Where(z => z.Alive && z.Type != ZombieType.Panzer).ToList())
                            KillZombie(z, DamageCause.Nuke);
                        AddPoints(400);
                        break;
                }
            }
        }

        // ================================================================== mystery box

        private void UpdateBox(double dt)
        {
            switch (BoxState)
            {
                case BoxState.Spinning:
                    _boxTimer -= dt;
                    _boxCycle -= dt;
                    if (_boxCycle <= 0)
                    {
                        _boxCycle = 0.12;
                        var pool = WeaponCatalog.BoxPool;
                        BoxDisplay = pool[_rng.Next(pool.Count)].Name;
                    }
                    if (_boxTimer > 0) break;

                    if (FireSale <= 0 && _boxUses >= 4 && BoxLocations.Count > 1 && _rng.NextDouble() < 0.2)
                    {
                        BoxState = BoxState.Leaving;
                        _boxTimer = 3;
                        BoxDisplay = "Teddy Bear";
                        Player.Points += 950;
                        Center("BYE BYE! (950 refunded)", 3);
                    }
                    else
                    {
                        var owned = Player.Weapons.Select(w => w.Def).ToHashSet();
                        var pool = WeaponCatalog.BoxPool.Where(w => !owned.Contains(w)).ToList();
                        if (pool.Count == 0) pool = WeaponCatalog.BoxPool.ToList();
                        _boxOffer = pool[_rng.Next(pool.Count)];
                        BoxDisplay = _boxOffer.Name;
                        BoxState = BoxState.Offering;
                        _boxTimer = 8;
                    }
                    break;

                case BoxState.Offering:
                    if ((_boxTimer -= dt) <= 0) { BoxState = BoxState.Idle; BoxDisplay = ""; _boxOffer = null; }
                    break;

                case BoxState.Leaving:
                    if ((_boxTimer -= dt) <= 0)
                    {
                        int next;
                        do next = _rng.Next(BoxLocations.Count); while (next == BoxIndex);
                        BoxIndex = next;
                        _boxUses = 0;
                        BoxState = BoxState.Idle;
                        BoxDisplay = "";
                        string where = Map.Areas.TryGetValue(Map.AreaAt(BoxPos), out var a) ? a.Name : "somewhere";
                        Say($"The Mystery Box moved to the {where}");
                        _events.Add(new GameEvent(GameEventType.BoxMoved, BoxIndex.ToString()));
                    }
                    break;
            }
        }

        // ================================================================== interaction

        private void UpdatePrompt(InputState input)
        {
            Prompt = "";
            var box = Interactables.First(i => i.Kind == InteractKind.Box);
            box.Pos = BoxPos;

            Interactable? best = null;
            double bestD = double.MaxValue;
            foreach (var it in Interactables)
            {
                if (it.Kind == InteractKind.Door && it.Door!.Open) continue;
                double d = Vec2.Distance(it.Pos, Player.Pos);
                if (d <= it.Radius && d < bestD) { best = it; bestD = d; }
            }
            if (best == null) return;

            Prompt = PromptFor(best);
            if (Pressed(input.Interact, _prev.Interact)) Use(best);
        }

        private string PromptFor(Interactable it)
        {
            var p = Player;
            switch (it.Kind)
            {
                case InteractKind.Door:
                {
                    var names = it.Door!.Areas.Where(a => !Map.Areas[a].Open).Select(a => Map.Areas[a].Name).ToList();
                    if (names.Count == 0) names = it.Door.Areas.Select(a => Map.Areas[a].Name).ToList();
                    return $"[E] Open the way to {string.Join(" / ", names)} - {it.Door.Cost}";
                }
                case InteractKind.Perk:
                {
                    var perk = Enum.Parse<Perk>(it.Key);
                    if (p.Perks.Contains(perk)) return $"{Perks.DisplayName(perk)} (owned)";
                    if (!PowerOn && perk != Perk.QuickRevive) return $"{Perks.DisplayName(perk)} - the power must be on";
                    if (p.Perks.Count >= Perks.MaxPerks) return "Perk limit reached";
                    return $"[E] Buy {Perks.DisplayName(perk)} - {Perks.Cost(perk)}";
                }
                case InteractKind.WallBuy:
                {
                    var def = WeaponCatalog.Get(it.Key);
                    var owned = p.Weapons.FirstOrDefault(w => w.Def == def);
                    return owned == null ? $"[E] Buy {def.Name} - {def.WallCost}" : $"[E] Buy ammo for {owned.Name} - {AmmoCost(owned)}";
                }
                case InteractKind.Box:
                    return BoxState switch
                    {
                        BoxState.Idle => $"[E] Mystery Box - {BoxCost}",
                        BoxState.Spinning => $"Mystery Box: {BoxDisplay}...",
                        BoxState.Offering => $"[E] Take {BoxDisplay}",
                        _ => "",
                    };
                case InteractKind.Power:
                    return PowerOn ? "Power is on" : "[E] Turn on the power";
                case InteractKind.Pad:
                    if (!PowerOn) return $"{it.Key} landing pad - the power must be on";
                    return PadsActive[it.Index] ? $"{it.Key} landing pad online" : $"[E] Activate the {it.Key} landing pad";
                case InteractKind.PackAPunch:
                    if (!PowerOn) return "Pack-a-Punch - the power must be on";
                    if (!PackAPunchReady) return $"Pack-a-Punch - activate the landing pads ({PadsActive.Count(a => a)}/3)";
                    if (!p.Current.CanPack) return "Pack-a-Punch - this weapon can't be upgraded";
                    return $"[E] Pack-a-Punch {p.Current.Name} - 5000";
                case InteractKind.Gondola:
                {
                    if (!PowerOn) return "Gondola - the power must be on";
                    if (p.GondolaCooldown > 0) return $"Gondola returning ({Math.Ceiling(p.GondolaCooldown)}s)";
                    var other = OtherGondola(it);
                    return $"[E] Ride the gondola to the {other.Key}";
                }
                case InteractKind.BowPedestal:
                    if (BowTaken) return "";
                    if (!BowReady) return $"Feed the dragons to claim the bow ({Dragons.Count(d => d.Fed)}/{Dragons.Count} fed)";
                    return "[E] Take the Wrath of the Ancients";
                case InteractKind.BowAltar:
                {
                    var bow = p.Weapons.FirstOrDefault(w => w.Def.Kind == WeaponKind.Bow);
                    if (bow == null) return $"{it.Key} altar - requires the Wrath of the Ancients";
                    if (bow.Bow != BowVariant.Base) return $"{it.Key} altar - your bow is already upgraded";
                    if (p.BowKills < 10) return $"{it.Key} altar - bow kills {p.BowKills}/10";
                    return $"[E] Reforge the bow: {it.Key} Bow";
                }
            }
            return "";
        }

        private int BoxCost => FireSale > 0 ? 10 : 950;

        private static int AmmoCost(WeaponInstance w) => w.Packed ? 4500 : w.Def.WallCost / 2;

        private Interactable OtherGondola(Interactable it) =>
            Interactables.First(i => i.Kind == InteractKind.Gondola && i != it);

        private bool Pay(int cost)
        {
            if (Player.Points < cost) { Say("Not enough points"); return false; }
            Player.Points -= cost;
            return true;
        }

        private void Use(Interactable it)
        {
            var p = Player;
            switch (it.Kind)
            {
                case InteractKind.Door:
                    if (!Pay(it.Door!.Cost)) return;
                    Map.OpenDoor(it.Door.Id);
                    Say($"Opened: {string.Join(" / ", it.Door.Areas.Select(a => Map.Areas[a].Name))}");
                    _events.Add(new GameEvent(GameEventType.DoorOpened, it.Door.Id.ToString()));
                    break;

                case InteractKind.Perk:
                {
                    var perk = Enum.Parse<Perk>(it.Key);
                    if (p.Perks.Contains(perk) || p.Perks.Count >= Perks.MaxPerks) return;
                    if (!PowerOn && perk != Perk.QuickRevive) return;
                    if (!Pay(Perks.Cost(perk))) return;
                    p.Perks.Add(perk);
                    if (perk == Perk.Juggernog) p.Health = p.MaxHealth;
                    Center(Perks.DisplayName(perk).ToUpperInvariant(), 2.5);
                    break;
                }

                case InteractKind.WallBuy:
                {
                    var def = WeaponCatalog.Get(it.Key);
                    var owned = p.Weapons.FirstOrDefault(w => w.Def == def);
                    if (owned != null)
                    {
                        if (owned.Reserve >= owned.MaxReserve) { Say("Ammo is already full"); return; }
                        if (!Pay(AmmoCost(owned))) return;
                        owned.Refill();
                        Say($"{owned.Name} ammo refilled");
                    }
                    else
                    {
                        if (!Pay(def.WallCost)) return;
                        GiveWeapon(new WeaponInstance(def));
                    }
                    break;
                }

                case InteractKind.Box:
                    if (BoxState == BoxState.Idle)
                    {
                        if (!Pay(BoxCost)) return;
                        BoxState = BoxState.Spinning;
                        _boxTimer = 2.5;
                        _boxCycle = 0;
                        _boxUses++;
                    }
                    else if (BoxState == BoxState.Offering && _boxOffer != null)
                    {
                        GiveWeapon(new WeaponInstance(_boxOffer));
                        _boxOffer = null;
                        BoxState = BoxState.Idle;
                        BoxDisplay = "";
                    }
                    break;

                case InteractKind.Power:
                    if (PowerOn) return;
                    PowerOn = true;
                    Center("THE POWER IS ON", 3);
                    _events.Add(new GameEvent(GameEventType.PowerOn));
                    break;

                case InteractKind.Pad:
                    if (!PowerOn || PadsActive[it.Index]) return;
                    PadsActive[it.Index] = true;
                    _events.Add(new GameEvent(GameEventType.PadActivated, it.Index.ToString()));
                    if (PackAPunchReady)
                    {
                        Center("PACK-A-PUNCH IS READY (Undercroft)", 4);
                        _events.Add(new GameEvent(GameEventType.PackAPunchReady));
                    }
                    else Say($"Landing pad online ({PadsActive.Count(a => a)}/3)");
                    break;

                case InteractKind.PackAPunch:
                    if (!PackAPunchReady || !p.Current.CanPack) return;
                    if (!Pay(5000)) return;
                    p.Current.Pack();
                    p.ReloadTimer = 0;
                    AddCircle(it.Pos, 6, 6, 0.6, grow: true);
                    Center(p.Current.Name.ToUpperInvariant(), 3);
                    break;

                case InteractKind.Gondola:
                {
                    if (!PowerOn || p.GondolaCooldown > 0) return;
                    var other = OtherGondola(it);
                    AddCircle(p.Pos, 5, 4, 0.5, grow: true);
                    p.Pos = other.Pos;
                    p.GondolaCooldown = 20;
                    AddCircle(p.Pos, 5, 4, 0.5, grow: true);
                    Say($"Gondola: arrived at the {other.Key}");
                    break;
                }

                case InteractKind.BowPedestal:
                    if (BowTaken || !BowReady) return;
                    BowTaken = true;
                    GiveWeapon(new WeaponInstance(WeaponCatalog.Bow));
                    Center("WRATH OF THE ANCIENTS", 3);
                    break;

                case InteractKind.BowAltar:
                {
                    var bow = p.Weapons.FirstOrDefault(w => w.Def.Kind == WeaponKind.Bow);
                    if (bow == null || bow.Bow != BowVariant.Base || p.BowKills < 10) return;
                    bow.UpgradeBow(Enum.Parse<BowVariant>(it.Key));
                    AddCircle(it.Pos, 8, BowColor(bow.Bow), 0.8, grow: true);
                    Center(bow.Name.ToUpperInvariant(), 3);
                    break;
                }
            }
        }

        private void GiveWeapon(WeaponInstance w)
        {
            var p = Player;
            p.ReloadTimer = 0;
            if (p.Weapons.Count < p.MaxWeapons)
            {
                p.Weapons.Add(w);
                p.WeaponIndex = p.Weapons.Count - 1;
            }
            else
            {
                LoseWeapon(p.WeaponIndex);
                p.Weapons.Insert(p.WeaponIndex, w);
            }
            Say($"Picked up {w.Name}");
        }

        private void LoseWeapon(int index)
        {
            var p = Player;
            if (p.Weapons[index].Def.Kind == WeaponKind.Bow) BowTaken = false; // the pedestal hands it out again
            p.Weapons.RemoveAt(index);
        }

        // ================================================================== messages & effects

        private void Center(string text, double seconds)
        {
            CenterMessage = text;
            _centerTimer = seconds;
        }

        private void Say(string text)
        {
            _feed.Add((text, 4));
            if (_feed.Count > 5) _feed.RemoveAt(0);
        }

        private void AddLine(Vec2 a, Vec2 b, short color, double life) =>
            Effects.Add(new Effect { Shape = EffectShape.Line, A = a, B = b, Color = color, Life = life, MaxLife = life });

        private void AddCircle(Vec2 c, double r, short color, double life, bool grow) =>
            Effects.Add(new Effect { Shape = EffectShape.Circle, A = c, Radius = r, Color = color, Life = life, MaxLife = life, Grow = grow });

        // ================================================================== debug / testing hooks

        /// <summary>Cheat used by the simulator and by the DERZCHEAT command.</summary>
        public void GivePoints(int points) => Player.Points += points;
    }
}
