using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DoomChat
{
    /// <summary>
    /// Serverless chat over a shared network folder.
    ///
    ///   &lt;root&gt;\channels\&lt;channel&gt;\&lt;user&gt;.jsonl   each person appends only to their own file (no locking
    ///                                            conflicts, safe with DFS replication)
    ///   &lt;root&gt;\presence\&lt;user&gt;.json              heartbeat every 20 s: who is online and in which channels
    ///
    /// Every client polls the folder (default every 1.5 s) and reads only the bytes added since the last poll.
    /// </summary>
    public sealed class FolderChatClient : IChatTransport
    {
        static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        const int HistoryCount = 200;
        const long HistoryTailBytes = 256 * 1024;   // read at most the last 256 KB of each person's file on join
        static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);
        static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
        static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(70);

        readonly string _root, _user, _display, _machine, _fileUser;
        readonly object _lock = new();
        readonly HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);   // file path -> bytes read
        readonly Dictionary<string, string> _presenceSig = new(StringComparer.OrdinalIgnoreCase);
        readonly CancellationTokenSource _cts = new();
        DateTime _lastHeartbeat = DateTime.MinValue;
        ConnectionState _state = ConnectionState.Disconnected;
        long _lastId;

        public event Action<ChatMessage>? MessageReceived;
        public event Action<string, List<ChatMessage>>? HistoryReceived;
        public event Action<string, List<ChatUser>>? PresenceReceived;
        public event Action<ConnectionState, string?>? StateChanged;

        public FolderChatClient(string root, string user, string display, string machine)
        {
            _root = root;
            _user = user; _display = display; _machine = machine;
            _fileUser = SafeName(user);
        }

        static string SafeName(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            var r = new string(s.ToLowerInvariant().Select(c => bad.Contains(c) ? '_' : c).ToArray());
            return r.Length == 0 ? "_" : r;
        }

        string ChannelDir(string ch) => Path.Combine(_root, "channels", ch);
        string PresenceDir => Path.Combine(_root, "presence");
        string MyPresenceFile => Path.Combine(PresenceDir, _fileUser + ".json");

        public void Start() => Task.Run(RunAsync);

        public void Join(string channel)
        {
            lock (_lock) _channels.Add(channel);
            _lastHeartbeat = DateTime.MinValue; // publish new channel list soon
        }

        public Task<bool> SendMessageAsync(string channel, string text, string? drawing)
        {
            return Task.Run(() =>
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var id = now.UtcTicks;
                    lock (_lock) { if (id <= _lastId) id = _lastId + 1; _lastId = id; }
                    var msg = new ChatMessage { Id = id, Channel = channel, User = _user, Display = _display, Ts = now, Text = text, Drawing = drawing };
                    var dir = ChannelDir(channel);
                    Directory.CreateDirectory(dir);
                    var line = JsonSerializer.Serialize(msg, Json) + "\n";
                    var file = Path.Combine(dir, _fileUser + ".jsonl");
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                            var bytes = Encoding.UTF8.GetBytes(line);
                            fs.Write(bytes, 0, bytes.Length);
                            break;
                        }
                        catch (IOException) when (attempt < 5) { Thread.Sleep(200); }
                    }
                    MessageReceived?.Invoke(msg); // show it straight away; the poller de-duplicates by Id
                    return true;
                }
                catch (Exception ex)
                {
                    SetState(ConnectionState.Disconnected, "Can't write to chat folder: " + ex.Message);
                    return false;
                }
            });
        }

        async Task RunAsync()
        {
            SetState(ConnectionState.Connecting, null);
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (!Directory.Exists(_root)) throw new DirectoryNotFoundException("Chat folder not found: " + _root);
                    Directory.CreateDirectory(PresenceDir);
                    if (DateTime.UtcNow - _lastHeartbeat > HeartbeatInterval) WriteHeartbeat();
                    string[] chans;
                    lock (_lock) chans = _channels.ToArray();
                    foreach (var ch in chans)
                    {
                        lock (_lock) { if (!_loaded.Contains(ch)) { LoadHistory(ch); _loaded.Add(ch); continue; } }
                        PollChannel(ch);
                    }
                    PollPresence(chans);
                    SetState(ConnectionState.Connected, null);
                }
                catch (Exception ex)
                {
                    SetState(ConnectionState.Disconnected, ex is UnauthorizedAccessException
                        ? "No permission to use the chat folder" : ex.Message);
                }
                try { await Task.Delay(_state == ConnectionState.Connected ? PollInterval : TimeSpan.FromSeconds(10), _cts.Token).ConfigureAwait(false); }
                catch { break; }
            }
        }

        void SetState(ConnectionState st, string? err)
        {
            if (st == _state && st == ConnectionState.Connected) return;
            _state = st;
            StateChanged?.Invoke(st, err);
        }

        void WriteHeartbeat()
        {
            string[] chans;
            lock (_lock) chans = _channels.ToArray();
            var json = JsonSerializer.Serialize(new { user = _user, display = _display, machine = _machine, ts = DateTimeOffset.UtcNow, channels = chans }, Json);
            File.WriteAllText(MyPresenceFile, json);
            _lastHeartbeat = DateTime.UtcNow;
        }

        void LoadHistory(string ch)
        {
            var dir = ChannelDir(ch);
            var all = new List<ChatMessage>();
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.jsonl"))
                {
                    long len;
                    all.AddRange(ReadFrom(f, -1, out len));
                    _offsets[f] = len;
                }
            }
            var list = all.GroupBy(m => m.Id).Select(g => g.First()).OrderBy(m => m.Ts).ThenBy(m => m.Id)
                .Skip(Math.Max(0, all.Count - HistoryCount)).ToList();
            HistoryReceived?.Invoke(ch, list);
        }

        void PollChannel(string ch)
        {
            var dir = ChannelDir(ch);
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.jsonl"))
            {
                long known;
                lock (_lock) known = _offsets.TryGetValue(f, out var o) ? o : 0;
                long size;
                try { size = new FileInfo(f).Length; } catch { continue; }
                if (size == known) continue;
                if (size < known) known = 0; // file was trimmed/replaced
                var msgs = ReadFrom(f, known, out var newLen);
                lock (_lock) _offsets[f] = newLen;
                foreach (var m in msgs.OrderBy(m => m.Id)) MessageReceived?.Invoke(m);
            }
        }

        /// <summary>Reads complete lines from offset (or the file tail when offset &lt; 0). Returns new offset.</summary>
        static List<ChatMessage> ReadFrom(string file, long offset, out long newOffset)
        {
            var result = new List<ChatMessage>();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = offset >= 0 ? offset : Math.Max(0, fs.Length - HistoryTailBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            // only consume up to the last complete line; a half-written line is picked up next poll
            int lastNl = Array.LastIndexOf(buf, (byte)'\n', read - 1 < 0 ? 0 : read - 1);
            if (read == 0 || lastNl < 0) { newOffset = start; return result; }
            var text = Encoding.UTF8.GetString(buf, 0, lastNl + 1);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (offset < 0 && i == 0 && start > 0) continue; // first line of a tail read may be partial
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                try { var m = JsonSerializer.Deserialize<ChatMessage>(line, Json); if (m != null) result.Add(m); } catch { }
            }
            newOffset = start + lastNl + 1;
            return result;
        }

        sealed class Presence
        {
            public string User { get; set; } = "";
            public string Display { get; set; } = "";
            public string Machine { get; set; } = "";
            public DateTimeOffset Ts { get; set; }
            public string[] Channels { get; set; } = Array.Empty<string>();
        }

        void PollPresence(string[] chans)
        {
            var now = DateTimeOffset.UtcNow;
            var online = new List<Presence>();
            foreach (var f in Directory.GetFiles(PresenceDir, "*.json"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(10)) continue;
                    string txt;
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs)) txt = sr.ReadToEnd();
                    var p = JsonSerializer.Deserialize<Presence>(txt, Json);
                    if (p != null && now - p.Ts < OnlineWindow) online.Add(p);
                }
                catch { }
            }
            foreach (var ch in chans)
            {
                var users = online.Where(p => p.Channels.Contains(ch, StringComparer.OrdinalIgnoreCase))
                    .GroupBy(p => p.User, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ChatUser { User = g.First().User, Display = g.First().Display, Machine = string.Join(", ", g.Select(x => x.Machine).Distinct()) })
                    .OrderBy(u => u.Display, StringComparer.OrdinalIgnoreCase).ToList();
                var sig = string.Join("|", users.Select(u => u.User + "/" + u.Display));
                lock (_lock)
                {
                    if (_presenceSig.TryGetValue(ch, out var old) && old == sig) continue;
                    _presenceSig[ch] = sig;
                }
                PresenceReceived?.Invoke(ch, users);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { if (File.Exists(MyPresenceFile)) File.Delete(MyPresenceFile); } catch { }
        }
    }
}
