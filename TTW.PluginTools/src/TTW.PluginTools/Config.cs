using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TTW.PluginTools
{
    /// <summary>
    /// Site settings from TTW.PluginTools.config.json, which sits next to the DLL.
    /// Comments and trailing commas are allowed so the CAD team can annotate it.
    /// </summary>
    public sealed class PluginConfig
    {
        public string DefaultToolsFolder { get; set; } = "";
        public bool AllowFolderChange { get; set; } = true;
        public string ArchivePrefix { get; set; } = "_";
        public ThemeConfig Theme { get; set; } = new ThemeConfig();

        public static string ConfigPath =>
            Path.Combine(Path.GetDirectoryName(typeof(PluginConfig).Assembly.Location) ?? "", "TTW.PluginTools.config.json");

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        /// <summary>Loads the config; a missing or broken file falls back to defaults (and says why).</summary>
        public static PluginConfig Load(out string problem)
        {
            problem = null;
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new PluginConfig();
            }
            catch (Exception ex)
            {
                problem = $"Couldn't read {Path.GetFileName(ConfigPath)} ({ex.Message}) - using defaults.";
            }
            return new PluginConfig();
        }
    }

    public sealed class ThemeConfig
    {
        public string Title { get; set; } = "Plugin Tools";
        public string Subtitle { get; set; } = "Civil 3D";
        public string BodyFont { get; set; } = "Arial Nova, Arial";
        public string HeadingFont { get; set; } = "Arial Nova, Arial";
        /// <summary>Colour overrides by brush name without the "B." (e.g. "Accent": "#024360").</summary>
        public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>TTW Brand Guidelines defaults (same as Advanced Tools).</summary>
        public static readonly (string Key, string Hex)[] Defaults =
        {
            ("HeaderBg", "#024360"), ("HeaderFg", "#F7F4E7"), ("HeaderSubtle", "#8ECAEF"),
            ("Accent", "#024360"), ("AccentHover", "#03587E"), ("AccentFg", "#F7F4E7"),
            ("Bg", "#F7F4E7"), ("Surface", "#FFFDF8"), ("Hover", "#EFEADB"), ("Selection", "#DDE8EC"),
            ("Border", "#DED7C4"), ("Text", "#1E1711"), ("Heading", "#024360"), ("Muted", "#6F6A5D"),
            ("Read", "#03587E"), ("Success", "#325442"), ("Failure", "#A84933"),
        };
    }

    /// <summary>Per-person choices, kept in %APPDATA%\TTW\PluginTools\settings.json.</summary>
    public sealed class UserSettings
    {
        public string ToolsFolder { get; set; }
        public bool ShowArchived { get; set; }
        /// <summary>Plugin DLL -> the .csproj it's built from, for "Build and reload". Keys are lower case.</summary>
        public Dictionary<string, string> SourceProjects { get; set; } = new Dictionary<string, string>();

        public string SourceProjectFor(string dll) =>
            SourceProjects != null && SourceProjects.TryGetValue(dll.ToLowerInvariant(), out var p) ? p : null;

        public void SetSourceProject(string dll, string project)
        {
            SourceProjects ??= new Dictionary<string, string>();
            SourceProjects[dll.ToLowerInvariant()] = project;
        }

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TTW", "PluginTools", "settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), PluginConfig.JsonOptions) ?? new UserSettings();
            }
            catch { /* a broken settings file just means defaults */ }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, PluginConfig.JsonOptions));
            }
            catch { /* not worth interrupting anyone over */ }
        }

        /// <summary>The folder actually in use: the person's choice if allowed, else the site default.</summary>
        public static string EffectiveFolder(PluginConfig config, UserSettings settings) =>
            config.AllowFolderChange && !string.IsNullOrWhiteSpace(settings.ToolsFolder)
                ? settings.ToolsFolder
                : config.DefaultToolsFolder;
    }
}
