using System.Text.RegularExpressions;

namespace MPSellerTools.Core.Platform;

/// <summary>
/// Normalizes and validates a company slug before it is ever used to build a
/// database name, file path, or URL segment (brief §10 — no unvalidated
/// strings may reach ports, database identifiers, or paths). A valid slug
/// only ever contains lowercase ASCII letters, digits, and single hyphens,
/// so it is always safe to embed directly in a SQL Server database name,
/// a Windows directory name, or a URL path segment without further escaping.
/// </summary>
public static partial class SlugValidator
{
    private const int MinLength = 3;
    private const int MaxLength = 40;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidPattern();

    /// <summary>Lowercases, trims, and collapses whitespace/underscores to single hyphens.</summary>
    public static string Normalize(string input)
    {
        var lowered = input.Trim().ToLowerInvariant();
        var withHyphens = Regex.Replace(lowered, @"[\s_]+", "-");
        var collapsed = Regex.Replace(withHyphens, "-{2,}", "-");
        return collapsed.Trim('-');
    }

    public static bool IsValid(string slug) =>
        slug.Length is >= MinLength and <= MaxLength && ValidPattern().IsMatch(slug);
}
