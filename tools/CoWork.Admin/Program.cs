using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CoWork.Core;
using CoWork.Loader;

// CoWork Admin - keep this, and cowork-private.key next to it, to yourself.
// Anyone with the private key can let people in and publish updates that every PC will run.
string here = AppContext.BaseDirectory;
string keyFile = Path.Combine(here, "cowork-private.key");
string logFile = Path.Combine(here, "issued-licences.csv");
var config = ReadConfig(Path.Combine(here, "cowork.json"));

if (args.Length >= 3 && args[0] == "sign") // build step: sign <folder> <version>
{
    Updater.Sign(args[1], args[2], Signer(ReadKey()));
    Console.WriteLine($"Signed {args[1]} as {args[2]}");
    return;
}

Console.WriteLine("CoWork Admin");
Console.WriteLine("============");
if (!File.Exists(keyFile)) Console.WriteLine($"Note: no cowork-private.key next to this program ({here}). You'll be asked where it is.");
while (true)
{
    Console.WriteLine();
    Console.WriteLine("  1  Approve access requests");
    Console.WriteLine("  P  Pre-approve people (before they ask)");
    Console.WriteLine("  2  Issue a licence");
    Console.WriteLine("  3  Revoke (cancel) a licence");
    Console.WriteLine("  4  Publish an update");
    Console.WriteLine("  5  Check a licence key");
    Console.WriteLine("  6  List licences issued");
    Console.WriteLine("  0  Exit");
    string pick = Ask("Choose");
    try
    {
        switch (pick)
        {
            case "1": Approve(); break;
            case "p": case "P": PreApprove(); break;
            case "2": IssueByHand(); break;
            case "3": Revoke(); break;
            case "4": Publish(); break;
            case "5": CheckKey(); break;
            case "6": ListIssued(); break;
            case "0": case "": return;
        }
    }
    catch (Exception ex) { Console.WriteLine("Problem: " + ex.Message); }
}

// ---------------------------------------------------------------- the menu items

void Approve()
{
    string data = DataFolder();
    var requests = Licensing.ReadRequests(data);
    if (requests.Count == 0) { Console.WriteLine("No one is waiting."); return; }
    foreach (var r in requests)
    {
        Console.WriteLine();
        Console.WriteLine($"{r.Name}  ({r.Domain}\\{r.User}, PC {r.Machine}, asked {r.Time.LocalDateTime:d MMM HH:mm})");
        string a = Ask("Let them in? y = yes, n = no (removes the request), s = skip").ToLowerInvariant();
        if (a == "s" || a == "") continue;
        string file = Path.Combine(Licensing.RequestsFolder(data), Rooms.Safe(r.User) + ".json");
        if (a != "y") { File.Delete(file); Console.WriteLine("Request removed."); continue; }
        DateOnly? until = AskDate("Until (yyyy-mm-dd, blank = no end date)");
        bool pcOnly = Ask($"Only on PC {r.Machine}? (y/N)").ToLowerInvariant() == "y";
        var info = new LicenseInfo(Licensing.NewId(), r.Name, new[] { r.User }, r.Domain, pcOnly ? new[] { r.Machine } : null, until, DateTimeOffset.UtcNow);
        string key = Licensing.Issue(info, ReadKey());
        Directory.CreateDirectory(Licensing.Folder(data));
        File.WriteAllText(Licensing.UserKeyFile(data, r.User), key);
        File.Delete(file);
        Log(info);
        Console.WriteLine($"Approved: {Licensing.Describe(info)}. Their CoWork unlocks within about 15 seconds.");
    }
}

void PreApprove()
{
    string data = DataFolder();
    string logins = Ask("Windows logins to let in, comma separated (e.g. danielV, coreyy)");
    var list = logins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l != "*").ToList();
    if (list.Count == 0) { Console.WriteLine("Nobody named - cancelled."); return; }
    string domain = AskDomain();
    DateOnly? until = AskDate("Until (yyyy-mm-dd, blank = no end date)");
    Directory.CreateDirectory(Licensing.Folder(data));
    foreach (string login in list)
    {
        string name = Ask($"Name for {login} (blank = {login})");
        var info = new LicenseInfo(Licensing.NewId(), name.Length > 0 ? name : login, new[] { login }, domain.Length > 0 ? domain : null,
            null, until, DateTimeOffset.UtcNow);
        File.WriteAllText(Licensing.UserKeyFile(data, login), Licensing.Issue(info, ReadKey()));
        Log(info);
        Console.WriteLine($"Pre-approved: {Licensing.Describe(info)}");
    }
    Console.WriteLine("They'll go straight in the first time they open Civil 3D with CoWork - no request needed.");
}

void IssueByHand()
{
    string name = Ask("Who is it for (name or team)");
    string users = Ask("Their Windows logins, comma separated (or * for everyone on a domain)");
    string[] list = users.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (list.Length == 0) { Console.WriteLine("Nobody named - cancelled."); return; }
    string domain = AskDomain();
    string pcs = Ask("Only on these PCs, comma separated (blank = any PC)");
    DateOnly? until = AskDate("Until (yyyy-mm-dd, blank = no end date)");
    var info = new LicenseInfo(Licensing.NewId(), name, list, domain.Length > 0 ? domain : null,
        pcs.Length > 0 ? pcs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null, until, DateTimeOffset.UtcNow);
    string key = Licensing.Issue(info, ReadKey());
    Log(info);
    Console.WriteLine();
    Console.WriteLine(Licensing.Describe(info));
    Console.WriteLine("Licence key (they paste this into CoWork's licence window):");
    Console.WriteLine();
    Console.WriteLine(key);
    Console.WriteLine();
    string save = Path.Combine(here, "licences", $"{Rooms.Safe(name)}-{info.Id}.key");
    Directory.CreateDirectory(Path.GetDirectoryName(save)!);
    File.WriteAllText(save, key);
    Console.WriteLine($"Also saved to {save}");
    if (list.Length == 1 && list[0] != "*" && TryDataFolder() is { } data &&
        Ask($"Put it in the shared folder so {list[0]}'s CoWork unlocks by itself? (Y/n)").ToLowerInvariant() != "n")
    {
        Directory.CreateDirectory(Licensing.Folder(data));
        File.WriteAllText(Licensing.UserKeyFile(data, list[0]), key);
        Console.WriteLine("Done.");
    }
}

void Revoke()
{
    string data = DataFolder();
    var issued = ReadLog();
    string adminCopy = Path.Combine(here, "revoked.key");
    var (revoked, newest) = Licensing.Revoked(
        File.Exists(Licensing.RevokedFile(data)) ? File.ReadAllText(Licensing.RevokedFile(data)) : null,
        File.Exists(adminCopy) ? File.ReadAllText(adminCopy) : null);
    int serial = Licensing.ReadRevokedList(newest)?.Serial ?? 0;
    var live = issued.Where(i => !revoked.Contains(i.Id)).ToList();
    if (live.Count == 0) { Console.WriteLine("No licences to revoke (none issued from this PC, or all revoked)."); return; }
    for (int i = 0; i < live.Count; i++) Console.WriteLine($"  {i + 1,2}  [{live[i].Id}] {live[i].Text}");
    string pick = Ask("Which to revoke (numbers or ids, comma separated)");
    var ids = new List<string>();
    foreach (string p in pick.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        ids.Add(int.TryParse(p, out int n) && n >= 1 && n <= live.Count ? live[n - 1].Id : p);
    if (ids.Count == 0) return;
    revoked.UnionWith(ids);
    Directory.CreateDirectory(Licensing.Folder(data));
    string signed = Licensing.SignRevoked(new Licensing.RevokedList(serial + 1, revoked.ToArray()), ReadKey());
    File.WriteAllText(Licensing.RevokedFile(data), signed);
    File.WriteAllText(adminCopy, signed); // your own copy, in case someone deletes the shared one
    foreach (string id in ids)
        foreach (string f in Directory.GetFiles(Licensing.Folder(data), "*.key"))
            if (Licensing.Read(File.ReadAllText(f))?.Id == id) File.Delete(f);
    Console.WriteLine($"Revoked {string.Join(", ", ids)}. CoWork locks for them within 10 minutes (or at their next start).");
}

void Publish()
{
    string app = Path.Combine(here, "app");
    string source = Ask($"Folder with the new CoWork files (blank = {app})");
    if (source.Length == 0) source = app;
    string dll = Path.Combine(source, "CoWork.dll");
    if (!File.Exists(dll)) { Console.WriteLine("No CoWork.dll there."); return; }
    string version = AssemblyName.GetAssemblyName(dll).Version?.ToString(3) ?? "1.0.0";
    string release = config.Updates ?? Ask("Release folder (the 'updates' path)");
    string latest = File.Exists(Path.Combine(release, Updater.LatestFile)) ? File.ReadAllText(Path.Combine(release, Updater.LatestFile)).Trim() : "none";
    Console.WriteLine($"Publishing CoWork {version} to {release} (currently: {latest}).");
    string v = Ask($"Version number (blank = {version})");
    if (v.Length > 0) version = v;
    if (Ask("Go ahead? Everyone gets it next time they start Civil 3D (y/N)").ToLowerInvariant() != "y") return;
    Updater.Manifest m = Updater.Publish(source, release, version, Signer(ReadKey()));
    Console.WriteLine($"Published {m.Version}: {m.Files.Count} files, signed.");
}

void CheckKey()
{
    string key = Ask("Paste the key");
    LicenseInfo? info = Licensing.Read(key);
    Console.WriteLine(info == null ? "Not a genuine CoWork licence." : "Genuine: " + Licensing.Describe(info) + $" (id {info.Id})");
}

void ListIssued()
{
    var issued = ReadLog();
    var revoked = Licensing.Revoked(
        TryDataFolder() is { } d && File.Exists(Licensing.RevokedFile(d)) ? File.ReadAllText(Licensing.RevokedFile(d)) : null,
        File.Exists(Path.Combine(here, "revoked.key")) ? File.ReadAllText(Path.Combine(here, "revoked.key")) : null).Ids;
    if (issued.Count == 0) Console.WriteLine("None issued from this PC yet.");
    foreach (var i in issued) Console.WriteLine($"  [{i.Id}] {i.Text}{(revoked.Contains(i.Id) ? "  (REVOKED)" : "")}");
}

// ---------------------------------------------------------------- helpers

// Licences are tied to your office's Windows domain unless you say otherwise, so a home PC with a local account
// that happens to have the same name doesn't count.
static string AskDomain()
{
    string mine = Environment.UserDomainName;
    string d = Ask($"Windows domain it works on (blank = {mine}, the office's; type * for any)");
    return d == "*" ? "" : d.Length > 0 ? d : mine;
}

string ReadKey()
{
    string path = keyFile;
    while (!File.Exists(path))
    {
        path = Ask("Where is cowork-private.key").Trim('"');
        if (path.Length == 0) throw new InvalidOperationException("The private key is needed for this.");
    }
    return File.ReadAllText(path).Trim();
}

static Func<byte[], byte[]> Signer(string privateKey) => data =>
{
    using var ec = ECDsa.Create();
    ec.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
    return ec.SignData(data, HashAlgorithmName.SHA256);
};

string DataFolder() => TryDataFolder() ?? throw new InvalidOperationException("Can't reach the CoWork folder: " + config.Folder);

string? TryDataFolder() => config.Folder is { } f && Directory.Exists(f) ? f : null;

void Log(LicenseInfo i)
{
    bool head = !File.Exists(logFile);
    using var w = File.AppendText(logFile);
    if (head) w.WriteLine("id,issued,licensee,users,domain,pcs,expires");
    w.WriteLine(string.Join(",", i.Id, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Csv(i.Licensee), Csv(string.Join(" ", i.Users)), Csv(i.Domain ?? ""),
        Csv(string.Join(" ", i.Machines ?? Array.Empty<string>())), i.Expires?.ToString("yyyy-MM-dd") ?? ""));
}

List<(string Id, string Text)> ReadLog()
{
    if (!File.Exists(logFile)) return new();
    return File.ReadAllLines(logFile).Skip(1).Select(l => l.Split(',')).Where(p => p.Length >= 7)
        .Select(p => (p[0], $"{p[2].Trim('"')} - {p[3].Trim('"')}{(p[4].Length > 2 ? " on " + p[4].Trim('"') : "")}{(p[6].Length > 0 ? " until " + p[6] : "")} (issued {p[1]})")).ToList();
}

static string Csv(string s) => "\"" + s.Replace("\"", "'") + "\"";

static string Ask(string q)
{
    Console.Write(q + ": ");
    return (Console.ReadLine() ?? "").Trim();
}

static DateOnly? AskDate(string q)
{
    while (true)
    {
        string s = Ask(q);
        if (s.Length == 0) return null;
        if (DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out DateOnly d)) return d;
        Console.WriteLine("Please use yyyy-mm-dd, e.g. 2027-06-30.");
    }
}

static (string? Folder, string? Updates) ReadConfig(string path)
{
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        string? Get(string k) => doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return (Get("folder"), Get("updates"));
    }
    catch { return (null, null); }
}
