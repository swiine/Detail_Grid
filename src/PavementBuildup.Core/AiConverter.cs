using System.Reflection;
using System.Runtime.Loader;

namespace PavementBuildup.Core;

/// <summary>Per-user "Interpret with AI" preferences (the API key is stored separately, encrypted).</summary>
public sealed class AiSettings
{
    /// <summary>Send entered text to the AI with the build-up prompt before reading it.</summary>
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "claude-opus-5-5";
}

/// <summary>
/// Runs the AI conversion in PavementBuildup.Ai.dll, loaded into its own AssemblyLoadContext from the
/// bundle's "ai" folder. The Anthropic SDK needs newer System.Text.Json etc. than AutoCAD's process has
/// loaded; isolating it avoids version clashes. Only strings and a Task cross the boundary.
/// </summary>
public static class AiConverter
{
    public const string FolderName = "ai";
    private const string AssemblyFile = "PavementBuildup.Ai.dll";

    private static readonly object Gate = new();
    private static MethodInfo? _convert;
    private static string? _loadedFrom;

    /// <summary>The ai folder next to <paramref name="pluginFolder"/>, or null when the AI part isn't installed.</summary>
    public static string? FindFolder(string pluginFolder)
    {
        var folder = Path.Combine(pluginFolder, FolderName);
        return File.Exists(Path.Combine(folder, AssemblyFile)) ? folder : null;
    }

    /// <summary>
    /// Converts <paramref name="text"/> to the one-line build-up format. Throws <see cref="InvalidOperationException"/>
    /// with a user-facing message (its Data["kind"] says why) when the AI can't be used.
    /// </summary>
    public static async Task<string> ConvertAsync(string aiFolder, string apiKey, string model, string systemPrompt, string text, CancellationToken cancellationToken = default)
    {
        var convert = Load(aiFolder);
        var task = (Task<string>)convert.Invoke(null, new object?[] { apiKey, model, systemPrompt, text, cancellationToken })!;
        return await task.ConfigureAwait(false);
    }

    private static MethodInfo Load(string aiFolder)
    {
        lock (Gate)
        {
            if (_convert is not null && string.Equals(_loadedFrom, aiFolder, StringComparison.OrdinalIgnoreCase))
                return _convert;

            var path = Path.Combine(aiFolder, AssemblyFile);
            if (!File.Exists(path))
                throw Unavailable($"The AI component isn't installed ({path} is missing).");

            var context = new IsolatedContext(path);
            var assembly = context.LoadFromAssemblyPath(path);
            var type = assembly.GetType("PavementBuildup.Ai.BuildupInterpreter", throwOnError: true)!;
            _convert = type.GetMethod("ConvertAsync", BindingFlags.Public | BindingFlags.Static)
                       ?? throw Unavailable("The AI component is out of date (ConvertAsync not found).");
            _loadedFrom = aiFolder;
            return _convert;
        }
    }

    private static InvalidOperationException Unavailable(string message)
    {
        var ex = new InvalidOperationException(message);
        ex.Data["kind"] = "missing";
        return ex;
    }

    /// <summary>Resolves the AI assembly's dependencies from its own folder (via its .deps.json), framework from the host.</summary>
    private sealed class IsolatedContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedContext(string mainAssemblyPath) : base("PavementBuildup.Ai", isCollectible: false) =>
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}

/// <summary>The instructions given to the AI: a company file if the standard names one, else the built-in prompt.</summary>
public static class AiPrompt
{
    public static string Load(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return File.ReadAllText(customPath);
        return BuiltIn;
    }

    /// <summary>"AI Pavement Build-up Prompt.txt" from the repository, embedded at build time.</summary>
    public static string BuiltIn { get; } = ReadEmbedded();

    private static string ReadEmbedded()
    {
        using var stream = typeof(AiPrompt).Assembly.GetManifestResourceStream("AiPrompt.txt")
                           ?? throw new InvalidOperationException("Built-in AI prompt is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
