using Dapper;
using FlowBoard.Api.Auth;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;
using FlowBoard.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowBoard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class ReportsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;

    public ReportsController(DbConnectionFactory db, ProjectAuthorizer authz)
    { _db = db; _authz = authz; }

    /// <summary>
    /// Cumulative Flow Diagram data for the project.
    /// Auto-takes a snapshot of today's column counts on each call (idempotent).
    /// Returns one row per (day, column) over the last <c>days</c> calendar days.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/reports/cfd")]
    public async Task<IActionResult> Cfd(Guid projectId, [FromQuery] int days = 30)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        if (days is <= 0 or > 365) days = 30;

        using var c = _db.Create();
        await c.ExecuteAsync(CfdQueries.UpsertTodaySnapshot, new { ProjectId = projectId });
        var points = await c.QueryAsync<CfdPoint>(CfdQueries.GetCfdData, new { ProjectId = projectId, Days = days });
        return Ok(new { cfd = points });
    }
}
