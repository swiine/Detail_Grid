using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>
    /// Time on job (off unless turned on in Settings). Once a minute: if Civil 3D is the window in front, you've used
    /// the keyboard or mouse in the last 5 minutes, and the drawing in front of you is in a job folder (6-digit job
    /// number), that minute goes to the job. Saved only on this PC (see <see cref="TimeLog"/>).
    /// </summary>
    internal static class TimeTracker
    {
        private static DispatcherTimer? _timer;
        private static readonly TimeLog Log = new(TimeLog.DefaultFolder);
        private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

        public static void Start()
        {
            _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick -= Tick;
            _timer.Tick += Tick;
            _timer.Start();
        }

        private static void Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!ChatHub.Settings.TimeTracking || IdleFor() > Idle || !CivilInFront()) return;
                string? drawing = AcApp.DocumentManager.MdiActiveDocument?.Name;
                if (drawing == null || !Path.IsPathRooted(drawing)) return;
                if (Backups.Locate(LiveDrafting.Unc(drawing)).Job is not { } job) return;
                DateOnly today = DateOnly.FromDateTime(DateTime.Today);
                Task.Run(() => { try { Log.Add(today, job); } catch (Exception ex) { CoWork.Core.Log.Swallowed(ex); } });
            }
            catch (Exception ex) { CoWork.Core.Log.Swallowed(ex); }
        }

        private static string SummaryShown => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork", "time-summary.txt");

        /// <summary>Once a day, at the first start: your hours on your last working day, with a button to copy them for a timesheet.</summary>
        public static void DailySummary()
        {
            if (!ChatHub.Settings.TimeTracking) return;
            try
            {
                string today = DateTime.Today.ToString("yyyy-MM-dd");
                if (File.Exists(SummaryShown) && File.ReadAllText(SummaryShown).Trim() == today) return;
                Directory.CreateDirectory(Path.GetDirectoryName(SummaryShown)!);
                File.WriteAllText(SummaryShown, today);
                if (Log.LastWorkingDay(DateOnly.FromDateTime(DateTime.Today)) is not { } last) return;
                string when = last.Day == DateOnly.FromDateTime(DateTime.Today.AddDays(-1)) ? "Yesterday" : last.Day.ToString("dddd d MMM");
                string summary = TimeLog.Summary(last.Entries);
                Commands.Write($"\n[CoWork] {when}: {summary}");
                CiUi.Announce($"{when}: time on jobs", summary, "Copy for timesheet", () => CopyDay(last.Entries));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CoWork.Core.Log.Swallowed(ex); }
        }

        /// <summary>Copies your last working day as "job[tab]hours" lines. Returns what happened, to show.</summary>
        public static string CopyLastDay()
        {
            if (Log.LastWorkingDay(DateOnly.FromDateTime(DateTime.Today)) is not { } last) return "Nothing recorded in the last week.";
            return CopyDay(last.Entries) ? $"Copied {last.Day:ddd d MMM} - paste it into your timesheet." : "Couldn't copy - try again.";
        }

        private static bool CopyDay(System.Collections.Generic.List<TimeLog.Entry> entries)
        {
            try { System.Windows.Clipboard.SetText(TimeLog.Timesheet(entries)); return true; }
            catch (Exception ex) { CoWork.Core.Log.Swallowed(ex); return false; }
        }

        /// <summary>"Today: 241796 3.5 h ... This week: ..."</summary>
        public static string Report()
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            string t = TimeLog.Summary(Log.Between(today, today)), w = TimeLog.Summary(Log.Between(monday, today));
            string y = Log.LastWorkingDay(today) is { } last ? $"{(last.Day == today.AddDays(-1) ? "Yesterday" : last.Day.ToString("ddd d MMM"))}: {TimeLog.Summary(last.Entries)}\n" : "";
            return $"{y}Today: {(t.Length > 0 ? t : "nothing yet")}\nThis week: {(w.Length > 0 ? w : "nothing yet")}";
        }

        /// <summary>Saves a month as a CSV in Documents and shows it in Explorer. Returns what happened, to show.</summary>
        public static string Export(DateOnly anyDayInMonth)
        {
            var first = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"CoWork time {first:yyyy-MM}.csv");
            try { File.WriteAllText(file, TimeLog.Csv(Log.Between(first, first.AddMonths(1).AddDays(-1)))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"Couldn't save {Path.GetFileName(file)} - is it open in Excel? Close it and try again."; }
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); } catch (Exception ex) { CoWork.Core.Log.Swallowed(ex); }
            return "Saved: " + file;
        }

        // Windows: how long since the last key or mouse input, and whether Civil 3D is the window in front.
        [StructLayout(LayoutKind.Sequential)]
        private struct LastInput { public uint Size; public uint Time; }

        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        private static TimeSpan IdleFor()
        {
            var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
        }

        private static bool CivilInFront()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)Environment.ProcessId;
        }
    }
}
