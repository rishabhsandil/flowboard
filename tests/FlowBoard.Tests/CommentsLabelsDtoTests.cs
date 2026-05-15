using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FlowBoard.Api.Models;

namespace FlowBoard.Tests;

/// <summary>
/// Per project rule A6, validation attributes on positional record params
/// don't run through <see cref="Validator"/>; ASP.NET model binding picks
/// them up but we still want a unit-level guarantee that the rules are
/// declared (e.g. body length cap survived a refactor).
/// </summary>
public class CommentsLabelsDtoTests
{
    [Fact]
    public void CreateCommentRequest_BodyHasLengthCap()
    {
        var attr = GetParameterAttribute<CreateCommentRequest, StringLengthAttribute>("body");
        Assert.NotNull(attr);
        Assert.Equal(5000, attr!.MaximumLength);
        Assert.Equal(1,    attr.MinimumLength);
    }

    [Fact]
    public void CreateCommentRequest_BodyIsRequired()
    {
        var attr = GetParameterAttribute<CreateCommentRequest, RequiredAttribute>("body");
        Assert.NotNull(attr);
    }

    [Fact]
    public void UpdateCommentRequest_BodyHasSameRules()
    {
        // Edits should follow the same length contract as new comments —
        // otherwise a client could PATCH an oversized body past validation.
        var len = GetParameterAttribute<UpdateCommentRequest, StringLengthAttribute>("body");
        Assert.NotNull(len);
        Assert.Equal(5000, len!.MaximumLength);
        Assert.NotNull(GetParameterAttribute<UpdateCommentRequest, RequiredAttribute>("body"));
    }

    [Fact]
    public void CreateLabelRequest_NameHasLengthCap()
    {
        var attr = GetParameterAttribute<CreateLabelRequest, StringLengthAttribute>("name");
        Assert.NotNull(attr);
        Assert.Equal(40, attr!.MaximumLength);
    }

    [Fact]
    public void CreateLabelRequest_ColorMatchesHexPattern()
    {
        var attr = GetParameterAttribute<CreateLabelRequest, RegularExpressionAttribute>("color");
        Assert.NotNull(attr);
        // Pattern is the same one the Postgres CHECK uses on labels.color.
        Assert.Equal("^#[0-9a-fA-F]{6}$", attr!.Pattern);
    }

    [Fact]
    public void UpdateLabelRequest_NameIsOptionalButCapped()
    {
        // PATCH semantics — Required must NOT be present.
        Assert.Null(GetParameterAttribute<UpdateLabelRequest, RequiredAttribute>("name"));
        var len = GetParameterAttribute<UpdateLabelRequest, StringLengthAttribute>("name");
        Assert.NotNull(len);
        Assert.Equal(40, len!.MaximumLength);
    }

    [Fact]
    public void AttachLabelRequest_LabelIdIsRequired()
    {
        var attr = GetParameterAttribute<AttachLabelRequest, RequiredAttribute>("labelId");
        Assert.NotNull(attr);
    }

    /// <summary>
    /// Reads a <typeparamref name="TAttr"/> off the primary-constructor
    /// parameter named <paramref name="name"/> on record <typeparamref name="T"/>.
    /// Case-insensitive match because record params are camelCase.
    /// </summary>
    private static TAttr? GetParameterAttribute<T, TAttr>(string name) where TAttr : Attribute
    {
        var ctor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var p = ctor.GetParameters().FirstOrDefault(
                    x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Parameter '{name}' not found on {typeof(T).Name}");
        return p.GetCustomAttribute<TAttr>();
    }
}
