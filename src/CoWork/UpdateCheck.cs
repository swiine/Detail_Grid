using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoWork.Core;
using CoWork.Loader;

namespace CoWork
{
    /// <summary>
    /// Updates while CoWork is running. The loader only looks for a new version at the moment Civil 3D starts - if the
    /// network drive isn't ready yet, it misses it. So CoWork also checks a minute after starting and every 30 minutes:
    /// a newer signed release is copied to this PC and checked (exactly as the loader would), and you're told to
    /// restart Civil 3D. The next start then uses it without needing the network at all.
    /// </summary>
    internal static class UpdateCheck
    {
        private static Timer? _timer;
        private static string? _announced;
        private static System.Windows.Threading.Dispatcher? _ui;

        public static void Start()
        {
            _ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _timer?.Dispose();
            _timer = new Timer(_ => Run(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        }

        /// <summary>The release folder from cowork.json next to CoWork (null if updates aren't set up).</summary>
        private static string? ReleaseFolder()
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CiSettings.DefaultsFile));
                return doc.RootElement.TryGetProperty("updates", out JsonElement u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } s ? s : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>The version in the release folder's latest.txt (null if not reachable).</summary>
        public static string? Published()
        {
            try { return ReleaseFolder() is { } r && File.Exists(Path.Combine(r, Updater.LatestFile)) ? File.ReadAllText(Path.Combine(r, Updater.LatestFile)).Trim() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>Checks now (any thread). Returns the version that's ready, if newer than the one running.</summary>
        public static string? Run()
        {
            try
            {
                string? release = ReleaseFolder();
                if (release == null || !Directory.Exists(release)) return null;
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork", "app");
                Updater.Choice? c = Updater.Choose(release, cache, Path.Combine(cache, "_none"));
                if (c == null || !Version.TryParse(c.Version, out Version? ready) || !Version.TryParse(Plugin.Version, out Version? running) || ready <= running) return null;
                if (_announced == c.Version) return c.Version;
                _announced = c.Version;
                Log.Info($"Update {c.Version} copied to this PC and checked ({c.Note})");
                _ui?.BeginInvoke(new Action(() =>
                {
                    Commands.Write($"\n[CoWork] CoWork {c.Version} is ready - restart Civil 3D to start using it.");
                    CiUi.Announce($"CoWork {c.Version} is ready", "Restart Civil 3D when it suits you to start using it.", "OK", () => { });
                }));
                return c.Version;
            }
            catch (Exception ex) { Log.Swallowed(ex); return null; }
        }
    }
}
