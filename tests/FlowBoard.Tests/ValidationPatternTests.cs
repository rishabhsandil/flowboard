using System.Text.RegularExpressions;
using FlowBoard.Api.Models;

namespace FlowBoard.Tests;

/// <summary>
/// Validation guard rails. Positional record parameters put attributes on the
/// parameter (not the property), so ASP.NET model binding reads them but
/// <see cref="System.ComponentModel.DataAnnotations.Validator"/> does not.
/// We test the underlying regex patterns directly — they're shared between
/// the DTOs and Postgres CHECK constraints (rule A6 in coding-standards).
/// </summary>
public class ValidationPatternTests
{
    private static bool IsMatch(string pattern, string input) =>
        Regex.IsMatch(input, pattern);

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("critical")]
    public void Priority_Accepts(string value)
        => Assert.True(IsMatch(GetConst("Priority"), value));

    [Theory]
    [InlineData("urgent")]
    [InlineData("LOW")]
    [InlineData("")]
    [InlineData(" low")]
    public void Priority_Rejects(string value)
        => Assert.False(IsMatch(GetConst("Priority"), value));

    [Theory]
    [InlineData("planned")]
    [InlineData("active")]
    [InlineData("completed")]
    public void SprintStatus_Accepts(string value)
        => Assert.True(IsMatch(GetConst("SprintStatus"), value));

    [Theory]
    [InlineData("done")]
    [InlineData("Active")]
    [InlineData("PLANNED")]
    public void SprintStatus_Rejects(string value)
        => Assert.False(IsMatch(GetConst("SprintStatus"), value));

    [Theory]
    [InlineData("#22d3ee")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#aBcDeF")]
    public void HexColor_Accepts(string value)
        => Assert.True(IsMatch(GetConst("HexColor"), value));

    [Theory]
    [InlineData("red")]
    [InlineData("#ff")]         // too short
    [InlineData("22d3ee")]      // missing #
    [InlineData("#22d3eeff")]   // 8 hex chars (no alpha support)
    [InlineData("#22d3eg")]     // non-hex char
    public void HexColor_Rejects(string value)
        => Assert.False(IsMatch(GetConst("HexColor"), value));

    [Fact]
    public void PageRequest_Defaults_AreSafe()
    {
        var dto = new PageRequest();
        Assert.Equal(0, dto.Skip);
        Assert.Equal(50, dto.Take);
    }

    /// <summary>
    /// Reach into the internal ValidationPatterns class. We deliberately keep
    /// the class internal so it doesn't pollute the public API surface, and
    /// reach in here so the tests don't bit-rot if a pattern is renamed.
    /// </summary>
    private static string GetConst(string name)
    {
        var t = typeof(CreateIssueRequest).Assembly
            .GetType("FlowBoard.Api.Models.ValidationPatterns")
            ?? throw new InvalidOperationException("ValidationPatterns class moved");
        var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException($"Pattern '{name}' missing");
        return (string)f.GetValue(null)!;
    }
}
