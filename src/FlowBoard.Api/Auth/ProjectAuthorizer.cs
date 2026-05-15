using Dapper;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;

namespace FlowBoard.Api.Auth;

/// <summary>Centralised authorization checks against the project_members table.</summary>
public class ProjectAuthorizer
{
    private readonly DbConnectionFactory _db;
    public ProjectAuthorizer(DbConnectionFactory db) { _db = db; }

    public async Task<bool> IsMemberAsync(Guid projectId, Guid userId)
    {
        using var c = _db.Create();
        var row = await c.ExecuteScalarAsync<int?>(
            ProjectQueries.IsMember, new { ProjectId = projectId, UserId = userId });
        return row.HasValue;
    }

    public async Task<bool> IsOwnerAsync(Guid projectId, Guid userId)
    {
        using var c = _db.Create();
        var row = await c.ExecuteScalarAsync<int?>(
            ProjectQueries.IsOwner, new { ProjectId = projectId, UserId = userId });
        return row.HasValue;
    }
}
