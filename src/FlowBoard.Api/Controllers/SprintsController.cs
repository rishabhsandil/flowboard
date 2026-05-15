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
public class SprintsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;

    public SprintsController(DbConnectionFactory db, ProjectAuthorizer authz)
    { _db = db; _authz = authz; }

    [HttpGet("projects/{projectId:guid}/sprints")]
    public async Task<IActionResult> List(Guid projectId, [FromQuery] PageRequest page)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var args = new { ProjectId = projectId, page.Skip, page.Take };
        var sprints = await c.QueryAsync<Sprint>(SprintQueries.ListByProject, args);
        var total   = await c.ExecuteScalarAsync<int>(SprintQueries.CountByProject, args);
        return Ok(new Paged<Sprint>(sprints, total, page.Skip, page.Take));
    }

    [HttpPost("projects/{projectId:guid}/sprints")]
    public async Task<IActionResult> Create(Guid projectId, CreateSprintRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        if (req.EndDate < req.StartDate)
            return BadRequest(new { error = "end_date_before_start_date" });

        using var c = _db.Create();
        var sprint = await c.QuerySingleAsync<Sprint>(SprintQueries.Insert, new
        {
            ProjectId = projectId,
            req.Name, req.Goal, req.StartDate, req.EndDate, req.Status
        });
        return Created($"/api/sprints/{sprint.Id}", new { sprint });
    }

    [HttpPatch("sprints/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateSprintRequest req)
    {
        if (!await AuthorizeSprint(id)) return Forbid();
        using var c = _db.Create();
        var sprint = await c.QuerySingleAsync<Sprint>(SprintQueries.Update, new
        {
            Id = id, req.Name, req.Goal, req.StartDate, req.EndDate, req.Status
        });
        return Ok(new { sprint });
    }

    [HttpDelete("sprints/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await AuthorizeSprint(id)) return Forbid();
        using var c = _db.Create();
        await c.ExecuteAsync(SprintQueries.Delete, new { Id = id });
        return NoContent();
    }

    /// <summary>Velocity for the chart: bar (per sprint) + line (cumulative).</summary>
    [HttpGet("sprints/{id:guid}/velocity")]
    public async Task<IActionResult> Velocity(Guid id)
    {
        if (!await AuthorizeSprint(id)) return Forbid();
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid>(
            ProjectQueries.SprintProjectId, new { SprintId = id });
        var rows = await c.QueryAsync<SprintVelocityRow>(
            SprintQueries.Velocity, new { ProjectId = projectId });
        return Ok(new { velocity = rows });
    }

    /// <summary>
    /// Daily burndown: one row per calendar day from sprint start to
    /// min(sprint end, today), showing remaining story points.
    /// </summary>
    [HttpGet("sprints/{id:guid}/burndown")]
    public async Task<IActionResult> Burndown(Guid id)
    {
        if (!await AuthorizeSprint(id)) return Forbid();
        using var c = _db.Create();
        var points = await c.QueryAsync<BurndownPoint>(SprintQueries.Burndown, new { SprintId = id });
        return Ok(new { burndown = points });
    }

    [HttpGet("sprints/{id:guid}/issues")]
    public async Task<IActionResult> SprintIssues(Guid id)
    {
        if (!await AuthorizeSprint(id)) return Forbid();
        using var c = _db.Create();
        var issues = await c.QueryAsync(SprintQueries.SprintIssues, new { SprintId = id });
        return Ok(new { issues });
    }

    private async Task<bool> AuthorizeSprint(Guid sprintId)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.SprintProjectId, new { SprintId = sprintId });
        return projectId.HasValue && await _authz.IsMemberAsync(projectId.Value, User.GetUserId());
    }
}
