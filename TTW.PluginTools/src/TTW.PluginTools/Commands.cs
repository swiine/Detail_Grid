using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TTW.PluginTools.Commands))]

namespace TTW.PluginTools
{
    public class Commands
    {
        private static PluginWindow _window;

        /// <summary>Opens the Plugin Tools window (or brings it back to the front).</summary>
        [CommandMethod("TTWPLUGINS")]
        public void ShowWindow()
        {
            if (_window == null)
            {
                _window = new PluginWindow();
                _window.Closed += (s, e) => _window = null;
                AcApp.ShowModelessWindow(_window);
            }
            else
            {
                if (_window.WindowState == System.Windows.WindowState.Minimized)
                    _window.WindowState = System.Windows.WindowState.Normal;
                _window.Show();
                _window.Activate();
                _window.Reload();
            }
        }

        /// <summary>
        /// Runs one tool without the window - this is what ribbon buttons call:
        ///   ^C^C_TTWPLUGINRUN;COUNTBLOCKS;
        /// Accepts a command name, or "Folder\File.dll|COMMAND" when two DLLs share a name.
        /// </summary>
        [CommandMethod("TTWPLUGINRUN")]
        public void RunTool()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var prompt = new PromptStringOptions("\nPlugin tool to run (command name, or Folder\\File.dll|COMMAND): ") { AllowSpaces = true };
            var answer = ed.GetString(prompt);
            if (answer.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(answer.StringResult)) return;
            string wanted = answer.StringResult.Trim().Trim('"');

            var config = PluginConfig.Load(out _);
            string folder = UserSettings.EffectiveFolder(config, UserSettings.Load());
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                ed.WriteMessage($"\nTTW Plugin Tools: can't reach the tools folder: {folder}");
                return;
            }

            var tools = ToolScanner.Scan(folder, config.ArchivePrefix, ToolRunner.ReferenceDirs()).Tools;
            string file = null, command = wanted;
            int bar = wanted.LastIndexOf('|');
            if (bar >= 0) { file = wanted[..bar]; command = wanted[(bar + 1)..]; }

            var matches = tools.Where(t => string.Equals(t.CommandName, command, StringComparison.OrdinalIgnoreCase)
                                        && (file == null
                                            || string.Equals(t.RelativeDllPath, file, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(t.DllPath, file, StringComparison.OrdinalIgnoreCase)))
                               .OrderBy(t => t.IsArchived)   // prefer the live copy over an archived one
                               .ToList();
            if (matches.Count == 0)
            {
                ed.WriteMessage($"\nTTW Plugin Tools: can't find '{wanted}' in {folder}");
                return;
            }

            // Already in command context here, so run directly
            ToolRunner.Run(matches[0], doc);
        }

        /// <summary>Lets the LISP loader check the plugin really loaded: (ttwplugins-loaded-p)</summary>
        [LispFunction("ttwplugins-loaded-p")]
        public object LoadedP(ResultBuffer args) => true;
    }
}
