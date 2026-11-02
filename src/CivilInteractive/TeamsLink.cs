using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CivilInteractive.Core;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilInteractive
{
    /// <summary>
    /// The Teams link: sign in with your work Microsoft account, see friends' Teams status, and chat with them in
    /// Teams from the palette (both ways). Off until IT sets the tenant and app ids in civilinteractive.json.
    /// Signing in uses Microsoft's own sign-in page in your browser; the sign-in is then remembered in an
    /// encrypted file only your Windows account can read (%LOCALAPPDATA%\CivilInteractive\teams.cache).
    /// </summary>
    public static class TeamsLink
    {
        private static readonly TimeSpan PresenceEvery = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MessagesEvery = TimeSpan.FromSeconds(6);

        private static Dispatcher? _ui;
        private static IPublicClientApplication? _app;
        private static TeamsGraph? _graph;
        private static Timer? _timer;
        private static int _busy;
        private static DateTime _nextPresence = DateTime.MinValue, _pausedUntil = DateTime.MinValue;
        private static readonly ConcurrentDictionary<string, TeamsPresence> _presence = new();   // Teams user id -> status
        private static readonly ConcurrentDictionary<string, string> _idsByEmail = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsSetUp => ChatHub.Settings.Teams.IsSetUp;
        public static TeamsUser? Me { get; private set; }
        public static bool SignedIn => Me != null;
        public static string? Problem { get; private set; }

        /// <summary>Sign-in state or friends' statuses changed (raised on the UI thread).</summary>
        public static event Action? Changed;

        private sealed class MsalTokens : ITokenSource
        {
            public async Task<string> GetTokenAsync(CancellationToken ct)
            {
                IPublicClientApplication app = _app ?? throw new InvalidOperationException("Not signed in to Teams");
                IAccount? account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
                if (account == null) throw new InvalidOperationException("Not signed in to Teams");
                AuthenticationResult r = await app.AcquireTokenSilent(TeamsGraph.Scopes, account).ExecuteAsync(ct).ConfigureAwait(false);
                return r.AccessToken;
            }
        }

        /// <summary>Called at startup: quietly signs back in if you signed in before.</summary>
        public static void Start()
        {
            _ui = Dispatcher.CurrentDispatcher;
            if (!IsSetUp) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureApp().ConfigureAwait(false);
                    if ((await _app!.GetAccountsAsync().ConfigureAwait(false)).Any()) await AfterSignIn().ConfigureAwait(false);
                }
                catch (Exception ex) { SetProblem("Teams: " + ex.Message); }
            });
        }

        private static async Task EnsureApp()
        {
            if (_app != null) return;
            TeamsSettings t = ChatHub.Settings.Teams;
            _app = PublicClientApplicationBuilder.Create(t.ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, t.TenantId)
                .WithRedirectUri("http://localhost")
                .Build();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CivilInteractive");
            Directory.CreateDirectory(dir);
            var storage = new StorageCreationPropertiesBuilder("teams.cache", dir).Build(); // encrypted for this Windows user
            MsalCacheHelper cache = await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);
            cache.RegisterCache(_app.UserTokenCache);
            _graph = new TeamsGraph(new MsalTokens());
        }

        /// <summary>Signs in with Microsoft's sign-in page (opens in your browser).</summary>
        public static async Task SignInAsync()
        {
            if (!IsSetUp) { SetProblem("The Teams link isn't switched on yet (waiting for IT)."); return; }
            try
            {
                await EnsureApp().ConfigureAwait(true);
                IntPtr owner = AcApp.MainWindow.Handle;
                await _app!.AcquireTokenInteractive(TeamsGraph.Scopes)
                    .WithParentActivityOrWindow(owner)
                    .WithUseEmbeddedWebView(false)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync().ConfigureAwait(true);
                await AfterSignIn().ConfigureAwait(true);
            }
            catch (MsalException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
            {
                SetProblem(null);
            }
            catch (Exception ex)
            {
                SetProblem("Teams sign-in didn't work: " + ex.Message);
            }
        }

        public static async Task SignOutAsync()
        {
            _timer?.Dispose();
            _timer = null;
            if (_app != null)
                foreach (IAccount a in await _app.GetAccountsAsync().ConfigureAwait(true)) await _app.RemoveAsync(a).ConfigureAwait(true);
            Me = null;
            _presence.Clear();
            Ui(() => Changed?.Invoke());
        }

        private static async Task AfterSignIn()
        {
            Me = await _graph!.MeAsync().ConfigureAwait(false);
            SetProblem(null);
            _nextPresence = DateTime.MinValue;
            _timer?.Dispose();
            _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), MessagesEvery);
            Ui(() => Changed?.Invoke());
        }

        // ------------------------------------------------------------------ friends

        /// <summary>A friend's Teams status ("Available", "In a meeting"...), or null if not known.</summary>
        public static string? StatusOf(Friend f)
        {
            string? id = TeamsIdOf(f);
            return id != null && _presence.TryGetValue(id, out TeamsPresence? p) ? p.Label : null;
        }

        /// <summary>Their Teams id: from their own CivilInteractive (if they signed in), else looked up by email.</summary>
        private static string? TeamsIdOf(Friend f)
        {
            OnlinePerson? seen = ChatHub.LastSeenOf(f.User);
            if (!string.IsNullOrEmpty(seen?.TeamsId)) return seen!.TeamsId;
            string? email = seen?.Email ?? f.Email;
            return email != null && _idsByEmail.TryGetValue(email, out string? id) ? id : null;
        }

        private static async Task<string?> ResolveAsync(Friend f)
        {
            if (TeamsIdOf(f) is { } id) return id;
            string? email = ChatHub.LastSeenOf(f.User)?.Email ?? f.Email;
            if (email == null || _graph == null) return null;
            TeamsUser? u = await _graph.FindUserAsync(email).ConfigureAwait(false);
            if (u == null) return null;
            _idsByEmail[email] = u.Id;
            return u.Id;
        }

        /// <summary>Opens (or starts) your Teams chat with a friend as a room in the palette.</summary>
        public static async Task<Room?> OpenChatAsync(Friend f)
        {
            if (!SignedIn || _graph == null) { SetProblem("Sign in to Teams first."); return null; }
            try
            {
                string? theirId = await ResolveAsync(f).ConfigureAwait(true);
                if (theirId == null) { SetProblem($"Couldn't find {f.Name} in Teams (no work email known yet)."); return null; }
                string chatId = await _graph.OneOnOneChatAsync(Me!.Id, theirId).ConfigureAwait(true);
                Room room = ChatHub.AddTeamsRoom(f, chatId);
                await PollRoom(room).ConfigureAwait(true);
                return room;
            }
            catch (Exception ex)
            {
                SetProblem("Teams: " + ex.Message);
                return null;
            }
        }

        /// <summary>Sends a message in a Teams room.</summary>
        public static void Send(Room room, string text)
        {
            if (_graph == null || room.TeamsChatId == null) return;
            string chat = room.TeamsChatId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _graph.SendAsync(chat, text).ConfigureAwait(false);
                    await PollRoom(room).ConfigureAwait(false);
                }
                catch (Exception ex) { SetProblem("Teams: message not sent - " + ex.Message); }
            });
        }

        // ------------------------------------------------------------------ background

        private static void Poll()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (DateTime.UtcNow < _pausedUntil || _graph == null || Me == null) return;
                    foreach (Room r in ChatHub.Rooms.Where(r => r.TeamsChatId != null).ToList()) await PollRoom(r).ConfigureAwait(false);
                    if (DateTime.UtcNow >= _nextPresence)
                    {
                        _nextPresence = DateTime.UtcNow + PresenceEvery;
                        var ids = new List<string>();
                        foreach (Friend f in ChatHub.Settings.Friends)
                            if (await ResolveAsync(f).ConfigureAwait(false) is { } id) ids.Add(id);
                        foreach (TeamsPresence p in await _graph.PresenceAsync(ids).ConfigureAwait(false)) _presence[p.UserId] = p;
                        Ui(() => Changed?.Invoke());
                    }
                    if (Problem != null) SetProblem(null);
                }
                catch (TeamsThrottledException ex) { _pausedUntil = DateTime.UtcNow + ex.RetryAfter; }
                catch (MsalUiRequiredException) { Me = null; SetProblem("Teams sign-in expired - click Sign in to Teams."); }
                catch (Exception ex) { SetProblem("Teams: " + ex.Message); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        private static async Task PollRoom(Room room)
        {
            if (_graph == null || room.TeamsChatId == null || Me == null) return;
            List<TeamsMessage> msgs = await _graph.MessagesAsync(room.TeamsChatId).ConfigureAwait(false);
            string myLogin = Environment.UserName;
            var mapped = msgs.Select(m => new ChatMessage("t:" + m.Id, room.Id, m.FromId == Me.Id ? myLogin : "teams:" + m.FromId,
                m.FromId == Me.Id ? ChatHub.Settings.Name : m.FromName, m.Time, m.Text)).ToList();
            Ui(() => ChatHub.AddMessages(room, mapped));
        }

        private static void SetProblem(string? p)
        {
            Problem = p;
            Ui(() => Changed?.Invoke());
        }

        private static void Ui(Action a) => _ui?.BeginInvoke(new Action(() => { try { a(); } catch { } }));
    }
}
