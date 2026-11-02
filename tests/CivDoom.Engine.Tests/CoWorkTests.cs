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
        Assert.Equal("job-241920", Rooms.ForDrawing(@"\\ttw\dfs\ttwcad\jobs\24\241920\civil\sheets\9\a.dwg"));
        Assert.Equal("job-241459", Rooms.ForDrawing(@"Z:\jobs\24\241459\x.dwg"));
        Assert.Null(Rooms.ForDrawing(@"C:\temp\x.dwg"));
        Assert.Equal("Job 241920", Rooms.Title("job-241920"));
        Assert.Equal("Lobby", Rooms.Title(Rooms.Lobby));
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

        new CiSettings { Folder = @"\\ttw\dfs\TTWCADGroups\Civil\_Manage\_Applications\TTWChat", DisplayName = "Hana" }.Save(user);
        s = CiSettings.Load(defaults, user);
        Assert.Null(s.Folder); // refused
        Assert.Equal("Hana", s.Name);
        Assert.False(CiSettings.IsAllowedFolder(@"A:\x\ttwchat\y"));
        Assert.True(CiSettings.IsAllowedFolder(@"\\ttw\dfs\ttwcad\jobs\24\241920\civil\sheets\9\_test script\CoWork-data"));
    }

    [Fact]
    public void ShippedDefaultsPointAtTheTestScriptFolder()
    {
        string repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "CivDoom.sln"))) repo = Path.GetDirectoryName(repo)!;
        string defaults = Path.Combine(repo, "bundle", "CoWork.bundle", "Contents", "cowork.json");
        CiSettings s = CiSettings.Load(defaults, Path.Combine(_folder, "nobody.json"));
        Assert.Equal(@"\\ttw\dfs\ttwcad\jobs\24\241920\civil\sheets\9\_test script\CoWork-data", s.Folder);
        Assert.True(s.Popups);
    }

    [Fact]
    public void ThemeAndOwnCommandsAreKeptPerPerson()
    {
        string defaults = Path.Combine(_folder, "d.json"), user = Path.Combine(_folder, "u.json");
        Directory.CreateDirectory(_folder);
        CiSettings fresh = CiSettings.Load(defaults, user);
        Assert.Equal("ttw", fresh.ThemeName); // TTW colours unless you pick otherwise
        Assert.Equal(CiSettings.DefaultCommands, fresh.MenuCommands);

        var mine = CiSettings.LoadUserOnly(user);
        mine.Theme = "Civil3D";
        mine.Commands = new() { new MenuCommand("Isolate", "LAYISO", "layers") };
        mine.Save(user);
        CiSettings back = CiSettings.Load(defaults, user);
        Assert.Equal("civil3d", back.ThemeName);
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
        Assert.True(s.AddFriend(new Friend("cal", "Cal", "cal@ttw.com.au")));
        Assert.False(s.AddFriend(new Friend("CAL", "Callum"))); // already there: renamed, email kept
        s.AddFriend(new Friend("anna", "Anna"));
        s.Save(user);
        CiSettings back = CiSettings.LoadUserOnly(user);
        Assert.Equal(new[] { "Anna", "Callum" }, back.Friends.Select(f => f.Name));
        Assert.Equal("cal@ttw.com.au", back.Friends[1].Email);
        Assert.True(back.IsFriend("Cal"));
        Assert.True(back.RemoveFriend("anna"));
        Assert.False(back.IsFriend("anna"));
    }

    [Fact]
    public void FriendsSeeWhatYouAreDoingAndWhenYouWereLastOn()
    {
        var hana = new ChatStore(_folder, "hana", "Hana", "PC1");
        hana.SayOnline(new[] { Rooms.Lobby }, drawing: "sheet9.dwg", liveSession: "1a2b3c4d", email: "hana@ttw.com.au", teamsId: "aad-123");
        var cal = new ChatStore(_folder, "cal", "Cal", "PC2");
        OnlinePerson p = Assert.Single(cal.Online(TimeSpan.FromMinutes(1)));
        Assert.Equal("sheet9.dwg", p.Drawing);
        Assert.Equal("1a2b3c4d", p.LiveSession);
        Assert.Equal("aad-123", p.TeamsId);

        hana.SayOffline();
        Assert.Empty(cal.Online(TimeSpan.FromMinutes(1)));
        OnlinePerson last = Assert.Single(cal.LastSeen());
        Assert.True(last.Left);
        Assert.Equal("hana@ttw.com.au", last.Email); // still known
        Assert.True(DateTimeOffset.UtcNow - last.Seen < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DraftingSessionFiles()
    {
        var s = new LiveSession(LiveFolder.NewSessionId(), @"\\ttw\dfs\ttwcad\jobs\24\241920\sheet9.dwg", "hana-1a2b", "Hana", DateTimeOffset.UtcNow);
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
