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
public class CommentsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;
    private readonly ActivityLogger _activity;

    public CommentsController(DbConnectionFactory db, ProjectAuthorizer authz, ActivityLogger activity)
    { _db = db; _authz = authz; _activity = activity; }

    [HttpGet("issues/{issueId:guid}/comments")]
    public async Task<IActionResult> List(Guid issueId)
    {
        var projectId = await GetIssueProject(issueId);
        if (projectId is null || !await _authz.IsMemberAsync(projectId.Value, User.GetUserId()))
            return Forbid();

        using var c = _db.Create();
        var rows = await c.QueryAsync<CommentWithAuthor>(CommentQueries.ListByIssue, new { IssueId = issueId });
        return Ok(new { items = rows });
    }

    [HttpPost("issues/{issueId:guid}/comments")]
    public async Task<IActionResult> Create(Guid issueId, CreateCommentRequest req)
    {
        var projectId = await GetIssueProject(issueId);
        if (projectId is null || !await _authz.IsMemberAsync(projectId.Value, User.GetUserId()))
            return Forbid();

        var actor = User.GetUserId();
        using var c = _db.Create();

        var comment = await c.QuerySingleAsync<Comment>(CommentQueries.Insert, new
        {
            IssueId  = issueId,
            AuthorId = actor,
            req.Body,
        });

        var mentioned = await PersistMentionsAsync(c, projectId.Value, comment.Id, req.Body);

        await _activity.LogAsync(c, projectId.Value, issueId, actor, "comment_added", new
        {
            commentId = comment.Id,
            snippet   = Snippet(req.Body),
        });

        foreach (var uid in mentioned)
        {
            await _activity.LogAsync(c, projectId.Value, issueId, actor, "mention", new
            {
                commentId         = comment.Id,
                mentionedUserId   = uid,
            });
        }

        return Created($"/api/comments/{comment.Id}", new { comment, mentionedUserIds = mentioned });
    }

    [HttpPatch("comments/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateCommentRequest req)
    {
        using var c = _db.Create();
        var existing = await c.QuerySingleOrDefaultAsync<Comment>(CommentQueries.GetById, new { Id = id });
        if (existing is null) return NotFound();

        var actor = User.GetUserId();
        // Only the author can edit their own comment.
        if (existing.AuthorId != actor) return Forbid();

        var projectId = await c.ExecuteScalarAsync<Guid?>(
            CommentQueries.ProjectIdForComment, new { CommentId = id });
        if (projectId is null) return NotFound();
        if (!await _authz.IsMemberAsync(projectId.Value, actor)) return Forbid();

        var updated = await c.QuerySingleAsync<Comment>(CommentQueries.Update, new
        {
            Id = id,
            req.Body,
        });

        // Re-resolve mentions: drop old rows, parse fresh, log only NEW mentions.
        await c.ExecuteAsync(CommentQueries.DeleteMentionsForComment, new { CommentId = id });
        var mentioned = await PersistMentionsAsync(c, projectId.Value, id, req.Body);

        await _activity.LogAsync(c, projectId.Value, existing.IssueId, actor, "comment_edited", new
        {
            commentId = id,
            snippet   = Snippet(req.Body),
        });

        return Ok(new { comment = updated, mentionedUserIds = mentioned });
    }

    [HttpDelete("comments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        using var c = _db.Create();
        var existing = await c.QuerySingleOrDefaultAsync<Comment>(CommentQueries.GetById, new { Id = id });
        if (existing is null) return NotFound();

        var actor = User.GetUserId();
        var projectId = await c.ExecuteScalarAsync<Guid?>(
            CommentQueries.ProjectIdForComment, new { CommentId = id });
        if (projectId is null) return NotFound();

        // Author OR project owner may delete.
        var isAuthor = existing.AuthorId == actor;
        var isOwner  = await _authz.IsOwnerAsync(projectId.Value, actor);
        if (!isAuthor && !isOwner) return Forbid();

        await c.ExecuteAsync(CommentQueries.Delete, new { Id = id });
        await _activity.LogAsync(c, projectId.Value, existing.IssueId, actor, "comment_deleted", new
        {
            commentId = id,
        });
        return NoContent();
    }

    // -------- helpers --------

    private async Task<Guid?> GetIssueProject(Guid issueId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<Guid?>(
            ProjectQueries.IssueProjectId, new { IssueId = issueId });
    }

    /// <summary>
    /// Extracts unique <c>@handles</c> from <paramref name="body"/>, resolves
    /// them against the project's members, and writes <c>mentions</c> rows.
    /// Returns the list of mentioned user ids.
    /// </summary>
    private static async Task<Guid[]> PersistMentionsAsync(
        System.Data.IDbConnection c, Guid projectId, Guid commentId, string body)
    {
        var handles = MentionParser.Extract(body);
        if (handles.Length == 0) return Array.Empty<Guid>();

        var ids = (await c.QueryAsync<Guid>(
            CommentQueries.ResolveMentionHandles,
            new { ProjectId = projectId, Handles = handles })).ToArray();
        if (ids.Length == 0) return Array.Empty<Guid>();

        await c.ExecuteAsync(CommentQueries.InsertMentions, new
        {
            CommentId = commentId,
            UserIds   = ids,
        });
        return ids;
    }

    private static string Snippet(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= 140 ? trimmed : trimmed[..140] + "…";
    }
}
