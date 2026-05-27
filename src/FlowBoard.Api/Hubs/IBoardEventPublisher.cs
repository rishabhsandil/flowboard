namespace FlowBoard.Api.Hubs;

/// <summary>
/// Publishes typed events to every client connected to the
/// <c>project:{projectId}</c> SignalR group. Implementations should be
/// non-blocking and never throw — a SignalR failure must not break the
/// underlying write that triggered it.
/// </summary>
public interface IBoardEventPublisher
{
    /// <param name="projectId">The project whose subscribers should receive the event.</param>
    /// <param name="eventType">Stable identifier (e.g. <c>issue_updated</c>) — clients dispatch on this.</param>
    /// <param name="payload">Arbitrary serialisable object; goes straight to the client as JSON.</param>
    Task PublishAsync(Guid projectId, string eventType, object payload);
}
