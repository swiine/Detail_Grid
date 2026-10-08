using System.Security.Cryptography;
using System.Text;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>"Interpret with AI": the API key, readiness checks and the call itself.</summary>
internal static class AiService
{
    private static readonly string KeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "ai-key.dat");

    private static string PluginFolder => Path.GetDirectoryName(typeof(AiService).Assembly.Location) ?? "";

    /// <summary>The saved key (encrypted for this Windows user), else the ANTHROPIC_API_KEY environment variable.</summary>
    public static string? ApiKey => SavedKey ?? NullIfBlank(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    public static string? SavedKey
    {
        get
        {
            try
            {
                if (!File.Exists(KeyPath)) return null;
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser);
                return NullIfBlank(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public static void SaveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            if (File.Exists(KeyPath)) File.Delete(KeyPath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>True when AI is switched on, installed and has a key; otherwise <paramref name="reason"/> says what's missing.</summary>
    public static bool IsReady(AiSettings settings, out string reason)
    {
        reason = "";
        if (!settings.Enabled) { reason = "Interpret with AI is off."; return false; }
        if (AiConverter.FindFolder(PluginFolder) is null) { reason = "The AI component isn't installed (Contents\\ai is missing from the bundle)."; return false; }
        if (ApiKey is null) { reason = "No AI API key yet. Add one in AI settings (PAVEAI)."; return false; }
        return true;
    }

    /// <summary>Converts free text to the build-up format using the standard's (or built-in) AI prompt.</summary>
    public static Task<string> InterpretAsync(string text, AiSettings settings, CadStandard standard, CancellationToken cancellationToken = default)
    {
        var folder = AiConverter.FindFolder(PluginFolder)
                     ?? throw new InvalidOperationException("The AI component isn't installed.");
        return AiConverter.ConvertAsync(folder, ApiKey ?? "", settings.Model, AiPrompt.Load(standard.AiPromptFile), text, cancellationToken);
    }

    /// <summary>Blocking version for command-line commands (runs off AutoCAD's thread so it can't deadlock).</summary>
    public static string Interpret(string text, AiSettings settings, CadStandard standard) =>
        Task.Run(() => InterpretAsync(text, settings, standard)).GetAwaiter().GetResult();

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
