using CoWork.Core;

namespace CivDoom.Engine.Tests;

public class CoWorkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ci-" + Guid.NewGuid().ToString("N"));

    public CoWorkTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public void MessagesReachOtherPeople()
    {
        var hana = new ChatStore(_folder, "hana", "Hana", "PC1");
        var cal = new ChatStore(_folder, "cal", "Cal", "PC2");
        Assert.Empty(cal.History(Rooms.Lobby));

        hana.Post(Rooms.Lobby, "morning all");
        hana.Post(Rooms.Lobby, "anyone free to check sheet 9?");
        List<ChatMessage> got = cal.ReadNew(Rooms.Lobby);
        Assert.Equal(new[] { "morning all", "anyone free to check sheet 9?" }, got.Select(m => m.Text));
        Assert.All(got, m => Assert.Equal("Hana", m.Name));
        Assert.Empty(cal.ReadNew(Rooms.Lobby)); // nothing new since

        cal.Post(Rooms.Lobby, "on it", drawing: @"Z:\jobs\24\241920\sheet9.dwg");
        ChatMessage reply = Assert.Single(hana.ReadNew(Rooms.Lobby), m => m.User == "cal");
        Assert.Equal(@"Z:\jobs\24\241920\sheet9.dwg", reply.Drawing);

        // A fresh start (reopening Civil 3D) shows the history in order.
        var hanaAgain = new ChatStore(_folder, "hana", "Hana", "PC1");
        Assert.Equal(new[] { "morning all", "anyone free to check sheet 9?", "on it" }, hanaAgain.History(Rooms.Lobby).Select(m => m.Text));
    }

    [Fact]
    public void EachPersonOnlyWritesTheirOwnFiles()
    {
        new ChatStore(_folder, "hana", "Hana", "PC1").Post(Rooms.Lobby, "a");
        new ChatStore(_folder, "cal", "Cal", "PC2").Post(Rooms.Lobby, "b");
        string[] people = Directory.GetDirectories(Path.Combine(_folder, "rooms", Rooms.Lobby)).Select(Path.GetFileName).Order().ToArray()!;
        Assert.Equal(new[] { "cal", "hana" }, people);
    }

    [Fact]
    public void ButtonsTravelWithMessages()
    {
        var hana = new ChatStore(_folder, "hana", "Hana", "PC1");
        hana.Post("job-241920", "Drafting together in sheet9.dwg", button: new MessageButton("Join CoWork", "CWJOIN 1a2b3c4d"));
        ChatMessage m = Assert.Single(new ChatStore(_folder, "cal", "Cal", "PC2").History("job-241920"));
        Assert.Equal("Join CoWork", m.Button?.Label);
        Assert.Equal("CWJOIN 1a2b3c4d", m.Button?.Command);
    }

    [Fact]
    public void WhoIsOnline()
    {
        var hana = new ChatStore(_folder, "hana", "Hana", "PC1");
        hana.SayOnline(new[] { Rooms.Lobby, "job-241920" });
        OnlinePerson p = Assert.Single(new ChatStore(_folder, "cal", "Cal", "PC2").Online(TimeSpan.FromMinutes(1)));
        Assert.Equal("Hana", p.Name);
        Assert.Contains("job-241920", p.Rooms);
        hana.SayOffline();
        Assert.Empty(hana.Online(TimeSpan.FromMinutes(1)));
        Assert.Single(hana.LastSeen());
    }

    [Fact]
    public void JobRoomsComeFromTheDrawingPath()
    {
        Assert.Equal("job-241920", Rooms.ForDrawing(@"\\server\projects\jobs\24\241920\civil\sheets\9\a.dwg"));
        Assert.Equal("job-241459", Rooms.ForDrawing(@"Z:\jobs\24\241459\x.dwg"));
        Assert.Null(Rooms.ForDrawing(@"C:\temp\x.dwg"));
        Assert.Equal("Job 241920", Rooms.Title("job-241920"));
        Assert.Equal("General", Rooms.Title(Rooms.Lobby)); // shown as General; the id stays "lobby" so history is kept
        Assert.False(Rooms.IsValid("..\\evil"));
        Assert.Throws<ArgumentException>(() => new ChatStore(_folder, "a", "A", "P").Post("../x", "hi"));
    }

    [Fact]
    public void SettingsNeverPointAtAnotherChatToolsFolder()
    {
        string defaults = Path.Combine(_folder, "cowork.json"), user = Path.Combine(_folder, "user.json");
        File.WriteAllText(defaults, "{ \"folder\": \"\\\\\\\\server\\\\share\\\\CoWork-data\" }");
        CiSettings s = CiSettings.Load(defaults, user);
        Assert.Equal(@"\\server\share\CoWork-data", s.Folder);

        new CiSettings { Folder = @"\\server\apps\TTWChat", DisplayName = "Hana" }.Save(user);
        s = CiSettings.Load(defaults, user);
        Assert.Null(s.Folder); // refused
        Assert.Equal("Hana", s.Name);
        Assert.False(CiSettings.IsAllowedFolder(@"A:\x\ttwchat\y"));
        Assert.True(CiSettings.IsAllowedFolder(@"\\server\projects\jobs\24\241920\civil\sheets\9\_test script\CoWork-data"));
    }

    [Fact]
    public void ShippedDefaultsPointAtACoWorkFolder()
    {
        string repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "CivDoom.sln"))) repo = Path.GetDirectoryName(repo)!;
        string defaults = Path.Combine(repo, "bundle", "CoWork.bundle", "Contents", "cowork.json");
        CiSettings s = CiSettings.Load(defaults, Path.Combine(_folder, "nobody.json"));
        Assert.StartsWith(@"\\", s.Folder);                // a network folder everyone can reach
        Assert.EndsWith(@"\CoWork-data", s.Folder);
        Assert.True(CiSettings.IsAllowedFolder(s.Folder));
        Assert.True(s.Popups);
    }

    [Fact]
    public void LockWhileEditing_FirstToGrabKeepsIt()
    {
        var now = DateTimeOffset.UtcNow;
        LivePerson P(string id, string name, DateTimeOffset seen, params (string Obj, double Ago)[] grabs) =>
            new(id, name, name.ToLower(), "PC", false, 0, 0, seen, "#3CC85A", grabs.ToDictionary(g => g.Obj, g => now.AddSeconds(-g.Ago)));

        var sam = P("sam-1", "Sam", now, ("h:1A", 5), ("g:kerb", 2));
        var jo = P("jo-1", "Jo", now, ("g:kerb", 4));                 // grabbed the kerb before Sam
        var gone = P("old-1", "Old", now.AddSeconds(-40), ("h:2B", 1)); // PC stopped updating: lets go

        var mine = new Dictionary<string, DateTimeOffset> { ["h:1A"] = now.AddSeconds(-1), ["h:3C"] = now };
        Dictionary<string, LivePerson> held = LiveLocks.HeldByOthers(new[] { sam, jo, gone }, "me-1", mine, now);

        Assert.Equal("Sam", held["h:1A"].Name);   // Sam grabbed it before me
        Assert.Equal("Jo", held["g:kerb"].Name);  // earliest of the others
        Assert.False(held.ContainsKey("h:2B"));   // stale
        Assert.False(held.ContainsKey("h:3C"));   // only mine

        // Seen from Sam's PC, the same answer: Jo has the kerb, Sam keeps 1A (Sam was before me).
        var samMine = sam.Editing!;
        var meAsOther = P("me-1", "Me", now, ("h:1A", 1), ("h:3C", 0));
        var fromSam = LiveLocks.HeldByOthers(new[] { jo, meAsOther }, "sam-1", samMine, now);
        Assert.Equal("Jo", fromSam["g:kerb"].Name);
        Assert.False(fromSam.ContainsKey("h:1A"));
        Assert.Equal("Me", fromSam["h:3C"].Name);
    }

    [Fact]
    public void CrosshairColourIsYoursOrSteady()
    {
        Assert.Equal("#3CC85A", CrosshairColours.For("sam", "#3cc85a"));
        string auto = CrosshairColours.For("sam", null);
        Assert.Equal(auto, CrosshairColours.For("SAM", "not-a-colour"));
        Assert.Contains(CrosshairColours.Choices, c => c.Hex == auto);
        Assert.Equal("#12AB9F", CrosshairColours.For("sam", "#12ab9f"));   // any colour from the wheel
        Assert.Equal("#FF0000", CrosshairColours.FromHsv(0, 1, 1));
        Assert.Equal("#00FF00", CrosshairColours.FromHsv(120, 1, 1));
        Assert.Equal("#808080", CrosshairColours.FromHsv(200, 0, 0.5));
        var (h, sat, v) = CrosshairColours.ToHsv("#3C9BF0");
        Assert.Equal("#3C9BF0", CrosshairColours.FromHsv(h, sat, v));  // there and back
    }

    [Fact]
    public void MentionsReachThePersonNamed()
    {
        Assert.True(Mentions.IsFor("@Sam can you check the kerb?", "ssmith", "Sam Smith"));
        Assert.True(Mentions.IsFor("thanks @SamSmith.", "ssmith", "Sam Smith"));
        Assert.True(Mentions.IsFor("cc @ssmith", "ssmith", "Sam Smith"));
        Assert.False(Mentions.IsFor("email sam@example.com", "sam", "Sam Smith")); // an email address isn't a mention
        Assert.False(Mentions.IsFor("@Samantha hi", "ssmith", "Sam Smith"));
        Assert.False(Mentions.IsFor("no mention here Sam", "ssmith", "Sam Smith"));
        Assert.Equal(new[] { "Jo", "Sam" }, Mentions.Tokens("@Jo and @Sam."));

        Assert.Equal("@Sam", Mentions.HandleFor("Sam Smith", new[] { "Sam Smith", "Jo Lee" }));
        Assert.Equal("@SamSmith", Mentions.HandleFor("Sam Smith", new[] { "Sam Smith", "Sam Jones" }));
    }

    [Fact]
    public void LayersPointsAndPointGroupsTravelInBatches()
    {
        string dir = Path.Combine(_folder, "live", "s1", "me");
        var batch = new LiveBatch(1, "me", DateTimeOffset.UtcNow, new(), new(), new(),
            LayerStates: new() { new LiveLayer("C-ROAD", "#FF8000", Off: false, Frozen: true, Locked: false, "DASHED", 35, true, "Kerbs") },
            Points: new() { new LivePoint(1001, 300100.5, 6250200.25, 12.345, "TOK", "Kerb 1") },
            ErasedPoints: new() { 1002 },
            Groups: new() { new LivePointGroup("Kerbs", "All kerb points", false, "1000-1999", null, "TOK*", null, null, null, null, null, null, null) },
            ErasedGroups: new() { "Old" });
        Assert.False(batch.IsEmpty);
        Assert.True(new LiveBatch(2, "me", DateTimeOffset.UtcNow, new(), new(), new()).IsEmpty);

        LiveFolder.WriteBatch(dir, batch);
        LiveBatch back = Assert.Single(LiveFolder.BatchesAfter(dir, 0));
        Assert.Equal("#FF8000", back.LayerStates![0].Colour);
        Assert.True(back.LayerStates[0].Frozen);
        Assert.Equal(1001u, back.Points![0].Number);
        Assert.Equal(12.345, back.Points[0].Elevation);
        Assert.Equal(new uint[] { 1002 }, back.ErasedPoints);
        Assert.Equal("TOK*", back.Groups![0].IncludeRawDescriptions);
        Assert.Equal("Old", Assert.Single(back.ErasedGroups!));
    }

    [Fact]
    public void WhoHasThisDrawingOpen()
    {
        var sam = new ChatStore(_folder, "sam", "Sam", "PC1");
        sam.SayOnline(new[] { Rooms.Lobby }, "C-SHEET-09.dwg", openDrawings: new[] { @"\\server\projects\jobs\24\241920\C-SHEET-09.dwg", @"\\server\projects\jobs\24\241920\C-ROAD-02.dwg" });
        var me = new ChatStore(_folder, "alex", "Alex", "PC2");
        OnlinePerson seen = Assert.Single(me.Online(TimeSpan.FromMinutes(1)), p => p.User == "sam");
        Assert.True(seen.HasOpen(@"\\SERVER\projects\jobs\24\241920\c-sheet-09.dwg"));   // case doesn't matter
        Assert.True(seen.HasOpen("//server/projects/jobs/24/241920/C-ROAD-02.dwg"));       // nor slashes
        Assert.False(seen.HasOpen(@"\\server\projects\jobs\24\241920\C-DRAIN-04.dwg"));

        sam.SayOffline();
        Assert.Null(me.LastSeen().Single(p => p.User == "sam").OpenDrawings); // closed Civil 3D: nothing open
    }

    [Fact]
    public void SharedViewsRoundTrip()
    {
        var v = new SharedView(SharedView.NewId(), @"\\server\projects\jobs\24\241920\C-SHEET-09.dwg", 300100, 6250200, 80, 45,
            new[] { "1A2", "1A3" }, "Sam", DateTimeOffset.UtcNow);
        v.Save(_folder);
        SharedView back = SharedView.Load(_folder, v.Id)!;
        Assert.Equal(v.Drawing, back.Drawing);
        Assert.Equal(new[] { "1A2", "1A3" }, back.Handles);
        Assert.Null(SharedView.Load(_folder, @"..\..\secrets"));   // only plain ids
        Assert.Null(SharedView.Load(_folder, "abcdef0123"));       // not there
    }

    [Fact]
    public void InterlockPixelArtIsCleanInEveryScheme()
    {
        Assert.Equal(16, Brand.InterlockPixels.Length);
        Assert.All(Brand.InterlockPixels, row => Assert.Equal(16, row.Length));
        string[] g = Brand.InterlockPixels;
        // The two brackets (and their shadows) never touch, even diagonally, so they stay apart at 16 px.
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (char.ToLower(g[y][x]) == 'a')
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int yy = y + dy, xx = x + dx;
                            if (yy is >= 0 and < 16 && xx is >= 0 and < 16) Assert.NotEqual('c', char.ToLower(g[yy][xx]));
                        }
        foreach (Palette p in Brand.Palettes)
        {
            Assert.NotEqual(p.Accent, p.Accent2);                  // two different brackets in every scheme
            Assert.Equal(p.Rail, Brand.PixelColour('b', p));
            Assert.Null(Brand.PixelColour('.', p));
        }
        Assert.Equal("#000000", Brand.Shade("#000000"));
        Assert.Equal("#996633", Brand.Shade("#FFAA55"));
    }

    [Fact]
    public void LicencesAreSignedAndCheckedForThePersonAndPc()
    {
        var (pub, priv) = Licensing.NewKeyPair();
        var today = new DateOnly(2026, 11, 2);
        (LicenseState, LicenseInfo?) Check(string key, string user = "sam", string domain = "OFFICE", string pc = "PC1") =>
            Licensing.Check(key, user, domain, pc, today, pub);

        string named = Licensing.Issue(new LicenseInfo("a1", "Sam Smith", new[] { "sam", "OFFICE\\jo" }), priv);
        Assert.Equal(LicenseState.Valid, Check(named).Item1);
        Assert.Equal(LicenseState.Valid, Check(named, "JO").Item1);                     // logins aren't case sensitive
        Assert.Equal(LicenseState.NotForThisUser, Check(named, "alex").Item1);
        Assert.Equal(LicenseState.Valid, Check("  " + named[..20] + "\n" + named[20..] + " ").Item1); // pasted with line breaks

        string site = Licensing.Issue(new LicenseInfo("a2", "Whole office", new[] { "*" }, Domain: "OFFICE"), priv);
        Assert.Equal(LicenseState.Valid, Check(site, "anyone").Item1);
        Assert.Equal(LicenseState.NotForThisUser, Check(site, "anyone", "HOME").Item1);  // only on the office domain

        string pcs = Licensing.Issue(new LicenseInfo("a3", "Drafting PCs", new[] { "*" }, Machines: new[] { "PC1" }), priv);
        Assert.Equal(LicenseState.Valid, Check(pcs).Item1);
        Assert.Equal(LicenseState.NotForThisPc, Check(pcs, pc: "PC2").Item1);

        string dated = Licensing.Issue(new LicenseInfo("a4", "Trial", new[] { "*" }, Expires: new DateOnly(2026, 11, 2)), priv);
        Assert.Equal(LicenseState.Valid, Check(dated).Item1);                             // works on its last day
        Assert.Equal(LicenseState.Expired, Licensing.Check(dated, "sam", "OFFICE", "PC1", today.AddDays(1), pub).Item1);

        // Tampering, fakes and other people's keys are all rejected.
        string tampered = named.Replace(named.Split('.')[1], Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            "{\"id\":\"a1\",\"licensee\":\"Sam Smith\",\"users\":[\"*\"]}")).TrimEnd('='));
        Assert.Equal(LicenseState.Invalid, Check(tampered).Item1);
        Assert.Equal(LicenseState.Invalid, Check("CW1.not.real").Item1);
        Assert.Equal(LicenseState.Missing, Check("  ").Item1);
        var (otherPub, _) = Licensing.NewKeyPair();
        Assert.Equal(LicenseState.Invalid, Licensing.Check(named, "sam", "OFFICE", "PC1", today, otherPub).Item1);

        Assert.Equal("Whole office - everyone on OFFICE - no expiry", Licensing.Describe(Licensing.Read(site, pub)!));
    }

    [Fact]
    public void RequestsApprovalsAndRevokingWork()
    {
        var (pub, priv) = Licensing.NewKeyPair();
        var today = new DateOnly(2026, 11, 2);
        Licensing.WriteRequest(_folder, new Licensing.Request("jo", "OFFICE", "PC7", "Jo Lee", DateTimeOffset.UtcNow));
        Licensing.Request req = Assert.Single(Licensing.ReadRequests(_folder));
        Assert.Equal("PC7", req.Machine);

        string jo = Licensing.Issue(new LicenseInfo("j1", "Jo Lee", new[] { "jo" }, "OFFICE"), priv);
        string other = Licensing.Issue(new LicenseInfo("x9", "Someone else", new[] { "sam" }), priv);
        var none = new HashSet<string>();
        Assert.Equal(LicenseState.Valid, Licensing.Find(new[] { null, other, jo }, none, "jo", "OFFICE", "PC7", today, pub).State);
        Assert.Equal(LicenseState.NotForThisUser, Licensing.Find(new[] { other }, none, "jo", "OFFICE", "PC7", today, pub).State);

        string revoked = Licensing.SignRevoked(new Licensing.RevokedList(1, new[] { "j1" }), priv);
        HashSet<string> ids = Licensing.ReadRevoked(revoked, pub);
        Assert.Contains("j1", ids);
        Assert.Equal(LicenseState.Revoked, Licensing.Find(new[] { jo }, ids, "jo", "OFFICE", "PC7", today, pub).State);
        string body = revoked.Split('.')[1];
        string edited = revoked.Replace(body, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[\"zz\"]")).TrimEnd('='));
        Assert.Empty(Licensing.ReadRevoked(edited, pub));                         // an edited list doesn't count
        Assert.Equal(Licensing.UserKeyFile(_folder, "Jo"), Licensing.UserKeyFile(_folder, "jo"));
    }

    [Fact]
    public void RevokesCantBeUndoneByDeletingOrSwappingTheList()
    {
        var (pub, priv) = Licensing.NewKeyPair();
        string first = Licensing.SignRevoked(new Licensing.RevokedList(1, new[] { "a" }), priv);
        string second = Licensing.SignRevoked(new Licensing.RevokedList(2, new[] { "a", "b" }), priv);

        var (ids, keep) = Licensing.Revoked(second, null, pub);      // PC sees the new list and keeps it
        Assert.Equal(new[] { "a", "b" }, ids.OrderBy(x => x));
        Assert.Equal(second, keep);

        Assert.Contains("b", Licensing.Revoked(null, keep, pub).Ids);     // shared list deleted: still revoked
        Assert.Contains("b", Licensing.Revoked(first, keep, pub).Ids);    // older list put back: still revoked
        Assert.Contains("b", Licensing.Revoked("CWR2.forged.sig", keep, pub).Ids); // junk: still revoked
        Assert.Empty(Licensing.Revoked(null, null, pub).Ids);
    }

    [Fact]
    public void DeputiesCanApprovePeopleUntilRevoked()
    {
        var (pub, priv) = Licensing.NewKeyPair();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (cert, deputyFile) = Licensing.MakeDeputy("Daniel V", today.AddDays(30), priv);
        Assert.Equal("Daniel V", Licensing.DeputyOf(deputyFile, pub)!.Name);

        string key = Licensing.IssueAsDeputy(new LicenseInfo("s1", "Sam", new[] { "sam" }, "OFFICE"), deputyFile, today, pub);
        var none = new HashSet<string>();
        var (state, info, _) = Licensing.Find(new[] { key }, none, "sam", "OFFICE", "PC1", today, pub);
        Assert.Equal(LicenseState.Valid, state);
        Assert.Equal("Daniel V", info!.IssuedBy);
        Assert.Equal(cert.Id, info.DeputyId);
        Assert.EndsWith("approved by Daniel V", Licensing.Describe(info));

        // Revoking the deputy stops everything they issued; revoking just the licence works too.
        Assert.Equal(LicenseState.Revoked, Licensing.Find(new[] { key }, new HashSet<string> { cert.Id }, "sam", "OFFICE", "PC1", today, pub).State);
        Assert.Equal(LicenseState.Revoked, Licensing.Find(new[] { key }, new HashSet<string> { "s1" }, "sam", "OFFICE", "PC1", today, pub).State);

        // A deputy key made with someone else's main key is worthless here.
        var (_, otherPriv) = Licensing.NewKeyPair();
        var (_, fakeDeputy) = Licensing.MakeDeputy("Mallory", null, otherPriv);
        Assert.Throws<ArgumentException>(() => Licensing.IssueAsDeputy(new LicenseInfo("m1", "Me", new[] { "*" }), fakeDeputy, today, pub));

        // Deputy rights run out: no new approvals.
        var (_, expired) = Licensing.MakeDeputy("Old deputy", today.AddDays(-1), priv);
        Assert.Throws<InvalidOperationException>(() => Licensing.IssueAsDeputy(new LicenseInfo("o1", "X", new[] { "x" }), expired, today, pub));

        // A deputy can't pass off a licence as the main key's by swapping the deputy part out.
        string withoutDeputy = string.Join('.', key.Split('.').Take(3));
        Assert.Equal(LicenseState.Invalid, Licensing.Check(withoutDeputy, "sam", "OFFICE", "PC1", today, pub).State);
    }

    [Fact]
    public void DeputiesCanRevokeLicencesButNotDeputies()
    {
        var (pub, priv) = Licensing.NewKeyPair();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (dan, danFile) = Licensing.MakeDeputy("Daniel V", null, priv);
        var (amy, amyFile) = Licensing.MakeDeputy("Amy", null, priv);
        string sam = Licensing.Issue(new LicenseInfo("sam1", "Sam", new[] { "sam" }), priv);
        string jo = Licensing.IssueAsDeputy(new LicenseInfo("jo1", "Jo", new[] { "jo" }), amyFile, today, pub);
        (LicenseState, LicenseInfo?, string?) Find(string key, string user, Licensing.RevokedSet r) => Licensing.Find(new[] { key }, r, user, "", "PC", today, pub);

        // Daniel revokes Sam (issued by you) and tries to revoke Amy (a deputy) - only Sam is affected.
        string danList = Licensing.SignRevokedAsDeputy(new Licensing.RevokedList(1, new[] { "sam1", amy.Id }), danFile, pub);
        var r = Licensing.RevokedAll(new[] { danList }, pub);
        Assert.Equal(LicenseState.Revoked, Find(sam, "sam", r).Item1);
        Assert.Equal(LicenseState.Valid, Find(jo, "jo", r).Item1);           // Amy's licences still work

        // Deleting Daniel's shared list doesn't undo it: the PC kept the newest one.
        var again = Licensing.RevokedAll(new[] { null, r.Keep[dan.Id] }, pub);
        Assert.Equal(LicenseState.Revoked, Find(sam, "sam", again).Item1);

        // You revoke Daniel: his revokes stop counting (and nothing he signs counts any more).
        string mainList = Licensing.SignRevoked(new Licensing.RevokedList(1, new[] { dan.Id }), priv);
        var afterDan = Licensing.RevokedAll(new[] { danList, mainList }, pub);
        Assert.Equal(LicenseState.Valid, Find(sam, "sam", afterDan).Item1);

        // A deputy list signed by an outsider's deputy is ignored.
        var (_, otherPriv) = Licensing.NewKeyPair();
        var (_, mallory) = Licensing.MakeDeputy("Mallory", null, otherPriv);
        Assert.Throws<ArgumentException>(() => Licensing.SignRevokedAsDeputy(new Licensing.RevokedList(1, new[] { "sam1" }), mallory, pub));
    }

    [Fact]
    public void ThemeAndOwnCommandsAreKeptPerPerson()
    {
        string defaults = Path.Combine(_folder, "d.json"), user = Path.Combine(_folder, "u.json");
        Directory.CreateDirectory(_folder);
        CiSettings fresh = CiSettings.Load(defaults, user);
        Assert.Equal("mint", fresh.ThemeName); // Slate & Mint unless you pick otherwise
        Assert.Equal("interlock", fresh.LogoName);
        Assert.Equal(CiSettings.DefaultCommands, fresh.MenuCommands);

        var mine = CiSettings.LoadUserOnly(user);
        mine.Theme = "Civil3D";
        mine.Logo = "nodes"; // a retired logo
        mine.Commands = new() { new MenuCommand("Isolate", "LAYISO", "layers") };
        mine.Save(user);
        CiSettings back = CiSettings.Load(defaults, user);
        Assert.Equal("civil3d", back.ThemeName);
        Assert.Equal("interlock", back.LogoName); // falls back to Interlock
        Assert.Equal("mint", new CiSettings { Theme = "old-theme" }.ThemeName); // unknown or retired schemes fall back
        mine.HiddenRooms = new() { "job-241920", Rooms.Private("me", "sam") };
        mine.Save(user);
        Assert.Equal(2, CiSettings.Load(defaults, user).HiddenRooms!.Count); // closed tabs stay closed after a restart
        Assert.Equal(TimeSpan.FromMilliseconds(500), fresh.ChatRefresh);   // fast by default
        Assert.Equal(TimeSpan.FromMilliseconds(200), fresh.LiveRefresh);
        Assert.Equal(TimeSpan.FromMilliseconds(250), new CiSettings { RefreshMs = 10 }.ChatRefresh); // never hammer the share
        Assert.Equal("LAYISO", Assert.Single(back.MenuCommands).Command);
    }

    [Fact]
    public void PrivateChatsAndFriends()
    {
        Assert.Equal("dm-cal--hana", Rooms.Private("Hana", "cal"));
        Assert.Equal(Rooms.Private("hana", "cal"), Rooms.Private("cal", "hana"));
        Assert.Equal("cal", Rooms.OtherPerson("dm-cal--hana", "Hana"));
        Assert.Null(Rooms.OtherPerson("dm-cal--hana", "tom"));
        Assert.True(Rooms.IsValid(Rooms.Private("j.smith", "a_b")));

        // Cal messages Hana privately; Hana's store finds the room without opening it first.
        var cal = new ChatStore(_folder, "cal", "Cal", "PC2");
        cal.Post(Rooms.Private("cal", "hana"), "got a sec?");
        var hana = new ChatStore(_folder, "hana", "Hana", "PC1");
        Assert.Equal(new[] { "dm-cal--hana" }, hana.PrivateRoomsForMe());
        Assert.Empty(new ChatStore(_folder, "tom", "Tom", "PC3").PrivateRoomsForMe());

        string user = Path.Combine(_folder, "me.json");
        var s = new CiSettings();
        Assert.True(s.AddFriend(new Friend("cal", "Cal", "cal@example.com")));
        Assert.False(s.AddFriend(new Friend("CAL", "Callum"))); // already there: renamed, email kept
        s.AddFriend(new Friend("anna", "Anna"));
        s.Save(user);
        CiSettings back = CiSettings.LoadUserOnly(user);
        Assert.Equal(new[] { "Anna", "Callum" }, back.Friends.Select(f => f.Name));
        Assert.Equal("cal@example.com", back.Friends[1].Email);
        Assert.True(back.IsFriend("Cal"));
        Assert.True(back.RemoveFriend("anna"));
        Assert.False(back.IsFriend("anna"));
    }

    [Fact]
    public void FriendsSeeWhatYouAreDoingAndWhenYouWereLastOn()
    {
        var hana = new ChatStore(_folder, "hana", "Hana", "PC1");
        hana.SayOnline(new[] { Rooms.Lobby }, drawing: "sheet9.dwg", liveSession: "1a2b3c4d", email: "hana@example.com", teamsId: "aad-123");
        var cal = new ChatStore(_folder, "cal", "Cal", "PC2");
        OnlinePerson p = Assert.Single(cal.Online(TimeSpan.FromMinutes(1)));
        Assert.Equal("sheet9.dwg", p.Drawing);
        Assert.Equal("1a2b3c4d", p.LiveSession);
        Assert.Equal("aad-123", p.TeamsId);

        hana.SayOffline();
        Assert.Empty(cal.Online(TimeSpan.FromMinutes(1)));
        OnlinePerson last = Assert.Single(cal.LastSeen());
        Assert.True(last.Left);
        Assert.Equal("hana@example.com", last.Email); // still known
        Assert.True(DateTimeOffset.UtcNow - last.Seen < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DraftingSessionFiles()
    {
        var s = new LiveSession(LiveFolder.NewSessionId(), @"\\server\projects\jobs\24\241920\sheet9.dwg", "hana-1a2b", "Hana", DateTimeOffset.UtcNow);
        LiveFolder.WriteSession(_folder, s);
        Assert.Empty(LiveFolder.ActiveSessions(_folder)); // the host hasn't said they're here yet

        string hostDir = LiveFolder.PersonDir(_folder, s.Id, s.HostId);
        LiveFolder.WriteHere(hostDir, new LivePerson(s.HostId, "Hana", "hana", "PC1", true, 10, 20, DateTimeOffset.UtcNow));
        Assert.Equal(s.Id, Assert.Single(LiveFolder.ActiveSessions(_folder)).Id);

        string calDir = LiveFolder.PersonDir(_folder, s.Id, "cal-9f9f");
        LiveFolder.WriteHere(calDir, new LivePerson("cal-9f9f", "Cal", "cal", "PC2", false, 1, 2, DateTimeOffset.UtcNow));
        LivePerson other = Assert.Single(LiveFolder.Others(LiveFolder.SessionDir(_folder, s.Id), s.HostId));
        Assert.Equal("Cal", other.Name);

        LiveFolder.WriteBatch(calDir, new LiveBatch(1, "cal-9f9f", DateTimeOffset.UtcNow, new() { "h:2A" }, new(), new()));
        LiveFolder.WriteBatch(calDir, new LiveBatch(2, "cal-9f9f", DateTimeOffset.UtcNow, new(), new() { "g:abc" }, new()));
        Assert.Equal(new long[] { 1, 2 }, LiveFolder.BatchesAfter(calDir, 0).Select(b => b.Seq));
        Assert.Equal(new long[] { 2 }, LiveFolder.BatchesAfter(calDir, 1).Select(b => b.Seq));
        Assert.Equal("g:abc", Assert.Single(LiveFolder.BatchesAfter(calDir, 1)[0].Erased));

        LiveFolder.WriteSession(_folder, s with { Ended = true });
        Assert.Empty(LiveFolder.ActiveSessions(_folder));
    }
}
