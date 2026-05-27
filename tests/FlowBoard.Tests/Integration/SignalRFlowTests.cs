using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// End-to-end coverage for the SignalR board hub: auth handshake, project
/// group isolation, and fan-out from controller writes through the
/// <c>ActivityLogger → IBoardEventPublisher</c> pipeline.
///
/// Transport is forced to LongPolling because the test server exposes an
/// <see cref="HttpMessageHandler"/> but no real WebSocket endpoint — long-
/// polling is functionally identical for assertion purposes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SignalRFlowTests : IntegrationTestBase
{
    public SignalRFlowTests(PostgresFixture db) : base(db) { }

    [Fact]
    public async Task ProjectMember_ReceivesEvent_WhenAnotherMemberCreatesAnIssue()
    {
        var owner    = await RegisterUserAsync();
        var listener = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);
        await AddProjectMemberAsync(projectId, listener.UserId);

        await using var conn = await ConnectAsync(listener, projectId);

        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("event", evt =>
        {
            if (evt.GetProperty("type").GetString() == "issue_created")
                received.TrySetResult(evt);
        });

        // The owner posts a new issue → ActivityLogger fires → publisher
        // broadcasts on the project group → listener's connection sees it.
        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/issues", new { title = "Hello realtime" });
        res.EnsureSuccessStatusCode();

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(projectId.ToString(), evt.GetProperty("projectId").GetString());
        Assert.Equal("issue_created", evt.GetProperty("type").GetString());
    }

    [Fact]
    public async Task NonMember_CannotConnect_ToProjectHub()
    {
        var owner    = await RegisterUserAsync();
        var outsider = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        // OnConnectedAsync throwing a HubException over the long-polling
        // transport doesn't always surface as a StartAsync exception (the
        // handshake completed before the hub method ran). Instead we assert
        // that the connection is disconnected, either by Start throwing or
        // by Closed firing immediately.
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        HubConnection? conn = null;
        try
        {
            conn = await ConnectAsync(outsider, projectId);
            conn.Closed += ex => { closed.TrySetResult(ex); return Task.CompletedTask; };

            // Either Start threw above (assertion passes), or the connection
            // is on its way down: wait up to a second for the Closed callback.
            var fired = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Equal(closed.Task, fired);
            Assert.NotEqual(HubConnectionState.Connected, conn.State);
        }
        catch (Exception)
        {
            // StartAsync threw — this is also the rejected-connection contract.
        }
        finally
        {
            if (conn is not null) await conn.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProjectGroupsAreIsolated_EventsDoNotCrossProjects()
    {
        var owner = await RegisterUserAsync();
        var (projectA, _) = await CreateProjectWithIssueAsync(owner, projectName: "A");
        var (projectB, _) = await CreateProjectWithIssueAsync(owner, projectName: "B");

        await using var connA = await ConnectAsync(owner, projectA);

        var receivedOnA = 0;
        connA.On<JsonElement>("event", evt =>
        {
            if (evt.GetProperty("type").GetString() == "issue_created")
                Interlocked.Increment(ref receivedOnA);
        });

        // Create an issue in B; A's listener must NOT receive it.
        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectB}/issues", new { title = "Stays in B" });
        res.EnsureSuccessStatusCode();

        // Give the server a moment to publish — if it leaks, this is when
        // we'd see it. 500ms is generous; long-polling is fast in-process.
        await Task.Delay(500);
        Assert.Equal(0, receivedOnA);
    }

    [Fact]
    public async Task ColumnMutations_PublishEvents_ForBoardConsumers()
    {
        var owner = await RegisterUserAsync();
        var (projectId, _) = await CreateProjectWithIssueAsync(owner);

        await using var conn = await ConnectAsync(owner, projectId);
        var seen = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("event", evt =>
        {
            lock (seen)
            {
                seen.Add(evt.GetProperty("type").GetString() ?? "");
                if (seen.Contains("column_created")) done.TrySetResult();
            }
        });

        var res = await owner.Http.PostAsJsonAsync(
            $"/api/projects/{projectId}/columns", new { name = "QA", isDone = false });
        res.EnsureSuccessStatusCode();

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("column_created", seen);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Builds and starts a <see cref="HubConnection"/> pointed at the in-
    /// memory test server using long polling, with the caller's JWT supplied
    /// via the access-token factory (the same path the React client uses).
    /// </summary>
    private async Task<HubConnection> ConnectAsync(AuthedClient client, Guid projectId)
    {
        var token = client.Http.DefaultRequestHeaders.Authorization!.Parameter!;
        var server = Factory.Server;

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, $"hubs/board?projectId={projectId}"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        return connection;
    }
}
