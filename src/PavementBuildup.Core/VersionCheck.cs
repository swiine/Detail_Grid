using System.Text.Json;

namespace PavementBuildup.Core;

/// <summary>The released version on the network: _support\version.json in the release folder.</summary>
public sealed record ReleaseInfo(string Version, string Date, string Notes, string Folder);

/// <summary>
/// Tells people when a newer version is on the network, so everyone stays on the same one.
/// Each copy's _support\update.json says where the released tool lives (e.g. \\server\CAD\TTW_Pave_Tool);
/// that folder's _support\version.json says which version it is.
/// </summary>
public static class VersionCheck
{
    public const string UpdateFile = "update.json";
    public const string VersionFile = "version.json";

    private sealed class UpdateSettings { public string ReleaseFolder { get; set; } = ""; }
    private sealed class VersionInfo { public string Version { get; set; } = ""; public string Date { get; set; } = ""; public string Notes { get; set; } = ""; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The release folder named in the nearest _support\update.json (searched from <paramref name="startFolders"/> and 4 folders up), or null.</summary>
    public static string? ReleaseFolder(IEnumerable<string> startFolders)
    {
        foreach (var start in startFolders)
        {
            try
            {
                var dir = new DirectoryInfo(start);
                for (int up = 0; up <= 4 && dir is not null; up++, dir = dir.Parent)
                {
                    var file = Path.Combine(dir.FullName, "_support", UpdateFile);
                    if (!File.Exists(file)) continue;
                    var folder = JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(file), Options)?.ReleaseFolder?.Trim();
                    return string.IsNullOrEmpty(folder) ? null : folder;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or System.Security.SecurityException) { }
        }
        return null;
    }

    /// <summary>The version in the release folder (its _support\version.json, or version.json at the top), or null if it can't be read.</summary>
    public static ReleaseInfo? Latest(string releaseFolder)
    {
        foreach (var file in new[] { Path.Combine(releaseFolder, "_support", VersionFile), Path.Combine(releaseFolder, VersionFile) })
        {
            try
            {
                if (!File.Exists(file)) continue;
                var v = JsonSerializer.Deserialize<VersionInfo>(File.ReadAllText(file), Options);
                if (v is null || v.Version.Trim().Length == 0) continue;
                return new ReleaseInfo(v.Version.Trim(), v.Date.Trim(), v.Notes.Trim(), releaseFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException) { }
        }
        return null;
    }

    /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/> ("1.2.0" &gt; "1.1.3").</summary>
    public static bool IsNewer(string latest, string current) =>
        Version.TryParse(Normalise(latest), out var l) && Version.TryParse(Normalise(current), out var c) && l > c;

    private static string Normalise(string v)
    {
        var s = v.Trim().TrimStart('v', 'V');
        int plus = s.IndexOfAny(new[] { '+', '-', ' ' });
        if (plus > 0) s = s[..plus];
        return s.Contains('.') ? s : s + ".0";
    }

    /// <summary>The newer release, or null when up to date / no release folder / the network isn't there (gives up after <paramref name="timeout"/>).</summary>
    public static async Task<ReleaseInfo?> CheckAsync(IEnumerable<string> startFolders, string currentVersion, TimeSpan timeout)
    {
        var work = Task.Run(() =>
        {
            var folder = ReleaseFolder(startFolders);
            if (folder is null) return null;
            var latest = Latest(folder);
            return latest is not null && IsNewer(latest.Version, currentVersion) ? latest : null;
        });
        var done = await Task.WhenAny(work, Task.Delay(timeout)).ConfigureAwait(false);
        return done == work ? await work.ConfigureAwait(false) : null;
    }
}
