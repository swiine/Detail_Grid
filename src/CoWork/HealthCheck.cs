using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>CWCHECK: everything CoWork depends on, checked in one go (safe to run off the UI thread).</summary>
    internal static class HealthCheck
    {
        public static List<Health.Result> Run(string? activeDrawing)
        {
            var r = new List<Health.Result>
            {
                new(Health.Level.Ok, "CoWork", $"Version {Plugin.Version} on {Environment.MachineName} ({Environment.UserDomainName}\\{Environment.UserName})"),
                new(LicenceGate.Ok ? Health.Level.Ok : Health.Level.Fail, "Licence", LicenceGate.Describe(LicenceGate.State, LicenceGate.Info)),
            };
            r.Add(Updates());
            var (folder, clock) = Health.CheckFolder(ChatHub.DataFolder);
            r.Add(folder);
            if (clock != null) r.Add(clock);
            r.Add(!ServerLink.Configured
                ? new Health.Result(Health.Level.Ok, "Office server", "None set (sessions start their own)")
                : ServerLink.Connected
                    ? new Health.Result(Health.Level.Ok, "Office server", ServerLink.Status)
                    : new Health.Result(Health.Level.Warn, "Office server", ServerLink.Status));
            if (ServerLink.Configured) r.Add(Health.Probe("Office server connection", ChatHub.Settings.Server!));
            if (LiveDrafting.IsActive)
            {
                r.Add(new Health.Result(SessionChannel.Fast ? Health.Level.Ok : Health.Level.Warn, "CoWork session", SessionChannel.Status));
                if (LiveDrafting.SessionServer is { } ss)
                    r.Add(LiveDrafting.IsHost
                        ? new Health.Result(Health.Level.Ok, "Session server", $"This PC is the session server ({ss}). If guests see \"Shared folder only\", this PC's firewall is blocking them - run Allow-CoWork-Sessions.cmd here as admin (or ask IT).")
                        : Health.Probe("Session server", ss));
                else if (!ServerLink.Connected)
                    r.Add(new Health.Result(Health.Level.Warn, "Session server", "The host didn't start one (their CoWork may be older than 1.4) - this session uses the shared folder."));
            }
            else r.Add(new Health.Result(Health.Level.Ok, "Session server", "Starts by itself when a CoWork session starts (run this check during a session to test it)."));
            r.Add(CivilSync.Available
                ? new Health.Result(Health.Level.Ok, "Civil 3D", "Civil 3D objects available (points and point groups sync)")
                : new Health.Result(Health.Level.Warn, "Civil 3D", "Running in plain AutoCAD - Civil 3D objects won't sync"));
            if (activeDrawing != null && Path.IsPathRooted(activeDrawing))
            {
                Backups.Where w = Backups.Locate(LiveDrafting.Unc(activeDrawing));
                r.Add(new Health.Result(w.InCivil ? Health.Level.Ok : Health.Level.Warn, "Safety copies",
                    w.Job != null ? $"Job {w.Job}: copies go in {w.Root}" : $"No 6-digit job number in the path: copies go in {w.Root}"));
            }
            return r;
        }

        /// <summary>Which version is running, which is published, and what the loader said last time Civil 3D started.</summary>
        private static Health.Result Updates()
        {
            try
            {
                string? last = null;
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork", "loader.log");
                if (File.Exists(log)) last = File.ReadAllLines(log).LastOrDefault();
                string? published = UpdateCheck.Published();
                string text = $"Running {Plugin.Version}" + (published != null ? $", published {published}" : ", release folder not reachable") +
                              (last != null ? $" - last start: {last}" : "");
                bool behind = published != null && Version.TryParse(published, out Version? p) && Version.TryParse(Plugin.Version, out Version? v) && p > v;
                return new Health.Result(published == null || behind ? Health.Level.Warn : Health.Level.Ok, "Updates",
                    text + (behind ? " - restart Civil 3D to update (it's copied to this PC once ready)" : ""));
            }
            catch (Exception ex) { Log.Swallowed(ex); return new Health.Result(Health.Level.Warn, "Updates", "Couldn't check: " + ex.Message); }
        }

        public static string ActiveDrawing()
        {
            try { return AcApp.DocumentManager.MdiActiveDocument?.Name ?? ""; } catch (Exception ex) { Log.Swallowed(ex); return ""; }
        }
    }
}
