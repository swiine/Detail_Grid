using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace CoWork.Loader
{
    /// <summary>
    /// Automatic updates. Releases live in a network folder:
    /// <code>
    ///   CoWork-release\latest.txt                 the version everyone should run, e.g. "1.3.0"
    ///   CoWork-release\1.3.0\CoWork.dll ...        that version's files
    ///   CoWork-release\1.3.0\manifest.json + .sig  every file's SHA-256, signed with the issuer's private key
    /// </code>
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
        public static Manifest Publish(string sourceDir, string releaseRoot, string version, Func<byte[], byte[]> sign)
        {
            if (!System.Version.TryParse(version, out _)) throw new ArgumentException("Version must look like 1.3.0");
            string target = Path.Combine(releaseRoot, version);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            foreach (string f in Directory.GetFiles(sourceDir))
                if (Path.GetFileName(f) is not (ManifestFile or SignatureFile)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)));
            Manifest m = Sign(target, version, sign);
            File.WriteAllText(Path.Combine(releaseRoot, LatestFile), version);
            return m;
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
        /// Picks what to run: the release folder's latest (copied to this PC and checked), else the newest checked copy
        /// already here, else the copy that came with the installer. Old copies are tidied away.
        /// </summary>
        public static Choice? Choose(string? releaseRoot, string cacheRoot, string bundledApp, string publicKey = PublicKey)
        {
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
            try
            {
                string latestFile = releaseRoot == null ? "" : Path.Combine(releaseRoot, LatestFile);
                if (releaseRoot != null && File.Exists(latestFile) && System.Version.TryParse(File.ReadAllText(latestFile).Trim(), out System.Version? latest)
                    && !found.Any(f => f.V >= latest))
                {
                    // Copy first, then check the copy: what runs is exactly what was checked.
                    string src = Path.Combine(releaseRoot, latest.ToString());
                    string tmp = Path.Combine(cacheRoot, latest + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(tmp);
                    foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
                    if (Verify(tmp, publicKey) is { } m && m.Version == latest.ToString())
                    {
                        string final = Path.Combine(cacheRoot, latest.ToString());
                        if (Directory.Exists(final)) Directory.Delete(final, true);
                        Directory.Move(tmp, final);
                        found.Add((latest, final, m));
                        note = $"updated to {latest}";
                    }
                    else
                    {
                        Directory.Delete(tmp, true);
                        note = $"release {latest} isn't signed correctly - ignored";
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                note = "release folder not reachable - using the copy on this PC";
            }

            if (found.Count == 0) return null;
            var best = found.OrderByDescending(f => f.V).First();
            Tidy(cacheRoot, keep: found.OrderByDescending(f => f.V).Take(2).Select(f => f.Dir));
            return new Choice(best.Dir, best.M.Version, note, best.M);
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
