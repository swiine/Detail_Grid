using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;

namespace TTW.PluginTools
{
    public sealed class RunResult
    {
        public bool Success { get; init; }
        /// <summary>Sent to the command line rather than run here (Session commands).</summary>
        public bool StartedOnCommandLine { get; init; }
        public string Message { get; init; }
        public TimeSpan Elapsed { get; init; }
    }

    /// <summary>
    /// Loads plugin DLLs and runs their commands.
    ///
    /// Loading: see PluginHost. Each plugin is copied locally and loaded into its own unloadable
    /// context, so the copy on A: stays unlocked, and when it's updated the next run loads the
    /// new version - no restart.
    ///
    /// Running: the command method is called inside one outer transaction. Its own
    /// transactions nest inside that one, so if it throws, or the tool is read-only,
    /// aborting the outer transaction rolls back everything it did.
    /// </summary>
    public static class ToolRunner
    {
        // Plugins already in Civil 3D before Plugin Tools got to them (NETLOAD, startup suite),
        // with the stamp they had then. .NET can't unload those, so they're used as they are.
        private static readonly Dictionary<string, (Assembly Assembly, string Stamp)> External =
            new Dictionary<string, (Assembly, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Folders the scanner needs to read plugin attributes: .NET, WPF, AutoCAD and Civil 3D.</summary>
        public static IEnumerable<string> ReferenceDirs()
        {
            yield return RuntimeEnvironment.GetRuntimeDirectory();
            yield return Path.GetDirectoryName(typeof(System.Windows.Window).Assembly.Location);
            string acad = Path.GetDirectoryName(typeof(CommandMethodAttribute).Assembly.Location);
            yield return acad;
            yield return Path.Combine(acad, "C3D");
        }

        /// <summary>
        /// Loads the tool's DLL if it isn't already, or reloads it if it has changed since.
        /// Must be called in command context. Returns a warning (or null) to show the user, and
        /// in <paramref name="note"/> anything worth a line on the command line.
        /// </summary>
        public static Assembly EnsureLoaded(ToolInfo tool, Editor ed, out string warning, out string note)
        {
            warning = null;
            note = null;

            var plugin = PluginHost.Find(tool.DllPath);
            if (plugin != null)
            {
                if (!PluginHost.IsOutOfDate(plugin)) return plugin.Assembly;
                plugin = PluginHost.Load(tool.DllPath, ed);
                note = $"Loaded the updated version of {plugin.Name}.";
                return plugin.Assembly;
            }

            if (External.TryGetValue(tool.DllPath, out var external))
            {
                if (external.Stamp != Stamp(tool.DllPath))
                    warning = "This plugin has been updated, but it was loaded outside Plugin Tools (NETLOAD or the startup suite), " +
                              "so .NET can't swap it. Restart Civil 3D and load it only through Plugin Tools to get live reloading.";
                return external.Assembly;
            }

            // Already in Civil 3D under the same name, loaded some other way?
            // Use that one: a second copy would clash with its commands.
            var name = AssemblyName.GetAssemblyName(tool.DllPath);
            var existing = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!SamePath(existing.Location, tool.DllPath))
                    warning = $"Using the copy of {name.Name} that was already loaded from {existing.Location}. " +
                              "It was loaded outside Plugin Tools, so it can't be unloaded or reloaded until Civil 3D restarts.";
                External[tool.DllPath] = (existing, Stamp(tool.DllPath));
                return existing;
            }

            return PluginHost.Load(tool.DllPath, ed).Assembly;
        }

        /// <summary>
        /// Runs a tool. Must be called in command context: from a command, or inside
        /// DocumentCollection.ExecuteInCommandContextAsync (the window does that).
        /// Never throws - failures come back in the result.
        /// </summary>
        public static RunResult Run(ToolInfo tool, Document doc)
        {
            var ed = doc.Editor;
            var sw = Stopwatch.StartNew();
            ed.WriteMessage($"\nTTW Plugin Tools: running {tool.Name}{(tool.IsRead ? " (read-only)" : "")}");

            MethodInfo method;
            Type type;
            try
            {
                var asm = EnsureLoaded(tool, ed, out string warning, out string note);
                if (warning != null) tool.Warning = warning;
                if (note != null) ed.WriteMessage($"\nTTW Plugin Tools: {note}");
                type = asm.GetType(tool.TypeName, throwOnError: true);
                method = type.GetMethod(tool.MethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    binder: null, Type.EmptyTypes, modifiers: null)
                    ?? throw new MissingMethodException(tool.TypeName, tool.MethodName);
            }
            catch (Exception ex)
            {
                return Fail(ed, sw, $"Couldn't load {Path.GetFileName(tool.DllPath)}: {Unwrap(ex).Message}", Unwrap(ex), rolledBack: false);
            }

            if (tool.RunAsCommand)
            {
                // Session commands run outside any drawing; just start them
                doc.SendStringToExecute("_." + tool.CommandName + " ", true, false, true);
                return new RunResult { Success = true, StartedOnCommandLine = true, Message = $"Started {tool.CommandName} on the command line." };
            }

            try
            {
                object target = method.IsStatic ? null : Activator.CreateInstance(type, nonPublic: true);
                using (doc.LockDocument())
                using (Transaction outer = doc.Database.TransactionManager.StartTransaction())
                {
                    try
                    {
                        method.Invoke(target, null);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    }

                    if (tool.IsRead) outer.Abort();
                    else outer.Commit();
                }
            }
            catch (Exception ex)
            {
                // Leaving the using block without Commit aborted the outer transaction
                return Fail(ed, sw, Unwrap(ex).Message, Unwrap(ex), rolledBack: true);
            }

            sw.Stop();
            string msg = $"Done in {Seconds(sw.Elapsed)}" + (tool.IsRead ? " (read-only - nothing in the drawing was kept)" : "");
            ed.WriteMessage($"\nTTW Plugin Tools: {msg}");
            return new RunResult { Success = true, Message = msg, Elapsed = sw.Elapsed };
        }

        private static RunResult Fail(Autodesk.AutoCAD.EditorInput.Editor ed, Stopwatch sw, string reason, Exception ex, bool rolledBack)
        {
            sw.Stop();
            string msg = rolledBack
                ? $"Failed after {Seconds(sw.Elapsed)} - changes in this drawing were rolled back. {reason}"
                : reason;
            ed.WriteMessage($"\nTTW Plugin Tools: {msg}");
            if (ex != null && ex is not Autodesk.AutoCAD.Runtime.Exception { ErrorStatus: ErrorStatus.UserBreak })
                ed.WriteMessage($"\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return new RunResult { Success = false, Message = msg, Elapsed = sw.Elapsed };
        }

        private static Exception Unwrap(Exception ex) =>
            ex is TargetInvocationException { InnerException: not null } t ? t.InnerException : ex;

        internal static string Seconds(TimeSpan t) => t.TotalSeconds < 10 ? $"{t.TotalSeconds:0.0} s" : $"{t.TotalSeconds:0} s";

        private static string Stamp(string file) => File.GetLastWriteTimeUtc(file).Ticks.ToString();

        private static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
