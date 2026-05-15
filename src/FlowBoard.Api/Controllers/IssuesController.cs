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
public class IssuesController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;
    private readonly ActivityLogger _activity;

    public IssuesController(DbConnectionFactory db, ProjectAuthorizer authz, ActivityLogger activity)
    { _db = db; _authz = authz; _activity = activity; }

    [HttpPost("projects/{projectId:guid}/issues")]
    public async Task<IActionResult> Create(Guid projectId, CreateIssueRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();

        using var c = _db.Create();
        // Default to first column if none specified
        var columnId = req.ColumnId;
        if (columnId is null)
        {
            var boardId = await c.ExecuteScalarAsync<Guid?>(
                ProjectQueries.GetBoardId, new { ProjectId = projectId });
            if (boardId is null) return NotFound();
            columnId = await c.ExecuteScalarAsync<Guid?>(
                ProjectQueries.GetFirstColumnId,
                new { BoardId = boardId.Value });
        }

        var issue = await c.QuerySingleAsync<Issue>(IssueQueries.Insert, new
        {
            ProjectId   = projectId,
            ColumnId    = columnId,
            req.EpicId,
            req.SprintId,
            req.AssigneeId,
            req.ParentId,
            req.Title,
            req.Description,
            Priority    = req.Priority ?? "medium",
            StoryPoints = req.StoryPoints ?? 0,
        });

        await _activity.LogAsync(c, projectId, issue.Id, User.GetUserId(), "issue_created", new
        {
            title    = issue.Title,
            priority = issue.Priority,
        });
        return Created($"/api/issues/{issue.Id}", new { issue });
    }

    [HttpGet("issues/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        if (!await AuthorizeIssue(id)) return Forbid();
        using var c = _db.Create();
        var issue = await c.QuerySingleOrDefaultAsync<Issue>(IssueQueries.GetById, new { Id = id });
        return issue is null ? NotFound() : Ok(new { issue });
    }

    /// <summary>
    /// Project-wide issue list. All filters are optional; status/priority/
    /// search apply directly, and epic/sprint/assignee accept either a real
    /// id or the empty Guid sentinel ("00000000-…") meaning "no <em>foo</em>".
    /// Results are ordered open-first → priority → most-recently-updated.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/issues")]
    public async Task<IActionResult> List(
        Guid projectId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] Guid? epicId,
        [FromQuery] Guid? sprintId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] Guid? labelId,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        // Whitelist the small enum-ish strings so a malicious client can't
        // smuggle anything weird into the SQL parameter (it's parameterised
        // either way; this just turns garbage into a 400).
        if (status   is not null && status   is not ("open" or "closed"))            return BadRequest(new { error = "invalid_status" });
        if (priority is not null && priority is not ("low" or "medium" or "high" or "critical"))
            return BadRequest(new { error = "invalid_priority" });
        if (skip < 0)        skip = 0;
        if (take is <= 0 or > 100) take = 50;

        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var args = new
        {
            ProjectId  = projectId,
            Status     = status,
            Priority   = priority,
            EpicId     = epicId,
            SprintId   = sprintId,
            AssigneeId = assigneeId,
            LabelId    = labelId,
            Search     = trimmedSearch,
            Skip       = skip,
            Take       = take,
        };

        using var c = _db.Create();
        var rows  = (await c.QueryAsync<IssueListRow>(IssueQueries.ListByProject, args)).ToList();
        var total = await c.ExecuteScalarAsync<int>(IssueQueries.CountByProject, args);
        return Ok(new
        {
            items = rows,
            total,
            skip,
            take,
        });
    }

    [HttpGet("projects/{projectId:guid}/members")]
    public async Task<IActionResult> Members(Guid projectId)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var members = await c.QueryAsync<ProjectMember>(IssueQueries.ListMembers, new { ProjectId = projectId });
        return Ok(new { items = members });
    }

    [HttpPatch("issues/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateIssueRequest req)
    {
        if (!await AuthorizeIssue(id)) return Forbid();
        using var c = _db.Create();

        // Snapshot before so we can diff and log only changed fields.
        var before = await c.QuerySingleOrDefaultAsync<Issue>(IssueQueries.GetById, new { Id = id });
        if (before is null) return NotFound();

        var issue = await c.QuerySingleAsync<Issue>(IssueQueries.Update, new
        {
            Id = id,
            req.Title,
            req.Description,
            req.Priority,
            req.StoryPoints,
            req.ColumnId,
            req.EpicId,
            req.SprintId,
            req.AssigneeId,
            req.ParentId,
            SetClosed = req.Closed
        });

        var actor = User.GetUserId();
        var changes = new List<object>();
        if (before.Title       != issue.Title)       changes.Add(new { field = "title",       from = before.Title,       to = issue.Title });
        if (before.Description != issue.Description) changes.Add(new { field = "description" });
        if (before.Priority    != issue.Priority)    changes.Add(new { field = "priority",    from = before.Priority,    to = issue.Priority });
        if (before.StoryPoints != issue.StoryPoints) changes.Add(new { field = "storyPoints", from = before.StoryPoints, to = issue.StoryPoints });
        if (before.ColumnId    != issue.ColumnId)    changes.Add(new { field = "columnId",    from = before.ColumnId,    to = issue.ColumnId });
        if (before.EpicId      != issue.EpicId)      changes.Add(new { field = "epicId",      from = before.EpicId,      to = issue.EpicId });
        if (before.SprintId    != issue.SprintId)    changes.Add(new { field = "sprintId",    from = before.SprintId,    to = issue.SprintId });
        if (before.AssigneeId  != issue.AssigneeId)  changes.Add(new { field = "assigneeId",  from = before.AssigneeId,  to = issue.AssigneeId });

        // Treat close/reopen as their own event types (more useful in the UI).
        var wasClosed = before.ClosedAt.HasValue;
        var nowClosed = issue.ClosedAt.HasValue;
        if (wasClosed != nowClosed)
        {
            await _activity.LogAsync(c, issue.ProjectId, issue.Id, actor,
                nowClosed ? "issue_closed" : "issue_reopened", new { });
        }

        if (changes.Count > 0)
        {
            await _activity.LogAsync(c, issue.ProjectId, issue.Id, actor, "issue_updated", new
            {
                changes,
            });
        }

        return Ok(new { issue });
    }

    [HttpDelete("issues/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await AuthorizeIssue(id)) return Forbid();
        using var c = _db.Create();
        // Capture project + title before the row is gone (CASCADE clears activities tied to this issue).
        var before = await c.QuerySingleOrDefaultAsync<Issue>(IssueQueries.GetById, new { Id = id });
        await c.ExecuteAsync(IssueQueries.Delete, new { Id = id });
        if (before is not null)
        {
            // Issue is gone — log against the project with issueId=null.
            await _activity.LogAsync(c, before.ProjectId, null, User.GetUserId(), "issue_deleted", new
            {
                issueId = before.Id,
                title   = before.Title,
            });
        }
        return NoContent();
    }

    /// <summary>Bulk reorder after drag-and-drop. One UPDATE round-trip via unnest().</summary>
    [HttpPatch("projects/{projectId:guid}/issues/reorder")]
    public async Task<IActionResult> Reorder(Guid projectId, ReorderIssuesRequest req)
    {
        if (!await _authz.IsMemberAsync(projectId, User.GetUserId())) return Forbid();
        if (req.Items is null || req.Items.Length == 0) return NoContent();

        var ids       = req.Items.Select(x => x.Id).ToArray();
        var columns   = req.Items.Select(x => x.ColumnId).ToArray();
        var positions = req.Items.Select(x => x.Position).ToArray();

        using var c = _db.Create();
        await c.ExecuteAsync(IssueQueries.Reorder, new
        {
            Ids = ids, ColumnIds = columns, Positions = positions
        });
        return NoContent();
    }

    /// <summary>Returns direct child issues of the given parent issue.</summary>
    [HttpGet("issues/{id:guid}/children")]
    public async Task<IActionResult> GetChildren(Guid id)
    {
        if (!await AuthorizeIssue(id)) return Forbid();
        using var c = _db.Create();
        var children = await c.QueryAsync<Issue>(IssueQueries.ChildrenByParent, new { ParentId = id });
        return Ok(new { items = children });
    }

    private async Task<bool> AuthorizeIssue(Guid issueId)
    {
        using var c = _db.Create();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
        return projectId.HasValue && await _authz.IsMemberAsync(projectId.Value, User.GetUserId());
    }
}
