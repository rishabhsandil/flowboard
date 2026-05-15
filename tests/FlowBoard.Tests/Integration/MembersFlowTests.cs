using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for the project member invitation + role management
/// flow. Mirrors the rules baked into <c>ProjectsController</c>:
///   • only owners can invite / change roles / remove others
///   • re-inviting the same user is idempotent (200, no duplicate row)
///   • last-owner protection blocks demotions and removals that would
///     leave the project ownerless
///   • members can self-leave (subject to last-owner protection)
///   • inviting a non-existent email surfaces <c>user_not_found</c>
/// </summary>
public sealed class MembersFlowTests : IntegrationTestBase
{
    public MembersFlowTests(PostgresFixture db) : base(db) { }

    private async Task<Guid> CreateProjectAsync(AuthedClient owner)
    {
        var res = await owner.Http.PostAsJsonAsync("/api/projects", new { name = "Demo" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("project").GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Owner_CanInviteUserByEmail()
    {
        var owner  = await RegisterUserAsync();
        var invite = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);

        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new { email = invite.Email });
        Assert.True(res.IsSuccessStatusCode, $"expected 2xx, got {(int)res.StatusCode}");

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/members");
        var ids = list.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToHashSet();
        Assert.Contains(invite.UserId.ToString(), ids);
    }

    [Fact]
    public async Task Invite_WithUnknownEmail_Returns404UserNotFound()
    {
        var owner = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);

        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members",
            new { email = "nobody@example.test" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("user_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Invite_IsIdempotent_NoDuplicateMembership()
    {
        var owner  = await RegisterUserAsync();
        var invite = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);

        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = invite.Email });
        var second = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = invite.Email });
        // The endpoint returns 200 either way — what matters is no 500 and
        // exactly one membership row in the DB.
        Assert.True(second.IsSuccessStatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/members");
        var rows = list.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("id").GetString() == invite.UserId.ToString())
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    public async Task Owner_CanPromoteMemberToOwner()
    {
        var owner  = await RegisterUserAsync();
        var invite = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);
        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = invite.Email });

        var promote = await owner.Http.PatchAsJsonAsync(
            $"/api/projects/{projectId}/members/{invite.UserId}",
            new { role = "owner" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/members");
        var row = list.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == invite.UserId.ToString());
        Assert.Equal("owner", row.GetProperty("role").GetString());
    }

    [Fact]
    public async Task DemotingTheLastOwner_IsBlocked()
    {
        var owner = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);

        var demote = await owner.Http.PatchAsJsonAsync(
            $"/api/projects/{projectId}/members/{owner.UserId}",
            new { role = "member" });
        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);

        var body = await demote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("last_owner", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task LastOwner_CannotLeave()
    {
        var owner = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);

        var leave = await owner.Http.DeleteAsync(
            $"/api/projects/{projectId}/members/{owner.UserId}");
        Assert.Equal(HttpStatusCode.BadRequest, leave.StatusCode);

        var body = await leave.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("last_owner", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Member_CanSelfLeave()
    {
        var owner  = await RegisterUserAsync();
        var member = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);
        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = member.Email });

        var leave = await member.Http.DeleteAsync(
            $"/api/projects/{projectId}/members/{member.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        // The member can no longer see the project (Forbidden / 403, since
        // they're no longer a member).
        var afterList = await member.Http.GetAsync($"/api/projects/{projectId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, afterList.StatusCode);
    }

    [Fact]
    public async Task NonOwner_CannotRemoveOtherMembers()
    {
        var owner   = await RegisterUserAsync();
        var memberA = await RegisterUserAsync();
        var memberB = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);
        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = memberA.Email });
        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = memberB.Email });

        // memberA tries to kick memberB → forbidden.
        var kick = await memberA.Http.DeleteAsync(
            $"/api/projects/{projectId}/members/{memberB.UserId}");
        Assert.Equal(HttpStatusCode.Forbidden, kick.StatusCode);
    }

    [Fact]
    public async Task MemberInvite_WritesActivityRow()
    {
        var owner  = await RegisterUserAsync();
        var invite = await RegisterUserAsync();
        var projectId = await CreateProjectAsync(owner);

        await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { email = invite.Email });

        var feed = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/activity");
        var types = feed.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToHashSet();
        Assert.Contains("member_added", types);
    }
}
