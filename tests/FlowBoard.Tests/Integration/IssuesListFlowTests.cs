using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Functional coverage for the project-wide issue list (the Issues page).
/// Exercises every filter the UI exposes plus the auth + pagination contract.
/// All assertions go through the HTTP boundary so the SQL, controller, and
/// authorization layers are tested together.
/// </summary>
public sealed class IssuesListFlowTests : IntegrationTestBase
{
    public IssuesListFlowTests(PostgresFixture db) : base(db) { }

    private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

    [Fact]
    public async Task List_WithoutFilters_ReturnsEveryIssueInProject()
    {
        var owner = await RegisterUserAsync();
        var (projectId, firstId) = await CreateProjectWithIssueAsync(owner);
        var secondId = await CreateIssueAsync(owner, projectId, "second");
        var thirdId  = await CreateIssueAsync(owner, projectId, "third");

        var page = await GetListAsync(owner, projectId);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, page.GetProperty("total").GetInt32());

        var ids = items.Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToHashSet();
        Assert.Contains(firstId,  ids);
        Assert.Contains(secondId, ids);
        Assert.Contains(thirdId,  ids);
    }

    [Fact]
    public async Task List_StatusFilter_OpenAndClosedAreSeparable()
    {
        var owner = await RegisterUserAsync();
        var (projectId, openId) = await CreateProjectWithIssueAsync(owner);
        var closedId = await CreateIssueAsync(owner, projectId, "to-close");
        // Close one issue via PATCH.
        var patch = await owner.Http.PatchAsJsonAsync($"/api/issues/{closedId}", new { closed = true });
        patch.EnsureSuccessStatusCode();

        var openOnly = await GetListAsync(owner, projectId, ("status", "open"));
        var openIds = openOnly.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { openId }, openIds);

        var closedOnly = await GetListAsync(owner, projectId, ("status", "closed"));
        var closedIds = closedOnly.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { closedId }, closedIds);
    }

    [Fact]
    public async Task List_PriorityFilter_NarrowsToExactValue()
    {
        var owner = await RegisterUserAsync();
        var (projectId, mediumId) = await CreateProjectWithIssueAsync(owner);
        var criticalId = await CreateIssueAsync(owner, projectId, "fire", priority: "critical");

        var page = await GetListAsync(owner, projectId, ("priority", "critical"));
        var ids = page.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { criticalId }, ids);
        // sanity: medium issue still exists, just filtered out
        Assert.NotEqual(criticalId, mediumId);
    }

    [Fact]
    public async Task List_SearchFilter_IsCaseInsensitiveSubstringOnTitle()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner, issueTitle: "Login bug");
        var hit = await CreateIssueAsync(owner, projectId, "fix LOGIN flow");
        var miss = await CreateIssueAsync(owner, projectId, "unrelated chore");

        var page = await GetListAsync(owner, projectId, ("search", "login"));
        var ids = page.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToHashSet();
        Assert.Contains(hit, ids);
        Assert.DoesNotContain(miss, ids);
        Assert.Equal(2, page.GetProperty("total").GetInt32()); // both "Login bug" and "fix LOGIN flow"
    }

    [Fact]
    public async Task List_EpicFilter_RealIdAndEmptyGuidSentinelBothWork()
    {
        var owner = await RegisterUserAsync();
        var (projectId, unattachedId) = await CreateProjectWithIssueAsync(owner);
        var epicId = await CreateEpicAsync(owner, projectId);
        var attachedId = await CreateIssueAsync(owner, projectId, "with-epic", epicId: epicId);

        var byEpic = await GetListAsync(owner, projectId, ("epicId", epicId.ToString()));
        var byEpicIds = byEpic.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { attachedId }, byEpicIds);

        var noEpic = await GetListAsync(owner, projectId, ("epicId", EmptyGuid));
        var noEpicIds = noEpic.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { unattachedId }, noEpicIds);
    }

    [Fact]
    public async Task List_LabelFilter_ReturnsRowOnce_EvenWithMultipleLabels()
    {
        // Guards against a JOIN-not-EXISTS regression — the row must not
        // duplicate when an issue carries several labels.
        var owner = await RegisterUserAsync();
        var (projectId, issueId) = await CreateProjectWithIssueAsync(owner);

        var labelA = await CreateLabelAsync(owner, projectId, "alpha");
        var labelB = await CreateLabelAsync(owner, projectId, "beta");
        await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId = labelA });
        await owner.Http.PostAsJsonAsync($"/api/issues/{issueId}/labels", new { labelId = labelB });

        var page = await GetListAsync(owner, projectId, ("labelId", labelA.ToString()));
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(1, page.GetProperty("total").GetInt32());

        // The row carries both labels in its bundled labelsJson, ordered by lower(name).
        var labels = JsonDocument.Parse(items[0].GetProperty("labelsJson").GetString()!).RootElement
            .EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "alpha", "beta" }, labels);
    }

    [Fact]
    public async Task List_AssigneeFilter_UnassignedSentinelMatchesNullColumn()
    {
        var owner = await RegisterUserAsync();
        var assignee = await RegisterUserAsync();
        var (projectId, unassignedId) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, assignee.UserId);
        var assignedId = await CreateIssueAsync(owner, projectId, "owned",
            assigneeId: assignee.UserId);

        var unassigned = await GetListAsync(owner, projectId, ("assigneeId", EmptyGuid));
        var unIds = unassigned.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { unassignedId }, unIds);

        var assigned = await GetListAsync(owner, projectId,
            ("assigneeId", assignee.UserId.ToString()));
        var asIds = assigned.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!));
        Assert.Equal(new[] { assignedId }, asIds);
    }

    [Fact]
    public async Task List_RowsBundleJoinedNames_ForUiTable()
    {
        // The UI relies on these joined fields (epicTitle, sprintName, etc.)
        // being present so the table renders without N+1 lookups.
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        var epicId = await CreateEpicAsync(owner, projectId, title: "Auth", color: "#abcdef");
        await CreateIssueAsync(owner, projectId, "ssoooo", epicId: epicId);

        var page = await GetListAsync(owner, projectId, ("search", "ssoooo"));
        var item = page.GetProperty("items").EnumerateArray().Single();

        Assert.Equal("Auth",    item.GetProperty("epicTitle").GetString());
        Assert.Equal("#abcdef", item.GetProperty("epicColor").GetString());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("columnName").GetString()));
        Assert.Equal("[]",      item.GetProperty("labelsJson").GetString());
    }

    [Fact]
    public async Task List_Pagination_RespectsSkipAndTake()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        for (int i = 0; i < 4; i++) await CreateIssueAsync(owner, projectId, $"i{i}");

        var page = await GetListAsync(owner, projectId, ("skip", "1"), ("take", "2"));
        Assert.Equal(5, page.GetProperty("total").GetInt32()); // 1 from helper + 4
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page.GetProperty("skip").GetInt32());
        Assert.Equal(2, page.GetProperty("take").GetInt32());
    }

    [Fact]
    public async Task List_BogusStatus_Returns400()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        var res = await owner.Http.GetAsync($"/api/projects/{projectId}/issues?status=banana");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_status", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task List_NonMember_IsForbidden()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        var stranger = await RegisterUserAsync();

        var res = await stranger.Http.GetAsync($"/api/projects/{projectId}/issues");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Members_ListsOwnerAndMembers_OwnerFirst()
    {
        var owner = await RegisterUserAsync(name: "Aaa Owner");
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        var bob = await RegisterUserAsync(name: "Bob Member");
        await AddProjectMemberAsync(projectId, bob.UserId);

        var res = await owner.Http.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/members");
        var members = res.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, members.Count);
        // Owner first regardless of alphabetical order.
        Assert.Equal("owner", members[0].GetProperty("role").GetString());
        Assert.Equal(owner.UserId, Guid.Parse(members[0].GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task Members_NonMember_IsForbidden()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        var stranger = await RegisterUserAsync();

        var res = await stranger.Http.GetAsync($"/api/projects/{projectId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    // -------- helpers ---------------------------------------------------

    private static async Task<JsonElement> GetListAsync(
        AuthedClient client, Guid projectId, params (string, string)[] qs)
    {
        var query = qs.Length == 0 ? "" : "?" + string.Join("&",
            qs.Select(p => $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
        var res = await client.Http.GetAsync($"/api/projects/{projectId}/issues{query}");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateIssueAsync(
        AuthedClient owner, Guid projectId, string title,
        string? priority = null, Guid? epicId = null, Guid? assigneeId = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
        };
        if (priority is not null)   payload["priority"]   = priority;
        if (epicId is not null)     payload["epicId"]     = epicId;
        if (assigneeId is not null) payload["assigneeId"] = assigneeId;

        var res = await owner.Http.PostAsJsonAsync($"/api/projects/{projectId}/issues", payload);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("issue").GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateEpicAsync(
        AuthedClient owner, Guid projectId, string title = "Epic", string color = "#123456")
    {
        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/epics", new { title, color });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("epic").GetProperty("id").GetString()!);
    }

    private async Task<Guid> CreateLabelAsync(AuthedClient owner, Guid projectId, string name)
    {
        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/labels", new { name });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("label").GetProperty("id").GetString()!);
    }
}
