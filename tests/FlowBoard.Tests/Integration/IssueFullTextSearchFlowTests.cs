using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for the issues-list full-text search upgrade:
///   • description matches (was: title-only ILIKE)
///   • prefix-style matching ("auth" → "authentication")
///   • multi-word AND semantics
///   • &lt; 3-char queries still fall back to ILIKE on title
///   • symbolic-only queries return everything (no-op)
///   • title hits rank ahead of description hits
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IssueFullTextSearchFlowTests : IntegrationTestBase
{
    public IssueFullTextSearchFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task LongQuery_MatchesAcrossTitleAndDescription()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner, issueTitle: "first");
        var inDesc = await CreateIssueAsync(owner, projectId, "Refactor pipeline",
            description: "Replace the legacy authentication layer with JWT.");
        var noisy  = await CreateIssueAsync(owner, projectId, "Style nits",
            description: "Tweak Tailwind spacing tokens.");

        var ids = await SearchAsync(owner, projectId, "authentication");
        Assert.Contains(inDesc,  ids);
        Assert.DoesNotContain(noisy, ids);
    }

    [Fact]
    public async Task PrefixMatch_PicksUpStemmedForm()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner, issueTitle: "Authentication redesign");
        var control = await CreateIssueAsync(owner, projectId, "Unrelated");

        // "auth" is shorter than the indexed word but the :* suffix on every
        // token gives us prefix matching, so "authentication" hits.
        var ids = await SearchAsync(owner, projectId, "auth");
        Assert.DoesNotContain(control, ids);
        Assert.NotEmpty(ids);
    }

    [Fact]
    public async Task MultiWordQuery_RequiresAllTokens()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner, issueTitle: "first");
        var both    = await CreateIssueAsync(owner, projectId, "Migrate auth to JWT",
            description: "Cutover plan for the new login flow.");
        var onlyOne = await CreateIssueAsync(owner, projectId, "Login UI polish",
            description: "Tighter spacing on the form.");

        // Both tokens must appear (AND semantics).
        var ids = await SearchAsync(owner, projectId, "auth login");
        Assert.Contains(both, ids);
        Assert.DoesNotContain(onlyOne, ids);
    }

    [Fact]
    public async Task ShortQuery_FallsBackToTitleSubstring()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner, issueTitle: "first");
        var titleHit = await CreateIssueAsync(owner, projectId, "QA tickets backlog");
        var descOnly = await CreateIssueAsync(owner, projectId, "Sprint cleanup",
            description: "QA workflow updates.");

        // 2 chars → ILIKE on title only; description hits should NOT come back.
        var ids = await SearchAsync(owner, projectId, "qa");
        Assert.Contains(titleHit, ids);
        Assert.DoesNotContain(descOnly, ids);
    }

    [Fact]
    public async Task SymbolicOnlyQuery_ReturnsEmpty_NotEverything()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        await CreateIssueAsync(owner, projectId, "second");

        // "@@@" → cleaner strips every char → no FTS query is built. The user
        // typed *something*; matching every issue would be more surprising
        // than returning zero rows. (The empty-search case still uses the
        // null-search branch and returns everything.)
        var page = await GetSearchPageAsync(owner, projectId, "@@@");
        Assert.Equal(0, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TitleHits_RankAheadOfDescriptionHits()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner, issueTitle: "filler");
        var descHit  = await CreateIssueAsync(owner, projectId, "Unrelated",
            description: "incidental dashboard mention");
        var titleHit = await CreateIssueAsync(owner, projectId, "Dashboard redesign",
            description: "general polish pass");

        // Both rows contain "dashboard"; title weight (A) must beat body weight (B).
        var ids = await SearchAsync(owner, projectId, "dashboard");
        Assert.Equal(titleHit, ids.First());
        Assert.Contains(descHit, ids);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<Guid> CreateIssueAsync(
        AuthedClient owner, Guid projectId, string title,
        string? description = null)
    {
        var payload = description is null
            ? (object)new { title }
            : new { title, description };
        var res = await owner.Http.PostAsJsonAsync($"/api/projects/{projectId}/issues", payload);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("issue").GetProperty("id").GetString()!);
    }

    private async Task<List<Guid>> SearchAsync(AuthedClient owner, Guid projectId, string q)
    {
        var page = await GetSearchPageAsync(owner, projectId, q);
        return page.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!))
            .ToList();
    }

    private async Task<JsonElement> GetSearchPageAsync(AuthedClient owner, Guid projectId, string q)
    {
        var url = $"/api/projects/{projectId}/issues?search={Uri.EscapeDataString(q)}";
        return await owner.Http.GetFromJsonAsync<JsonElement>(url);
    }
}
