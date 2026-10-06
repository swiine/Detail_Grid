using System.Collections.Generic;

namespace DerZombies.Core
{
    public enum FeatureKind
    {
        PlayerStart,
        Perk,
        WallBuy,
        BoxLocation,
        PowerSwitch,
        LandingPad,
        PackAPunch,
        Gondola,
        Dragon,
        BowPedestal,
        BowAltar,
    }

    /// <summary>A fixed point of interest on the map, placed on a grid cell.</summary>
    public sealed class FeatureDef
    {
        public FeatureKind Kind { get; }
        public int Col { get; }
        public int Row { get; }
        /// <summary>Perk / weapon / altar key, depending on <see cref="Kind"/>.</summary>
        public string Key { get; }

        public FeatureDef(FeatureKind kind, int col, int row, string key = "")
        {
            Kind = kind; Col = col; Row = row; Key = key;
        }
    }

    /// <summary>
    /// The castle layout. A fan-made, top-down take on the Der Eisendrache castle
    /// (Black Ops III): it keeps the area names and progression (power, landing pads,
    /// Pack-a-Punch, dragons, the Wrath of the Ancients bow and its four upgrades)
    /// but the geometry is simplified onto a 60 x 40 grid of 5-unit cells.
    ///
    /// Legend:
    ///   #        wall / obstacle
    ///   A-Z      floor; the letter is the area (see <see cref="AreaNames"/>)
    ///   0-9 a-z  a buyable door/debris; every cell with the same char is one door
    ///
    /// Row 0 is the north (top) edge of the map.
    /// </summary>
    public static class MapData
    {
        public const double CellSize = 5.0;

        public static readonly string[] Layout =
        {
            "############################################################",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTT##TTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR8MMMMMMMM###MMMMMMMM9TTTTTTTTT##TTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR8MMMMMMMM###MMMMMMMM9TTTTTTTTT##TTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMM###MMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "#RRRRRRRRRRRRRRRRRR#MMMMMMMMMMMMMMMMMMM#TTTTTTTTTTTTTTTTTTT#",
            "######ee#####################77####################bb#######",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBBBBBBBBBB#",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBBBBBBBBBB#",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBBBBBBBBBB#",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBB###BBBBB#",
            "#AAAA##AAAAAAA#CCCCCCCCCCCCC####CCCCCCCCCCCCC#BBBBB###BBBBB#",
            "#AAAA##AAAAAAA5CCCCCCCCCCCCC####CCCCCCCCCCCCCaBBBBB###BBBBB#",
            "#AAAAAAAAAAAAA5CCCCCCCCCCCCC####CCCCCCCCCCCCCaBBBBB###BBBBB#",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCC####CCCCCCCCCCCCC#BBBBB###BBBBB#",
            "#AAAAAAAA##AAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBB###BBBBB#",
            "#AAAAAAAA##AAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBBBBBBBBBB#",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBBBBBBBBBB#",
            "#AAAAAAAAAAAAA#CCCCCCCCCCCCCCCCCCCCCCCCCCCCCC#BBBBBBBBBBBBB#",
            "######66###########33#####################44########cc######",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUU###UUUUU#LLLLL##LLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUU###UUUUU#LLLLL##LLLLL1SSSSSSSSSSS2GGGGGGGG##GGGG##GGG#",
            "#UUUUU###UUUUU#LLLLLLLLLLLL1SSSSSSSSSSS2GGGGGGGG##GGGG##GGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSS##SSSS#GGGGGGGG##GGGG##GGG#",
            "#UUUUUUUUUUUUUdLLLLLLLLLLLL#SSSSS##SSSS#GGGGGGGG##GGGG##GGG#",
            "#UUUUUUUUUUUUUdLLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "#UUUUUUUUUUUUU#LLLLLLLLLLLL#SSSSSSSSSSS#GGGGGGGGGGGGGGGGGGG#",
            "############################################################",
        };

        public const char StartArea = 'S';

        public static readonly Dictionary<char, string> AreaNames = new Dictionary<char, string>
        {
            ['S'] = "Spawn",
            ['L'] = "Lower Courtyard",
            ['G'] = "Gatehouse",
            ['C'] = "Upper Courtyard",
            ['A'] = "Armory",
            ['B'] = "Bridge",
            ['U'] = "Undercroft",
            ['M'] = "Mission Control",
            ['R'] = "Rocket Test Site",
            ['T'] = "Clock Tower",
        };

        public static readonly Dictionary<char, int> DoorCosts = new Dictionary<char, int>
        {
            ['1'] = 750,  // Spawn <-> Lower Courtyard
            ['2'] = 750,  // Spawn <-> Gatehouse
            ['3'] = 1000, // Lower Courtyard <-> Upper Courtyard
            ['4'] = 1000, // Gatehouse <-> Upper Courtyard
            ['5'] = 1250, // Upper Courtyard <-> Armory
            ['6'] = 1250, // Armory <-> Undercroft
            ['7'] = 1250, // Upper Courtyard <-> Mission Control
            ['8'] = 1500, // Mission Control <-> Rocket Test Site
            ['9'] = 1500, // Mission Control <-> Clock Tower
            ['a'] = 1000, // Upper Courtyard <-> Bridge
            ['b'] = 1250, // Bridge <-> Clock Tower
            ['c'] = 1000, // Gatehouse <-> Bridge
            ['d'] = 1500, // Lower Courtyard <-> Undercroft
            ['e'] = 1250, // Rocket Test Site <-> Armory
        };

        public static readonly FeatureDef[] Features =
        {
            new FeatureDef(FeatureKind.PlayerStart, 33, 36),

            new FeatureDef(FeatureKind.Perk, 37, 37, nameof(Perk.QuickRevive)),
            new FeatureDef(FeatureKind.Perk, 43, 14, nameof(Perk.Juggernog)),
            new FeatureDef(FeatureKind.Perk, 16, 37, nameof(Perk.SpeedCola)),
            new FeatureDef(FeatureKind.Perk, 57, 27, nameof(Perk.DoubleTap)),
            new FeatureDef(FeatureKind.Perk, 2, 2, nameof(Perk.StaminUp)),
            new FeatureDef(FeatureKind.Perk, 57, 2, nameof(Perk.MuleKick)),
            new FeatureDef(FeatureKind.Perk, 2, 23, nameof(Perk.WidowsWine)),

            new FeatureDef(FeatureKind.WallBuy, 29, 27, "RK5"),
            new FeatureDef(FeatureKind.WallBuy, 37, 27, "Sheiva"),
            new FeatureDef(FeatureKind.WallBuy, 25, 27, "KRM-262"),
            new FeatureDef(FeatureKind.WallBuy, 41, 27, "Kuda"),
            new FeatureDef(FeatureKind.WallBuy, 16, 23, "HVK-30"),
            new FeatureDef(FeatureKind.WallBuy, 40, 14, "L-CAR 9"),
            new FeatureDef(FeatureKind.WallBuy, 12, 14, "KN-44"),
            new FeatureDef(FeatureKind.WallBuy, 21, 2, "VMP"),
            new FeatureDef(FeatureKind.WallBuy, 57, 23, "ICR-1"),

            // The first box location listed is where the box starts.
            new FeatureDef(FeatureKind.BoxLocation, 36, 14),
            new FeatureDef(FeatureKind.BoxLocation, 50, 37),
            new FeatureDef(FeatureKind.BoxLocation, 37, 10),
            new FeatureDef(FeatureKind.BoxLocation, 7, 15),
            new FeatureDef(FeatureKind.BoxLocation, 12, 10),

            new FeatureDef(FeatureKind.PowerSwitch, 24, 2),

            new FeatureDef(FeatureKind.LandingPad, 13, 3, "Rocket Test Site"),
            new FeatureDef(FeatureKind.LandingPad, 46, 9, "Clock Tower"),
            new FeatureDef(FeatureKind.LandingPad, 45, 30, "Gatehouse"),

            new FeatureDef(FeatureKind.PackAPunch, 4, 28),

            new FeatureDef(FeatureKind.Gondola, 24, 36, "Lower Courtyard"),
            new FeatureDef(FeatureKind.Gondola, 16, 10, "Rocket Test Site"),

            new FeatureDef(FeatureKind.Dragon, 22, 15, "Courtyard Dragon"),
            new FeatureDef(FeatureKind.Dragon, 4, 8, "Rocket Dragon"),
            new FeatureDef(FeatureKind.Dragon, 56, 37, "Gatehouse Dragon"),

            new FeatureDef(FeatureKind.BowPedestal, 44, 3),

            new FeatureDef(FeatureKind.BowAltar, 55, 9, nameof(BowVariant.Storm)),
            new FeatureDef(FeatureKind.BowAltar, 11, 37, nameof(BowVariant.Fire)),
            new FeatureDef(FeatureKind.BowAltar, 11, 24, nameof(BowVariant.Void)),
            new FeatureDef(FeatureKind.BowAltar, 49, 23, nameof(BowVariant.Wolf)),
        };
    }
}
