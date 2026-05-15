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
public class EpicsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;

    public EpicsController(DbConnectionFactory db, ProjectAuthorizer authz)
    { _db = db; _authz = authz; }

    [HttpGet("projects/{projectId:guid}/epics")]
    public async Task<IActionResult> List(Guid projectId, [FromQuery] PageRequest page)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var args = new { ProjectId = projectId, page.Skip, page.Take };
        var epics = await c.QueryAsync<EpicWithProgress>(EpicQueries.ListWithProgress, args);
        var total = await c.ExecuteScalarAsync<int>(EpicQueries.CountByProject, args);
        return Ok(new Paged<EpicWithProgress>(epics, total, page.Skip, page.Take));
    }

    [HttpGet("epics/{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(EpicQueries.GetProjectId, new { Id = id });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, User.GetUserId())) return Forbid();

        var epic = await c.QuerySingleOrDefaultAsync<EpicWithProgress>(
            EpicQueries.GetWithProgress, new { Id = id });
        return epic is null ? NotFound() : Ok(new { epic });
    }

    [HttpPost("projects/{projectId:guid}/epics")]
    public async Task<IActionResult> Create(Guid projectId, CreateEpicRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var epic = await c.QuerySingleAsync<Epic>(EpicQueries.Insert, new
        {
            ProjectId = projectId,
            req.Title, req.Description, req.Color, req.StartDate, req.DueDate
        });
        return Created($"/api/epics/{epic.Id}", new { epic });
    }

    [HttpPatch("epics/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateEpicRequest req)
    {
        if (!await AuthorizeEpic(id)) return Forbid();
        using var c = _db.Create();
        var epic = await c.QuerySingleAsync<Epic>(EpicQueries.Update, new
        {
            Id = id,
            req.Title, req.Description, req.Color, req.StartDate, req.DueDate
        });
        return Ok(new { epic });
    }

    [HttpDelete("epics/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await AuthorizeEpic(id)) return Forbid();
        using var c = _db.Create();
        await c.ExecuteAsync(EpicQueries.Delete, new { Id = id });
        return NoContent();
    }

    private async Task<bool> AuthorizeEpic(Guid epicId)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.EpicProjectId, new { EpicId = epicId });
        return projectId.HasValue && await _authz.IsMemberAsync(projectId.Value, User.GetUserId());
    }
}
