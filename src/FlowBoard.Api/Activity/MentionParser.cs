using System.Text.RegularExpressions;

namespace FlowBoard.Api.Activity;

/// <summary>
/// Extracts <c>@handle</c> tokens from comment bodies. A "handle" is the
/// canonical form of a user's display name: lowercased and stripped of
/// whitespace (so "Jane Doe" is mentionable as <c>@janedoe</c>). The regex
/// uses a lookbehind on whitespace/punctuation so emails like
/// <c>foo@bar.com</c> are NOT matched.
/// </summary>
public static class MentionParser
{
    private static readonly Regex MentionRegex =
        new(@"(?<=^|\s|[(\[{,.;:!?])@([A-Za-z0-9_-]{2,40})", RegexOptions.Compiled);

    /// <summary>
    /// Returns the distinct, lowercased handles found in <paramref name="body"/>.
    /// </summary>
    public static string[] Extract(string? body)
    {
        if (string.IsNullOrEmpty(body)) return Array.Empty<string>();
        return MentionRegex.Matches(body)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToArray();
    }
}
