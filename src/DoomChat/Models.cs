using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoomChat
{
    public sealed class ChatMessage
    {
        public long Id { get; set; }
        public string Channel { get; set; } = "";
        public string User { get; set; } = "";
        public string Display { get; set; } = "";
        public DateTimeOffset Ts { get; set; }
        public string Text { get; set; } = "";
        public string? Drawing { get; set; }
    }

    public sealed class ChatUser
    {
        public string User { get; set; } = "";
        public string Display { get; set; } = "";
        public string Machine { get; set; } = "";
    }

    public enum ConnectionState { Disconnected, Connecting, Connected }

    /// <summary>One chat channel shown as a tab in the palette.</summary>
    public sealed class ChannelState
    {
        public ChannelState(string name, string title) { Name = name; Title = title; }
        public string Name { get; }
        public string Title { get; }
        public List<ChatMessage> Messages { get; } = new();
        public List<ChatUser> Online { get; set; } = new();
        public int Unread { get; set; }
    }

    public sealed class ChatSettings
    {
        /// <summary>The shared folder everyone uses unless doomchat.defaults.json or the user says otherwise.</summary>
        public const string DefaultFolder = @"\\ttw\dfs\ttwcad\jobs\24\241459\civil\sheets\1\1 - Copy\xrefs\test\DoomChat-data";

        /// <summary>WebSocket address of a TTW Chat server (with the relay update), e.g. ws://cadserver:5280/ws</summary>
        public string ServerUrl { get; set; } = "ws://localhost:5280/ws";
        /// <summary>
        /// Shared network folder for the lobby chat and CivDOOM games (games go in its "doom" sub-folder).
        /// When set, it is used instead of ServerUrl.
        /// </summary>
        public string? ChatFolder { get; set; } = DefaultFolder;
        /// <summary>Name shown to others. Blank = Windows login name.</summary>
        public string? DisplayName { get; set; }
        /// <summary>Show a status-bar balloon when a message arrives while the palette is closed.</summary>
        public bool PopupNotifications { get; set; } = true;

        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        static string UserFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoomChat", "settings.json");

        /// <summary>Defaults shipped next to the DLL in the bundle (the shared folder everyone uses). CivDOOM reads it too.</summary>
        static string DefaultsFile => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "doomchat.defaults.json");

        public static ChatSettings Load()
        {
            var s = TryRead(DefaultsFile) ?? new ChatSettings();
            var user = TryRead(UserFile);
            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(user.ServerUrl)) s.ServerUrl = user.ServerUrl;
                if (user.ChatFolder != null) s.ChatFolder = user.ChatFolder; // "" = user chose server mode
                if (!string.IsNullOrWhiteSpace(user.DisplayName)) s.DisplayName = user.DisplayName;
                s.PopupNotifications = user.PopupNotifications;
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
            File.WriteAllText(UserFile, JsonSerializer.Serialize(this, Json));
        }

        static ChatSettings? TryRead(string path)
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(path), Json) : null; }
            catch { return null; }
        }
    }
}
