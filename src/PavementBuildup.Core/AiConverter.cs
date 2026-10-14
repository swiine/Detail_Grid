using System.Reflection;
using System.Runtime.Loader;

namespace PavementBuildup.Core;

public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
}

public static class AiConverter
{
    public const string FolderName = "ai";
    private const string AssemblyFile = "PavementBuildup.Ai.dll";

    private static readonly object Gate = new();
    private static MethodInfo? _convert;
    private static string? _loadedFrom;

    public static string? FindFolder(string pluginFolder)
    {
        var folder = Path.Combine(pluginFolder, FolderName);
        return File.Exists(Path.Combine(folder, AssemblyFile)) ? folder : null;
    }

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

public static class AiPrompt
{
    public static string Load(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            return File.ReadAllText(customPath);
        return BuiltIn;
    }

    public static string ForChat(string prompt, string text) => prompt.TrimEnd() + Environment.NewLine + Environment.NewLine + text.Trim();

    public static string CleanReply(string reply)
    {
        var lines = (reply ?? "").Replace("\r", "").Split('\n').Where(l => !l.TrimStart().StartsWith("```")).ToList();
        var text = string.Join("\n", lines).Trim();
        foreach (var prefix in new[] { "Output:", "Converted:", "Result:" })
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                text = text[prefix.Length..].Trim();
        return text.Trim('`', '"', '\u201C', '\u201D').Trim();
    }

    public static string BuiltIn { get; } = ReadEmbedded();

    private static string ReadEmbedded()
    {
        using var stream = typeof(AiPrompt).Assembly.GetManifestResourceStream("AiPrompt.txt")
                           ?? throw new InvalidOperationException("Built-in AI prompt is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
