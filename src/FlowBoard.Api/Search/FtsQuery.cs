using System.Text.RegularExpressions;

namespace FlowBoard.Api.Search;

/// <summary>
/// Helpers for turning free-form user input into a safe Postgres
/// <c>tsquery</c> string. We can't pass the raw user text to
/// <c>to_tsquery</c> — its operator syntax (<c>&amp;</c>, <c>|</c>,
/// <c>!</c>, <c>:</c>) would raise a parse error on anything but the
/// simplest input. Instead we strip every non-alphanumeric character,
/// split on whitespace, and join the tokens with <c>&amp;</c>, attaching
/// <c>:*</c> to each so prefix matching works ("auth" → matches
/// "authentication").
///
/// Returning null means "no usable query" — the caller should treat that
/// as "no full-text search this request" (typically falling back to a
/// substring ILIKE for very short inputs).
/// </summary>
public static class FtsQuery
{
    // Anything that isn't a Unicode letter / digit / whitespace becomes a
    // single space, so we never have to think about escaping tsquery
    // operators or quotes.
    private static readonly Regex NonAlphaNum =
        new(@"[^\p{L}\p{Nd}\s]+", RegexOptions.Compiled);

    /// <summary>
    /// Cleans <paramref name="input"/> and returns a prefix-style
    /// <c>tsquery</c> string suitable for <c>to_tsquery('english', ...)</c>,
    /// or <c>null</c> when there are no usable tokens left after sanitisation.
    /// </summary>
    public static string? BuildPrefixTsQuery(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var cleaned = NonAlphaNum.Replace(input, " ");
        var tokens = cleaned.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        // Lower-case so the resulting tsquery is canonical (the english
        // dictionary lowercases internally; we mirror it for stable logs).
        var withPrefix = tokens.Select(t => t.ToLowerInvariant() + ":*");
        return string.Join(" & ", withPrefix);
    }
}
