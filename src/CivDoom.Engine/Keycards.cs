namespace CivDoom.Engine;

/// <summary>Keycards and the doors they open.</summary>
public enum KeyColor
{
    None,
    Red,
    Blue,
    Yellow,
}

public static class Keycards
{
    /// <summary>All keycard colours, in the order levels hand them out.</summary>
    public static readonly KeyColor[] All = { KeyColor.Red, KeyColor.Blue, KeyColor.Yellow };

    public static int Rgb(KeyColor key) => key switch
    {
        KeyColor.Red => 0xE03030,
        KeyColor.Blue => 0x3A78F0,
        KeyColor.Yellow => 0xF0C820,
        _ => 0xB0B0B0,
    };

    public static string Name(KeyColor key) => key.ToString().ToUpperInvariant();

    /// <summary>Parses "red" / "Blue" / "YELLOW"; None if it isn't a key colour.</summary>
    public static KeyColor Parse(string? text) =>
        Enum.TryParse(text?.Trim(), ignoreCase: true, out KeyColor k) && k != KeyColor.None ? k : KeyColor.None;
}
