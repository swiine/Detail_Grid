using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CoWork.Core;

/// <summary>Gets a Microsoft Graph access token for the signed-in person (the plugin uses MSAL for this).</summary>
public interface ITokenSource
{
    Task<string> GetTokenAsync(CancellationToken ct);
}

public sealed record TeamsUser(string Id, string Name, string? Email);

/// <summary>Teams status: Availability is Available / Busy / DoNotDisturb / BeRightBack / Away / Offline / PresenceUnknown.</summary>
public sealed record TeamsPresence(string UserId, string Availability, string Activity)
{
    /// <summary>How it's shown: "Available", "Busy", "In a meeting", "Do not disturb", "Away", "Offline".</summary>
    public string Label => Activity switch
    {
        "InAMeeting" => "In a meeting",
        "InACall" or "InAConferenceCall" => "In a call",
        "Presenting" => "Presenting",
        _ => Availability switch
        {
            "Available" or "AvailableIdle" => "Available",
            "Busy" or "BusyIdle" => "Busy",
            "DoNotDisturb" => "Do not disturb",
            "BeRightBack" => "Be right back",
            "Away" => "Away",
            "Offline" => "Offline",
            _ => "Unknown",
        },
    };
}

public sealed record TeamsMessage(string Id, string FromId, string FromName, DateTimeOffset Time, string Text);

/// <summary>Teams said "slow down": try again after <see cref="RetryAfter"/>.</summary>
public sealed class TeamsThrottledException : Exception
{
    public TeamsThrottledException(TimeSpan retryAfter) : base("Teams asked us to slow down") => RetryAfter = retryAfter;
    public TimeSpan RetryAfter { get; }
}

/// <summary>
/// The few Microsoft Graph calls CoWork's Teams link uses, all as the signed-in person (delegated):
/// who am I, find a colleague, their Teams status, the one-on-one chat with them, and reading / sending its messages.
/// </summary>
public sealed class TeamsGraph : IDisposable
{
    public const string GraphBase = "https://graph.microsoft.com/v1.0";

    /// <summary>The delegated permissions it needs (what IT approves).</summary>
    public static readonly string[] Scopes =
    {
        "User.Read",            // who you are
        "User.ReadBasic.All",   // find a colleague's Teams account from their email
        "Presence.Read.All",    // colleagues' Teams status
        "Chat.ReadWrite",       // open your one-on-one chats, read them and send messages
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ITokenSource _tokens;
    private readonly HttpClient _http;
    private readonly string _base;

    public TeamsGraph(ITokenSource tokens, HttpMessageHandler? handler = null, string baseUrl = GraphBase)
    {
        _tokens = tokens;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
        _base = baseUrl.TrimEnd('/');
    }

    public async Task<TeamsUser> MeAsync(CancellationToken ct = default)
    {
        JsonNode me = await SendAsync(HttpMethod.Get, "/me?$select=id,displayName,mail,userPrincipalName", null, ct).ConfigureAwait(false);
        return ToUser(me);
    }

    /// <summary>A colleague by email or sign-in name, or null if there's no such account.</summary>
    public async Task<TeamsUser?> FindUserAsync(string email, CancellationToken ct = default)
    {
        try
        {
            JsonNode u = await SendAsync(HttpMethod.Get, $"/users/{Uri.EscapeDataString(email)}?$select=id,displayName,mail,userPrincipalName", null, ct).ConfigureAwait(false);
            return ToUser(u);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static TeamsUser ToUser(JsonNode u) => new(
        (string)u["id"]!, (string?)u["displayName"] ?? "", (string?)u["mail"] ?? (string?)u["userPrincipalName"]);

    /// <summary>Teams status for up to 650 people at once.</summary>
    public async Task<List<TeamsPresence>> PresenceAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        string[] ids = userIds.Distinct().Take(650).ToArray();
        if (ids.Length == 0) return new();
        JsonNode r = await SendAsync(HttpMethod.Post, "/communications/getPresencesByUserId", new JsonObject { ["ids"] = new JsonArray(ids.Select(i => (JsonNode)i!).ToArray()) }, ct).ConfigureAwait(false);
        return r["value"]!.AsArray().Select(p => new TeamsPresence((string)p!["id"]!, (string?)p["availability"] ?? "PresenceUnknown", (string?)p["activity"] ?? "")).ToList();
    }

    /// <summary>The one-on-one Teams chat between you and a colleague (Teams returns the existing one, or starts it).</summary>
    public async Task<string> OneOnOneChatAsync(string myId, string theirId, CancellationToken ct = default)
    {
        JsonObject Member(string id) => new()
        {
            ["@odata.type"] = "#microsoft.graph.aadUserConversationMember",
            ["roles"] = new JsonArray("owner"),
            ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{id}')",
        };
        var body = new JsonObject { ["chatType"] = "oneOnOne", ["members"] = new JsonArray(Member(myId), Member(theirId)) };
        JsonNode chat = await SendAsync(HttpMethod.Post, "/chats", body, ct).ConfigureAwait(false);
        return (string)chat["id"]!;
    }

    /// <summary>The latest messages in a chat, oldest first (system notices like "X added Y" are left out).</summary>
    public async Task<List<TeamsMessage>> MessagesAsync(string chatId, int top = 30, CancellationToken ct = default)
    {
        JsonNode r = await SendAsync(HttpMethod.Get, $"/chats/{Uri.EscapeDataString(chatId)}/messages?$top={Math.Clamp(top, 1, 50)}", null, ct).ConfigureAwait(false);
        var result = new List<TeamsMessage>();
        foreach (JsonNode? m in r["value"]!.AsArray())
        {
            if (m == null || (string?)m["messageType"] != "message" || m["deletedDateTime"] is JsonValue) continue;
            JsonNode? from = m["from"]?["user"];
            if (from == null) continue;
            string content = (string?)m["body"]?["content"] ?? "";
            string text = (string?)m["body"]?["contentType"] == "html" ? HtmlToText(content) : content;
            result.Add(new TeamsMessage((string)m["id"]!, (string?)from["id"] ?? "", (string?)from["displayName"] ?? "",
                DateTimeOffset.Parse((string)m["createdDateTime"]!, System.Globalization.CultureInfo.InvariantCulture), text));
        }
        return result.OrderBy(m => m.Time).ToList();
    }

    public async Task SendAsync(string chatId, string text, CancellationToken ct = default)
    {
        var body = new JsonObject { ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = text } };
        await SendAsync(HttpMethod.Post, $"/chats/{Uri.EscapeDataString(chatId)}/messages", body, ct).ConfigureAwait(false);
    }

    /// <summary>Teams message HTML as plain text (line breaks kept, tags and entities removed).</summary>
    public static string HtmlToText(string html)
    {
        string s = Regex.Replace(html, @"<\s*br\s*/?\s*>|</\s*(p|div|li)\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, _base + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(ct).ConfigureAwait(false));
        if (body != null) req.Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json");
        using HttpResponseMessage res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if ((int)res.StatusCode == 429)
            throw new TeamsThrottledException(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
        string text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            string detail = "";
            try { detail = (string?)JsonNode.Parse(text)?["error"]?["message"] ?? ""; } catch (JsonException) { }
            throw new HttpRequestException($"Teams said {(int)res.StatusCode} {res.ReasonPhrase}{(detail.Length > 0 ? ": " + detail : "")}", null, res.StatusCode);
        }
        return text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
    }

    public void Dispose() => _http.Dispose();
}
