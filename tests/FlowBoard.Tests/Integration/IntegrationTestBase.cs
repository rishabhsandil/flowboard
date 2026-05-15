using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// Shared scaffolding for the API integration tests:
///   • spins up an in-memory <see cref="FlowBoardFactory"/> per test
///   • TRUNCATEs the fixture DB before each test (no cross-test bleed)
///   • bundles JSON helpers + a one-call <see cref="RegisterUserAsync"/>
///     that returns an <see cref="HttpClient"/> already authenticated as
///     a brand-new user
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Db;
    protected readonly FlowBoardFactory Factory;

    /// <summary>Camel-case JSON to match ASP.NET Core's default web policy.</summary>
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected IntegrationTestBase(PostgresFixture db)
    {
        Db = db;
        Factory = new FlowBoardFactory(db.ConnectionString);
    }

    public async Task InitializeAsync() => await Db.ResetDataAsync();
    public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }

    // -------- HTTP helpers ----------------------------------------------

    /// <summary>Anonymous client (no Authorization header).</summary>
    protected HttpClient Anon() => Factory.CreateClient();

    /// <summary>
    /// Registers a fresh user and returns a client whose Authorization
    /// header is pre-populated. Email is randomized so multiple users in
    /// a single test never collide.
    /// </summary>
    protected async Task<AuthedClient> RegisterUserAsync(string? name = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email  = $"u{suffix}@example.test";
        name     ??= $"User {suffix}";

        var http = Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/auth/register", new
        {
            email, name, password = "TestPassword123!"
        });
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;
        var userId = Guid.Parse(body.GetProperty("user").GetProperty("id").GetString()!);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AuthedClient(http, userId, email, name);
    }

    /// <summary>
    /// Convenience to add an existing user as a member of a project.
    /// Bypasses the (not-yet-implemented) invite flow by writing the
    /// `project_members` row directly.
    /// </summary>
    protected async Task AddProjectMemberAsync(Guid projectId, Guid userId, string role = "member")
    {
        await using var c = Db.OpenConnection();
        await Dapper.SqlMapper.ExecuteAsync(c, @"
            INSERT INTO project_members (project_id, user_id, role)
            VALUES (@p, @u, @r)
            ON CONFLICT DO NOTHING;",
            new { p = projectId, u = userId, r = role });
    }

    /// <summary>Reads a JSON body and returns the typed property.</summary>
    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res)
        => await res.Content.ReadFromJsonAsync<JsonElement>();

    // -------- domain helpers --------------------------------------------

    /// <summary>
    /// Creates a project owned by the supplied user, then creates one
    /// issue inside it. Returns both ids — the most common starting state
    /// for the comments / labels / activity tests.
    /// </summary>
    protected async Task<(Guid ProjectId, Guid IssueId)> CreateProjectWithIssueAsync(
        AuthedClient owner, string projectName = "Demo", string issueTitle = "First task")
    {
        var projRes = await owner.Http.PostAsJsonAsync("/api/projects", new { name = projectName });
        projRes.EnsureSuccessStatusCode();
        var projJson = await projRes.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(projJson.GetProperty("project").GetProperty("id").GetString()!);

        var issueRes = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues", new { title = issueTitle });
        issueRes.EnsureSuccessStatusCode();
        var issueJson = await issueRes.Content.ReadFromJsonAsync<JsonElement>();
        var issueId = Guid.Parse(issueJson.GetProperty("issue").GetProperty("id").GetString()!);

        return (projectId, issueId);
    }
}

/// <summary>Small bundle returned by <see cref="IntegrationTestBase.RegisterUserAsync"/>.</summary>
public sealed record AuthedClient(HttpClient Http, Guid UserId, string Email, string Name);
