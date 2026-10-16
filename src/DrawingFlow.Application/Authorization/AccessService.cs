using DrawingFlow.Application.Abstractions;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Application.Authorization;

public sealed record OrganizationAccess(Organization Organization, OrganizationRole Role, OrganizationPermission Permissions);

public sealed record ProjectAccess(Project Project, OrganizationRole? OrganizationRole, ProjectRole EffectiveRole, ProjectPermission Permissions);

/// <summary>
/// Серверная проверка прав. Отсутствие доступа к объекту скрывается как «не найдено»,
/// чтобы не раскрывать существование чужих организаций и проектов (защита от IDOR).
/// Администратор сервера действует как владелец любой организации.
/// </summary>
public sealed class AccessService(IAppDbContext db, ICurrentUser currentUser)
{
    public const string OrganizationNotFound = "Организация не найдена.";
    public const string ProjectNotFound = "Проект не найден.";

    public const string AgentForbidden = "Операция недоступна агенту синхронизации.";

    public async Task<OrganizationRole?> GetOrganizationRoleAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (currentUser.IsAgent)
        {
            return null;
        }

        var userId = currentUser.RequireUserId();
        if (currentUser.IsServerAdministrator)
        {
            return OrganizationRole.Owner;
        }

        return await db.OrganizationMemberships
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OrganizationAccess> RequireOrganizationAsync(Guid organizationId, OrganizationPermission permission, CancellationToken cancellationToken)
    {
        if (currentUser.IsAgent)
        {
            throw new ForbiddenException(AgentForbidden);
        }

        currentUser.RequireUserId();
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        var role = organization is null ? null : await GetOrganizationRoleAsync(organizationId, cancellationToken);
        if (organization is null || role is null)
        {
            throw new NotFoundException(OrganizationNotFound);
        }

        var permissions = AccessRules.PermissionsOf(role.Value);
        if (!permissions.Has(permission))
        {
            throw new ForbiddenException();
        }

        return new OrganizationAccess(organization, role.Value, permissions);
    }

    public async Task<ProjectAccess> RequireProjectAsync(Guid projectId, ProjectPermission permission, CancellationToken cancellationToken)
    {
        if (currentUser.IsAgent)
        {
            return await RequireAgentProjectAsync(projectId, permission, cancellationToken);
        }

        var userId = currentUser.RequireUserId();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException(ProjectNotFound);

        var organizationRole = await GetOrganizationRoleAsync(project.OrganizationId, cancellationToken);
        var explicitRole = await db.ProjectMemberships
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .Select(m => (ProjectRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        var effectiveRole = AccessRules.EffectiveProjectRole(organizationRole, explicitRole)
            ?? throw new NotFoundException(ProjectNotFound);

        var permissions = AccessRules.PermissionsOf(effectiveRole);
        if (!permissions.Has(permission))
        {
            throw new ForbiddenException();
        }

        return new ProjectAccess(project, organizationRole, effectiveRole, permissions);
    }

    /// <summary>
    /// Агент видит только проекты, для которых у него есть включённая папка наблюдения, и действует с правами
    /// <see cref="AccessRules.AgentProjectPermissions"/>; регистрация документов разрешена лишь при включённой автоматической регистрации.
    /// </summary>
    private async Task<ProjectAccess> RequireAgentProjectAsync(Guid projectId, ProjectPermission permission, CancellationToken cancellationToken)
    {
        var agentId = currentUser.RequireAgentId();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException(ProjectNotFound);

        var folders = await db.WatchedFolders.AsNoTracking()
            .Where(f => f.AgentInstallationId == agentId && f.ProjectId == projectId && f.IsEnabled
                        && f.Agent!.RevokedAtUtc == null && f.Agent.OrganizationId == project.OrganizationId)
            .Select(f => f.AutoRegisterUnknown)
            .ToListAsync(cancellationToken);
        if (folders.Count == 0)
        {
            throw new NotFoundException(ProjectNotFound);
        }

        var permissions = AccessRules.AgentPermissions(folders.Any(auto => auto));
        if (!permissions.Has(permission))
        {
            throw new ForbiddenException(AgentForbidden);
        }

        return new ProjectAccess(project, null, ProjectRole.Engineer, permissions);
    }
}
