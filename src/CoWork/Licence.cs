using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.AutoCAD.Windows;
using CoWork.Core;

namespace CoWork
{
    /// <summary>
    /// Only people you've approved can use CoWork. A licence (signed with your private key) can come from:
    /// a key the person pasted (%APPDATA%\CoWork\license.key), one you approved for them in the shared folder
    /// (CoWork-data\licences\&lt;login&gt;.key - written by the admin tool), or a site key shipped with CoWork.
    /// Without one, CoWork shows only the licence window. Revoked or expired licences lock it again (checked at
    /// start and every 10 minutes).
    /// </summary>
    internal static class LicenceGate
    {
        private static readonly Guid WindowId = new("A4C2E7B1-3D58-4F69-9E0B-6C1D2F7A8B34");
        private static PaletteSet? _window;
        private static LicencePanel? _panel;
        private static DispatcherTimer? _watch, _recheck;
        private static Action? _onUnlocked;

        public static LicenseState State { get; private set; } = LicenseState.Missing;
        public static LicenseInfo? Info { get; private set; }
        public static bool Ok => State == LicenseState.Valid;

        public static string User => Environment.UserName;
        public static string Domain => Environment.UserDomainName;
        public static string Machine => Environment.MachineName;
        /// <summary>The licence key in use (shown to the CoWork server when connecting).</summary>
        public static string? Key { get; private set; }

        public static string PastedKeyFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoWork", "license.key");
        private static string DataFolder => CiSettings.Load().Folder ?? "";

        private static string? ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>Looks for this person's licence again (reads a few small files; safe to call off the UI thread).</summary>
        public static (LicenseState, LicenseInfo?) Look()
        {
            string data = DataFolder;
            string here = Path.GetDirectoryName(typeof(LicenceGate).Assembly.Location) ?? "";
            var keys = new[]
            {
                ReadText(PastedKeyFile),
                data.Length > 0 ? ReadText(Licensing.UserKeyFile(data, User)) : null,
                ReadText(Path.Combine(here, "license.key")),
                // A site key put in the installed bundle (CoWork itself now runs from %LOCALAPPDATA%\CoWork\app).
                ReadText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "CoWork.bundle", "Contents", "license.key")),
                ReadText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "ApplicationPlugins", "CoWork.bundle", "Contents", "license.key")),
            };
            // Revokes: the newest signed list from you and from each deputy, from the shared folder or the copies this
            // PC kept - so deleting a list, or putting an older one back, doesn't bring a cancelled licence back.
            string keptDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork", "revoked");
            var texts = new System.Collections.Generic.List<string?>();
            void AddAll(string dir, string pattern)
            {
                try { if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir, pattern)) texts.Add(ReadText(f)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (data.Length > 0) AddAll(Licensing.Folder(data), "revoked*.key");
            AddAll(keptDir, "*.key");
            texts.Add(ReadText(Path.Combine(Path.GetDirectoryName(keptDir)!, "revoked.key"))); // kept by 1.1.1
            Licensing.RevokedSet revoked = Licensing.RevokedAll(texts);
            foreach (var (signer, text) in revoked.Keep)
            {
                string file = Path.Combine(keptDir, Rooms.Safe(signer) + ".key");
                if (ReadText(file)?.Trim() == text) continue;
                try { Directory.CreateDirectory(keptDir); File.WriteAllText(file, text); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            var (state, info, key) = Licensing.Find(keys, revoked, User, Domain, Machine, DateOnly.FromDateTime(DateTime.Today));
            Key = state == LicenseState.Valid ? key : null;
            // Keep a working key on this PC, so the next start doesn't depend on the network being ready.
            if (state == LicenseState.Valid && key != null && ReadText(PastedKeyFile)?.Trim() != key.Trim())
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(PastedKeyFile)!); File.WriteAllText(PastedKeyFile, key.Trim()); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return (state, info);
        }

        public static void Refresh() => (State, Info) = Look();

        /// <summary>At startup: start CoWork if licensed, otherwise show the licence window and wait for approval.</summary>
        public static void StartOrWait(Action start)
        {
            _onUnlocked = start;
            Refresh();
            CoWork.Core.Log.Info("Licence at startup: " + Describe(State, Info));
            if (Ok) { start(); KeepChecking(); return; }
            // Not found yet: the network drive may still be connecting. Look again quietly for 30 s before asking.
            int tries = 0;
            var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            retry.Tick += async (_, _) =>
            {
                retry.Stop();
                var (state, info) = await Task.Run(Look);
                (State, Info) = (state, info);
                if (Ok) { CoWork.Core.Log.Info("Licence found after a retry"); Changed(); return; }
                if (++tries < 10) { retry.Start(); return; }
                CoWork.Core.Log.Info("No licence after 30 s: " + Describe(State, Info));
                ShowWindow();
                Watch();
            };
            retry.Start();
        }

        /// <summary>Shown when a command is used without a licence.</summary>
        public static bool Check()
        {
            if (Ok) return true;
            Commands.Write("\nCoWork isn't licensed for you yet - see the CoWork licence window.");
            ShowWindow();
            return false;
        }

        /// <summary>The licence window (CWLICENSE opens it any time, e.g. to see who it's licensed to).</summary>
        public static void ShowWindow()
        {
            if (_window == null)
            {
                _panel = new LicencePanel();
                _window = new PaletteSet("CoWork licence", "CWLICENSE", WindowId)
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton,
                    MinimumSize = new System.Drawing.Size(280, 360),
                    DockEnabled = DockSides.Left | DockSides.Right,
                };
                _window.AddVisual("Licence", _panel);
                _window.Visible = true;
                if (_window.Size.Width < 300) _window.Size = new System.Drawing.Size(330, 460);
            }
            _panel!.Refresh();
            _window.Visible = true;
        }

        private static void HideWindow()
        {
            try { if (_window != null) _window.Visible = false; } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
        }

        /// <summary>A key was pasted: save it for this person and check it.</summary>
        public static LicenseState UseKey(string key)
        {
            var (state, _) = Licensing.Check(key, User, Domain, Machine, DateOnly.FromDateTime(DateTime.Today));
            if (state != LicenseState.Valid) return state;
            Directory.CreateDirectory(Path.GetDirectoryName(PastedKeyFile)!);
            File.WriteAllText(PastedKeyFile, key.Trim());
            Changed();
            return State;
        }

        /// <summary>Asks for access: leaves a request in the shared folder for the admin tool to approve.</summary>
        public static string Request()
        {
            string data = DataFolder;
            if (data.Length == 0 || !Directory.Exists(data)) return "Can't reach the CoWork folder - send your login and PC name instead.";
            try
            {
                Licensing.WriteRequest(data, new Licensing.Request(User, Domain, Machine, CiSettings.Load().Name, DateTimeOffset.UtcNow));
                return "Request sent. CoWork will unlock by itself once it's approved.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Couldn't send the request: " + ex.Message; }
        }

        /// <summary>While locked: check every 15 seconds whether a licence has been approved.</summary>
        private static void Watch()
        {
            _watch ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _watch.Tick -= OnWatch;
            _watch.Tick += OnWatch;
            _watch.Start();
        }

        private static async void OnWatch(object? s, EventArgs e)
        {
            var (state, info) = await Task.Run(Look);
            if (state == State && state != LicenseState.Valid) return;
            (State, Info) = (state, info);
            Changed();
        }

        /// <summary>While licensed: check every 10 minutes that it hasn't been revoked or run out.</summary>
        private static void KeepChecking()
        {
            _recheck ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _recheck.Tick -= OnRecheck;
            _recheck.Tick += OnRecheck;
            _recheck.Start();
        }

        private static async void OnRecheck(object? s, EventArgs e)
        {
            var (state, info) = await Task.Run(Look);
            (State, Info) = (state, info);
            if (Ok) return;
            _recheck?.Stop();
            Commands.Write($"\nCoWork has been locked: {Describe(state, info)}");
            try { LiveDrafting.Leave(notify: true); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            try { ChatHub.Stop(); } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); }
            CiUi.Hide();
            _started = false;
            ShowWindow();
            Watch();
        }

        private static bool _started;

        private static void Changed()
        {
            _panel?.Refresh();
            if (!Ok) return;
            _watch?.Stop();
            if (!_started)
            {
                _started = true;
                HideWindow();
                Commands.Write($"\nCoWork licence accepted: {Licensing.Describe(Info!)}");
                _onUnlocked?.Invoke();
                KeepChecking();
            }
        }

        internal static void MarkStarted() => _started = true;

        public static string Describe(LicenseState state, LicenseInfo? info) => state switch
        {
            LicenseState.Valid => "Licensed: " + Licensing.Describe(info!),
            LicenseState.Missing => "CoWork needs a licence. Ask for access below.",
            LicenseState.Invalid => "That key isn't a genuine CoWork licence.",
            LicenseState.Revoked => "Your CoWork licence has been cancelled.",
            LicenseState.Expired => $"Your licence ran out on {info?.Expires:d MMM yyyy}.",
            LicenseState.NotForThisUser => $"That licence isn't for {Domain}\\{User}.",
            LicenseState.NotForThisPc => $"That licence isn't for this PC ({Machine}).",
            _ => state.ToString(),
        };
    }

    /// <summary>The licence window: status, request access, your details, paste a key.</summary>
    internal sealed class LicencePanel : UserControl
    {
        private static readonly Theme T = Theme.Current;
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10), FontSize = 13 };
        private readonly TextBlock _requestNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBox _key = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
        private readonly TextBlock _keyNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) };

        public LicencePanel()
        {
            Background = T.Back;
            Foreground = T.Text;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12.5;
            var p = new StackPanel { Margin = new Thickness(14) };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            head.Children.Add(new ContentControl { Content = Logos.Image(T.Palette, 26, tile: true), Margin = new Thickness(0, 0, 10, 0) });
            head.Children.Add(new TextBlock { Text = "CoWork", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(head);
            p.Children.Add(_status);

            p.Children.Add(Label("Ask for access"));
            var request = Button("Request access", filled: true);
            request.Click += (_, _) => { _requestNote.Text = LicenceGate.Request(); _requestNote.Foreground = T.Dim; };
            p.Children.Add(request);
            p.Children.Add(_requestNote);

            p.Children.Add(Label("Your details"));
            string details = $"Login: {LicenceGate.Domain}\\{LicenceGate.User}\nPC: {LicenceGate.Machine}";
            p.Children.Add(new TextBox { Text = details, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = T.Text, FontFamily = new FontFamily("Consolas") });
            var copy = Button("Copy", filled: false);
            copy.Click += (_, _) => { try { Clipboard.SetText(details); copy.Content = "Copied"; } catch (System.Exception swallowed) { CoWork.Core.Log.Swallowed(swallowed); } };
            p.Children.Add(copy);

            p.Children.Add(Label("Got a licence key? Paste it here"));
            _key.Background = T.Field;
            _key.Foreground = T.Text;
            _key.BorderBrush = T.Edge;
            _key.CaretBrush = T.Text;
            p.Children.Add(_key);
            var use = Button("Use this key", filled: false);
            use.Click += (_, _) =>
            {
                LicenseState s = LicenceGate.UseKey(_key.Text);
                _keyNote.Text = s == LicenseState.Valid ? "Accepted - CoWork is starting." : LicenceGate.Describe(s, Licensing.Read(_key.Text));
                _keyNote.Foreground = s == LicenseState.Valid ? T.Good : T.Warn;
            };
            p.Children.Add(use);
            p.Children.Add(_keyNote);

            Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Refresh();
        }

        public void Refresh()
        {
            _status.Text = LicenceGate.Describe(LicenceGate.State, LicenceGate.Info);
            _status.Foreground = LicenceGate.Ok ? T.Good : T.Text;
        }

        private static TextBlock Label(string text) =>
            new() { Text = text.ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = 11, Foreground = T.Accent, Margin = new Thickness(0, 12, 0, 4) };

        private static Button Button(string text, bool filled) => new()
        {
            Content = text, Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Left, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0),
            Background = filled ? T.Accent : Brushes.Transparent, Foreground = filled ? T.AccentText : T.Accent, BorderBrush = T.Accent,
        };
    }
}
