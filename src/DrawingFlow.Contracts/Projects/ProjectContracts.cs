
namespace DrawingFlow.Contracts.Projects;

public sealed record ProjectDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    string Name,
    string? Description,
    ProjectStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    ProjectRole CurrentUserRole,
    int MemberCount,
    IReadOnlyList<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

public sealed record CreateProjectRequest(
    string Code,
    string Name,
    string? Description = null);

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    ProjectStatus Status);

public sealed record ProjectMemberDto(
    Guid UserId,
    string Login,
    string DisplayName,
    ProjectRole Role,
    DateTime AddedAtUtc);

public sealed record AddProjectMemberRequest(string Login, ProjectRole Role);

public sealed record UpdateProjectMemberRequest(ProjectRole Role);

public enum ProjectSortField
{
    Name = 0,
    Code = 1,
    UpdatedAt = 2,
    CreatedAt = 3,
}

/// <summary>Параметры запроса списка проектов организации.</summary>
public sealed record ProjectListQuery(
    string? Search = null,
    ProjectStatus? Status = null,
    ProjectSortField Sort = ProjectSortField.Name,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);
