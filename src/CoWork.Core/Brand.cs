namespace CoWork.Core;

/// <summary>A colour scheme for the CoWork strip (all "#RRGGBB"). Accent2 is the logo's second bracket.</summary>
public sealed record Palette(string Id, string Name, string Back, string Card, string Text, string Dim, string Accent, string AccentText,
    string Rail, string Mine, string Edge, string Field, string Good, string Warn, string Accent2);

/// <summary>CoWork's own look: six colour schemes, Civil 3D's greys and blue, and six logos.</summary>
public static class Brand
{
    public static readonly Palette[] Palettes =
    {
        new("mint",    "Slate & Mint",      "#1E2A30", "#26363D", "#EAF2EF", "#93A8A3", "#3DDC97", "#0D2A1F", "#172126", "#21463C", "#3A4F57", "#141D21", "#5CC98A", "#F07F72", "#4A9CFF"),
        new("amber",   "Midnight & Amber",  "#1B2030", "#252B3D", "#F1EFE8", "#9EA3B5", "#F2A541", "#2A1C05", "#141826", "#3A3222", "#3A4258", "#121622", "#5CC98A", "#F07F72", "#5EC8FF"),
        new("coral",   "Graphite & Coral",  "#232427", "#2E3034", "#F2F2F0", "#A3A5AA", "#FF6B5B", "#FFFFFF", "#1A1B1D", "#4A2E2B", "#45484E", "#18191B", "#5CC98A", "#F5B946", "#36C2C2"),
        new("sand",    "Deep Teal & Sand",  "#10353A", "#164349", "#F4EEDF", "#9DB9B6", "#E9C46A", "#10353A", "#0B2A2E", "#1F5157", "#2C6168", "#0A2629", "#7FD1A6", "#F08C7E", "#E76F51"),
        new("violet",  "Ink & Violet",      "#1D1B2B", "#28253A", "#EEEAF7", "#A49FBC", "#9B8CFF", "#15122A", "#161422", "#352F55", "#3E3A57", "#141220", "#5CC98A", "#F07F72", "#FF7AC6"),
        new("survey",  "Survey Orange",     "#24282B", "#2F3438", "#F3F1EC", "#A6ABAE", "#FF8C1A", "#1E1300", "#1B1E20", "#4A3520", "#474D52", "#1A1D1F", "#5CC98A", "#F07F72", "#4A9CFF"),
        new("civil3d", "Civil 3D",          "#3B4453", "#454F61", "#F2F4F7", "#A7B1BF", "#0696D7", "#FFFFFF", "#2E3440", "#2D4F6E", "#5B6678", "#2B313C", "#5FBF7F", "#E0675A", "#F2A541"),
    };

    /// <summary>The logo: "Interlock", two brackets slotting together, coloured by the scheme. Kept as a list so more
    /// can be added later.</summary>
    public static readonly (string Id, string Name)[] Logos = { ("interlock", "Interlock") };

    /// <summary>
    /// The 16 x 16 pixel-art Interlock for the status bar. a = Accent, A = Accent shadow, c = Accent2,
    /// C = Accent2 shadow, b = tile (Rail), . = transparent.
    /// </summary>
    public static readonly string[] InterlockPixels =
    {
        "................",
        ".bbbbbbbbbbbbbb.",
        "bbbbbbbbbbbbbbbb",
        "bbbaaaaaabbbbbbb",
        "bbbaaaaaaAbbbbbb",
        "bbbaaAAAAAbbbbbb",
        "bbbaaAbbbbbbbbbb",
        "bbbaaAbbbbbccbbb",
        "bbbaaAbbbbbccCbb",
        "bbbbAAbbbbbccCbb",
        "bbbbbbbbbbbccCbb",
        "bbbbbbbccccccCbb",
        "bbbbbbbccccccCbb",
        "bbbbbbbbCCCCCCbb",
        ".bbbbbbbbbbbbbb.",
        "................",
    };


    /// <summary>A darker shade of a colour (for the logo's 3D shadow).</summary>
    public static string Shade(string hex, double factor = 0.6)
    {
        int rgb = Convert.ToInt32(hex.TrimStart('#'), 16);
        int r = (int)((rgb >> 16 & 255) * factor), g = (int)((rgb >> 8 & 255) * factor), b = (int)((rgb & 255) * factor);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>The colour of each pixel letter in <see cref="InterlockPixels"/> for a scheme (null = transparent).</summary>
    public static string? PixelColour(char c, Palette p) => c switch
    {
        'a' => p.Accent, 'A' => Shade(p.Accent), 'c' => p.Accent2, 'C' => Shade(p.Accent2), 'b' => p.Rail, _ => null,
    };

    public const string DefaultPalette = "mint", DefaultLogo = "interlock";

    public static Palette PaletteFor(string? id) =>
        Palettes.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];

    public static string LogoFor(string? id) =>
        Logos.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)).Id ?? DefaultLogo;
}
