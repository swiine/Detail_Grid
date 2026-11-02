using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoWork.Core;

/// <summary>
/// What a CoWork licence grants. Users: Windows logins it's for, or "*" for everyone (then usually limited to a
/// Windows domain, e.g. the office's). Machines: optional PC names. Expires: last day it works (null = never).
/// </summary>
public sealed record LicenseInfo(string Id, string Licensee, string[] Users, string? Domain = null, string[]? Machines = null,
    DateOnly? Expires = null, DateTimeOffset? Issued = null);

public enum LicenseState { Missing, Invalid, Revoked, Expired, NotForThisUser, NotForThisPc, Valid }

/// <summary>
/// Licence keys: the licence details plus an ECDSA (P-256) signature made with the issuer's private key. CoWork holds
/// only the public key, so it can check a key but never make one. A key looks like "CW1.&lt;details&gt;.&lt;signature&gt;";
/// spaces and line breaks in it are ignored, so it survives being pasted from an email.
/// </summary>
public static class Licensing
{
    /// <summary>The public half of the issuer's key pair (the private half stays with whoever issues licences).</summary>
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+YvabTQjpe/7MMuOQcmUmVnLwOawsaqoKBr1Z5CIkJ0oZPgxJV5Eld9ynmAIdNU07XOOKPD0KKj+OYSZNEO38A==";

    private const string Prefix = "CW1.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A new key pair: (public key to build into CoWork, private key to keep safe), both base64.</summary>
    public static (string PublicKey, string PrivateKey) NewKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()), Convert.ToBase64String(ec.ExportPkcs8PrivateKey()));
    }

    /// <summary>Makes a licence key (needs the private key).</summary>
    public static string Issue(LicenseInfo info, string privateKey)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(info, Json);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        byte[] sig = ec.SignData(body, HashAlgorithmName.SHA256);
        return Prefix + B64(body) + "." + B64(sig);
    }

    /// <summary>Reads a key and checks its signature (null if it isn't a genuine key).</summary>
    public static LicenseInfo? Read(string? key, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        string k = new(key.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!k.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        string[] parts = k[Prefix.Length..].Split('.');
        if (parts.Length != 2) return null;
        try
        {
            byte[] body = UnB64(parts[0]), sig = UnB64(parts[1]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!ec.VerifyData(body, sig, HashAlgorithmName.SHA256)) return null;
            return JsonSerializer.Deserialize<LicenseInfo>(body, Json);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException) { return null; }
    }

    /// <summary>Whether a key lets this person, on this PC, use CoWork today.</summary>
    public static (LicenseState State, LicenseInfo? Info) Check(string? key, string user, string domain, string machine, DateOnly today,
        string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(key)) return (LicenseState.Missing, null);
        LicenseInfo? info = Read(key, publicKey);
        if (info == null) return (LicenseState.Invalid, null);
        if (info.Expires is { } last && today > last) return (LicenseState.Expired, info);
        bool everyone = info.Users.Any(u => u.Trim() == "*");
        bool named = info.Users.Any(u => string.Equals(u.Trim(), user, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(u.Trim(), domain + "\\" + user, StringComparison.OrdinalIgnoreCase));
        bool domainOk = string.IsNullOrWhiteSpace(info.Domain) || string.Equals(info.Domain, domain, StringComparison.OrdinalIgnoreCase);
        if (!(everyone || named) || !domainOk) return (LicenseState.NotForThisUser, info);
        if (info.Machines is { Length: > 0 } pcs && !pcs.Any(m => string.Equals(m.Trim(), machine, StringComparison.OrdinalIgnoreCase)))
            return (LicenseState.NotForThisPc, info);
        return (LicenseState.Valid, info);
    }

    /// <summary>A short, human description of a licence ("Sam Smith - sam, jo - until 31 Dec 2027").</summary>
    public static string Describe(LicenseInfo i) =>
        $"{i.Licensee} - " + (i.Users.Contains("*") ? "everyone" + (i.Domain != null ? $" on {i.Domain}" : "") : string.Join(", ", i.Users))
        + (i.Machines is { Length: > 0 } m ? $" - PCs: {string.Join(", ", m)}" : "")
        + (i.Expires is { } e ? $" - until {e:d MMM yyyy}" : " - no expiry");

    // ---------------------------------------------------------------- approving people and revoking licences

    /// <summary>Where licences, requests and the revoked list live in the CoWork folder.</summary>
    public static string Folder(string dataFolder) => Path.Combine(dataFolder, "licences");
    public static string UserKeyFile(string dataFolder, string user) => Path.Combine(Folder(dataFolder), Rooms.Safe(user) + ".key");
    public static string RequestsFolder(string dataFolder) => Path.Combine(Folder(dataFolder), "requests");
    public static string RevokedFile(string dataFolder) => Path.Combine(Folder(dataFolder), "revoked.key");

    /// <summary>Asking for access: who, from which PC (the issuer approves it in the admin tool).</summary>
    public sealed record Request(string User, string Domain, string Machine, string Name, DateTimeOffset Time);

    public static void WriteRequest(string dataFolder, Request r)
    {
        Directory.CreateDirectory(RequestsFolder(dataFolder));
        File.WriteAllText(Path.Combine(RequestsFolder(dataFolder), Rooms.Safe(r.User) + ".json"), JsonSerializer.Serialize(r, Json));
    }

    public static List<Request> ReadRequests(string dataFolder)
    {
        var list = new List<Request>();
        string dir = RequestsFolder(dataFolder);
        if (!Directory.Exists(dir)) return list;
        foreach (string f in Directory.GetFiles(dir, "*.json"))
            try { if (JsonSerializer.Deserialize<Request>(File.ReadAllText(f), Json) is { } r) list.Add(r); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return list.OrderBy(r => r.Time).ToList();
    }

    /// <summary>A signed list of licence ids that no longer work ("CWR1.&lt;ids&gt;.&lt;signature&gt;").</summary>
    public static string SignRevoked(IEnumerable<string> ids, string privateKey)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(ids.Distinct().OrderBy(x => x).ToArray(), Json);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return "CWR1." + B64(body) + "." + B64(ec.SignData(body, HashAlgorithmName.SHA256));
    }

    /// <summary>The revoked ids, if the list is genuine (an unsigned or edited list counts as empty).</summary>
    public static HashSet<string> ReadRevoked(string? text, string publicKey = PublicKey)
    {
        var none = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(text)) return none;
        string t = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!t.StartsWith("CWR1.", StringComparison.Ordinal)) return none;
        string[] parts = t[5..].Split('.');
        if (parts.Length != 2) return none;
        try
        {
            byte[] body = UnB64(parts[0]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!ec.VerifyData(body, UnB64(parts[1]), HashAlgorithmName.SHA256)) return none;
            return new HashSet<string>(JsonSerializer.Deserialize<string[]>(body, Json) ?? Array.Empty<string>());
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException) { return none; }
    }

    /// <summary>
    /// Finds this person's licence and checks it: a key they pasted, one you approved for them in the shared folder,
    /// or a site key shipped with the install - the first that works wins. A revoked licence doesn't work.
    /// </summary>
    public static (LicenseState State, LicenseInfo? Info, string? Key) Find(IEnumerable<string?> candidateKeys, ISet<string> revoked,
        string user, string domain, string machine, DateOnly today, string publicKey = PublicKey)
    {
        (LicenseState, LicenseInfo?, string?) best = (LicenseState.Missing, null, null);
        foreach (string? key in candidateKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var (state, info) = Check(key, user, domain, machine, today, publicKey);
            if (state == LicenseState.Valid && info != null && revoked.Contains(info.Id)) state = LicenseState.Revoked;
            if (state == LicenseState.Valid) return (state, info, key);
            if (best.Item1 == LicenseState.Missing || state > best.Item1) best = (state, info, key);
        }
        return best;
    }

    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UnB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }
}
