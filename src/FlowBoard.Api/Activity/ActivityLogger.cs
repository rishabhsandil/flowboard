using System.Data;
using System.Text.Json;
using Dapper;
using FlowBoard.Api.Hubs;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;

namespace FlowBoard.Api.Activity;

/// <summary>
/// Writes rows to the <c>activities</c> table. The payload is serialised to
/// JSON here so callers can pass any anonymous object and stay free of the
/// JSON/JSONB plumbing.
///
/// As a side-effect every successful insert is broadcast through
/// <see cref="IBoardEventPublisher"/> so connected SignalR clients receive
/// a real-time event. Activities are therefore the canonical event log —
/// any new mutation that wants real-time fan-out only has to call
/// <see cref="LogAsync(System.Data.IDbConnection,System.Guid,System.Nullable{System.Guid},System.Guid,string,object?)"/>.
/// </summary>
public class ActivityLogger
{
    private readonly DbConnectionFactory _db;
    private readonly IBoardEventPublisher _publisher;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ActivityLogger(DbConnectionFactory db, IBoardEventPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public Task LogAsync(Guid projectId, Guid? issueId, Guid actorId, string type, object? payload = null)
    {
        using var c = _db.Create();
        return LogAsync(c, projectId, issueId, actorId, type, payload);
    }

    /// <summary>Variant that reuses an existing connection so callers can batch within a transaction.</summary>
    public async Task LogAsync(IDbConnection connection, Guid projectId, Guid? issueId, Guid actorId, string type, object? payload = null)
    {
        var resolvedPayload = payload ?? new { };
        var json = JsonSerializer.Serialize(resolvedPayload, JsonOpts);
        await connection.ExecuteAsync(ActivityQueries.Insert, new
        {
            ProjectId = projectId,
            IssueId   = issueId,
            ActorId   = actorId,
            Type      = type,
            Payload   = json,
        });

        // Fan out to SignalR subscribers. Publisher swallows transient
        // failures so a disconnecting client never poisons the activity
        // write that produced the event.
        await _publisher.PublishAsync(projectId, type, new
        {
            issueId,
            actorId,
            payload = resolvedPayload,
        });
    }
}
