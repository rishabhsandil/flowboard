using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// End-to-end tests for the comments + mentions + activity flow. These hit
/// the real ASP.NET Core pipeline against a real Postgres database, so a
/// regression in any layer (controller, query, JSONB cast, mention regex,
/// foreign keys) shows up here.
/// </summary>
public sealed class CommentsFlowTests : IntegrationTestBase
{
    public CommentsFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task PostingComment_PersistsAndAppearsInList()
    {
        var owner = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments", new { body = "Looks good to me" });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/issues/{issueId}/comments");
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Looks good to me", items[0].GetProperty("body").GetString());
        Assert.Equal(owner.UserId.ToString(), items[0].GetProperty("authorId").GetString());
        Assert.False(items[0].GetProperty("edited").GetBoolean());
    }

    [Fact]
    public async Task EditingOwnComment_FlipsEditedFlag()
    {
        var owner = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        var created = await (await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments", new { body = "first" })).Content.ReadFromJsonAsync<JsonElement>();
        var commentId = created.GetProperty("comment").GetProperty("id").GetString()!;

        var patch = await owner.Http.PatchAsJsonAsync($"/api/comments/{commentId}", new { body = "fixed" });
        patch.EnsureSuccessStatusCode();

        var list = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/issues/{issueId}/comments");
        var item = list.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("fixed", item.GetProperty("body").GetString());
        Assert.True(item.GetProperty("edited").GetBoolean());
    }

    [Fact]
    public async Task NonAuthor_CannotEditComment()
    {
        var owner = await RegisterUserAsync();
        var member = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, member.UserId);

        var created = await (await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments", new { body = "owner's note" })).Content.ReadFromJsonAsync<JsonElement>();
        var commentId = created.GetProperty("comment").GetProperty("id").GetString()!;

        var patch = await member.Http.PatchAsJsonAsync($"/api/comments/{commentId}", new { body = "hacked" });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }

    [Fact]
    public async Task ProjectOwner_CanDeleteOthersComments_ButNonOwnerMemberCannot()
    {
        var owner = await RegisterUserAsync();
        var memberA = await RegisterUserAsync();
        var memberB = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, memberA.UserId);
        await AddProjectMemberAsync(projectId, memberB.UserId);

        // memberA leaves a comment.
        var created = await (await memberA.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments", new { body = "hi" })).Content.ReadFromJsonAsync<JsonElement>();
        var commentId = created.GetProperty("comment").GetProperty("id").GetString()!;

        // memberB (not author, not owner) → forbidden.
        var memberDelete = await memberB.Http.DeleteAsync($"/api/comments/{commentId}");
        Assert.Equal(HttpStatusCode.Forbidden, memberDelete.StatusCode);

        // owner → allowed.
        var ownerDelete = await owner.Http.DeleteAsync($"/api/comments/{commentId}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotListOrPostComments()
    {
        var owner    = await RegisterUserAsync();
        var outsider = await RegisterUserAsync();
        var (_, issueId) = await CreateProjectWithIssueAsync(owner);

        var get = await outsider.Http.GetAsync($"/api/issues/{issueId}/comments");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var post = await outsider.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments", new { body = "intruder" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task MentionsResolve_AgainstProjectMembers_NotOutsiders()
    {
        var owner    = await RegisterUserAsync(name: "Alice Wonder"); // handle: alicewonder
        var member   = await RegisterUserAsync(name: "Bob Builder");  // handle: bobbuilder
        var outsider = await RegisterUserAsync(name: "Eve Smith");    // handle: evesmith — NOT in project

        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, member.UserId);

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments",
            new { body = "ping @bobbuilder cc @evesmith" });
        post.EnsureSuccessStatusCode();
        var json = await post.Content.ReadFromJsonAsync<JsonElement>();
        var resolved = json.GetProperty("mentionedUserIds").EnumerateArray()
            .Select(e => Guid.Parse(e.GetString()!)).ToHashSet();

        Assert.Contains(member.UserId, resolved);
        Assert.DoesNotContain(outsider.UserId, resolved);
    }

    [Fact]
    public async Task EditingComment_RewritesMentionRows()
    {
        var owner  = await RegisterUserAsync(name: "Alice Wonder");
        var bob    = await RegisterUserAsync(name: "Bob Builder");
        var carol  = await RegisterUserAsync(name: "Carol Danvers");
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, bob.UserId);
        await AddProjectMemberAsync(projectId, carol.UserId);

        // First version mentions Bob only.
        var initial = await (await owner.Http.PostAsJsonAsync(
            $"/api/issues/{issueId}/comments", new { body = "hey @bobbuilder" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var commentId = Guid.Parse(initial.GetProperty("comment").GetProperty("id").GetString()!);

        // Edit to mention Carol instead.
        await owner.Http.PatchAsJsonAsync($"/api/comments/{commentId}",
            new { body = "actually @caroldanvers" });

        // Inspect the mentions table directly.
        await using var c = Db.OpenConnection();
        var rows = (await Dapper.SqlMapper.QueryAsync<Guid>(
            c, "SELECT mentioned_user_id FROM mentions WHERE comment_id = @id",
            new { id = commentId })).ToList();

        Assert.Single(rows);
        Assert.Equal(carol.UserId, rows[0]);
    }
}
