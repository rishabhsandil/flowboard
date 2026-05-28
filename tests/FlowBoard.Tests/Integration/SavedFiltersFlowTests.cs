using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for saved board filter presets: CRUD, ownership
/// guard, and duplicate-name rejection.
/// </summary>
public sealed class SavedFiltersFlowTests : IntegrationTestBase
{
    public SavedFiltersFlowTests(PostgresFixture db) : base(db) { }

    private static readonly object SampleFilters = new
    {
        search   = "",
        epic     = "all",
        sprint   = "active",
        assignee = "me",
        priority = "all",
        label    = "all",
    };

    [Fact]
    public async Task CreateAndList_RoundTrips()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var post = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/saved-filters",
            new { name = "My Sprint", filters = SampleFilters });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/saved-filters");
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("My Sprint",  items[0].GetProperty("name").GetString());
        Assert.Equal("active",     items[0].GetProperty("filters").GetProperty("sprint").GetString());
        Assert.Equal("me",         items[0].GetProperty("filters").GetProperty("assignee").GetString());
    }

    [Fact]
    public async Task Delete_RemovesPreset()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var created = await CreateSavedFilterAsync(owner, projectId, "Temp");

        var del = await owner.Http.DeleteAsync($"/api/saved-filters/{created}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/saved-filters");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task DuplicateName_CaseInsensitive_Returns409()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        await CreateSavedFilterAsync(owner, projectId, "Sprint view");

        var dup = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/saved-filters",
            new { name = "SPRINT VIEW", filters = SampleFilters });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        var body = await dup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("saved_filter_name_exists", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task OtherUser_CannotDelete_AnotherUsersPreset()
    {
        var owner = await RegisterUserAsync();
        var other = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, other.UserId);

        var filterId = await CreateSavedFilterAsync(owner, projectId, "Private");

        // Other member tries to delete owner's filter.
        var del = await other.Http.DeleteAsync($"/api/saved-filters/{filterId}");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotListOrCreate()
    {
        var owner    = await RegisterUserAsync();
        var outsider = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var list = await outsider.Http.GetAsync($"/api/projects/{projectId}/saved-filters");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var post = await outsider.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/saved-filters",
            new { name = "Sneaky", filters = SampleFilters });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task FiltersAreScopedPerUser_DifferentUsersSeeOwnPresets()
    {
        var alice = await RegisterUserAsync();
        var bob   = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(alice);
        await AddProjectMemberAsync(projectId, bob.UserId);

        await CreateSavedFilterAsync(alice, projectId, "Alice filter");
        await CreateSavedFilterAsync(bob,   projectId, "Bob filter");

        var aliceList = await alice.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/saved-filters");
        var bobList = await bob.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/saved-filters");

        var aliceItems = aliceList.GetProperty("items").EnumerateArray().ToList();
        var bobItems   = bobList.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(aliceItems);
        Assert.Equal("Alice filter", aliceItems[0].GetProperty("name").GetString());

        Assert.Single(bobItems);
        Assert.Equal("Bob filter", bobItems[0].GetProperty("name").GetString());
    }

    private async Task<Guid> CreateSavedFilterAsync(AuthedClient client, Guid projectId, string name)
    {
        var res = await client.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/saved-filters",
            new { name, filters = SampleFilters });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("savedFilter").GetProperty("id").GetString()!);
    }
}
