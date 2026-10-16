using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.Application.Agents;

/// <summary>
/// Папки наблюдения. Настраивать папку может пользователь с правом <see cref="ProjectPermission.ManageWatchedFolders"/> в проекте;
/// агент и проект должны принадлежать одной организации.
/// </summary>
public sealed class WatchedFolderService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit)
{
    public const string FolderNotFound = "Папка наблюдения не найдена.";

    public async Task<IReadOnlyList<WatchedFolderDto>> ListForAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await db.AgentInstallations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
            ?? throw new NotFoundException(AgentService.AgentNotFound);
        var organizationAccess = await access.RequireOrganizationAsync(agent.OrganizationId, OrganizationPermission.View, cancellationToken);

        var rows = await db.WatchedFolders.AsNoTracking()
            .Where(f => f.AgentInstallationId == agentId)
            .OrderBy(f => f.Project!.Code).ThenBy(f => f.Path)
            .Select(f => new { Folder = f, f.Project!.Code, f.Project.Name })
            .ToListAsync(cancellationToken);

        // Не администраторы видят только папки проектов, в которых состоят.
        if (!organizationAccess.Permissions.Has(OrganizationPermission.ViewAllProjects))
        {
            var userId = currentUser.RequireUserId();
            var myProjects = await db.ProjectMemberships.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.ProjectId).ToListAsync(cancellationToken);
            rows = rows.Where(r => myProjects.Contains(r.Folder.ProjectId)).ToList();
        }

        return rows.Select(r => ToDto(r.Folder, r.Code, r.Name)).ToList();
    }

    public async Task<WatchedFolderDto> CreateAsync(Guid agentId, SaveWatchedFolderRequest request, CancellationToken cancellationToken)
    {
        var agent = await db.AgentInstallations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
            ?? throw new NotFoundException(AgentService.AgentNotFound);
        await access.RequireOrganizationAsync(agent.OrganizationId, OrganizationPermission.View, cancellationToken);
        if (agent.IsRevoked)
        {
            throw new DomainRuleException("Ключ агента отозван: настройка папок невозможна. Зарегистрируйте агента заново.");
        }

        var projectAccess = await access.RequireProjectAsync(request.ProjectId, ProjectPermission.ManageWatchedFolders, cancellationToken);
        if (projectAccess.Project.OrganizationId != agent.OrganizationId)
        {
            throw new NotFoundException(AccessService.ProjectNotFound);
        }

        var userId = currentUser.RequireUserId();
        var folder = WatchedFolder.Create(agentId, request.ProjectId, request.Path, request.IncludePatterns, request.ExcludePatterns,
            ToDomain(request.MatchingRule), request.DocumentNumberPattern, request.AutoRegisterUnknown, request.IsEnabled, userId, clock.UtcNow);

        if (await db.WatchedFolders.AnyAsync(f => f.AgentInstallationId == agentId && f.NormalizedPath == folder.NormalizedPath, cancellationToken))
        {
            throw new ConflictException("Эта папка уже настроена для данного агента.");
        }

        db.WatchedFolders.Add(folder);
        audit.Record(AuditActions.WatchedFolderCreated, AuditEntityTypes.WatchedFolder, folder.Id.ToString(), agent.OrganizationId, folder.ProjectId,
            new { agentId, folder.Path, rule = folder.MatchingRule.ToString(), folder.AutoRegisterUnknown });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(folder, projectAccess.Project.Code, projectAccess.Project.Name);
    }

    public async Task<WatchedFolderDto> UpdateAsync(Guid folderId, SaveWatchedFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await db.WatchedFolders.Include(f => f.Agent).FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new NotFoundException(FolderNotFound);
        var projectAccess = await access.RequireProjectAsync(folder.ProjectId, ProjectPermission.ManageWatchedFolders, cancellationToken);
        if (request.ProjectId != folder.ProjectId)
        {
            throw new ValidationException("projectId", "Проект папки наблюдения изменить нельзя — создайте новую папку.");
        }

        folder.Update(request.Path, request.IncludePatterns, request.ExcludePatterns, ToDomain(request.MatchingRule), request.DocumentNumberPattern,
            request.AutoRegisterUnknown, request.IsEnabled && !(folder.Agent?.IsRevoked ?? false), clock.UtcNow);

        if (await db.WatchedFolders.AnyAsync(f => f.AgentInstallationId == folder.AgentInstallationId && f.Id != folderId && f.NormalizedPath == folder.NormalizedPath, cancellationToken))
        {
            throw new ConflictException("Эта папка уже настроена для данного агента.");
        }

        audit.Record(AuditActions.WatchedFolderUpdated, AuditEntityTypes.WatchedFolder, folder.Id.ToString(), projectAccess.Project.OrganizationId, folder.ProjectId,
            new { folder.Path, folder.IsEnabled, rule = folder.MatchingRule.ToString(), folder.AutoRegisterUnknown });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(folder, projectAccess.Project.Code, projectAccess.Project.Name);
    }

    public async Task DeleteAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var folder = await db.WatchedFolders.FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new NotFoundException(FolderNotFound);
        var projectAccess = await access.RequireProjectAsync(folder.ProjectId, ProjectPermission.ManageWatchedFolders, cancellationToken);
        db.WatchedFolders.Remove(folder);
        audit.Record(AuditActions.WatchedFolderDeleted, AuditEntityTypes.WatchedFolder, folder.Id.ToString(), projectAccess.Project.OrganizationId, folder.ProjectId,
            new { folder.Path });
        await db.SaveChangesAsync(cancellationToken);
    }

    public static WatchedFolderDto ToDto(WatchedFolder folder, string projectCode, string projectName) => new(
        folder.Id,
        folder.AgentInstallationId,
        folder.ProjectId,
        projectCode,
        projectName,
        folder.Path,
        folder.IncludePatterns,
        folder.ExcludePatterns,
        (FileMatchingRule)(int)folder.MatchingRule,
        folder.DocumentNumberPattern,
        folder.AutoRegisterUnknown,
        folder.IsEnabled,
        folder.CreatedAtUtc,
        folder.UpdatedAtUtc);

    private static Dom.FileMatchingRule ToDomain(FileMatchingRule rule)
    {
        var value = (Dom.FileMatchingRule)(int)rule;
        return Enum.IsDefined(value) ? value : throw new ValidationException("matchingRule", "Недопустимое правило сопоставления.");
    }
}
