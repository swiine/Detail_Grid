using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DoomChat
{
    /// <summary>How messages travel: a chat server (ChatClient) or a shared network folder (FolderChatClient).</summary>
    public interface IChatTransport : IDisposable
    {
        event Action<ChatMessage>? MessageReceived;
        event Action<string, List<ChatMessage>>? HistoryReceived;
        event Action<string, List<ChatUser>>? PresenceReceived;
        event Action<ConnectionState, string?>? StateChanged;

        void Start();
        void Join(string channel);
        Task<bool> SendMessageAsync(string channel, string text, string? drawing);
    }
}
