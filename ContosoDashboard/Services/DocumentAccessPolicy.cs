using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed record DocumentPermissions(
    bool CanRead,
    bool CanEdit,
    bool CanChangeProject,
    bool CanReplace,
    bool CanDelete,
    bool CanShare);

public sealed class DocumentAccessPolicy
{
    private readonly ApplicationDbContext _context;

    public DocumentAccessPolicy(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentPermissions?> EvaluateAsync(int actorUserId, int documentId)
    {
        var document = await _context.Documents
            .AsNoTracking()
            .Include(d => d.UploadedByUser)
            .Include(d => d.Project)
            .ThenInclude(project => project!.ProjectMembers)
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(user => user.UserId == actorUserId);
        if (document is null || actor is null)
            return null;

        var isOwner = document.UploadedByUserId == actorUserId;
        var isAdmin = actor.Role == UserRole.Administrator;
        var isProjectManager = document.Project?.ProjectManagerId == actorUserId;
        var isProjectMember = document.Project?.ProjectMembers.Any(member => member.UserId == actorUserId) == true;
        var isTeamLead = actor.Role == UserRole.TeamLead &&
                         !string.IsNullOrWhiteSpace(actor.Department) &&
                         string.Equals(document.UploadedByUser.Department, actor.Department, StringComparison.OrdinalIgnoreCase);
        var isShared = document.Shares.Any(share =>
            share.SharedWithUserId == actorUserId ||
            (!string.IsNullOrWhiteSpace(actor.Department) &&
             string.Equals(share.SharedWithDepartment, actor.Department, StringComparison.OrdinalIgnoreCase)));
        var canRead = isOwner || isAdmin || isProjectManager || isProjectMember || isTeamLead || isShared;

        return new DocumentPermissions(
            CanRead: canRead,
            CanEdit: isOwner || isTeamLead,
            CanChangeProject: isOwner,
            CanReplace: isOwner,
            CanDelete: isOwner || isProjectManager || isTeamLead || isAdmin,
            CanShare: isOwner);
    }

    public IQueryable<Document> ReadableBy(IQueryable<Document> query, User actor)
    {
        var userId = actor.UserId;
        var department = actor.Department;
        var isAdmin = actor.Role == UserRole.Administrator;
        var isTeamLead = actor.Role == UserRole.TeamLead && !string.IsNullOrWhiteSpace(department);

        return query.Where(document =>
            isAdmin ||
            document.UploadedByUserId == userId ||
            (document.ProjectId != null &&
             (document.Project!.ProjectManagerId == userId || document.Project.ProjectMembers.Any(member => member.UserId == userId))) ||
            (isTeamLead && document.UploadedByUser.Department == department) ||
            document.Shares.Any(share =>
                share.SharedWithUserId == userId ||
                (department != null && share.SharedWithDepartment == department)));
    }
}