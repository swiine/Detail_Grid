using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core;

/// <summary>Saved build-ups plus the last-used drawing settings, kept as JSON.</summary>
public sealed class PresetFile
{
    public List<Buildup> Presets { get; set; } = new();
    public DetailSettings Settings { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
}

public sealed class PresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Path { get; }

    public PresetStore(string path) => Path = path;

    /// <summary>%APPDATA%\PavementBuildup\presets.json</summary>
    public static PresetStore Default() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "presets.json"));

    /// <summary>Loads the file, seeding it with example build-ups the first time.</summary>
    public PresetFile Load()
    {
        if (!File.Exists(Path))
            return new PresetFile { Presets = Examples() };

        try
        {
            var file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(Path), Options) ?? new PresetFile();
            file.Settings ??= new DetailSettings();
            file.Ai ??= new AiSettings();
            file.Presets ??= new List<Buildup>();
            return file;
        }
        catch (JsonException)
        {
            // Keep the broken file for the user, start fresh.
            File.Copy(Path, Path + ".bak", overwrite: true);
            return new PresetFile { Presets = Examples() };
        }
    }

    public void Save(PresetFile file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, Options));
        File.Move(tmp, Path, overwrite: true);
    }

    /// <summary>Adds or replaces (by name, case-insensitive) a preset.</summary>
    public static void Upsert(PresetFile file, Buildup buildup)
    {
        int i = file.Presets.FindIndex(p => string.Equals(p.Name, buildup.Name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
            file.Presets[i] = buildup.Clone();
        else
            file.Presets.Add(buildup.Clone());
    }

    public static Buildup? Find(PresetFile file, string name) =>
        file.Presets.FirstOrDefault(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static List<Buildup> Examples() => new()
    {
        Make("CARRIAGEWAY - FLEXIBLE",
            "40 SMA 10 surface course; 60 AC 20 dense bin 40/60 binder course; 150 AC 32 dense base 40/60 base course; 225 Type 1 sub-base; 0 Geotextile separator"),
        Make("FOOTWAY - ASPHALT",
            "25 AC 6 dense surf 100/150 surface course; 50 AC 20 dense bin 100/150 binder course; 150 Type 1 sub-base"),
        Make("BLOCK PAVING - LIGHT TRAFFIC",
            "80 Concrete block paving; 40 Bedding sand laying course; 150 Type 1 sub-base"),
        Make("RIGID - CONCRETE",
            "250 PQC C32/40 pavement quality concrete; 0 Polythene slip membrane; 150 CBGM C8/10 sub-base; 150 Type 1 sub-base"),
        Make("RIGID - CRCP REINFORCED",
            "250 CRCP C32/40 continuously reinforced concrete [top H16@150 c90 + H12@600]; 0 Polythene slip membrane; 150 CBGM C8/10 sub-base; 150 Type 1 sub-base"),
    };

    private static Buildup Make(string name, string layers) => new() { Name = name, Layers = BuildupParser.Parse(layers) };
}
