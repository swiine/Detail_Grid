using System;
using CoWork.Core;

namespace CoWork
{
    /// <summary>
    /// Server mode. When cowork.json has "server", CoWork connects to the CoWork server and uses it to hear about
    /// changes the moment they happen and to pass crosshairs/locks live. The shared folder is still the record of
    /// everything, so if the server is off CoWork carries on through the folder (just with its usual refresh rate).
    /// </summary>
    internal static class ServerLink
    {
        private static ServerClient? _client;

        public static bool Configured => !string.IsNullOrWhiteSpace(ChatHub.Settings.Server);
        public static bool Connected => _client?.Connected == true;
        public static string? Refused => _client?.Denied;

        /// <summary>Connected or disconnected (any thread).</summary>
        public static event Action? StateChanged;

        /// <summary>A line for the Settings page.</summary>
        public static string Status =>
            !Configured ? "Shared folder only (no server set)"
            : Connected ? $"Connected to {ChatHub.Settings.Server} - live updates on"
            : Refused != null ? $"Server {ChatHub.Settings.Server} refused: {Refused}"
            : $"Can't reach {ChatHub.Settings.Server} - using the shared folder";

        public static void Start()
        {
            Stop();
            if (!Configured) return;
            var c = new ServerClient(ChatHub.Settings.Server!, () => new ServerClient.Hello(
                Environment.UserName, ChatHub.Settings.Name, Environment.MachineName, Environment.UserDomainName,
                LicenceGate.Key ?? "", Plugin.Version));
            c.StateChanged += up =>
            {
                Log.Info(up ? $"Connected to CoWork server {ChatHub.Settings.Server}" : "Lost the CoWork server - using the shared folder");
                ChatHub.Retime();
                LiveDrafting.Retime();
                ChatHub.Nudge(); // catch up on anything missed
                StateChanged?.Invoke();
            };
            c.Refused += why => { Log.Info("CoWork server refused this PC: " + why); StateChanged?.Invoke(); };
            c.Poked += topic =>
            {
                if (topic.StartsWith("live:", StringComparison.Ordinal)) LiveDrafting.Nudge(topic.Substring(5));
                else ChatHub.Nudge();
            };
            c.Online += _ => ChatHub.Nudge();
            c.Here += LiveDrafting.FromServer;
            _client = c;
            c.Start();
        }

        public static void Stop()
        {
            ServerClient? c = _client;
            _client = null;
            c?.Dispose();
        }

        /// <summary>Tell everyone something changed ("chat:room", "pins:room", "online", "live:session").</summary>
        public static void Poke(string topic) => _client?.Poke(topic);

        public static void SendHere(string session, LivePerson me) => _client?.SendHere(session, LiveFolder.PersonJson(me));
    }
}
