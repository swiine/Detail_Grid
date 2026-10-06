using System;
using System.Collections.Generic;
using System.Linq;

namespace DerZombies.Core
{
    public enum WeaponKind { Hitscan, RayGun, Bow }

    public enum BowVariant { None, Base, Storm, Fire, Void, Wolf }

    public sealed class WeaponDef
    {
        public string Name { get; init; } = "";
        public WeaponKind Kind { get; init; } = WeaponKind.Hitscan;
        public int Damage { get; init; }
        /// <summary>Seconds between shots.</summary>
        public double FireInterval { get; init; }
        public int MagSize { get; init; }
        public int MaxReserve { get; init; }
        public double ReloadTime { get; init; }
        public bool Auto { get; init; }
        public int Pellets { get; init; } = 1;
        /// <summary>Half-angle of the random spread cone, radians.</summary>
        public double Spread { get; init; } = 0.02;
        /// <summary>How many zombies one bullet can pass through.</summary>
        public int Pierce { get; init; } = 1;
        public double HeadshotChance { get; init; } = 0.2;
        public double Range { get; init; } = 140;
        /// <summary>Wall-buy price; 0 for weapons that only come from the box.</summary>
        public int WallCost { get; init; }
        public bool InBox { get; init; }
        public string PapName { get; init; } = "";
    }

    public sealed class WeaponInstance
    {
        public WeaponDef Def { get; }
        public bool Packed { get; private set; }
        public BowVariant Bow { get; private set; }
        public int Mag { get; set; }
        public int Reserve { get; set; }

        public WeaponInstance(WeaponDef def)
        {
            Def = def;
            Bow = def.Kind == WeaponKind.Bow ? BowVariant.Base : BowVariant.None;
            Mag = MagSize;
            Reserve = MaxReserve;
        }

        public string Name => Bow switch
        {
            BowVariant.None => Packed ? Def.PapName : Def.Name,
            BowVariant.Base => Def.Name,
            BowVariant.Storm => "Storm Bow",
            BowVariant.Fire => "Fire Bow",
            BowVariant.Void => "Void Bow",
            BowVariant.Wolf => "Wolf Bow",
            _ => Def.Name,
        };

        public int Damage => Packed ? (int)(Def.Damage * 2.5) : Def.Damage;
        public int MagSize => Packed ? (int)Math.Ceiling(Def.MagSize * 1.5) : Def.MagSize;
        public int MaxReserve => Bow > BowVariant.Base ? Def.MaxReserve * 2 : Packed ? (int)(Def.MaxReserve * 1.5) : Def.MaxReserve;
        public bool CanPack => !Packed && Def.Kind != WeaponKind.Bow;

        public void Pack()
        {
            Packed = true;
            Mag = MagSize;
            Reserve = MaxReserve;
        }

        public void UpgradeBow(BowVariant v)
        {
            Bow = v;
            Mag = MagSize;
            Reserve = MaxReserve;
        }

        public void Refill()
        {
            Mag = MagSize;
            Reserve = MaxReserve;
        }
    }

    /// <summary>Approximate, hand-tuned stats in the spirit of the BO3 arsenal.</summary>
    public static class WeaponCatalog
    {
        public static readonly WeaponDef MR6 = new WeaponDef
        {
            Name = "MR6", PapName = "Mr. Sixty (PaP MR6)", Damage = 60, FireInterval = 0.15, MagSize = 12, MaxReserve = 80, ReloadTime = 1.4,
        };

        public static readonly WeaponDef Bow = new WeaponDef
        {
            Name = "Wrath of the Ancients", Kind = WeaponKind.Bow, Damage = 2000, FireInterval = 0.7, MagSize = 1, MaxReserve = 30,
            ReloadTime = 0.35, Range = 120,
        };

        public static readonly List<WeaponDef> All = new List<WeaponDef>
        {
            MR6,
            // Wall-buys
            new WeaponDef { Name = "RK5", PapName = "Reaper of Kings (PaP RK5)", Damage = 75, FireInterval = 0.18, MagSize = 12, MaxReserve = 72, ReloadTime = 1.5, WallCost = 500 },
            new WeaponDef { Name = "L-CAR 9", PapName = "L-CAR 9 Supreme", Damage = 60, FireInterval = 0.08, MagSize = 15, MaxReserve = 120, ReloadTime = 1.6, Auto = true, WallCost = 750 },
            new WeaponDef { Name = "Sheiva", PapName = "Shiva-Sharp (PaP Sheiva)", Damage = 110, FireInterval = 0.22, MagSize = 20, MaxReserve = 160, ReloadTime = 2.0, Pierce = 2, WallCost = 900 },
            new WeaponDef { Name = "Kuda", PapName = "Kuda-Kaze (PaP Kuda)", Damage = 70, FireInterval = 0.075, MagSize = 30, MaxReserve = 180, ReloadTime = 2.2, Auto = true, WallCost = 1100, InBox = true },
            new WeaponDef { Name = "KN-44", PapName = "KN-4404 (PaP KN-44)", Damage = 90, FireInterval = 0.1, MagSize = 30, MaxReserve = 180, ReloadTime = 2.4, Auto = true, WallCost = 1400, InBox = true },
            new WeaponDef { Name = "KRM-262", PapName = "KRM-Hate (PaP KRM-262)", Damage = 90, Pellets = 8, Spread = 0.12, Range = 40, FireInterval = 0.6, MagSize = 7, MaxReserve = 56, ReloadTime = 3.0, WallCost = 1500, InBox = true },
            new WeaponDef { Name = "HVK-30", PapName = "HVK-Kill (PaP HVK-30)", Damage = 85, FireInterval = 0.085, MagSize = 30, MaxReserve = 180, ReloadTime = 2.3, Auto = true, WallCost = 1500, InBox = true },
            new WeaponDef { Name = "VMP", PapName = "VMP-Prime (PaP VMP)", Damage = 70, FireInterval = 0.065, MagSize = 30, MaxReserve = 180, ReloadTime = 2.0, Auto = true, WallCost = 1500, InBox = true },
            new WeaponDef { Name = "ICR-1", PapName = "ICR-Reign (PaP ICR-1)", Damage = 95, FireInterval = 0.11, MagSize = 30, MaxReserve = 180, ReloadTime = 2.2, Auto = true, WallCost = 1600, InBox = true },
            // Box only
            new WeaponDef { Name = "Ray Gun", PapName = "Porter's X2 Ray Gun", Kind = WeaponKind.RayGun, Damage = 1000, FireInterval = 0.25, MagSize = 20, MaxReserve = 160, ReloadTime = 2.5, Spread = 0.0, InBox = true },
            new WeaponDef { Name = "Dingo", PapName = "Dingo Dynamite (PaP Dingo)", Damage = 95, FireInterval = 0.08, MagSize = 80, MaxReserve = 400, ReloadTime = 4.0, Auto = true, InBox = true },
            new WeaponDef { Name = "Gorgon", PapName = "Medusa (PaP Gorgon)", Damage = 600, FireInterval = 0.9, MagSize = 4, MaxReserve = 24, ReloadTime = 3.0, Pierce = 5, HeadshotChance = 0.5, Spread = 0.0, Range = 200, InBox = true },
            new WeaponDef { Name = "Locus", PapName = "Locust (PaP Locus)", Damage = 500, FireInterval = 1.0, MagSize = 5, MaxReserve = 30, ReloadTime = 3.0, Pierce = 5, HeadshotChance = 0.5, Spread = 0.0, Range = 200, InBox = true },
            new WeaponDef { Name = "Drakon", PapName = "Draconian (PaP Drakon)", Damage = 300, FireInterval = 0.25, MagSize = 10, MaxReserve = 80, ReloadTime = 2.6, Pierce = 3, HeadshotChance = 0.4, Spread = 0.005, Range = 200, InBox = true },
            new WeaponDef { Name = "Haymaker 12", PapName = "Hay-Hammer (PaP Haymaker)", Damage = 70, Pellets = 8, Spread = 0.1, Range = 45, FireInterval = 0.25, MagSize = 10, MaxReserve = 60, ReloadTime = 3.0, Auto = true, InBox = true },
            new WeaponDef { Name = "Brecci", PapName = "Breccinator (PaP Brecci)", Damage = 85, Pellets = 8, Spread = 0.1, Range = 45, FireInterval = 0.3, MagSize = 8, MaxReserve = 64, ReloadTime = 2.8, InBox = true },
            new WeaponDef { Name = "Argus", PapName = "Argus Eye (PaP Argus)", Damage = 160, Pellets = 6, Spread = 0.09, Range = 50, FireInterval = 0.7, MagSize = 6, MaxReserve = 54, ReloadTime = 3.2, InBox = true },
            new WeaponDef { Name = "Man-o-War", PapName = "Man-o-Warlord (PaP Man-o-War)", Damage = 130, FireInterval = 0.13, MagSize = 30, MaxReserve = 150, ReloadTime = 2.4, Auto = true, InBox = true },
            new WeaponDef { Name = "Weevil", PapName = "Beetle (PaP Weevil)", Damage = 75, FireInterval = 0.07, MagSize = 40, MaxReserve = 240, ReloadTime = 2.2, Auto = true, InBox = true },
            new WeaponDef { Name = "Pharo", PapName = "Pharaoh (PaP Pharo)", Damage = 85, FireInterval = 0.06, MagSize = 24, MaxReserve = 192, ReloadTime = 2.0, Auto = true, InBox = true },
            new WeaponDef { Name = "Vesper", PapName = "Vesper Nights (PaP Vesper)", Damage = 60, FireInterval = 0.045, MagSize = 36, MaxReserve = 216, ReloadTime = 2.0, Auto = true, InBox = true },
        };

        public static WeaponDef Get(string name) =>
            All.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown weapon '{name}'.");

        public static IReadOnlyList<WeaponDef> BoxPool => All.Where(w => w.InBox).ToList();
    }

    public enum Perk { QuickRevive, Juggernog, SpeedCola, DoubleTap, StaminUp, MuleKick, WidowsWine }

    public static class Perks
    {
        public const int MaxPerks = 4;

        public static int Cost(Perk p) => p switch
        {
            Perk.QuickRevive => 500,
            Perk.Juggernog => 2500,
            Perk.SpeedCola => 3000,
            Perk.DoubleTap => 2000,
            Perk.StaminUp => 2000,
            Perk.MuleKick => 4000,
            Perk.WidowsWine => 4000,
            _ => 2000,
        };

        public static string DisplayName(Perk p) => p switch
        {
            Perk.QuickRevive => "Quick Revive",
            Perk.Juggernog => "Juggernog",
            Perk.SpeedCola => "Speed Cola",
            Perk.DoubleTap => "Double Tap Root Beer",
            Perk.StaminUp => "Stamin-Up",
            Perk.MuleKick => "Mule Kick",
            Perk.WidowsWine => "Widow's Wine",
            _ => p.ToString(),
        };

        public static string Short(Perk p) => p switch
        {
            Perk.QuickRevive => "QR",
            Perk.Juggernog => "JUG",
            Perk.SpeedCola => "SPD",
            Perk.DoubleTap => "DT",
            Perk.StaminUp => "STM",
            Perk.MuleKick => "MULE",
            Perk.WidowsWine => "WW",
            _ => p.ToString(),
        };

        /// <summary>AutoCAD Color Index used for each machine.</summary>
        public static short Color(Perk p) => p switch
        {
            Perk.QuickRevive => 4,   // cyan
            Perk.Juggernog => 1,     // red
            Perk.SpeedCola => 3,     // green
            Perk.DoubleTap => 30,    // orange
            Perk.StaminUp => 2,      // yellow
            Perk.MuleKick => 94,     // dark green
            Perk.WidowsWine => 6,    // magenta
            _ => 7,
        };
    }
}
