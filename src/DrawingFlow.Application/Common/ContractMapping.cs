using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Api = DrawingFlow.Contracts;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.Application.Common;

/// <summary>Преобразование доменных объектов в контракты API.</summary>
public static class ContractMapping
{
    public static Api.OrganizationRole ToApi(this Dom.OrganizationRole role) => (Api.OrganizationRole)(int)role;

    public static Api.ProjectRole ToApi(this Dom.ProjectRole role) => (Api.ProjectRole)(int)role;

    public static Api.ProjectStatus ToApi(this Dom.ProjectStatus status) => (Api.ProjectStatus)(int)status;

    public static Dom.OrganizationRole ToDomain(this Api.OrganizationRole role, string field = "role")
    {
        var value = (Dom.OrganizationRole)(int)role;
        return Enum.IsDefined(value) ? value : throw new ValidationException(field, "Недопустимая роль в организации.");
    }

    public static Dom.ProjectRole ToDomain(this Api.ProjectRole role, string field = "role")
    {
        var value = (Dom.ProjectRole)(int)role;
        return Enum.IsDefined(value) ? value : throw new ValidationException(field, "Недопустимая роль в проекте.");
    }

    public static Dom.ProjectStatus ToDomain(this Api.ProjectStatus status, string field = "status")
    {
        var value = (Dom.ProjectStatus)(int)status;
        return Enum.IsDefined(value) ? value : throw new ValidationException(field, "Недопустимый статус проекта.");
    }

    public static UserDto ToDto(this User user) => new(
        user.Id,
        user.Login,
        user.DisplayName,
        user.Email,
        user.IsServerAdministrator,
        user.IsActive,
        user.CreatedAtUtc,
        user.LastLoginAtUtc,
        user.MustChangePassword);

    public static OrganizationDto ToDto(this Organization organization, Dom.OrganizationRole currentUserRole, int projectCount, int memberCount) => new(
        organization.Id,
        organization.Code,
        organization.Name,
        organization.IsActive,
        organization.CreatedAtUtc,
        currentUserRole.ToApi(),
        projectCount,
        memberCount,
        PermissionNames(AccessRules.PermissionsOf(currentUserRole)));

    public static ProjectDto ToDto(this Project project, Dom.ProjectRole currentUserRole, int memberCount) => new(
        project.Id,
        project.OrganizationId,
        project.Code,
        project.Name,
        project.Description,
        project.Status.ToApi(),
        project.CreatedAtUtc,
        project.UpdatedAtUtc,
        currentUserRole.ToApi(),
        memberCount,
        PermissionNames(AccessRules.PermissionsOf(currentUserRole)));

    public static IReadOnlyList<string> PermissionNames(OrganizationPermission permissions) =>
        Enum.GetValues<OrganizationPermission>()
            .Where(p => p != OrganizationPermission.None && permissions.Has(p))
            .Select(p => p.ToString())
            .ToList();

    public static IReadOnlyList<string> PermissionNames(ProjectPermission permissions) =>
        Enum.GetValues<ProjectPermission>()
            .Where(p => p != ProjectPermission.None && permissions.Has(p))
            .Select(p => p.ToString())
            .ToList();
}
