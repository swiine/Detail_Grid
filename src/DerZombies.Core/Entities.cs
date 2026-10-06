using System.Collections.Generic;

namespace DerZombies.Core
{
    public enum ZombieType { Walker, Runner, Sprinter, Panzer }

    public sealed class Zombie
    {
        public ZombieType Type { get; internal set; }
        public Vec2 Pos { get; internal set; }
        public double Health { get; internal set; }
        public double MaxHealth { get; internal set; }
        public double Speed { get; internal set; }
        public double Radius { get; internal set; }
        public bool Alive { get; internal set; } = true;
        /// <summary>Remaining slow time from Widow's Wine webbing.</summary>
        public double Webbed { get; internal set; }
        public double HitFlash { get; internal set; }
        internal double AttackCooldown;
        internal double FlameCooldown;
        internal double StuckTimer;
    }

    public enum ProjectileKind { Arrow, Grenade, Wolf }

    public sealed class Projectile
    {
        public ProjectileKind Kind { get; internal set; }
        public Vec2 Pos { get; internal set; }
        public Vec2 Vel { get; internal set; }
        public double Radius { get; internal set; } = 0.6;
        public short Color { get; internal set; } = 7;
        public bool Alive { get; internal set; } = true;
        internal BowVariant Variant;
        internal double Life;
        internal double Travelled;
        internal int KillsLeft;
        internal Zombie? Target;
    }

    public enum PowerUpType { MaxAmmo, InstaKill, DoublePoints, Nuke, FireSale }

    public sealed class PowerUp
    {
        public PowerUpType Type { get; internal set; }
        public Vec2 Pos { get; internal set; }
        public double Life { get; internal set; }
        public bool Alive { get; internal set; } = true;

        public string Label => Type switch
        {
            PowerUpType.MaxAmmo => "MAX AMMO",
            PowerUpType.InstaKill => "INSTA-KILL",
            PowerUpType.DoublePoints => "DOUBLE POINTS",
            PowerUpType.Nuke => "KABOOM",
            PowerUpType.FireSale => "FIRE SALE",
            _ => Type.ToString(),
        };

        /// <summary>Blinks during its last five seconds.</summary>
        public bool Visible => Life > 5 || (int)(Life * 6) % 2 == 0;
    }

    public enum EffectShape { Line, Circle }

    /// <summary>Short-lived visual: tracers, explosions, lightning, fire, vortices, webs.</summary>
    public sealed class Effect
    {
        public EffectShape Shape { get; internal set; }
        public Vec2 A { get; internal set; }
        public Vec2 B { get; internal set; }
        public double Radius { get; internal set; }
        public short Color { get; internal set; } = 7;
        public double Life { get; internal set; }
        public double MaxLife { get; internal set; }
        public bool Grow { get; internal set; }
        public bool Alive => Life > 0;
        /// <summary>Radius to draw this frame (growing circles expand over their lifetime).</summary>
        public double CurrentRadius => Grow && MaxLife > 0 ? Radius * (0.3 + 0.7 * (1 - Life / MaxLife)) : Radius;
    }

    /// <summary>Lingering damage area left by the Fire Bow, or the Void Bow's vortex.</summary>
    internal sealed class Zone
    {
        public bool Vortex;
        public Vec2 Pos;
        public double Radius;
        public double Life;
        public double Dps;
        public Effect Visual = null!;
    }

    public sealed class Player
    {
        public Vec2 Pos { get; internal set; }
        public double AimAngle { get; internal set; }
        public double Health { get; internal set; } = 150;
        public double MaxHealth => Perks.Contains(Perk.Juggernog) ? 250 : 150;
        public int Points { get; internal set; } = 500;
        public int TotalPointsEarned { get; internal set; }
        public int Kills { get; internal set; }
        public int Headshots { get; internal set; }
        public int Grenades { get; internal set; } = 2;
        public List<Perk> Perks { get; } = new List<Perk>();
        public List<WeaponInstance> Weapons { get; } = new List<WeaponInstance>();
        public int WeaponIndex { get; internal set; }
        public double ReloadTimer { get; internal set; }
        public double Invulnerable { get; internal set; }
        public int SelfRevivesUsed { get; internal set; }
        public int BowKills { get; internal set; }
        public double HurtFlash { get; internal set; }

        public WeaponInstance Current => Weapons[WeaponIndex];
        public int MaxWeapons => Perks.Contains(Perk.MuleKick) ? 3 : 2;
        public double Speed => Perks.Contains(Perk.StaminUp) ? 16.5 : 13.0;
        public bool Reloading => ReloadTimer > 0;
        public const double Radius = 1.3;

        internal double FireCooldown;
        internal double KnifeCooldown;
        internal double SinceDamage = 99;
        internal double WidowCooldown;
        internal double GondolaCooldown;
    }
}
