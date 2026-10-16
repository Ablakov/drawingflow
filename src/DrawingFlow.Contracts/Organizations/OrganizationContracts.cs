
namespace DrawingFlow.Contracts.Organizations;

public sealed record OrganizationDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc,
    OrganizationRole CurrentUserRole,
    int ProjectCount,
    int MemberCount,
    IReadOnlyList<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

public sealed record CreateOrganizationRequest(
    string Code,
    string Name,
    // Логин пользователя, который станет владельцем. Если не указан — создатель.
    string? OwnerLogin = null);

public sealed record UpdateOrganizationRequest(string Name, bool IsActive = true);

public sealed record OrganizationMemberDto(
    Guid UserId,
    string Login,
    string DisplayName,
    string? Email,
    bool IsActive,
    OrganizationRole Role,
    DateTime JoinedAtUtc);

public sealed record AddOrganizationMemberRequest(string Login, OrganizationRole Role);

public sealed record UpdateOrganizationMemberRequest(OrganizationRole Role);
