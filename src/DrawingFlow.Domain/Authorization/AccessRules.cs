using DrawingFlow.Domain.Enums;

namespace DrawingFlow.Domain.Authorization;

/// <summary>
/// Правила доступа: какие права даёт роль. Единственный источник истины для сервера; UI лишь отражает их.
/// </summary>
public static class AccessRules
{
    public static OrganizationPermission PermissionsOf(OrganizationRole role) => role switch
    {
        OrganizationRole.Member => OrganizationPermission.View,
        OrganizationRole.OrganizationAdmin =>
            OrganizationPermission.View
            | OrganizationPermission.ManageMembers
            | OrganizationPermission.CreateProjects
            | OrganizationPermission.ViewAllProjects
            | OrganizationPermission.ViewAudit
            | OrganizationPermission.ManageAgents
            | OrganizationPermission.CreateUsers,
        OrganizationRole.Owner =>
            PermissionsOf(OrganizationRole.OrganizationAdmin) | OrganizationPermission.ManageSettings,
        _ => OrganizationPermission.None,
    };

    public static ProjectPermission PermissionsOf(ProjectRole role) => role switch
    {
        ProjectRole.Viewer => ProjectPermission.View,
        ProjectRole.Reviewer => ProjectPermission.View | ProjectPermission.Review,
        ProjectRole.Engineer =>
            ProjectPermission.View
            | ProjectPermission.RegisterDocuments
            | ProjectPermission.UploadVersions
            | ProjectPermission.SubmitForReview
            | ProjectPermission.ManageWatchedFolders,
        ProjectRole.ProjectManager =>
            PermissionsOf(ProjectRole.Engineer)
            | PermissionsOf(ProjectRole.Reviewer)
            | ProjectPermission.EditSettings
            | ProjectPermission.ManageMembers
            | ProjectPermission.Release
            | ProjectPermission.ViewAudit,
        _ => ProjectPermission.None,
    };

    /// <summary>Права агента в проекте, для которого у него есть включённая папка наблюдения.</summary>
    public const ProjectPermission AgentProjectPermissions = ProjectPermission.View | ProjectPermission.UploadVersions;

    /// <summary>Автоматическая регистрация неизвестных файлов разрешается только явной настройкой папки.</summary>
    public static ProjectPermission AgentPermissions(bool autoRegisterUnknown) =>
        autoRegisterUnknown ? AgentProjectPermissions | ProjectPermission.RegisterDocuments : AgentProjectPermissions;

    public static bool Has(this OrganizationPermission set, OrganizationPermission required) => (set & required) == required;

    public static bool Has(this ProjectPermission set, ProjectPermission required) => (set & required) == required;

    /// <summary>
    /// Эффективная роль в проекте: владельцы и администраторы организации действуют как руководители всех её проектов;
    /// у остальных участников — явно назначенная роль (или её отсутствие).
    /// </summary>
    public static ProjectRole? EffectiveProjectRole(OrganizationRole? organizationRole, ProjectRole? explicitProjectRole)
    {
        if (organizationRole is OrganizationRole.Owner or OrganizationRole.OrganizationAdmin)
        {
            return ProjectRole.ProjectManager;
        }

        return organizationRole is null ? null : explicitProjectRole;
    }

    /// <summary>Может ли участник с ролью <paramref name="actorRole"/> назначить (или снять) роль <paramref name="targetRole"/>.</summary>
    public static bool CanAssignOrganizationRole(OrganizationRole actorRole, OrganizationRole targetRole) => actorRole switch
    {
        OrganizationRole.Owner => true,
        OrganizationRole.OrganizationAdmin => targetRole != OrganizationRole.Owner,
        _ => false,
    };
}
