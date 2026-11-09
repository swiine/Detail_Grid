using System;
using System.Net;
using System.Threading.Tasks;
using CoWork.Core;

namespace CoWork
{
    /// <summary>
    /// The fast lane for a CoWork session. With an office server, the session uses it. Without one, the host's CoWork
    /// starts a small server inside Civil 3D when the session starts (its address goes in session.json) and guests
    /// connect to it - nothing to install or run. Changes, crosshairs and locks then go straight from PC to PC; the
    /// shared folder is still written as the record, and used on its own if the fast lane can't connect (a firewall,
    /// say).
    /// </summary>
    internal static class SessionChannel
    {
        public const int Port = 5392;

        private static CoWorkServer? _hosted;
        private static ServerClient? _own;
        private static string _session = "";
        private static string? _address;

        private static ServerClient? Client => ServerLink.Client is { Connected: true } office ? office : _own;

        /// <summary>Changes and crosshairs are going PC to PC right now.</summary>
        public static bool Fast => Client?.Connected == true;

        public static string Status =>
            _session.Length == 0 ? "Not in a session"
            : ServerLink.Client is { Connected: true } ? "Instant (office server)"
            : _own?.Connected == true ? (_hosted != null ? "Instant (this PC is the session server)" : $"Instant (via {_address})")
            : _address != null ? $"Shared folder only - can't reach {_address} (firewall?)" : "Shared folder only";

        /// <summary>
        /// Host: start the session's own server on this PC, unless the office server is connected. Returns its address
        /// ("PC:port") to put in session.json, or null.
        /// </summary>
        public static string? StartHosting()
        {
            if (ServerLink.Client is { Connected: true }) return null;
            StopHosting();
            foreach (int port in new[] { Port, 0 })
            {
                try
                {
                    var server = new CoWorkServer(port, ChatHub.DataFolder, null, msg => Log.Info("Session server: " + msg), IPAddress.Any);
                    server.Start();
                    _hosted = server;
                    Log.Info($"Started the session server on port {server.Port}");
                    return $"{Environment.MachineName}:{server.Port}";
                }
                catch (Exception ex) { Log.Swallowed(ex); }
            }
            return null;
        }

        /// <summary>Stops this PC's session server (after a delay, so a hand-over can move everyone across first).</summary>
        public static void StopHosting(TimeSpan? after = null)
        {
            CoWorkServer? s = _hosted;
            _hosted = null;
            if (s == null) return;
            if (after is { } wait) Task.Delay(wait).ContinueWith(_ => s.Dispose());
            else s.Dispose();
        }

        /// <summary>Join the fast lane for a session (the office server if connected, else the session's own server).</summary>
        public static void Enter(LiveSession s)
        {
            _session = s.Id;
            string? address = _hosted != null && s.Server != null && s.Server.StartsWith(Environment.MachineName + ":", StringComparison.OrdinalIgnoreCase)
                ? "127.0.0.1:" + _hosted.Port : s.Server;
            if (address != _address || _own == null)
            {
                _own?.Dispose();
                _own = null;
                _address = address;
                if (address != null)
                {
                    var c = new ServerClient(address, () => new ServerClient.Hello(Environment.UserName, ChatHub.Settings.Name, Environment.MachineName,
                        Environment.UserDomainName, LicenceGate.Key ?? "", Plugin.Version));
                    c.Here += LiveDrafting.FromServer;
                    c.Batch += LiveDrafting.FromServerBatch;
                    c.Poked += topic => { if (topic.StartsWith("live:", StringComparison.Ordinal)) LiveDrafting.Nudge(topic.Substring(5)); };
                    c.StateChanged += _ => Changed();
                    _own = c;
                    c.Start();
                }
            }
            _own?.InSession(s.Id);
            ServerLink.Client?.InSession(s.Id);
            Changed();
        }

        /// <summary>Leave the session's fast lane.</summary>
        public static void Exit()
        {
            _session = "";
            ServerLink.Client?.InSession("");
            _own?.Dispose();
            _own = null;
            _address = null;
            Changed();
        }

        /// <summary>The connection came up or went down.</summary>
        public static void Changed() => LiveDrafting.Retime();

        public static void Poke(string topic)
        {
            ServerLink.Poke(topic);
            if (Client != ServerLink.Client) _own?.Poke(topic);
        }

        public static void SendHere(string session, LivePerson me) => Client?.SendHere(session, LiveFolder.PersonJson(me));

        /// <summary>Sends a set of changes straight to the others. False if it has to go through the folder only.</summary>
        public static bool SendBatch(string session, string person, LiveBatch b, byte[]? dwg) =>
            Client?.SendBatch(session, person, LiveFolder.BatchJson(b), dwg) == true;
    }
}
