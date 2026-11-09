using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CoWork.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CoWork
{
    /// <summary>
    /// Opening a drawing someone is CoWorking in: instead of leaving you on a read-only copy, offer to join their
    /// session (a card with Join - nothing that blocks Civil 3D). Only for sessions whose host is still there.
    /// </summary>
    internal static class JoinOffer
    {
        private static readonly HashSet<string> Offered = new(StringComparer.OrdinalIgnoreCase);

        public static void Hook() => AcApp.DocumentManager.DocumentCreated += (_, e) => Check(e.Document?.Name);

        private static void Check(string? name)
        {
            if (name == null || !Path.IsPathRooted(name) || ChatHub.DataFolder.Length == 0) return;
            string? joining = LiveDrafting.Joining;
            string drawing = LiveDrafting.Unc(name);
            Task.Run(() => LiveFolder.ActiveSessions(ChatHub.DataFolder).FirstOrDefault(s => Paths.Same(s.Drawing, drawing)))
                .ContinueWith(t =>
                {
                    if (t.Exception != null || t.Result is not { } s || s.Id == LiveDrafting.SessionId || s.Id == joining || !Offered.Add(s.Id)) return;
                    string file = Path.GetFileName(s.Drawing);
                    Commands.Write($"\n[CoWork] {s.HostName} is CoWorking in {file} - your copy is read-only. Join their session to work in it together (CWJOIN {s.Id}).");
                    CiUi.Announce($"{s.HostName} is CoWorking in {file}",
                        LiveDrafting.IsActive ? "Join their session? (You'll leave the one you're in.)" : "Join their session to work in it together, instead of a read-only copy.",
                        "Join", () => Commands.Run("CWJOIN " + s.Id));
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
