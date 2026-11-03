using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>
    /// Safety copies of CoWork drawings (see <see cref="Backups"/>): made at the start of a session, every 30 minutes
    /// while hosting (if the drawing changed), before handing over, or on demand - and put back with Restore.
    /// </summary>
    internal static class BackupUi
    {
        private static readonly Dictionary<string, DateTime> _lastCopied = new(StringComparer.OrdinalIgnoreCase);

        public static int Keep => Math.Max(3, ChatHub.Settings.BackupKeep ?? 30);
        public static int Minutes => ChatHub.Settings.BackupMinutes ?? 30;

        /// <summary>Raised (on the UI thread) after a copy is made or restored.</summary>
        public static event Action? Changed;

        /// <summary>
        /// Copies the drawing as saved on disk, in the background. Skips it when <paramref name="onlyIfChanged"/> and
        /// the file hasn't been saved since the last copy.
        /// </summary>
        public static void MakeSoon(string drawing, string reason, bool onlyIfChanged = false, bool say = true)
        {
            Dispatcher ui = Dispatcher.CurrentDispatcher;
            string who = ChatHub.Settings.Name;
            Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(drawing)) return;
                    DateTime saved = File.GetLastWriteTimeUtc(drawing);
                    lock (_lastCopied)
                    {
                        if (onlyIfChanged && _lastCopied.TryGetValue(drawing, out DateTime last) && saved <= last) return;
                        _lastCopied[drawing] = saved;
                    }
                    string copy = Backups.Make(drawing, reason, who, DateTime.Now);
                    Backups.Prune(drawing, Keep);
                    Log.Info($"Backup ({reason}): {copy}");
                    ui.BeginInvoke(new Action(() =>
                    {
                        if (say) Commands.Write($"\n[CoWork] Safety copy saved ({reason}) in {Where(drawing)}.");
                        Changed?.Invoke();
                    }));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error("Backup " + drawing, ex);
                    ui.BeginInvoke(new Action(() => Commands.Write($"\n[CoWork] Couldn't save a safety copy: {ex.Message}")));
                }
            });
        }

        /// <summary>"241796\civil\_backup" - a short description of where a drawing's copies go.</summary>
        public static string Where(string drawing)
        {
            Backups.Where w = Backups.Locate(drawing);
            return w.Job != null && w.InCivil ? $"{w.Job}\\civil\\{Backups.FolderName}" : w.Root;
        }

        public static List<Backups.Copy> List(string drawing)
        {
            try { return Backups.List(drawing); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Swallowed(ex); return new(); }
        }

        /// <summary>Opens a copy read-only, to look at (or copy things out of).</summary>
        public static void OpenCopy(string copy)
        {
            try { AcApp.DocumentManager.MdiActiveDocument = DocumentCollectionExtension.Open(AcApp.DocumentManager, copy, true); }
            catch (Exception ex) { MessageBox.Show("Couldn't open the copy: " + ex.Message, "CoWork"); }
        }

        public static void ShowFolder(string drawing)
        {
            string dir = Backups.FolderFor(drawing);
            try { Process.Start(new ProcessStartInfo("explorer.exe", Directory.Exists(dir) ? $"\"{dir}\"" : $"\"{Backups.Locate(drawing).Root}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Swallowed(ex); }
        }

        /// <summary>
        /// Puts a copy back as the drawing: closes it on this PC (unsaved changes are lost - it asks first), keeps the
        /// current version as a "before restore" copy, restores, and reopens it.
        /// </summary>
        public static void Restore(string drawing, Backups.Copy copy)
        {
            string name = Path.GetFileName(drawing);
            if (LiveDrafting.IsActive && LiveDrafting.DrawingPath is { } live && SamePath(live, drawing))
            {
                MessageBox.Show($"{name} is in a CoWork session. End or leave CoWork first, then restore.", "CoWork");
                return;
            }
            Document[] mine = AcApp.DocumentManager.Cast<Document>().Where(d => Path.IsPathRooted(d.Name) && SamePath(LiveDrafting.Unc(d.Name), drawing)).ToArray();
            bool editingHere = mine.Any(d => !d.IsReadOnly);
            if (Backups.InUse(drawing) && !editingHere)
            {
                string who = ChatHub.OthersIn(drawing).FirstOrDefault()?.Name ?? "someone";
                MessageBox.Show($"{name} is open for editing ({who} has it). Ask them to close it, then restore.", "CoWork");
                return;
            }
            string question = $"Put {name} back to the copy from {copy.Time:ddd d MMM HH:mm}" +
                              (copy.Reason.Length > 0 ? $" ({copy.Reason}{(copy.Who.Length > 0 ? ", " + copy.Who : "")})" : "") + "?\n\n" +
                              "The drawing as it is now is kept as a \"before restore\" copy, so this can be undone." +
                              (mine.Length > 0 ? "\nIt will close and reopen on this PC - unsaved changes are lost." : "");
            if (MessageBox.Show(question, "CoWork - restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            void Go(object? sender, EventArgs e)
            {
                AcApp.Idle -= Go;
                foreach (Document d in mine)
                    try { d.CloseAndDiscard(); } catch (Exception ex) { Log.Swallowed(ex); }
                // AutoCAD takes a moment to let go of its lock file.
                for (int i = 0; i < 20 && Backups.InUse(drawing) && editingHere; i++) System.Threading.Thread.Sleep(150);
                try
                {
                    Backups.Restore(drawing, copy.File, ChatHub.Settings.Name, DateTime.Now);
                    Log.Info($"Restored {drawing} from {copy.File}");
                    Commands.Write($"\n[CoWork] {name} restored to the copy from {copy.Time:HH:mm}. The version before is kept in {Where(drawing)}.");
                    AcApp.DocumentManager.MdiActiveDocument = DocumentCollectionExtension.Open(AcApp.DocumentManager, drawing, false);
                }
                catch (Exception ex)
                {
                    Log.Error("Restore", ex);
                    MessageBox.Show("Couldn't restore: " + ex.Message, "CoWork");
                }
                Changed?.Invoke();
            }
            AcApp.Idle += Go;
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
