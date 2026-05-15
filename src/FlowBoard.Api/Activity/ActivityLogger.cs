using System.Data;
using System.Text.Json;
using Dapper;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;

namespace FlowBoard.Api.Activity;

/// <summary>
/// Writes rows to the <c>activities</c> table. The payload is serialised to
/// JSON here so callers can pass any anonymous object and stay free of the
/// JSON/JSONB plumbing.
/// </summary>
public class ActivityLogger
{
    private readonly DbConnectionFactory _db;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ActivityLogger(DbConnectionFactory db) { _db = db; }

    public Task LogAsync(Guid projectId, Guid? issueId, Guid actorId, string type, object? payload = null)
    {
        using var c = _db.Create();
        return LogAsync(c, projectId, issueId, actorId, type, payload);
    }

    /// <summary>Variant that reuses an existing connection so callers can batch within a transaction.</summary>
    public Task LogAsync(IDbConnection connection, Guid projectId, Guid? issueId, Guid actorId, string type, object? payload = null)
    {
        var json = JsonSerializer.Serialize(payload ?? new { }, JsonOpts);
        return connection.ExecuteAsync(ActivityQueries.Insert, new
        {
            ProjectId = projectId,
            IssueId   = issueId,
            ActorId   = actorId,
            Type      = type,
            Payload   = json,
        });
    }
}
