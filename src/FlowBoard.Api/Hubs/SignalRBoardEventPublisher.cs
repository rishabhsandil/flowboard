using Microsoft.AspNetCore.SignalR;

namespace FlowBoard.Api.Hubs;

/// <summary>
/// SignalR-backed implementation. Wraps the send in a try/catch so a
/// transient hub failure (e.g. a disconnecting client) cannot bubble up
/// and 500 the API request that triggered the publish. The exception is
/// logged with the offending event type for triage.
/// </summary>
public sealed class SignalRBoardEventPublisher : IBoardEventPublisher
{
    private readonly IHubContext<BoardHub> _hub;
    private readonly ILogger<SignalRBoardEventPublisher> _log;

    public SignalRBoardEventPublisher(
        IHubContext<BoardHub> hub,
        ILogger<SignalRBoardEventPublisher> log)
    {
        _hub = hub;
        _log = log;
    }

    public async Task PublishAsync(Guid projectId, string eventType, object payload)
    {
        try
        {
            await _hub.Clients
                .Group(BoardHub.GroupFor(projectId))
                .SendAsync("event", new { type = eventType, projectId, payload });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "SignalR publish failed for {EventType} on project {ProjectId}",
                eventType, projectId);
        }
    }
}
