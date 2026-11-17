using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoWork.Loader
{
    /// <summary>
    /// Automatic updates. Releases live in a network folder:
    /// <code>
    ///   CoWork-release\latest.txt                 the version everyone should run, e.g. "1.3.0"
    ///   CoWork-release\1.3.0\CoWork.dll ...        that version's files
    ///   CoWork-release\1.3.0\manifest.json + .sig  every file's SHA-256, signed with the issuer's private key
    ///   CoWork-release\test.txt                   optional: the version the test group gets first
    ///   CoWork-release\testers.txt                the test group's Windows logins, one per line
    ///   CoWork-release\previous.txt               what latest.txt said before, for a one-step roll back
    /// </code>
    /// Each PC runs exactly the version the release folder names for it (the test version for testers, latest for
    /// everyone else), so rolling back or taking someone out of the test group takes them back too.
    /// Each time Civil 3D starts, the loader copies a newer release to this PC (%LOCALAPPDATA%\CoWork\app\&lt;version&gt;),
    /// checks the signature and every file, and only then runs it. Nothing unsigned ever runs; if the network is down,
    /// the newest copy already on the PC (or the one that came with the installer) is used.
    /// Kept free of AutoCAD and of CoWork.Core so it can be tested on its own.
    /// </summary>
    public static class Updater
    {
        /// <summary>Same public key as CoWork.Core.Licensing.PublicKey.</summary>
        public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+YvabTQjpe/7MMuOQcmUmVnLwOawsaqoKBr1Z5CIkJ0oZPgxJV5Eld9ynmAIdNU07XOOKPD0KKj+OYSZNEO38A==";

        public const string ManifestFile = "manifest.json", SignatureFile = "manifest.sig", LatestFile = "latest.txt";
        public const string TestFile = "test.txt", TestersFile = "testers.txt", PreviousFile = "previous.txt", WantedFile = "wanted.txt";

        public sealed record FileHash(string Name, string Sha256);
        public sealed record Manifest(string Version, DateTimeOffset Published, List<FileHash> Files);
        public sealed record Choice(string Folder, string Version, string Note, Manifest Manifest);

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private static string Hash(string file)
        {
            using var s = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(s));
        }

        /// <summary>Writes manifest.json + manifest.sig for the files in a folder (sign = the private key's signer).</summary>
        public static Manifest Sign(string dir, string version, Func<byte[], byte[]> sign)
        {
            var files = Directory.GetFiles(dir)
                .Where(f => Path.GetFileName(f) is not (ManifestFile or SignatureFile))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FileHash(Path.GetFileName(f), Hash(f))).ToList();
            var m = new Manifest(version, DateTimeOffset.UtcNow, files);
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(m, Json);
            File.WriteAllBytes(Path.Combine(dir, ManifestFile), body);
            File.WriteAllText(Path.Combine(dir, SignatureFile), Convert.ToBase64String(sign(body)));
            return m;
        }

        /// <summary>Copies a build into the release folder, signs it, and makes it the version everyone gets.</summary>
        public static Manifest Publish(string sourceDir, string releaseRoot, string version, Func<byte[], byte[]> sign, bool testGroupOnly = false)
        {
            if (!System.Version.TryParse(version, out _)) throw new ArgumentException("Version must look like 1.3.0");
            string target = Path.Combine(releaseRoot, version);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            foreach (string f in Directory.GetFiles(sourceDir))
                if (Path.GetFileName(f) is not (ManifestFile or SignatureFile)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)));
            Manifest m = Sign(target, version, sign);
            if (testGroupOnly) File.WriteAllText(Path.Combine(releaseRoot, TestFile), version);
            else SetLatest(releaseRoot, version);
            return m;
        }

        private static string? ReadVersion(string file)
        {
            try { return File.Exists(file) && System.Version.TryParse(File.ReadAllText(file).Trim(), out System.Version? v) ? v.ToString() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>The version everyone runs, the test group's version (null if no test is running) and the one before latest.</summary>
        public static (string? Latest, string? Test, string? Previous) Channels(string releaseRoot) =>
            (ReadVersion(Path.Combine(releaseRoot, LatestFile)), ReadVersion(Path.Combine(releaseRoot, TestFile)), ReadVersion(Path.Combine(releaseRoot, PreviousFile)));

        /// <summary>The test group's logins.</summary>
        public static List<string> Testers(string releaseRoot)
        {
            try
            {
                string f = Path.Combine(releaseRoot, TestersFile);
                return !File.Exists(f) ? new List<string>() : File.ReadAllLines(f).SelectMany(l => l.Split('#')[0].Split(',', ';'))
                    .Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<string>(); }
        }

        public static void SetTesters(string releaseRoot, IEnumerable<string> logins) =>
            File.WriteAllLines(Path.Combine(releaseRoot, TestersFile), new[] { "# CoWork test group: Windows logins, one per line" }
                .Concat(logins.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)));

        /// <summary>
        /// The version this login should run: the test version if they're in the test group and it's newer than
        /// latest, otherwise latest. Null if the release folder has none (or can't be read).
        /// </summary>
        public static string? Wanted(string releaseRoot, string login)
        {
            var (latest, test, _) = Channels(releaseRoot);
            if (test != null && (latest == null || System.Version.Parse(test) > System.Version.Parse(latest))
                && Testers(releaseRoot).Contains(login, StringComparer.OrdinalIgnoreCase))
                return test;
            return latest;
        }

        /// <summary>Makes a version the one everyone runs, remembering the one before for <see cref="RollBack"/>.</summary>
        public static void SetLatest(string releaseRoot, string version)
        {
            var (latest, test, _) = Channels(releaseRoot);
            if (latest != null && latest != version) File.WriteAllText(Path.Combine(releaseRoot, PreviousFile), latest);
            File.WriteAllText(Path.Combine(releaseRoot, LatestFile), version);
            // A test version that's now no newer than everyone's is finished.
            if (test != null && System.Version.Parse(test) <= System.Version.Parse(version)) File.Delete(Path.Combine(releaseRoot, TestFile));
        }

        /// <summary>Gives the test group's version to everyone. Returns it (null if no test is running).</summary>
        public static string? Promote(string releaseRoot)
        {
            string? test = Channels(releaseRoot).Test;
            if (test == null) return null;
            SetLatest(releaseRoot, test);
            return test;
        }

        /// <summary>Stops the test: the test group goes back to everyone's version. Returns the test version stopped.</summary>
        public static string? EndTest(string releaseRoot)
        {
            string? test = Channels(releaseRoot).Test;
            if (test != null) File.Delete(Path.Combine(releaseRoot, TestFile));
            return test;
        }

        /// <summary>
        /// Everyone back to the version before latest (its folder must still be there). Returns (from, to), or null
        /// if there's nothing to go back to. Each PC goes back the next time it checks.
        /// </summary>
        public static (string From, string To)? RollBack(string releaseRoot)
        {
            var (latest, _, previous) = Channels(releaseRoot);
            if (latest == null || previous == null || previous == latest || !Directory.Exists(Path.Combine(releaseRoot, previous))) return null;
            File.WriteAllText(Path.Combine(releaseRoot, LatestFile), previous);
            File.Delete(Path.Combine(releaseRoot, PreviousFile));
            return (latest, previous);
        }

        /// <summary>The folder's manifest if it's signed by the issuer and every listed file matches (else null).</summary>
        public static Manifest? Verify(string dir, string publicKey = PublicKey)
        {
            try
            {
                string mf = Path.Combine(dir, ManifestFile), sf = Path.Combine(dir, SignatureFile);
                if (!File.Exists(mf) || !File.Exists(sf)) return null;
                byte[] body = File.ReadAllBytes(mf);
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
                if (!ec.VerifyData(body, Convert.FromBase64String(File.ReadAllText(sf).Trim()), HashAlgorithmName.SHA256)) return null;
                Manifest? m = JsonSerializer.Deserialize<Manifest>(body, Json);
                if (m == null || m.Files.Count == 0 || !System.Version.TryParse(m.Version, out _)) return null;
                foreach (FileHash f in m.Files)
                {
                    if (f.Name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || f.Name.Contains("..")) return null;
                    string path = Path.Combine(dir, f.Name);
                    if (!File.Exists(path) || !string.Equals(Hash(path), f.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
                }
                return m;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException or JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Picks what to run: the version the release folder names for this login (copied to this PC and checked),
        /// else - with the release folder out of reach - the one it named last time, else the newest checked copy here,
        /// else the copy that came with the installer. A copy newer than the one named (rolled back, or out of the test
        /// group) is withdrawn so it isn't picked again. Old copies are tidied away.
        /// </summary>
        public static Choice? Choose(string? releaseRoot, string cacheRoot, string bundledApp, string publicKey = PublicKey, string? login = null)
        {
            login ??= Environment.UserName;
            var found = new List<(System.Version V, string Dir, Manifest M)>();
            void Consider(string dir)
            {
                if (Verify(dir, publicKey) is { } m) found.Add((System.Version.Parse(m.Version), dir, m));
            }
            Consider(bundledApp);
            if (Directory.Exists(cacheRoot))
                foreach (string d in Directory.GetDirectories(cacheRoot))
                    if (!Path.GetFileName(d).Contains(".tmp")) Consider(d);

            string note = "";
            string wantedFile = Path.Combine(cacheRoot, WantedFile);
            System.Version? wanted = null;
            bool fromRelease = false;
            try
            {
                if (releaseRoot != null && Directory.Exists(releaseRoot) && Wanted(releaseRoot, login) is { } w)
                {
                    wanted = System.Version.Parse(w);
                    fromRelease = true;
                    if (!found.Any(f => f.V == wanted))
                    {
                        // Copy first, then check the copy: what runs is exactly what was checked.
                        string src = Path.Combine(releaseRoot, wanted.ToString());
                        string tmp = Path.Combine(cacheRoot, wanted + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.CreateDirectory(tmp);
                        foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
                        if (Verify(tmp, publicKey) is { } m && m.Version == wanted.ToString())
                        {
                            string final = Path.Combine(cacheRoot, wanted.ToString());
                            if (Directory.Exists(final)) Directory.Delete(final, true);
                            Directory.Move(tmp, final);
                            found.Add((wanted, final, m));
                            note = $"updated to {wanted}";
                        }
                        else
                        {
                            Directory.Delete(tmp, true);
                            note = $"release {wanted} isn't signed correctly - ignored";
                            wanted = null;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                note = "release folder not reachable - using the copy on this PC";
                wanted = null;
                fromRelease = false;
            }
            if (!fromRelease)
                try { if (File.Exists(wantedFile) && System.Version.TryParse(File.ReadAllText(wantedFile).Trim(), out System.Version? last)) wanted = last; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            if (found.Count == 0) return null;
            var chosen = wanted != null && found.Any(f => f.V == wanted) ? found.First(f => f.V == wanted) : found.OrderByDescending(f => f.V).First();
            // A newer copy installed by hand (from a zip) runs until it's published - installing it is asking for it.
            string bundledFull = Path.GetFullPath(bundledApp);
            if (found.FirstOrDefault(f => string.Equals(Path.GetFullPath(f.Dir), bundledFull, StringComparison.OrdinalIgnoreCase)) is { Dir: not null } b && b.V > chosen.V)
                chosen = b;
            var withdrawn = new List<string>();
            if (fromRelease && wanted != null && found.Any(f => f.V == wanted))
            {
                try
                {
                    Directory.CreateDirectory(cacheRoot);
                    File.WriteAllText(wantedFile, wanted.ToString());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                // Newer copies on this PC were rolled back (or the test ended for this login): withdraw them. Removing the
                // signature is enough - no loader, old or new, runs an unsigned copy - and works even while one is in use.
                foreach (var f in found.Where(f => f.V > wanted && IsUnder(f.Dir, cacheRoot)).ToList())
                {
                    try { File.Delete(Path.Combine(f.Dir, SignatureFile)); found.Remove(f); withdrawn.Add(f.Dir); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    note = $"{f.V} withdrawn - back to {wanted}";
                }
            }
            // A withdrawn copy may be the one running right now: it's tidied away next time, once it isn't.
            Tidy(cacheRoot, keep: found.OrderByDescending(f => f.V == chosen.V).ThenByDescending(f => f.V).Take(2).Select(f => f.Dir).Concat(withdrawn));
            return new Choice(chosen.Dir, chosen.M.Version, note, chosen.M);
        }

        private static bool IsUnder(string dir, string root) =>
            Path.GetFullPath(dir).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Server mode: asks the CoWork server for the latest version and, if this PC doesn't have it yet, downloads that
        /// release (manifest, signature and files) into <paramref name="downloadRoot"/> laid out like a release folder.
        /// Returns that folder to pass to <see cref="Choose"/> - which checks the signature as always, so a server can't
        /// slip in anything unsigned. Null if the server can't be reached or has nothing (the shared folder is used then).
        /// </summary>
        public static string? FetchFromServer(string address, string downloadRoot, string cacheRoot, TimeSpan? timeout = null)
        {
            try
            {
                string host = address.Trim();
                int port = 5391;
                int colon = host.LastIndexOf(':');
                if (colon > 0 && int.TryParse(host.Substring(colon + 1), out int p)) { port = p; host = host.Substring(0, colon); }
                using var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = (int)(timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds };
                if (!tcp.ConnectAsync(host, port).Wait(timeout ?? TimeSpan.FromSeconds(3))) return null;
                var stream = tcp.GetStream();
                var w = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
                var r = new StreamReader(stream, new UTF8Encoding(false));
                JsonObject? Ask(JsonObject q)
                {
                    w.WriteLine(q.ToJsonString());
                    while (true)
                    {
                        string? line = r.ReadLine();
                        if (line == null) return null;
                        if (JsonNode.Parse(line) is JsonObject a && a["t"]?.GetValue<string>() is "latest" or "file" or "error") return a;
                    }
                }
                string? version = Ask(new JsonObject { ["t"] = "latest", ["user"] = Environment.UserName })?["version"]?.GetValue<string>();
                if (string.IsNullOrEmpty(version) || !System.Version.TryParse(version, out System.Version? latest)) return null;
                if (Verify(Path.Combine(cacheRoot, latest.ToString())) != null) return null; // already here

                string dir = Path.Combine(downloadRoot, latest.ToString());
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                byte[]? Get(string name)
                {
                    JsonObject? a = Ask(new JsonObject { ["t"] = "file", ["version"] = latest.ToString(), ["name"] = name });
                    return a?["data"]?.GetValue<string>() is { } b64 ? Convert.FromBase64String(b64) : null;
                }
                byte[]? manifest = Get(ManifestFile), sig = Get(SignatureFile);
                if (manifest == null || sig == null) return null;
                Manifest? m = JsonSerializer.Deserialize<Manifest>(manifest, Json);
                if (m == null) return null;
                File.WriteAllBytes(Path.Combine(dir, ManifestFile), manifest);
                File.WriteAllBytes(Path.Combine(dir, SignatureFile), sig);
                foreach (FileHash f in m.Files)
                {
                    if (f.Name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || f.Name.Contains("..")) return null;
                    byte[]? data = Get(f.Name);
                    if (data == null) return null;
                    File.WriteAllBytes(Path.Combine(dir, f.Name), data);
                }
                File.WriteAllText(Path.Combine(downloadRoot, LatestFile), latest.ToString());
                return downloadRoot;
            }
            catch (Exception ex) when (ex is IOException or SocketException or AggregateException or UnauthorizedAccessException
                                       or FormatException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        private static void Tidy(string cacheRoot, IEnumerable<string> keep)
        {
            if (!Directory.Exists(cacheRoot)) return;
            var keepSet = new HashSet<string>(keep.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            foreach (string d in Directory.GetDirectories(cacheRoot))
                if (!keepSet.Contains(Path.GetFullPath(d)))
                    try { Directory.Delete(d, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // in use: next time
        }
    }
}
