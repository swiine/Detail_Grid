using System.Text.RegularExpressions;

namespace CoWork.Core;

/// <summary>
/// @mentions: "@Sam", "@SamSmith" or "@ssmith" in a message reaches that person with a pop-up, even if they've
/// turned ordinary pop-ups off. A mention matches someone's login, their whole name without spaces, or their first name.
/// </summary>
public static class Mentions
{
    private static readonly Regex At = new(@"(?<![\w@])@([\p{L}\p{N}][\p{L}\p{N}._-]*)", RegexOptions.Compiled);

    /// <summary>The words after each @ in a message.</summary>
    public static List<string> Tokens(string text) =>
        At.Matches(text).Select(m => m.Groups[1].Value.TrimEnd('.', '-', '_')).Where(t => t.Length > 0).ToList();

    /// <summary>Whether a message mentions this person.</summary>
    public static bool IsFor(string text, string login, string name)
    {
        if (!text.Contains('@')) return false;
        string whole = Squash(name), first = Squash(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
        foreach (string t in Tokens(text))
        {
            string s = Squash(t);
            if (s.Length == 0) continue;
            if (string.Equals(t, login, StringComparison.OrdinalIgnoreCase) || s == whole || (first.Length > 0 && s == first)) return true;
        }
        return false;
    }

    /// <summary>What to type to mention someone: their first name, or their whole name if another person shares it.</summary>
    public static string HandleFor(string name, IEnumerable<string> everyonesNames)
    {
        string first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        int same = everyonesNames.Count(n => Squash(n.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? n) == Squash(first));
        return "@" + (same > 1 ? string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : first);
    }

    private static string Squash(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
