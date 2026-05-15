using Dapper;
using FlowBoard.Api.Auth;
using FlowBoard.Api.Models;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;
using FlowBoard.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowBoard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class ActivitiesController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;

    public ActivitiesController(DbConnectionFactory db, ProjectAuthorizer authz)
    { _db = db; _authz = authz; }

    [HttpGet("issues/{issueId:guid}/activity")]
    public async Task<IActionResult> ForIssue(Guid issueId, [FromQuery] PageRequest page)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, User.GetUserId())) return Forbid();

        var args = new { IssueId = issueId, page.Skip, page.Take };
        var rows  = await c.QueryAsync<ActivityRow>(ActivityQueries.ListByIssue, args);
        var total = await c.ExecuteScalarAsync<int>(ActivityQueries.CountByIssue, new { IssueId = issueId });
        return Ok(new Paged<ActivityRow>(rows, total, page.Skip, page.Take));
    }

    [HttpGet("projects/{projectId:guid}/activity")]
    public async Task<IActionResult> ForProject(Guid projectId, [FromQuery] PageRequest page)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var args = new { ProjectId = projectId, page.Skip, page.Take };
        var rows  = await c.QueryAsync<ActivityRow>(ActivityQueries.ListByProject, args);
        var total = await c.ExecuteScalarAsync<int>(ActivityQueries.CountByProject, new { ProjectId = projectId });
        return Ok(new Paged<ActivityRow>(rows, total, page.Skip, page.Take));
    }
}
