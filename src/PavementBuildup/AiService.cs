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

    /// <summary>
    /// Anthropic account sign-in made with "ant auth login": stored under %APPDATA%\Anthropic (or
    /// ANTHROPIC_CONFIG_DIR) and picked up and refreshed by the Anthropic SDK itself when no key is given.
    /// </summary>
    public static string AccountFolder =>
        NullIfBlank(Environment.GetEnvironmentVariable("ANTHROPIC_CONFIG_DIR"))
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anthropic");

    /// <summary>Profile names that have stored sign-in credentials.</summary>
    public static IReadOnlyList<string> AccountProfiles
    {
        get
        {
            var dir = Path.Combine(AccountFolder, "credentials");
            try
            {
                return Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList()
                    : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>Which credential the AI will use, for display.</summary>
    public static string CredentialDescription =>
        SavedKey is { } k ? $"Saved API key ending …{k[^Math.Min(4, k.Length)..]}"
        : NullIfBlank(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) is not null ? "API key from the ANTHROPIC_API_KEY environment variable"
        : AccountProfiles.Count > 0 ? $"Signed in with your Anthropic account ({string.Join(", ", AccountProfiles)})"
        : "Not set up: paste an API key or sign in with your Anthropic account";

    /// <summary>True when AI is switched on, installed and has a key or account sign-in; otherwise <paramref name="reason"/> says what's missing.</summary>
    public static bool IsReady(AiSettings settings, out string reason)
    {
        reason = "";
        if (!settings.Enabled) { reason = "Interpret with AI is off."; return false; }
        if (AiConverter.FindFolder(PluginFolder) is null) { reason = "The AI component isn't installed (Contents\\ai is missing from the bundle)."; return false; }
        if (ApiKey is null && AccountProfiles.Count == 0) { reason = "AI isn't set up yet. Run PAVEAI to paste an API key or sign in with your Anthropic account."; return false; }
        return true;
    }

    /// <summary>Starts the one-time browser sign-in ("ant auth login"). False when the ant CLI isn't installed.</summary>
    public static bool StartAccountSignIn()
    {
        try
        {
            // cmd /k keeps the window open so the user sees "logged in" (or any error).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/k ant auth login") { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool AntInstalled =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(dir => { try { return File.Exists(Path.Combine(dir.Trim(), "ant.exe")); } catch (ArgumentException) { return false; } });

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
