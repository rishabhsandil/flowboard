using Dapper;
using FlowBoard.Api.Activity;
using FlowBoard.Api.Auth;
using FlowBoard.Api.Models;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;
using FlowBoard.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace FlowBoard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class ProjectsController : ControllerBase
{
    private readonly DbConnectionFactory _db;
    private readonly ProjectAuthorizer _authz;
    private readonly ActivityLogger _activity;

    public ProjectsController(DbConnectionFactory db, ProjectAuthorizer authz, ActivityLogger activity)
    { _db = db; _authz = authz; _activity = activity; }

    [HttpGet("projects")]
    public async Task<IActionResult> List([FromQuery] PageRequest page)
    {
        var userId = User.GetUserId();
        using var c = _db.Create();
        var args = new { UserId = userId, page.Skip, page.Take };
        var projects = await c.QueryAsync<Project>(ProjectQueries.ListForUser, args);
        var total    = await c.ExecuteScalarAsync<int>(ProjectQueries.CountForUser, args);
        return Ok(new Paged<Project>(projects, total, page.Skip, page.Take));
    }

    [HttpPost("projects")]
    public async Task<IActionResult> Create(CreateProjectRequest req)
    {
        var userId = User.GetUserId();
        var slug = await GenerateUniqueSlug(req.Name);

        using var c = _db.Create();
        await c.OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        try
        {
            var project = await c.QuerySingleAsync<Project>(ProjectQueries.Insert, new
            {
                req.Name,
                Slug = slug,
                req.Description,
                OwnerId = userId
            }, tx);

            await c.ExecuteAsync(ProjectQueries.InsertMember, new
            {
                ProjectId = project.Id, UserId = userId, Role = "owner"
            }, tx);

            var boardId = await c.ExecuteScalarAsync<Guid>(
                ProjectQueries.InsertBoard, new { ProjectId = project.Id }, tx);

            await c.ExecuteAsync(ProjectQueries.InsertDefaultColumns, new { BoardId = boardId }, tx);

            await tx.CommitAsync();
            return Created($"/api/projects/{project.Slug}", new { project });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpGet("projects/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        using var c = _db.Create();
        var project = await c.QuerySingleOrDefaultAsync<Project>(
            ProjectQueries.GetBySlugForUser, new { Slug = slug, UserId = User.GetUserId() });
        return project is null ? NotFound() : Ok(new { project });
    }

    [HttpPatch("projects/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateProjectRequest req)
    {
        if (!await _authz.IsMemberAsync(id, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        var project = await c.QuerySingleAsync<Project>(ProjectQueries.Update, new
        {
            Id = id, req.Name, req.Description
        });
        return Ok(new { project });
    }

    [HttpDelete("projects/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _authz.IsOwnerAsync(id, User.GetUserId())) return Forbid();
        using var c = _db.Create();
        await c.ExecuteAsync(ProjectQueries.Delete, new { Id = id });
        return NoContent();
    }

    // ---------- MEMBERS ----------
    // The list endpoint already lives on IssuesController for the assignee
    // picker; everything that *mutates* membership is gathered here so the
    // authorization story is in one place.

    /// <summary>
    /// Add a project member by email. Owner-only. Idempotent: re-adding
    /// an existing member is a 200 with the existing role (no role change
    /// — promotions go through <see cref="UpdateMemberRole"/>).
    /// Returns <c>404 user_not_found</c> if no account matches the email
    /// so the inviter knows to send a sign-up link.
    /// </summary>
    [HttpPost("projects/{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, AddMemberRequest req)
    {
        var actor = User.GetUserId();
        if (!await _authz.IsOwnerAsync(id, actor)) return Forbid();

        using var c = _db.Create();
        var user = await c.QuerySingleOrDefaultAsync<User>(
            ProjectQueries.FindUserByEmail, new { req.Email });
        if (user is null) return NotFound(new { error = "user_not_found" });

        var role = req.Role ?? "member";
        await c.ExecuteAsync(ProjectQueries.InsertMemberReturning, new
        {
            ProjectId = id, UserId = user.Id, Role = role
        });

        await _activity.LogAsync(c, id, null, actor, "member_added", new
        {
            userId = user.Id,
            email  = user.Email,
            name   = user.Name,
            role,
        });

        return Created(
            $"/api/projects/{id}/members/{user.Id}",
            new { member = new { id = user.Id, email = user.Email, name = user.Name, avatarUrl = user.AvatarUrl, role } });
    }

    /// <summary>
    /// Promote / demote. Owner-only. Refuses to demote the last owner so
    /// the project always has somebody who can manage it; the UI should
    /// gate the button on that count too, but enforce it server-side.
    /// </summary>
    [HttpPatch("projects/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid userId, UpdateMemberRoleRequest req)
    {
        var actor = User.GetUserId();
        if (!await _authz.IsOwnerAsync(id, actor)) return Forbid();

        using var c = _db.Create();
        var current = await c.ExecuteScalarAsync<string?>(
            ProjectQueries.GetMemberRole, new { ProjectId = id, UserId = userId });
        if (current is null) return NotFound(new { error = "member_not_found" });
        if (current == req.Role) return Ok(new { role = current });

        if (current == "owner" && req.Role == "member")
        {
            var owners = await c.ExecuteScalarAsync<int>(
                ProjectQueries.CountOwners, new { ProjectId = id });
            if (owners <= 1) return BadRequest(new { error = "last_owner" });
        }

        await c.ExecuteAsync(ProjectQueries.UpdateMemberRole, new
        {
            ProjectId = id, UserId = userId, Role = req.Role
        });
        await _activity.LogAsync(c, id, null, actor, "member_role_changed", new
        {
            userId, from = current, to = req.Role
        });
        return Ok(new { role = req.Role });
    }

    /// <summary>
    /// Remove a member, or — when the actor is removing themselves —
    /// "leave project". Owners may remove anyone except the last owner;
    /// non-owners may only remove themselves. Returning 404 instead of
    /// 403 when not a member at all keeps the response shape consistent
    /// with the role endpoint.
    /// </summary>
    [HttpDelete("projects/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        var actor = User.GetUserId();
        if (!await _authz.IsMemberAsync(id, actor)) return Forbid();

        var isSelf  = actor == userId;
        var isOwner = await _authz.IsOwnerAsync(id, actor);
        if (!isSelf && !isOwner) return Forbid();

        using var c = _db.Create();
        var targetRole = await c.ExecuteScalarAsync<string?>(
            ProjectQueries.GetMemberRole, new { ProjectId = id, UserId = userId });
        if (targetRole is null) return NotFound(new { error = "member_not_found" });

        if (targetRole == "owner")
        {
            var owners = await c.ExecuteScalarAsync<int>(
                ProjectQueries.CountOwners, new { ProjectId = id });
            if (owners <= 1) return BadRequest(new { error = "last_owner" });
        }

        await c.ExecuteAsync(ProjectQueries.DeleteMember, new
        {
            ProjectId = id, UserId = userId
        });
        await _activity.LogAsync(c, id, null, actor,
            isSelf ? "member_left" : "member_removed", new { userId });
        return NoContent();
    }

    // ----- helpers -----
    private async Task<string> GenerateUniqueSlug(string name)
    {
        var baseSlug = Slugify(name);
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "project";

        using var c = _db.Create();
        var slug = baseSlug;
        var i = 1;
        while (await c.ExecuteScalarAsync<int?>(ProjectQueries.SlugExists, new { Slug = slug }) is not null)
        {
            i++;
            slug = $"{baseSlug}-{i}";
        }
        return slug;
    }

    private static string Slugify(string s)
    {
        var lower = s.ToLowerInvariant();
        var hyphenated = Regex.Replace(lower, "[^a-z0-9]+", "-").Trim('-');
        return hyphenated.Length > 60 ? hyphenated[..60].Trim('-') : hyphenated;
    }
}
