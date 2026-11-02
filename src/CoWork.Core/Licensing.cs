using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoWork.Core;

/// <summary>
/// What a CoWork licence grants. Users: Windows logins it's for, or "*" for everyone (then usually limited to a
/// Windows domain, e.g. the office's). Machines: optional PC names. Expires: last day it works (null = never).
/// </summary>
/// IssuedBy: who approved it. DeputyId is filled in by CoWork from the deputy's signed key (never trusted from the body).
public sealed record LicenseInfo(string Id, string Licensee, string[] Users, string? Domain = null, string[]? Machines = null,
    DateOnly? Expires = null, DateTimeOffset? Issued = null, string? IssuedBy = null, string? DeputyId = null);

/// <summary>
/// A deputy admin: someone you've allowed to approve people. Signed with your main key; holds the deputy's own public
/// key. Licences they issue carry this, so CoWork can check the chain. Until = last day they can approve people.
/// </summary>
public sealed record DeputyCert(string Id, string Name, string PublicKey, DateOnly? Until, DateTimeOffset Issued);

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
    public static LicenseInfo? Read(string? key, string publicKey = PublicKey) => ReadChain(key, publicKey).Info;

    /// <summary>
    /// Reads a key: signed by your main key ("CW1.details.signature"), or by a deputy ("CW1.details.signature.deputy",
    /// where the deputy's key is itself signed by your main key). Null if anything in the chain doesn't check out.
    /// </summary>
    public static (LicenseInfo? Info, DeputyCert? Deputy) ReadChain(string? key, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(key)) return (null, null);
        string k = new(key.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!k.StartsWith(Prefix, StringComparison.Ordinal)) return (null, null);
        string[] parts = k[Prefix.Length..].Split('.');
        if (parts.Length is not (2 or 3)) return (null, null);
        try
        {
            DeputyCert? deputy = null;
            string signer = publicKey;
            if (parts.Length == 3)
            {
                deputy = ReadDeputy(Encoding.UTF8.GetString(UnB64(parts[2])), publicKey);
                if (deputy == null) return (null, null);
                signer = deputy.PublicKey;
            }
            byte[] body = UnB64(parts[0]), sig = UnB64(parts[1]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signer), out _);
            if (!ec.VerifyData(body, sig, HashAlgorithmName.SHA256)) return (null, null);
            LicenseInfo? info = JsonSerializer.Deserialize<LicenseInfo>(body, Json);
            if (info == null) return (null, null);
            return (info with { DeputyId = deputy?.Id, IssuedBy = deputy != null ? deputy.Name : info.IssuedBy }, deputy);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException) { return (null, null); }
    }

    // ---------------------------------------------------------------- deputies

    /// <summary>Makes a deputy (needs your main private key). Returns the deputy's key file: give it only to them.</summary>
    public static (DeputyCert Cert, string KeyFile) MakeDeputy(string name, DateOnly? until, string rootPrivateKey)
    {
        var (pub, priv) = NewKeyPair();
        var cert = new DeputyCert(NewId(), name, pub, until, DateTimeOffset.UtcNow);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(cert, Json);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(rootPrivateKey), out _);
        string signed = "CWA1." + B64(body) + "." + B64(ec.SignData(body, HashAlgorithmName.SHA256));
        return (cert, JsonSerializer.Serialize(new DeputyKeyFile(signed, priv), Json));
    }

    private sealed record DeputyKeyFile(string Cert, string PrivateKey);

    /// <summary>A deputy's signed certificate, if it was signed by your main key.</summary>
    public static DeputyCert? ReadDeputy(string? signed, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(signed) || !signed.StartsWith("CWA1.", StringComparison.Ordinal)) return null;
        string[] parts = signed[5..].Split('.');
        if (parts.Length != 2) return null;
        try
        {
            byte[] body = UnB64(parts[0]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ec.VerifyData(body, UnB64(parts[1]), HashAlgorithmName.SHA256) ? JsonSerializer.Deserialize<DeputyCert>(body, Json) : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException) { return null; }
    }

    /// <summary>The deputy in a deputy key file (null if it isn't one, or wasn't signed by your main key).</summary>
    public static DeputyCert? DeputyOf(string keyFile, string publicKey = PublicKey)
    {
        try { return JsonSerializer.Deserialize<DeputyKeyFile>(keyFile, Json) is { } f ? ReadDeputy(f.Cert, publicKey) : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Makes a licence as a deputy (with their key file). Refused once their deputy rights have run out.</summary>
    public static string IssueAsDeputy(LicenseInfo info, string deputyKeyFile, DateOnly today, string publicKey = PublicKey)
    {
        var f = JsonSerializer.Deserialize<DeputyKeyFile>(deputyKeyFile, Json) ?? throw new ArgumentException("Not a deputy key file");
        DeputyCert cert = ReadDeputy(f.Cert, publicKey) ?? throw new ArgumentException("This deputy key wasn't signed by the main CoWork key");
        if (cert.Until is { } until && today > until) throw new InvalidOperationException($"Deputy rights for {cert.Name} ended on {until:d MMM yyyy}");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(info with { IssuedBy = cert.Name, DeputyId = null, Issued = DateTimeOffset.UtcNow }, Json);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(f.PrivateKey), out _);
        return Prefix + B64(body) + "." + B64(ec.SignData(body, HashAlgorithmName.SHA256)) + "." + B64(Encoding.UTF8.GetBytes(f.Cert));
    }

    /// <summary>Whether a key lets this person, on this PC, use CoWork today.</summary>
    public static (LicenseState State, LicenseInfo? Info) Check(string? key, string user, string domain, string machine, DateOnly today,
        string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(key)) return (LicenseState.Missing, null);
        var (info, deputy) = ReadChain(key, publicKey);
        if (info == null) return (LicenseState.Invalid, null);
        // A deputy can only approve people while their deputy rights last.
        if (deputy?.Until is { } dl && info.Issued is { } issued && DateOnly.FromDateTime(issued.UtcDateTime) > dl) return (LicenseState.Invalid, null);
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
        + (i.Expires is { } e ? $" - until {e:d MMM yyyy}" : " - no expiry")
        + (i.IssuedBy != null ? $" - approved by {i.IssuedBy}" : "");

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

    /// <summary>
    /// A signed, numbered list of licence ids that no longer work ("CWR2.&lt;list&gt;.&lt;signature&gt;"). Each new list has a
    /// higher number and contains every earlier revocation, so PCs keep the highest-numbered one they've seen:
    /// deleting the file, or putting an older one back, can't undo a revoke.
    /// </summary>
    public sealed record RevokedList(int Serial, string[] Ids);

    public static string SignRevoked(RevokedList list, string privateKey)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(list with { Ids = list.Ids.Distinct().OrderBy(x => x).ToArray() }, Json);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return "CWR2." + B64(body) + "." + B64(ec.SignData(body, HashAlgorithmName.SHA256));
    }

    /// <summary>The list, if it's genuine (an unsigned or edited list counts as none).</summary>
    public static RevokedList? ReadRevokedList(string? text, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!t.StartsWith("CWR2.", StringComparison.Ordinal)) return null;
        string[] parts = t[5..].Split('.');
        if (parts.Length != 2) return null;
        try
        {
            byte[] body = UnB64(parts[0]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!ec.VerifyData(body, UnB64(parts[1]), HashAlgorithmName.SHA256)) return null;
            return JsonSerializer.Deserialize<RevokedList>(body, Json);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException) { return null; }
    }

    /// <summary>
    /// The revoked ids to apply: from the newest genuine list among the shared one and the copy this PC kept.
    /// Also returns the text to keep on this PC (the newest), so a missing or older shared list never wins.
    /// </summary>
    public static (HashSet<string> Ids, string? Keep) Revoked(string? shared, string? keptOnThisPc, string publicKey = PublicKey)
    {
        RevokedList? a = ReadRevokedList(shared, publicKey), b = ReadRevokedList(keptOnThisPc, publicKey);
        (RevokedList? best, string? text) = (a, b) switch
        {
            (null, null) => (null, null),
            (not null, null) => (a, shared),
            (null, not null) => (b, keptOnThisPc),
            _ => a!.Serial >= b!.Serial ? (a, shared) : (b, keptOnThisPc),
        };
        return (new HashSet<string>(best?.Ids ?? Array.Empty<string>()), text);
    }

    // ---------------------------------------------------------------- revokes by deputies

    /// <summary>A deputy's own revoked list (signed with their deputy key, carrying their certificate).</summary>
    public static string SignRevokedAsDeputy(RevokedList list, string deputyKeyFile, string publicKey = PublicKey)
    {
        var f = JsonSerializer.Deserialize<DeputyKeyFile>(deputyKeyFile, Json) ?? throw new ArgumentException("Not a deputy key file");
        if (ReadDeputy(f.Cert, publicKey) == null) throw new ArgumentException("This deputy key wasn't signed by the main CoWork key");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(list with { Ids = list.Ids.Distinct().OrderBy(x => x).ToArray() }, Json);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(f.PrivateKey), out _);
        return "CWR2." + B64(body) + "." + B64(ec.SignData(body, HashAlgorithmName.SHA256)) + "." + B64(Encoding.UTF8.GetBytes(f.Cert));
    }

    /// <summary>A revoked list and who signed it (Deputy null = your main key). Null if it doesn't check out.</summary>
    public static (RevokedList List, DeputyCert? Deputy)? ReadRevokedSigned(string? text, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!t.StartsWith("CWR2.", StringComparison.Ordinal)) return null;
        string[] parts = t[5..].Split('.');
        if (parts.Length is not (2 or 3)) return null;
        try
        {
            DeputyCert? deputy = null;
            string signer = publicKey;
            if (parts.Length == 3)
            {
                deputy = ReadDeputy(Encoding.UTF8.GetString(UnB64(parts[2])), publicKey);
                if (deputy == null) return null;
                signer = deputy.PublicKey;
            }
            byte[] body = UnB64(parts[0]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signer), out _);
            if (!ec.VerifyData(body, UnB64(parts[1]), HashAlgorithmName.SHA256)) return null;
            return JsonSerializer.Deserialize<RevokedList>(body, Json) is { } list ? (list, deputy) : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException) { return null; }
    }

    /// <summary>
    /// Everything revoked, from all the lists found (shared folder and the copies this PC kept): the newest list per
    /// signer counts. Your main list can revoke licences and deputies; a deputy's list can only revoke licences, and is
    /// ignored once that deputy is revoked. Keep = the newest list per signer, to keep on this PC.
    /// </summary>
    public sealed record RevokedSet(HashSet<string> ByMain, HashSet<string> ByDeputies, Dictionary<string, string> Keep)
    {
        public static readonly RevokedSet None = new(new(), new(), new());

        public bool Revokes(LicenseInfo i) =>
            ByMain.Contains(i.Id) || (i.DeputyId != null && ByMain.Contains(i.DeputyId)) || ByDeputies.Contains(i.Id);
    }

    public static RevokedSet RevokedAll(IEnumerable<string?> texts, string publicKey = PublicKey)
    {
        var best = new Dictionary<string, (RevokedList List, string Text)>();
        foreach (string? text in texts)
        {
            if (ReadRevokedSigned(text, publicKey) is not { } r) continue;
            string signer = r.Deputy?.Id ?? "main";
            if (!best.TryGetValue(signer, out var cur) || r.List.Serial > cur.List.Serial) best[signer] = (r.List, text!.Trim());
        }
        var byMain = new HashSet<string>(best.TryGetValue("main", out var m) ? m.List.Ids : Array.Empty<string>());
        var byDeputies = new HashSet<string>();
        foreach (var (signer, entry) in best)
            if (signer != "main" && !byMain.Contains(signer)) byDeputies.UnionWith(entry.List.Ids);
        return new RevokedSet(byMain, byDeputies, best.ToDictionary(kv => kv.Key, kv => kv.Value.Text));
    }

    public static string DeputyRevokedFile(string dataFolder, string deputyId) => Path.Combine(Folder(dataFolder), $"revoked-{deputyId}.key");

    /// <summary>Kept for the earlier test helpers: the ids in a genuine list (none if not genuine).</summary>
    public static HashSet<string> ReadRevoked(string? text, string publicKey = PublicKey) =>
        new(ReadRevokedList(text, publicKey)?.Ids ?? Array.Empty<string>());

    /// <summary>
    /// Finds this person's licence and checks it: a key they pasted, one you approved for them in the shared folder,
    /// or a site key shipped with the install - the first that works wins. A revoked licence doesn't work.
    /// </summary>
    public static (LicenseState State, LicenseInfo? Info, string? Key) Find(IEnumerable<string?> candidateKeys, ISet<string> revoked,
        string user, string domain, string machine, DateOnly today, string publicKey = PublicKey) =>
        Find(candidateKeys, new RevokedSet(new HashSet<string>(revoked), new(), new()), user, domain, machine, today, publicKey);

    public static (LicenseState State, LicenseInfo? Info, string? Key) Find(IEnumerable<string?> candidateKeys, RevokedSet revoked,
        string user, string domain, string machine, DateOnly today, string publicKey = PublicKey)
    {
        (LicenseState, LicenseInfo?, string?) best = (LicenseState.Missing, null, null);
        foreach (string? key in candidateKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var (state, info) = Check(key, user, domain, machine, today, publicKey);
            if (state == LicenseState.Valid && info != null && revoked.Revokes(info))
                state = LicenseState.Revoked; // the licence, or the deputy who issued it, was revoked
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
