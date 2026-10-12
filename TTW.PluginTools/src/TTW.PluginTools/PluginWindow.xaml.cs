using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TTW.PluginTools
{
    public partial class PluginWindow : Window
    {
        private const string DefaultStatus = "Double-click a tool to run it. Right-click for more.";

        private readonly PluginConfig _config;
        private readonly UserSettings _settings;
        private readonly string _configProblem;
        private List<ToolInfo> _allTools = new List<ToolInfo>();
        private ICollectionView _view;
        private FileSystemWatcher _watcher;
        private readonly DispatcherTimer _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool _videoPlaying, _updatingSlider, _running;

        private ToolInfo Selected => List.SelectedItem as ToolInfo;

        public PluginWindow()
        {
            InitializeComponent();
            _config = PluginConfig.Load(out _configProblem);
            _settings = UserSettings.Load();
            ApplyTheme(_config.Theme);

            SearchBox.TextChanged += (s, e) => OnSearchChanged();
            ClearSearch.Click += (s, e) => { SearchBox.Clear(); SearchBox.Focus(); };
            List.SelectionChanged += (s, e) => ShowSelected();
            List.MouseDoubleClick += (s, e) =>
            {
                if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListBoxItem) RunSelected();
            };
            List.ContextMenu = new ContextMenu();
            List.ContextMenuOpening += (s, e) => FillContextMenu(List.ContextMenu);
            RunButton.Click += (s, e) => RunSelected();
            WatchButton.Click += (s, e) => ToggleVideo();
            PlayPause.Click += (s, e) => TogglePlay();
            Video.MouseLeftButtonUp += (s, e) => TogglePlay();
            VideoExternal.Click += (s, e) => OpenVideoExternally();
            Video.MediaOpened += (s, e) => UpdateVideoTime();
            Video.MediaEnded += (s, e) => { Video.Pause(); Video.Position = TimeSpan.Zero; SetPlaying(false); UpdateVideoTime(); };
            VideoSlider.ValueChanged += (s, e) => SeekFromSlider();
            _videoTimer.Tick += (s, e) => UpdateVideoTime();
            _reloadTimer.Tick += (s, e) => { _reloadTimer.Stop(); Reload(); };
            PreviewKeyDown += OnPreviewKeyDown;
            Closed += (s, e) => { Video.Close(); _videoTimer.Stop(); _watcher?.Dispose(); };
            Loaded += (s, e) => { Reload(); SearchBox.Focus(); };
        }

        // ---------------------------------------------------------------- theme

        private void ApplyTheme(ThemeConfig theme)
        {
            theme ??= new ThemeConfig();
            foreach (var (key, hex) in ThemeConfig.Defaults)
            {
                string value = theme.Colors != null && theme.Colors.TryGetValue(key, out var v) ? v : hex;
                Resources["B." + key] = ToBrush(value) ?? ToBrush(hex);
            }
            Resources["F.Body"] = new FontFamily(theme.BodyFont ?? "Arial Nova, Arial");
            Resources["F.Heading"] = new FontFamily(theme.HeadingFont ?? "Arial Nova, Arial");
            Resources["F.Icon"] = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

            TitleText.Text = theme.Title ?? "Plugin Tools";
            SubtitleText.Text = theme.Subtitle ?? "";
            Title = "TTW " + TitleText.Text;
            Logo.Source = Asset("logo-alabaster.png");
            EmptyMark.Source = Asset("brandmark-waterblue.png");
        }

        private static Brush ToBrush(string hex)
        {
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
            catch { return null; }
        }

        private static ImageSource Asset(string file)
        {
            try
            {
                _ = PackUriHelper.UriSchemePack;   // makes sure pack:// is registered inside AutoCAD
                var img = new BitmapImage(new Uri($"pack://application:,,,/TTW.PluginTools;component/assets/{file}"));
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- list

        private string ToolsFolder => UserSettings.EffectiveFolder(_config, _settings);

        /// <summary>Rescans the tools folder, keeping the current selection where possible.</summary>
        public void Reload()
        {
            string keepKey = Selected?.Key;
            string folder = ToolsFolder;
            _allTools = new List<ToolInfo>();
            string listMessage = null;
            ToolScanner.Result result = null;

            if (string.IsNullOrWhiteSpace(folder))
                listMessage = _config.AllowFolderChange
                    ? "No tools folder yet. Right-click here and choose Change tools folder."
                    : "No tools folder is set up. Ask the CAD team.";
            else if (!Directory.Exists(folder))
                listMessage = "Can't reach the tools folder:\n" + folder;
            else
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    result = ToolScanner.Scan(folder, _config.ArchivePrefix, ToolRunner.ReferenceDirs());
                    _allTools = result.Tools;
                }
                catch (Exception ex)
                {
                    listMessage = "Can't reach the tools folder:\n" + folder + "\n\n" + ex.Message;
                }
                finally { Mouse.OverrideCursor = null; }
            }

            var visible = _allTools.Where(t => _settings.ShowArchived || !t.IsArchived)
                                   .OrderBy(t => t.Group == "" ? 0 : t.IsArchived ? 2 : 1)   // loose tools first, archive last
                                   .ThenBy(t => t.Group, StringComparer.CurrentCultureIgnoreCase)
                                   .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                                   .ToList();
            _view = CollectionViewSource.GetDefaultView(visible);
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ToolInfo.Group)));
            _view.Filter = o => ((ToolInfo)o).Matches(SearchBox.Text);
            List.ItemsSource = _view;

            if (listMessage == null && visible.Count == 0)
                listMessage = "No plugin tools in this folder yet. Put a plugin's .dll (and anything it needs) in a sub-folder of:\n" + folder;
            _folderMessage = listMessage;
            UpdateListEmpty();

            List.SelectedItem = visible.FirstOrDefault(t => t.Key == keepKey);
            ShowSelected();
            WatchFolder(Directory.Exists(folder ?? "") ? folder : null);

            if (_configProblem != null) SetStatus(_configProblem, false);
            else if (result != null && result.Problems.Count > 0)
            {
                SetStatus($"{result.Problems.Count} DLL(s) in the tools folder couldn't be read - see the command line.", false);
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                foreach (var p in result.Problems.Take(10)) ed?.WriteMessage($"\nTTW Plugin Tools: {p}");
                if (result.Problems.Count > 10) ed?.WriteMessage($"\n  ... and {result.Problems.Count - 10} more.");
            }
        }

        private string _folderMessage;

        private void OnSearchChanged()
        {
            bool has = SearchBox.Text.Length > 0;
            SearchHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            ClearSearch.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _view?.Refresh();
            if (Selected == null && _view != null)
                List.SelectedItem = _view.Cast<object>().FirstOrDefault();
            UpdateListEmpty();
        }

        private void UpdateListEmpty()
        {
            string msg = _folderMessage;
            if (msg == null && _view != null && _view.IsEmpty && SearchBox.Text.Length > 0)
                msg = $"No tools match \"{SearchBox.Text}\".";
            ListEmpty.Text = msg ?? "";
            ListEmpty.Visibility = msg == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowSelected()
        {
            StopVideo();
            var tool = Selected;
            Detail.DataContext = null;     // re-read Warning etc. even for the same tool
            Detail.DataContext = tool;
            Detail.Visibility = tool == null ? Visibility.Collapsed : Visibility.Visible;
            EmptyState.Visibility = tool == null ? Visibility.Visible : Visibility.Collapsed;
            RunButton.IsEnabled = tool != null && !_running;
        }

        /// <summary>Picks up new or changed DLLs, descriptions and videos without pressing Reload.</summary>
        private void WatchFolder(string folder)
        {
            if (_watcher != null && folder != null && string.Equals(_watcher.Path, folder, StringComparison.OrdinalIgnoreCase)) return;
            _watcher?.Dispose();
            _watcher = null;
            if (folder == null) return;
            try
            {
                _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                FileSystemEventHandler changed = (s, e) =>
                {
                    string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
                    if (ext == ".dll" || ext == ".txt" || ext == "" || ToolScanner.VideoExtensions.Contains(ext))
                        Dispatcher.BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
                };
                _watcher.Changed += changed;
                _watcher.Created += changed;
                _watcher.Deleted += changed;
                _watcher.Renamed += (s, e) => changed(s, e);
                _watcher.EnableRaisingEvents = true;
            }
            catch { _watcher = null; /* network folders don't always allow watching - Reload list still works */ }
        }

        // ---------------------------------------------------------------- run

        private async void RunSelected()
        {
            var tool = Selected;
            if (tool == null || _running) return;

            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { SetStatus("Open a drawing first.", false); return; }
            if (!string.IsNullOrEmpty(doc.CommandInProgress)) { SetStatus("Finish or cancel the current command first.", false); return; }

            _running = true;
            RunButton.IsEnabled = false;
            StopVideo();
            SetStatus($"Running {tool.Name}...", null);
            RunResult result = null;
            try
            {
                FocusDrawing();
                // Command context: the tool can prompt for points and selections like a normal command
                await AcApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
                {
                    result = ToolRunner.Run(tool, doc);
                    return Task.CompletedTask;
                }, null);
            }
            catch (Exception ex)
            {
                result = new RunResult { Success = false, Message = ex.Message };
            }
            finally
            {
                _running = false;
                ShowSelected();
            }

            if (result != null) SetStatus(result.Message, result.Success);
        }

        private static void FocusDrawing()
        {
            try { Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView(); } catch { /* not essential */ }
        }

        // ---------------------------------------------------------------- status

        /// <param name="success">true = green tick, false = red cross, null = plain.</param>
        private void SetStatus(string text, bool? success)
        {
            StatusText.Text = text;
            StatusText.ToolTip = text;
            StatusText.Foreground = (Brush)FindResource(success == true ? "B.Success" : success == false ? "B.Failure" : "B.Muted");
            StatusGlyph.Visibility = success == null ? Visibility.Collapsed : Visibility.Visible;
            StatusGlyph.Text = success == true ? "" : "";
            StatusGlyph.Foreground = StatusText.Foreground;
        }

        // ---------------------------------------------------------------- right-click menu

        private void FillContextMenu(ContextMenu menu)
        {
            var tool = Selected;
            menu.Items.Clear();

            if (tool != null)
            {
                menu.Items.Add(MenuItem("Run", RunSelected));
                menu.Items.Add(MenuItem("Check it loads", () => CheckLoads(tool)));
                if (PluginHost.Find(tool.DllPath) != null)
                {
                    menu.Items.Add(MenuItem("Reload plugin", () => ReloadPlugin(tool)));
                    menu.Items.Add(MenuItem("Unload plugin", () => UnloadPlugin(tool)));
                }
                bool linked = _settings.SourceProjectFor(tool.DllPath) != null;
                menu.Items.Add(MenuItem(linked ? "Build and reload" : "Build from source...", () => BuildAndReload(tool)));
                if (linked)
                    menu.Items.Add(MenuItem("Change source project...", () => PickSourceProject(tool)));
                menu.Items.Add(MenuItem("Show in folder", () => ShowInFolder(tool.DllPath)));
                menu.Items.Add(MenuItem("Copy ribbon button macro", () =>
                {
                    string macro = $"^C^C_TTWPLUGINRUN;{tool.CommandName};";
                    try { Clipboard.SetText(macro); SetStatus($"Copied {macro} - paste it into a ribbon button's Macro in CUI.", true); }
                    catch { SetStatus("Couldn't reach the clipboard - try again.", false); }
                }));
                menu.Items.Add(new Separator());
            }

            var archived = MenuItem("Show archived tools", () =>
            {
                _settings.ShowArchived = !_settings.ShowArchived;
                _settings.Save();
                Reload();
            });
            archived.IsCheckable = true;
            archived.IsChecked = _settings.ShowArchived;
            menu.Items.Add(archived);
            if (_config.AllowFolderChange)
                menu.Items.Add(MenuItem("Change tools folder", ChangeFolder));
            menu.Items.Add(MenuItem("Reload list", () => { Reload(); SetStatus("List reloaded.", null); }));
        }

        private static MenuItem MenuItem(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => click();
            return item;
        }

        private async void CheckLoads(ToolInfo tool)
        {
            await InCommandContext(null, doc =>
            {
                try
                {
                    var asm = ToolRunner.EnsureLoaded(tool, doc.Editor, out string warning, out string note);
                    bool found = asm.GetType(tool.TypeName) != null;
                    if (warning != null) tool.Warning = warning;
                    if (note != null) doc.Editor.WriteMessage($"\nTTW Plugin Tools: {note}");
                    SetStatus(found ? $"{Path.GetFileName(tool.DllPath)} loads OK - {tool.CommandName} is ready."
                                    : $"{Path.GetFileName(tool.DllPath)} loaded, but {tool.TypeName} wasn't found in it.", found);
                }
                catch (Exception ex)
                {
                    SetStatus($"{Path.GetFileName(tool.DllPath)} didn't load: {ex.Message}", false);
                }
            });
        }

        // ---------------------------------------------------------------- unload / reload / build

        private async void ReloadPlugin(ToolInfo tool)
        {
            await InCommandContext($"Reloading {Path.GetFileName(tool.DllPath)}...", doc =>
            {
                try
                {
                    var plugin = PluginHost.Load(tool.DllPath, doc.Editor);
                    tool.Warning = null;
                    SetStatus($"Reloaded {plugin.Name} from {Path.GetDirectoryName(tool.RelativeDllPath)}.", true);
                }
                catch (Exception ex)
                {
                    SetStatus($"{Path.GetFileName(tool.DllPath)} didn't load: {ex.Message}", false);
                }
            });
        }

        private async void UnloadPlugin(ToolInfo tool)
        {
            var plugin = PluginHost.Find(tool.DllPath);
            if (plugin == null) return;
            await InCommandContext($"Unloading {plugin.Name}...", doc =>
            {
                bool released = PluginHost.Unload(plugin, doc.Editor);
                SetStatus(PluginHost.UnloadMessage(plugin, released) + (released ? " It loads again the next time you run one of its tools." : ""), released);
            });
        }

        /// <summary>
        /// Compiles the plugin's project into its folder in the tools list, then swaps the
        /// running copy for the new build. Edit code, Build and reload, run it - no restart.
        /// </summary>
        private async void BuildAndReload(ToolInfo tool)
        {
            if (_running) return;
            string project = _settings.SourceProjectFor(tool.DllPath);
            if (project == null || !File.Exists(project))
            {
                project = PickSourceProject(tool);
                if (project == null) return;
            }

            string outDir = Path.GetDirectoryName(tool.DllPath);
            if (IsUnder(outDir, _config.DefaultToolsFolder)
                && MessageBox.Show(this,
                       $"This builds {Path.GetFileName(project)} into the shared tools folder:\n{outDir}\n\n" +
                       "Everyone using Plugin Tools gets the new build the next time they run it. Continue?",
                       "Build into the shared folder?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _running = true;
            RunButton.IsEnabled = false;
            SetStatus($"Building {Path.GetFileName(project)}...", null);
            BuildResult build;
            try
            {
                Mouse.OverrideCursor = Cursors.AppStarting;
                build = await PluginBuilder.BuildAsync(project, outDir);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _running = false;
                ShowSelected();
            }

            var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            if (!build.Success)
            {
                ed?.WriteMessage($"\nTTW Plugin Tools: build of {Path.GetFileName(project)} failed:\n{BuildErrors(build.Output)}\n");
                SetStatus("Build failed - the errors are on the command line.", false);
                return;
            }

            string built = $"Built {Path.GetFileName(project)} in {ToolRunner.Seconds(build.Elapsed)}";
            Reload();   // new or renamed commands
            if (PluginHost.Find(tool.DllPath) == null)
            {
                SetStatus(built + ". It loads the next time you run one of its tools.", true);
                return;
            }

            await InCommandContext(null, doc =>
            {
                try
                {
                    PluginHost.Load(tool.DllPath, doc.Editor);
                    SetStatus(built + " and reloaded it.", true);
                }
                catch (Exception ex)
                {
                    SetStatus(built + $", but it didn't load: {ex.Message}", false);
                }
            });
        }

        /// <summary>Asks which .csproj builds this plugin and remembers it. Null if cancelled.</summary>
        private string PickSourceProject(ToolInfo tool)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Project that builds {Path.GetFileName(tool.DllPath)}",
                Filter = "C# project (*.csproj)|*.csproj",
            };
            string current = _settings.SourceProjectFor(tool.DllPath);
            if (current != null && Directory.Exists(Path.GetDirectoryName(current)))
                dlg.InitialDirectory = Path.GetDirectoryName(current);
            if (dlg.ShowDialog(this) != true) return null;

            _settings.SetSourceProject(tool.DllPath, dlg.FileName);
            _settings.Save();
            SetStatus($"{Path.GetFileName(tool.DllPath)} is built from {dlg.FileName}.", null);
            return dlg.FileName;
        }

        /// <summary>Just the error lines from the compiler output, or its last lines if there are none.</summary>
        private static string BuildErrors(string output)
        {
            var lines = (output ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            var errors = lines.Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            return string.Join("\n", (errors.Count > 0 ? errors : lines.TakeLast(20)).Take(30));
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            try
            {
                string p = Path.GetFullPath(path).TrimEnd('\\') + "\\";
                string r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Runs work in command context, like a tool: loading registers the plugin's commands and
        /// runs its Initialize(), which AutoCAD expects to happen there.
        /// </summary>
        private async Task InCommandContext(string busy, Action<Autodesk.AutoCAD.ApplicationServices.Document> work)
        {
            if (_running) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { SetStatus("Open a drawing first.", false); return; }
            if (!string.IsNullOrEmpty(doc.CommandInProgress)) { SetStatus("Finish or cancel the current command first.", false); return; }

            _running = true;
            RunButton.IsEnabled = false;
            if (busy != null) SetStatus(busy, null);
            try
            {
                await AcApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
                {
                    work(doc);
                    return Task.CompletedTask;
                }, null);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, false);
            }
            finally
            {
                _running = false;
                ShowSelected();
            }
        }

        private void ShowInFolder(string path)
        {
            try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch (Exception ex) { SetStatus(ex.Message, false); }
        }

        private void ChangeFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Folder holding the plugin tools",
                InitialDirectory = Directory.Exists(ToolsFolder ?? "") ? ToolsFolder : null,
            };
            if (dlg.ShowDialog(this) != true) return;
            // Picking the site default again goes back to following it
            _settings.ToolsFolder = string.Equals(dlg.FolderName, _config.DefaultToolsFolder, StringComparison.OrdinalIgnoreCase) ? null : dlg.FolderName;
            _settings.Save();
            Reload();
            SetStatus("Tools folder: " + ToolsFolder, null);
        }

        // ---------------------------------------------------------------- video

        private void ToggleVideo()
        {
            if (VideoArea.Visibility == Visibility.Visible) { StopVideo(); return; }
            var tool = Selected;
            if (tool?.VideoPath == null) return;
            if (!File.Exists(tool.VideoPath)) { SetStatus("Tutorial video not found: " + tool.VideoPath, false); return; }

            DescriptionArea.Visibility = Visibility.Collapsed;
            VideoArea.Visibility = Visibility.Visible;
            WatchButtonText.Text = "Back to description";
            Video.Source = new Uri(tool.VideoPath);
            Video.Play();
            SetPlaying(true);
            _videoTimer.Start();
        }

        private void StopVideo()
        {
            _videoTimer.Stop();
            Video.Stop();
            Video.Source = null;
            SetPlaying(false);
            VideoArea.Visibility = Visibility.Collapsed;
            DescriptionArea.Visibility = Visibility.Visible;
            WatchButtonText.Text = "Watch tutorial";
        }

        private void TogglePlay()
        {
            if (Video.Source == null) return;
            if (_videoPlaying) Video.Pause(); else Video.Play();
            SetPlaying(!_videoPlaying);
        }

        private void SetPlaying(bool playing)
        {
            _videoPlaying = playing;
            PlayPause.Content = playing ? "" : "";   // pause / play
        }

        private void UpdateVideoTime()
        {
            if (!Video.NaturalDuration.HasTimeSpan) return;
            var total = Video.NaturalDuration.TimeSpan;
            var now = Video.Position;
            _updatingSlider = true;
            VideoSlider.Value = total.TotalSeconds > 0 ? now.TotalSeconds / total.TotalSeconds : 0;
            _updatingSlider = false;
            VideoTime.Text = $"{Time(now)} / {Time(total)}";
        }

        private void SeekFromSlider()
        {
            if (_updatingSlider || !Video.NaturalDuration.HasTimeSpan) return;
            Video.Position = TimeSpan.FromSeconds(VideoSlider.Value * Video.NaturalDuration.TimeSpan.TotalSeconds);
            UpdateVideoTime();
        }

        private void OpenVideoExternally()
        {
            var path = Selected?.VideoPath;
            if (path == null) return;
            if (_videoPlaying) TogglePlay();
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("Couldn't open the video: " + ex.Message, false); }
        }

        private static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        // ---------------------------------------------------------------- keys

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape when SearchBox.Text.Length > 0:
                    SearchBox.Clear();
                    e.Handled = true;
                    break;
                case Key.Enter when Selected != null && Keyboard.FocusedElement is not Button:
                    RunSelected();
                    e.Handled = true;
                    break;
                case Key.Down when SearchBox.IsKeyboardFocused && List.Items.Count > 0:
                    if (List.SelectedIndex < 0) List.SelectedIndex = 0;
                    (List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) as ListBoxItem)?.Focus();
                    e.Handled = true;
                    break;
                case Key.Space when VideoArea.Visibility == Visibility.Visible && !SearchBox.IsKeyboardFocused:
                    TogglePlay();
                    e.Handled = true;
                    break;
            }
        }
    }
}
