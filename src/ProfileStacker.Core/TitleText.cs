using System.Text.RegularExpressions;

namespace ProfileStacker.Core;

/// <summary>
/// Title text for the new views: the long view's title followed by "- CONTINUED". Civil 3D wants every
/// profile view name to be different, so the copies can't all be called "MR01 - WAREHOUSE ALIGNMENT".
/// Instead their style's title shows the long view's name as plain text, with the suffix after it.
/// </summary>
public static class TitleText
{
    // <[Graph View Name(CP)]>, with any format modifiers inside the brackets.
    private static readonly Regex ViewNameField = new(@"<\[\s*Graph\s+View\s+Name\s*(\(([^)\]]*)\))?\s*\]>", RegexOptions.IgnoreCase);

    /// <summary>
    /// The style's title text with the view name field replaced by <paramref name="viewName"/> (cased as
    /// the field asked) and <paramref name="suffix"/> after it, unless the title already ends that way.
    /// </summary>
    public static string Continued(string titleFormat, string viewName, string suffix)
    {
        string text = ViewNameField.Replace(titleFormat, m =>
        {
            string modifiers = m.Groups[2].Value.ToUpperInvariant();
            return modifiers.Contains("CU") ? viewName.ToUpperInvariant()
                 : modifiers.Contains("CL") ? viewName.ToLowerInvariant()
                 : viewName;
        });
        return Naming.WithSuffix(text, suffix);
    }

    /// <summary>Name for the style copy: one per long view, so re-running reuses it.</summary>
    public static string StyleName(string styleName, string viewName, string suffix) =>
        $"{styleName} ({viewName} {suffix.Trim()})";
}
