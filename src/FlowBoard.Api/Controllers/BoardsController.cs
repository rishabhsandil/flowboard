using System.Text.Json;
using Dapper;
using FlowBoard.Api.Activity;
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
public class BoardsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;
    private readonly ActivityLogger _activity;

    public BoardsController(DbConnectionFactory db, ProjectAuthorizer authz, ActivityLogger activity)
    { _db = db; _authz = authz; _activity = activity; }

    /// <summary>Returns the entire board (all columns + their issues) in one round-trip.</summary>
    [HttpGet("projects/{projectId:guid}/board")]
    public async Task<IActionResult> GetBoard(Guid projectId)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();

        using var c = _db.Create();
        var boardId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.GetBoardId, new { ProjectId = projectId });
        if (boardId is null) return NotFound();

        var rows = await c.QueryAsync<BoardColumnRow>(
            IssueQueries.GetBoard, new { BoardId = boardId.Value });

        // The issues column is JSON text; pass it through as raw JSON so the
        // client doesn't have to round-trip parse.
        var columns = rows.Select(r => new
        {
            id = r.ColumnId,
            name = r.ColumnName,
            position = r.ColumnPosition,
            isDone = r.ColumnIsDone,
            issues = JsonDocument.Parse(r.IssuesJson).RootElement.Clone()
        });

        return Ok(new { boardId, columns });
    }

    [HttpPost("projects/{projectId:guid}/columns")]
    public async Task<IActionResult> CreateColumn(Guid projectId, CreateColumnRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var boardId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.GetBoardId, new { ProjectId = projectId });
        if (boardId is null) return NotFound();

        var col = await c.QuerySingleAsync<BoardColumn>(
            ProjectQueries.InsertColumn, new { BoardId = boardId.Value, req.Name, req.IsDone });
        await _activity.LogAsync(c, projectId, null, User.GetUserId(), "column_created", new
        {
            columnId = col.Id, name = col.Name, isDone = col.IsDone, position = col.Position,
        });
        return Created($"/api/columns/{col.Id}", new { column = col });
    }

    [HttpPatch("columns/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateColumnRequest req)
    {
        if (!await AuthorizeColumn(id)) return Forbid();
        using var c = _db.Create();
        var col = await c.QuerySingleAsync<BoardColumn>(
            ProjectQueries.RenameColumn, new { Id = id, req.Name, req.IsDone });
        var projectId = await c.ExecuteScalarAsync<Guid>(
            ProjectQueries.ColumnProjectId, new { ColumnId = id });
        await _activity.LogAsync(c, projectId, null, User.GetUserId(), "column_updated", new
        {
            columnId = col.Id, name = col.Name, isDone = col.IsDone,
        });
        return Ok(new { column = col });
    }

    [HttpDelete("columns/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await AuthorizeColumn(id)) return Forbid();
        using var c = _db.Create();
        // Resolve the project before the cascade wipes the column row.
        var projectId = await c.ExecuteScalarAsync<Guid>(
            ProjectQueries.ColumnProjectId, new { ColumnId = id });
        await c.ExecuteAsync(ProjectQueries.DeleteColumn, new { Id = id });
        await _activity.LogAsync(c, projectId, null, User.GetUserId(), "column_deleted", new
        {
            columnId = id,
        });
        return NoContent();
    }

    [HttpPatch("projects/{projectId:guid}/columns/reorder")]
    public async Task<IActionResult> Reorder(Guid projectId, ReorderColumnsRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        if (req.Ids.Length != req.Positions.Length)
            return BadRequest(new { error = "ids/positions length mismatch" });

        using var c = _db.Create();
        await c.ExecuteAsync(ProjectQueries.ReorderColumns, new { req.Ids, req.Positions });
        await _activity.LogAsync(c, projectId, null, User.GetUserId(), "columns_reordered", new
        {
            ids = req.Ids, positions = req.Positions,
        });
        return NoContent();
    }

    private async Task<bool> AuthorizeColumn(Guid columnId)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.ColumnProjectId, new { ColumnId = columnId });
        return projectId.HasValue && await _authz.IsMemberAsync(projectId.Value, User.GetUserId());
    }
}
