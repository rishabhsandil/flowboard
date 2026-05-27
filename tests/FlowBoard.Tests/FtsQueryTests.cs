using FlowBoard.Api.Search;

namespace FlowBoard.Tests;

/// <summary>
/// Unit coverage for <see cref="FtsQuery.BuildPrefixTsQuery"/>. The helper is
/// the only place where untrusted user input becomes a Postgres
/// <c>tsquery</c> string, so the sanitiser carries the security boundary
/// here — these tests pin its contract.
/// </summary>
public class FtsQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!@#$%^&*()")]
    public void ReturnsNull_ForEmptyOrAllSymbolicInput(string? input)
    {
        Assert.Null(FtsQuery.BuildPrefixTsQuery(input));
    }

    [Fact]
    public void SingleWord_GetsPrefixSuffix()
    {
        Assert.Equal("auth:*", FtsQuery.BuildPrefixTsQuery("auth"));
    }

    [Fact]
    public void MultipleWords_JoinedWithAnd_EachPrefixed()
    {
        Assert.Equal("auth:* & login:*", FtsQuery.BuildPrefixTsQuery("auth login"));
    }

    [Fact]
    public void Lowercases_Input_ForStableTsQuery()
    {
        Assert.Equal("jwt:*", FtsQuery.BuildPrefixTsQuery("JWT"));
    }

    [Fact]
    public void StripsTsQueryOperators_NeverReachesParser()
    {
        // & | ! ( ) : would all be valid tsquery operators if we let them
        // through — instead they become whitespace and the surrounding words
        // survive.
        Assert.Equal("foo:* & bar:*", FtsQuery.BuildPrefixTsQuery("foo & bar"));
        Assert.Equal("foo:* & bar:*", FtsQuery.BuildPrefixTsQuery("foo | bar"));
        Assert.Equal("not:*",         FtsQuery.BuildPrefixTsQuery("!not"));
        Assert.Equal("a:* & b:*",     FtsQuery.BuildPrefixTsQuery("(a):(b)"));
    }

    [Fact]
    public void CollapsesRunsOfWhitespace_AndIgnoresEmptyTokens()
    {
        Assert.Equal("a:* & b:*", FtsQuery.BuildPrefixTsQuery("a    b"));
        Assert.Equal("a:*",       FtsQuery.BuildPrefixTsQuery("  a  "));
    }

    [Fact]
    public void KeepsUnicodeLettersAndDigits()
    {
        // Non-ASCII letters survive (Unicode \p{L}); digits survive too.
        Assert.Equal("café:* & 42:*", FtsQuery.BuildPrefixTsQuery("café 42"));
    }
}
