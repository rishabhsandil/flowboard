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
public class LabelsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;
    private readonly ActivityLogger _activity;

    public LabelsController(DbConnectionFactory db, ProjectAuthorizer authz, ActivityLogger activity)
    { _db = db; _authz = authz; _activity = activity; }

    // -------- project-scoped CRUD --------

    [HttpGet("projects/{projectId:guid}/labels")]
    public async Task<IActionResult> List(Guid projectId)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var rows = await c.QueryAsync<Label>(LabelQueries.ListByProject, new { ProjectId = projectId });
        return Ok(new { items = rows });
    }

    [HttpPost("projects/{projectId:guid}/labels")]
    public async Task<IActionResult> Create(Guid projectId, CreateLabelRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        try
        {
            var label = await c.QuerySingleAsync<Label>(LabelQueries.Insert, new
            {
                ProjectId = projectId,
                req.Name,
                Color = req.Color ?? "#64748b",
            });
            return Created($"/api/labels/{label.Id}", new { label });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            // duplicate (project_id, lower(name))
            return Conflict(new { error = "label_name_exists" });
        }
    }

    [HttpPatch("labels/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateLabelRequest req)
    {
        using var c = _db.Create();
        var existing = await c.QuerySingleOrDefaultAsync<Label>(LabelQueries.GetById, new { Id = id });
        if (existing is null) return NotFound();
        if (!await _authz.IsMemberAsync(existing.ProjectId, User.GetUserId())) return Forbid();
        try
        {
            var label = await c.QuerySingleAsync<Label>(LabelQueries.Update, new
            {
                Id = id, req.Name, req.Color,
            });
            return Ok(new { label });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { error = "label_name_exists" });
        }
    }

    [HttpDelete("labels/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        using var c = _db.Create();
        var existing = await c.QuerySingleOrDefaultAsync<Label>(LabelQueries.GetById, new { Id = id });
        if (existing is null) return NotFound();
        if (!await _authz.IsMemberAsync(existing.ProjectId, User.GetUserId())) return Forbid();
        await c.ExecuteAsync(LabelQueries.Delete, new { Id = id });
        return NoContent();
    }

    // -------- attach / detach to issues --------

    [HttpGet("issues/{issueId:guid}/labels")]
    public async Task<IActionResult> ForIssue(Guid issueId)
    {
        var projectId = await IssueProject(issueId);
        if (projectId is null || !await _authz.IsMemberAsync(projectId.Value, User.GetUserId()))
            return Forbid();
        using var c = _db.Create();
        var rows = await c.QueryAsync<Label>(LabelQueries.ListForIssue, new { IssueId = issueId });
        return Ok(new { items = rows });
    }

    [HttpPost("issues/{issueId:guid}/labels")]
    public async Task<IActionResult> Attach(Guid issueId, AttachLabelRequest req)
    {
        var projectId = await IssueProject(issueId);
        if (projectId is null || !await _authz.IsMemberAsync(projectId.Value, User.GetUserId()))
            return Forbid();

        using var c = _db.Create();
        // Cross-project guard: refuse to attach a label that belongs to a different project.
        var ok = await c.ExecuteScalarAsync<int?>(
            LabelQueries.ValidateLabelMatchesIssueProject,
            new { IssueId = issueId, req.LabelId });
        if (!ok.HasValue) return BadRequest(new { error = "label_project_mismatch" });

        await c.ExecuteAsync(LabelQueries.Attach, new { IssueId = issueId, req.LabelId });

        var label = await c.QuerySingleOrDefaultAsync<Label>(LabelQueries.GetById, new { Id = req.LabelId });
        if (label is not null)
        {
            await _activity.LogAsync(c, projectId.Value, issueId, User.GetUserId(), "label_added", new
            {
                labelId = label.Id, name = label.Name, color = label.Color,
            });
        }
        return NoContent();
    }

    [HttpDelete("issues/{issueId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> Detach(Guid issueId, Guid labelId)
    {
        var projectId = await IssueProject(issueId);
        if (projectId is null || !await _authz.IsMemberAsync(projectId.Value, User.GetUserId()))
            return Forbid();

        using var c = _db.Create();
        var label = await c.QuerySingleOrDefaultAsync<Label>(LabelQueries.GetById, new { Id = labelId });
        await c.ExecuteAsync(LabelQueries.Detach, new { IssueId = issueId, LabelId = labelId });
        if (label is not null)
        {
            await _activity.LogAsync(c, projectId.Value, issueId, User.GetUserId(), "label_removed", new
            {
                labelId = label.Id, name = label.Name, color = label.Color,
            });
        }
        return NoContent();
    }

    private async Task<Guid?> IssueProject(Guid issueId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
    }
}
