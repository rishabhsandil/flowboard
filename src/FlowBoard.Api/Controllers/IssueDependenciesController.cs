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

/// <summary>
/// CRUD endpoints for the directed "blocks / relates" graph between issues.
/// Both endpoints live under <c>/api/issues/{id}/dependencies</c>; the
/// listing returns outgoing + incoming edges so the UI renders both
/// directions ("blocks" vs "blocked by") in a single round-trip.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public class IssueDependenciesController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;
    private readonly ActivityLogger _activity;

    public IssueDependenciesController(
        DbConnectionFactory db, ProjectAuthorizer authz, ActivityLogger activity)
    { _db = db; _authz = authz; _activity = activity; }

    [HttpGet("issues/{issueId:guid}/dependencies")]
    public async Task<IActionResult> List(Guid issueId)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, User.GetUserId())) return Forbid();

        var rows = (await c.QueryAsync<IssueDependencyRow>(
            IssueDependencyQueries.ListForIssue, new { IssueId = issueId })).ToList();

        // Split for the client so each panel can render directly. The
        // server already orders by kind + created_at so insertion order is
        // preserved within each direction/kind bucket.
        var outgoing = rows.Where(r => r.Direction == "outgoing");
        var incoming = rows.Where(r => r.Direction == "incoming");
        return Ok(new { outgoing, incoming });
    }

    [HttpPost("issues/{issueId:guid}/dependencies")]
    public async Task<IActionResult> Create(Guid issueId, CreateIssueDependencyRequest req)
    {
        if (issueId == req.DependsOnId)
            return BadRequest(new { error = "dependency_self" });

        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, User.GetUserId())) return Forbid();

        // Both ends must live in the same project — refuses cross-project
        // links even if the actor happens to be a member of both.
        var sameProject = await c.ExecuteScalarAsync<int?>(
            IssueDependencyQueries.ValidateSameProject,
            new { IssueId = issueId, req.DependsOnId });
        if (!sameProject.HasValue)
            return BadRequest(new { error = "dependency_project_mismatch" });

        var kind = req.Kind ?? "blocks";

        // 1-hop cycle guard: if (B blocks A) exists, refuse (A blocks B).
        if (kind == "blocks")
        {
            var reverse = await c.ExecuteScalarAsync<int?>(
                IssueDependencyQueries.ReverseBlocksExists,
                new { IssueId = issueId, req.DependsOnId });
            if (reverse.HasValue)
                return Conflict(new { error = "dependency_cycle" });
        }

        var inserted = await c.QuerySingleOrDefaultAsync<IssueDependency>(
            IssueDependencyQueries.Insert,
            new { IssueId = issueId, req.DependsOnId, Kind = kind });

        if (inserted is not null)
        {
            await _activity.LogAsync(c, projectId.Value, issueId, User.GetUserId(),
                "dependency_added", new
                {
                    dependsOnId = req.DependsOnId,
                    kind,
                });
        }

        return Created(
            $"/api/issues/{issueId}/dependencies/{req.DependsOnId}",
            new { dependency = inserted ?? new IssueDependency(issueId, req.DependsOnId, kind, DateTime.UtcNow) });
    }

    [HttpDelete("issues/{issueId:guid}/dependencies/{dependsOnId:guid}")]
    public async Task<IActionResult> Delete(Guid issueId, Guid dependsOnId)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, User.GetUserId())) return Forbid();

        var affected = await c.ExecuteAsync(
            IssueDependencyQueries.Delete,
            new { IssueId = issueId, DependsOnId = dependsOnId });

        if (affected > 0)
        {
            await _activity.LogAsync(c, projectId.Value, issueId, User.GetUserId(),
                "dependency_removed", new { dependsOnId });
        }
        return NoContent();
    }

    /// <summary>
    /// Lightweight title-search inside the same project, used by the
    /// IssueModal dependency picker. Excludes the source issue itself so
    /// the user can never select it.
    /// </summary>
    [HttpGet("issues/{issueId:guid}/dependencies/search")]
    public async Task<IActionResult> Search(
        Guid issueId,
        [FromQuery] string? q,
        [FromQuery] int take = 10)
    {
        if (take is <= 0 or > 50) take = 10;
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, User.GetUserId())) return Forbid();

        var trimmed = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var rows = await c.QueryAsync<IssuePickerRow>(IssueDependencyQueries.SearchForPicker, new
        {
            ProjectId = projectId.Value,
            ExcludeId = issueId,
            Search    = trimmed,
            Take      = take,
        });
        return Ok(new { items = rows });
    }
}
