using System.Text.Json;
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
public class SavedFiltersController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;

    public SavedFiltersController(DbConnectionFactory db, ProjectAuthorizer authz)
    { _db = db; _authz = authz; }

    [HttpGet("projects/{projectId:guid}/saved-filters")]
    public async Task<IActionResult> List(Guid projectId)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var rows = await c.QueryAsync<SavedFilter>(
            SavedFilterQueries.ListByUserProject,
            new { ProjectId = projectId, UserId = User.GetUserId() });
        return Ok(new { items = rows.Select(Map) });
    }

    [HttpPost("projects/{projectId:guid}/saved-filters")]
    public async Task<IActionResult> Create(Guid projectId, CreateSavedFilterRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        try
        {
            var row = await c.QuerySingleAsync<SavedFilter>(SavedFilterQueries.Insert, new
            {
                ProjectId = projectId,
                UserId    = User.GetUserId(),
                req.Name,
                Filters   = req.Filters.GetRawText(),
            });
            return Created($"/api/saved-filters/{row.Id}", new { savedFilter = Map(row) });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { error = "saved_filter_name_exists" });
        }
    }

    [HttpDelete("saved-filters/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        using var c = _db.Create();
        var existing = await c.QuerySingleOrDefaultAsync<SavedFilter>(
            SavedFilterQueries.GetById, new { Id = id });
        if (existing is null) return NotFound();
        // Only the owner of the preset may delete it.
        if (existing.UserId != User.GetUserId()) return Forbid();
        await c.ExecuteAsync(SavedFilterQueries.Delete, new { Id = id });
        return NoContent();
    }

    private static object Map(SavedFilter sf)
    {
        using var doc = JsonDocument.Parse(sf.Filters);
        return new
        {
            id        = sf.Id,
            projectId = sf.ProjectId,
            userId    = sf.UserId,
            name      = sf.Name,
            filters   = doc.RootElement.Clone(),
            createdAt = sf.CreatedAt,
        };
    }
}
