using System;
using System.IO;

namespace TTW.PluginTools
{
    /// <summary>
    /// One tool in the list: a [CommandMethod] found in a plugin DLL.
    /// Name, description, group and read-only come from standard attributes on the
    /// command method (see README), so plugins don't need to reference this launcher.
    /// </summary>
    public sealed class ToolInfo
    {
        public string Name { get; init; }
        public string Description { get; init; }
        public string Group { get; init; } = "";
        /// <summary>[ReadOnly(true)]: anything the tool changes is thrown away.</summary>
        public bool IsRead { get; init; }
        /// <summary>Session commands can't be wrapped in a transaction, so they're sent to the command line.</summary>
        public bool RunAsCommand { get; init; }
        public bool IsArchived { get; init; }

        public string CommandName { get; init; }
        public string DllPath { get; init; }
        public string RelativeDllPath { get; init; }
        public string TypeName { get; init; }
        public string MethodName { get; init; }
        public string VideoPath { get; init; }
        public DateTime Modified { get; init; }

        /// <summary>Set after a run, e.g. when the DLL changed since it was loaded.</summary>
        public string Warning { get; set; }

        public string Key => DllPath + "|" + CommandName;
        public bool HasVideo => VideoPath != null;
        public bool HasWarning => !string.IsNullOrEmpty(Warning);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public string DescriptionDisplay => HasDescription
            ? Description
            : "No description yet. Add one to the command in the plugin's code:\n[Description(\"what it does\")]\n\nor put a " + CommandName + ".txt file next to the DLL.";

        public string ModeDisplay => RunAsCommand
            ? "Runs on the command line - changes can be undone with U, but aren't rolled back if it fails."
            : IsRead ? "Read-only: looks at the drawing but doesn't change it." : null;
        public bool HasModeDisplay => ModeDisplay != null;

        public string Details => $"{CommandName}  ·  {RelativeDllPath}  ·  Modified {Modified:d MMM yyyy, h:mm tt}";

        public bool Matches(string search) =>
            string.IsNullOrWhiteSpace(search)
            || Contains(Name, search) || Contains(Description, search) || Contains(CommandName, search) || Contains(Group, search)
            || Contains(Path.GetFileName(DllPath), search);

        private static bool Contains(string s, string find) =>
            s != null && s.IndexOf(find.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
