using FlowBoard.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlowBoard.Api.Hubs;

/// <summary>
/// Real-time fan-out for project-scoped board events. Each connected client
/// is added to a group named <c>project:{projectId}</c> once membership has
/// been verified; the server then publishes typed events to that group via
/// <c>IHubContext&lt;BoardHub&gt;</c> whenever an activity-logged mutation
/// happens.
///
/// Auth: JWT bearer (same scheme as the REST API). For the WebSocket
/// transport the client sends the token via the <c>access_token</c> query
/// string parameter — see the <c>OnMessageReceived</c> hook in
/// <c>Program.cs</c>. The hub itself is annotated with [Authorize] so an
/// anonymous connection is rejected at the framework level.
///
/// Membership is re-verified on every connect. If the caller cannot read
/// the project the connection is aborted with an exception; SignalR
/// propagates that to the client as a connection failure so the React app
/// can surface it.
/// </summary>
[Authorize]
public sealed class BoardHub : Hub
{
    private readonly ProjectAuthorizer _authz;
    private readonly ILogger<BoardHub> _log;

    public BoardHub(ProjectAuthorizer authz, ILogger<BoardHub> log)
    {
        _authz = authz;
        _log = log;
    }

    public static string GroupFor(Guid projectId) => $"project:{projectId}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        if (http is null)
        {
            // Should never happen for a real HTTP-borne connection; reject
            // so the client's StartAsync fails visibly.
            throw new HubException("invalid_connection");
        }

        if (!Guid.TryParse(http.Request.Query["projectId"], out var projectId))
        {
            _log.LogWarning("BoardHub connect rejected: missing or invalid projectId query string");
            throw new HubException("invalid_project_id");
        }

        if (Context.User is null)
        {
            // [Authorize] on the hub should make this unreachable.
            throw new HubException("unauthenticated");
        }

        var userId = Context.User.GetUserId();
        if (!await _authz.IsMemberAsync(projectId, userId))
        {
            _log.LogWarning(
                "BoardHub connect rejected: user {UserId} is not a member of project {ProjectId}",
                userId, projectId);
            throw new HubException("forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(projectId));
        // Stash on the connection so OnDisconnectedAsync can log it.
        Context.Items["projectId"] = projectId;
        _log.LogInformation(
            "BoardHub connected: user={UserId} project={ProjectId} connection={ConnectionId}",
            userId, projectId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR removes the connection from every group automatically on
        // disconnect; no explicit RemoveFromGroupAsync needed.
        if (Context.Items.TryGetValue("projectId", out var pid))
        {
            _log.LogInformation(
                "BoardHub disconnected: project={ProjectId} connection={ConnectionId}",
                pid, Context.ConnectionId);
        }
        return base.OnDisconnectedAsync(exception);
    }
}
