using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CoWork.Core;
using CoWork.Loader;

// CoWork Admin - keep this, and cowork-private.key next to it, to yourself.
// Anyone with the private key can let people in and publish updates that every PC will run.
string here = AppContext.BaseDirectory;
string keyFile = Path.Combine(here, "cowork-private.key");
string deputyFile = Path.Combine(here, "cowork-deputy.key");
// Main admin (has the private key) or a deputy (has a deputy key: can approve people, nothing else).
bool isDeputy = !File.Exists(keyFile) && File.Exists(deputyFile);
DeputyCert? me = isDeputy ? Licensing.DeputyOf(File.ReadAllText(deputyFile)) : null;
if (isDeputy && me == null) { Console.WriteLine("cowork-deputy.key isn't a valid CoWork deputy key."); Console.ReadLine(); return; }
string logFile = Path.Combine(here, "issued-licences.csv");
var config = ReadConfig(Path.Combine(here, "cowork.json"));

if (args.Length >= 3 && args[0] == "sign") // build step: sign <folder> <version>
{
    Updater.Sign(args[1], args[2], Signer(ReadKey()));
    Console.WriteLine($"Signed {args[1]} as {args[2]}");
    return;
}

Console.WriteLine(isDeputy ? $"CoWork Admin - deputy: {me!.Name}" : "CoWork Admin");
Console.WriteLine("============");
if (isDeputy && me!.Until is { } du) Console.WriteLine($"You can approve people until {du:d MMM yyyy}.");
if (!isDeputy && !File.Exists(keyFile)) Console.WriteLine($"Note: no cowork-private.key next to this program ({here}). You'll be asked where it is.");
while (true)
{
    Console.WriteLine();
    Console.WriteLine("  1  Approve access requests");
    Console.WriteLine("  P  Pre-approve people (before they ask)");
    Console.WriteLine("  2  Issue a licence");
    Console.WriteLine(isDeputy ? "  3  Revoke (cancel) a licence" : "  3  Revoke (cancel) a licence or a deputy");
    if (!isDeputy)
    {
        Console.WriteLine("  4  Publish an update");
        Console.WriteLine("  D  Make a deputy (someone else who can approve people)");
    }
    Console.WriteLine("  R  Problem reports and logs");
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
            case "4" when !isDeputy: Publish(); break;
            case "d" or "D" when !isDeputy: MakeDeputy(); break;
            case "r" or "R": Reports(); break;
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
        string key = Sign(info);
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
        File.WriteAllText(Licensing.UserKeyFile(data, login), Sign(info));
        Log(info);
        Console.WriteLine($"Pre-approved: {Licensing.Describe(info)}");
    }
    Console.WriteLine("They'll go straight in the first time they open Civil 3D with CoWork - no request needed.");
}

void MakeDeputy()
{
    string name = Ask("Deputy's name (e.g. Daniel V)");
    if (name.Length == 0) return;
    DateOnly? until = AskDate("They can approve people until (yyyy-mm-dd, blank = until you revoke them)");
    var (cert, file) = Licensing.MakeDeputy(name, until, ReadKey());
    string folder = Path.Combine(here, "deputies", Rooms.Safe(name) + "-" + cert.Id, "CoWork-Admin");
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "cowork-deputy.key"), file);
    if (Environment.ProcessPath is { } exe && Path.GetFileName(exe).StartsWith("CoWork-Admin", StringComparison.OrdinalIgnoreCase))
        File.Copy(exe, Path.Combine(folder, Path.GetFileName(exe)), true);
    if (File.Exists(Path.Combine(here, "cowork.json"))) File.Copy(Path.Combine(here, "cowork.json"), Path.Combine(folder, "cowork.json"), true);
    File.WriteAllText(Path.Combine(folder, "README-DEPUTY.txt"),
        $"CoWork Admin for {name} (deputy).\r\n\r\nDouble-click CoWork-Admin.exe. You can approve access requests, pre-approve people and issue\r\n" +
        "licences. Revoking and publishing updates stay with the main admin.\r\n\r\n" +
        "Keep cowork-deputy.key to yourself: anyone with it can let people into CoWork in your name.\r\n");
    Log(new LicenseInfo(cert.Id, name + " (DEPUTY)", new[] { "(approves people)" }, null, null, until, DateTimeOffset.UtcNow));
    Console.WriteLine($"Deputy made: {name} [{cert.Id}]{(until is { } u ? $", until {u:d MMM yyyy}" : "")}.");
    Console.WriteLine($"Give them this folder (zip it): {Path.GetDirectoryName(folder)}");
    Console.WriteLine("To take their rights away later: 3 Revoke, and pick their DEPUTY line (licences they issued stop too).");
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
    string key = Sign(info);
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
    Licensing.RevokedSet now = AllRevoked(data);
    // What can be revoked: licences in the shared folder, plus what's in this PC's log. Deputies can't revoke deputies.
    var items = new List<(string Id, string Text, bool Deputy)>();
    foreach (string f in Directory.Exists(Licensing.Folder(data)) ? Directory.GetFiles(Licensing.Folder(data), "*.key") : Array.Empty<string>())
        if (!Path.GetFileName(f).StartsWith("revoked", StringComparison.OrdinalIgnoreCase) && Licensing.Read(File.ReadAllText(f)) is { } li)
            items.Add((li.Id, Licensing.Describe(li), false));
    foreach (var (id, text) in ReadLog())
        if (items.All(x => x.Id != id)) items.Add((id, text, text.Contains("(DEPUTY)")));
    var live = items.Where(x => !now.ByMain.Contains(x.Id) && !now.ByDeputies.Contains(x.Id) && !(isDeputy && x.Deputy)).ToList();
    if (live.Count == 0) { Console.WriteLine("Nothing to revoke."); return; }
    for (int i = 0; i < live.Count; i++) Console.WriteLine($"  {i + 1,2}  [{live[i].Id}] {live[i].Text}");
    string pick = Ask("Which to revoke (numbers or ids, comma separated)");
    var ids = new List<string>();
    foreach (string p in pick.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        ids.Add(int.TryParse(p, out int n) && n >= 1 && n <= live.Count ? live[n - 1].Id : p);
    if (isDeputy) ids.RemoveAll(id => items.Any(x => x.Id == id && x.Deputy));
    if (ids.Count == 0) return;

    // Add to your own numbered list (main key) or this deputy's own list, and keep a copy here too.
    string signer = isDeputy ? me!.Id : "main";
    string sharedFile = isDeputy ? Licensing.DeputyRevokedFile(data, me!.Id) : Licensing.RevokedFile(data);
    string localCopy = Path.Combine(here, isDeputy ? "revoked-mine.key" : "revoked.key");
    Licensing.RevokedSet mine = Licensing.RevokedAll(new[] { ReadIf(sharedFile), ReadIf(localCopy) });
    var current = Licensing.ReadRevokedSigned(mine.Keep.GetValueOrDefault(signer))?.List ?? new Licensing.RevokedList(0, Array.Empty<string>());
    var next = new Licensing.RevokedList(current.Serial + 1, current.Ids.Concat(ids).ToArray());
    string signed = isDeputy ? Licensing.SignRevokedAsDeputy(next, File.ReadAllText(deputyFile)) : Licensing.SignRevoked(next, ReadKey());
    Directory.CreateDirectory(Licensing.Folder(data));
    File.WriteAllText(sharedFile, signed);
    File.WriteAllText(localCopy, signed); // your own copy, in case someone deletes the shared one
    foreach (string id in ids)
        foreach (string f in Directory.GetFiles(Licensing.Folder(data), "*.key"))
            if (Licensing.Read(File.ReadAllText(f))?.Id == id) File.Delete(f);
    Console.WriteLine($"Revoked {string.Join(", ", ids)}. CoWork locks for them within 10 minutes (or at their next start).");
}

Licensing.RevokedSet AllRevoked(string data)
{
    var texts = new List<string?> { ReadIf(Path.Combine(here, "revoked.key")), ReadIf(Path.Combine(here, "revoked-mine.key")) };
    if (Directory.Exists(Licensing.Folder(data))) texts.AddRange(Directory.GetFiles(Licensing.Folder(data), "revoked*.key").Select(ReadIf));
    return Licensing.RevokedAll(texts);
}

static string? ReadIf(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

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

void Reports()
{
    string data = DataFolder();
    var reports = CoWork.Core.Log.Reports(data).Take(20).ToList();
    if (reports.Count == 0) Console.WriteLine("No problem reports.");
    for (int i = 0; i < reports.Count; i++) Console.WriteLine($"  {i + 1,2}  {Path.GetFileNameWithoutExtension(reports[i])}");
    string logs = Path.Combine(data, "logs");
    Console.WriteLine($"Everyone's logs are in {logs}");
    string pick = Ask("Number to read (or a login to read their log, blank = back)");
    if (pick.Length == 0) return;
    string? file = int.TryParse(pick, out int n) && n >= 1 && n <= reports.Count ? reports[n - 1] : Path.Combine(logs, Rooms.Safe(pick) + ".txt");
    if (!File.Exists(file)) { Console.WriteLine("Not found."); return; }
    string[] lines = File.ReadAllLines(file);
    Console.WriteLine();
    foreach (string l in lines.Skip(Math.Max(0, lines.Length - 120))) Console.WriteLine(l);
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
    var all = TryDataFolder() is { } d ? AllRevoked(d) : Licensing.RevokedSet.None;
    var revoked = new HashSet<string>(all.ByMain.Concat(all.ByDeputies));
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

// Signs a licence with your main key, or as a deputy.
string Sign(LicenseInfo info) => isDeputy
    ? Licensing.IssueAsDeputy(info, File.ReadAllText(deputyFile), DateOnly.FromDateTime(DateTime.Today))
    : Licensing.Issue(info with { IssuedBy = Environment.UserName }, ReadKey());

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
