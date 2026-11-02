using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CivilInteractive.Core;

namespace CivDoom.Engine.Tests;

/// <summary>The Teams client against a fake Microsoft Graph that answers like the documented API.</summary>
public class TeamsGraphTests
{
    private sealed class Token : ITokenSource
    {
        public Task<string> GetTokenAsync(CancellationToken ct) => Task.FromResult("test-token");
    }

    /// <summary>Records each request and answers from a table of (method + path) -> (status, json).</summary>
    private sealed class FakeGraph : HttpMessageHandler
    {
        public readonly List<(string Method, string Path, string? Body, string? Auth)> Calls = new();
        public readonly Dictionary<string, (HttpStatusCode Status, string Json)> Answers = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.PathAndQuery.Replace("/v1.0", "");
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, Uri.UnescapeDataString(path), body, request.Headers.Authorization?.ToString()));
            string key = request.Method.Method + " " + Uri.UnescapeDataString(path);
            var (status, json) = Answers.TryGetValue(key, out var a) ? a : (HttpStatusCode.NotFound, "{\"error\":{\"message\":\"no route " + key + "\"}}");
            var res = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if ((int)status == 429) res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return res;
        }
    }

    private static (TeamsGraph, FakeGraph) Make()
    {
        var fake = new FakeGraph();
        return (new TeamsGraph(new Token(), fake, "https://graph.test/v1.0"), fake);
    }

    [Fact]
    public async Task WhoAmIAndFindingColleagues()
    {
        var (g, fake) = Make();
        fake.Answers["GET /me?$select=id,displayName,mail,userPrincipalName"] = (HttpStatusCode.OK,
            "{\"id\":\"me-1\",\"displayName\":\"Hana Lee\",\"mail\":\"hana@ttw.com.au\",\"userPrincipalName\":\"hana@ttw.com.au\"}");
        fake.Answers["GET /users/cal@ttw.com.au?$select=id,displayName,mail,userPrincipalName"] = (HttpStatusCode.OK,
            "{\"id\":\"cal-2\",\"displayName\":\"Cal Smith\",\"mail\":null,\"userPrincipalName\":\"cal@ttw.com.au\"}");

        TeamsUser me = await g.MeAsync();
        Assert.Equal(new TeamsUser("me-1", "Hana Lee", "hana@ttw.com.au"), me);
        TeamsUser? cal = await g.FindUserAsync("cal@ttw.com.au");
        Assert.Equal("cal@ttw.com.au", cal!.Email); // falls back to the sign-in name
        Assert.Null(await g.FindUserAsync("nobody@ttw.com.au"));
        Assert.All(fake.Calls, c => Assert.Equal("Bearer test-token", c.Auth));
    }

    [Fact]
    public async Task TeamsStatus()
    {
        var (g, fake) = Make();
        fake.Answers["POST /communications/getPresencesByUserId"] = (HttpStatusCode.OK,
            "{\"value\":[{\"id\":\"cal-2\",\"availability\":\"Busy\",\"activity\":\"InAMeeting\"},{\"id\":\"anna-3\",\"availability\":\"Available\",\"activity\":\"Available\"}]}");
        List<TeamsPresence> p = await g.PresenceAsync(new[] { "cal-2", "anna-3", "cal-2" });
        Assert.Equal("In a meeting", p.Single(x => x.UserId == "cal-2").Label);
        Assert.Equal("Available", p.Single(x => x.UserId == "anna-3").Label);
        JsonNode sent = JsonNode.Parse(fake.Calls.Single().Body!)!;
        Assert.Equal(2, sent["ids"]!.AsArray().Count); // duplicates dropped
        Assert.Empty(await g.PresenceAsync(Array.Empty<string>()));
        Assert.Equal("Do not disturb", new TeamsPresence("x", "DoNotDisturb", "DoNotDisturb").Label);
        Assert.Equal("Presenting", new TeamsPresence("x", "DoNotDisturb", "Presenting").Label);
        Assert.Equal("Away", new TeamsPresence("x", "Away", "Away").Label);
    }

    [Fact]
    public async Task OneOnOneChatAndMessages()
    {
        var (g, fake) = Make();
        fake.Answers["POST /chats"] = (HttpStatusCode.Created, "{\"id\":\"19:abc_def@unq.gbl.spaces\",\"chatType\":\"oneOnOne\"}");
        fake.Answers["GET /chats/19:abc_def@unq.gbl.spaces/messages?$top=30"] = (HttpStatusCode.OK, """
            {"value":[
              {"id":"3","messageType":"message","createdDateTime":"2026-11-02T01:03:00Z","from":{"user":{"id":"cal-2","displayName":"Cal Smith"}},
               "body":{"contentType":"html","content":"<p>Sure &amp; I'll check<br>sheet 9</p>"}},
              {"id":"2","messageType":"systemEventMessage","createdDateTime":"2026-11-02T01:02:00Z","from":null,"body":{"contentType":"html","content":""}},
              {"id":"1","messageType":"message","createdDateTime":"2026-11-02T01:01:00Z","from":{"user":{"id":"me-1","displayName":"Hana Lee"}},
               "body":{"contentType":"text","content":"can you check the kerb?"}}
            ]}
            """);
        fake.Answers["POST /chats/19:abc_def@unq.gbl.spaces/messages"] = (HttpStatusCode.Created, "{\"id\":\"4\"}");

        string chat = await g.OneOnOneChatAsync("me-1", "cal-2");
        Assert.Equal("19:abc_def@unq.gbl.spaces", chat);
        JsonNode create = JsonNode.Parse(fake.Calls[0].Body!)!;
        Assert.Equal("oneOnOne", (string)create["chatType"]!);
        Assert.Equal("https://graph.microsoft.com/v1.0/users('cal-2')", (string)create["members"]![1]!["user@odata.bind"]!);

        List<TeamsMessage> msgs = await g.MessagesAsync(chat);
        Assert.Equal(new[] { "can you check the kerb?", "Sure & I'll check\nsheet 9" }, msgs.Select(m => m.Text)); // oldest first, no system notice
        Assert.Equal("Cal Smith", msgs[1].FromName);

        await g.SendAsync(chat, "thanks!");
        JsonNode sent = JsonNode.Parse(fake.Calls.Last().Body!)!;
        Assert.Equal("text", (string)sent["body"]!["contentType"]!);
        Assert.Equal("thanks!", (string)sent["body"]!["content"]!);
    }

    [Fact]
    public async Task ErrorsAndSlowDowns()
    {
        var (g, fake) = Make();
        fake.Answers["GET /me?$select=id,displayName,mail,userPrincipalName"] = ((HttpStatusCode)429, "{}");
        var slow = await Assert.ThrowsAsync<TeamsThrottledException>(() => g.MeAsync());
        Assert.Equal(TimeSpan.FromSeconds(7), slow.RetryAfter);

        fake.Answers["GET /me?$select=id,displayName,mail,userPrincipalName"] = (HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Missing scope Presence.Read.All\"}}");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => g.MeAsync());
        Assert.Contains("Missing scope Presence.Read.All", ex.Message);
    }

    [Fact]
    public void TeamsLinkIsOffUntilItIsSetUp()
    {
        Assert.False(new TeamsSettings().IsSetUp);
        Assert.False(new TeamsSettings { TenantId = "t" }.IsSetUp);
        Assert.True(new TeamsSettings { TenantId = "t", ClientId = "c" }.IsSetUp);
        Assert.Equal(new[] { "User.Read", "User.ReadBasic.All", "Presence.Read.All", "Chat.ReadWrite" }, TeamsGraph.Scopes);
        Assert.Equal("Hello\nworld", TeamsGraph.HtmlToText("<div>Hello</div><div>world</div>"));
    }
}
