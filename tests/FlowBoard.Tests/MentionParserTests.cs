using FlowBoard.Api.Activity;

namespace FlowBoard.Tests;

/// <summary>
/// The mention regex is the contract between the comment body and the
/// <c>mentions</c> table. These tests pin the boundary cases so a future
/// "small tweak" to the pattern can't silently break notification routing
/// or — worse — turn email addresses into mentions.
/// </summary>
public class MentionParserTests
{
    [Theory]
    [InlineData("hello @alice",                    new[] { "alice" })]
    [InlineData("@alice at the start",             new[] { "alice" })]
    [InlineData("ping @alice and @bob please",     new[] { "alice", "bob" })]
    [InlineData("hey (@alice), can you check?",    new[] { "alice" })]
    [InlineData("end of line @alice.",             new[] { "alice" })]
    [InlineData("CASE @Alice equals @alice",       new[] { "alice" })]      // dedup + lowercase
    [InlineData("hyphen @user-name works",       new[] { "user-name" })]
    [InlineData("underscore @under_score ok",     new[] { "under_score" })]
    public void Extracts_ValidHandles(string body, string[] expected)
    {
        Assert.Equal(expected, MentionParser.Extract(body));
    }

    [Theory]
    [InlineData("contact me at foo@bar.com")]   // email — not a mention
    [InlineData("twitter@example")]              // no whitespace boundary
    [InlineData("price was $5@piece")]           // no left boundary
    [InlineData("ratio is 4@5")]                 // no left boundary
    [InlineData("@a")]                           // 1 char (min is 2)
    [InlineData("hi @!nope")]                    // invalid char after @
    public void Ignores_NonMentions(string body)
    {
        Assert.Empty(MentionParser.Extract(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_Or_Null_Returns_Empty(string? body)
    {
        Assert.Empty(MentionParser.Extract(body));
    }

    [Fact]
    public void Caps_Handle_At_40_Chars()
    {
        // The {2,40} quantifier is greedy with a hard upper bound: a 41-char
        // run matches the FIRST 40 chars, so the parser truncates rather
        // than skipping. That's the right behaviour — a 41-char name still
        // resolves to its 40-char handle on the server side.
        var fortyOne = "@" + new string('a', 41);
        var matches = MentionParser.Extract(fortyOne);
        Assert.Single(matches);
        Assert.Equal(40, matches[0].Length);

        // Exactly 40 → matches as-is.
        var ok = "@" + new string('a', 40);
        Assert.Single(MentionParser.Extract(ok));
    }

    [Fact]
    public void Whitespace_Variants_All_Count_As_Boundary()
    {
        var s = "line1\n@alice\tand @bob\r\n@carol";
        Assert.Equal(new[] { "alice", "bob", "carol" }, MentionParser.Extract(s));
    }
}
