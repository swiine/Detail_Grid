using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DoomChat
{
    /// <summary>
    /// WebSocket connection to the TTW Chat server with automatic reconnect.
    /// Events fire on background threads; ChatService marshals them to the UI thread.
    /// </summary>
    public sealed class ChatClient : IChatTransport
    {
        static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        readonly Uri _uri;
        readonly string _user, _display, _machine;
        readonly HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
        readonly SemaphoreSlim _sendLock = new(1, 1);
        readonly CancellationTokenSource _cts = new();
        ClientWebSocket? _ws;

        public event Action<ChatMessage>? MessageReceived;
        public event Action<string, List<ChatMessage>>? HistoryReceived;
        public event Action<string, List<ChatUser>>? PresenceReceived;
        public event Action<ConnectionState, string?>? StateChanged;

        public ChatClient(string url, string user, string display, string machine)
        {
            _uri = new Uri(url);
            _user = user; _display = display; _machine = machine;
        }

        public void Start() => Task.Run(RunAsync);

        public void Join(string channel)
        {
            lock (_channels) if (!_channels.Add(channel)) return;
            _ = SendAsync(new { type = "join", channel });
        }

        public Task<bool> SendMessageAsync(string channel, string text, string? drawing) =>
            SendAsync(new { type = "msg", channel, text, drawing });

        async Task RunAsync()
        {
            var delay = 2;
            while (!_cts.IsCancellationRequested)
            {
                StateChanged?.Invoke(ConnectionState.Connecting, null);
                string? error = null;
                using (var ws = new ClientWebSocket())
                {
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    try
                    {
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(10));
                            await ws.ConnectAsync(_uri, timeout.Token).ConfigureAwait(false);
                        }
                        _ws = ws;
                        await SendAsync(new { type = "hello", user = _user, display = _display, machine = _machine }).ConfigureAwait(false);
                        string[] chans;
                        lock (_channels) chans = _channels.ToArray();
                        foreach (var ch in chans) await SendAsync(new { type = "join", channel = ch }).ConfigureAwait(false);
                        StateChanged?.Invoke(ConnectionState.Connected, null);
                        delay = 2;
                        await ReceiveLoop(ws).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!_cts.IsCancellationRequested)
                    {
                        error = ex is OperationCanceledException ? "Connection timed out" : ex.Message;
                    }
                    catch { }
                    finally { _ws = null; }
                }
                if (_cts.IsCancellationRequested) break;
                StateChanged?.Invoke(ConnectionState.Disconnected, error ?? "Connection closed");
                try { await Task.Delay(TimeSpan.FromSeconds(delay), _cts.Token).ConfigureAwait(false); } catch { break; }
                delay = Math.Min(delay * 2, 60);
            }
        }

        async Task ReceiveLoop(ClientWebSocket ws)
        {
            var buffer = new byte[16384];
            while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);

                try { Dispatch(Encoding.UTF8.GetString(ms.ToArray())); }
                catch { /* ignore malformed packets */ }
            }
        }

        void Dispatch(string text)
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "msg":
                    var m = root.GetProperty("message").Deserialize<ChatMessage>(Json);
                    if (m != null) MessageReceived?.Invoke(m);
                    break;
                case "history":
                    var list = root.GetProperty("messages").Deserialize<List<ChatMessage>>(Json) ?? new();
                    HistoryReceived?.Invoke(root.GetProperty("channel").GetString() ?? "", list);
                    break;
                case "presence":
                    var users = root.GetProperty("users").Deserialize<List<ChatUser>>(Json) ?? new();
                    PresenceReceived?.Invoke(root.GetProperty("channel").GetString() ?? "", users);
                    break;
            }
        }

        async Task<bool> SendAsync(object payload)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return false;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
                return true;
            }
            catch { return false; }
            finally { _sendLock.Release(); }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _ws?.Abort(); } catch { }
        }
    }
}
