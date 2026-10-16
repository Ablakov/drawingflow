
namespace DrawingFlow.Contracts.Users;

public sealed record UserDto(
    Guid Id,
    string Login,
    string DisplayName,
    string? Email,
    bool IsServerAdministrator,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    bool MustChangePassword = false);

public sealed record OrganizationSummaryDto(Guid Id, string Code, string Name, bool IsActive, OrganizationRole Role);

/// <summary>Текущий пользователь и его организации.</summary>
public sealed record CurrentUserDto(UserDto User, IReadOnlyList<OrganizationSummaryDto> Organizations);

/// <summary>
/// Создание пользователя. Администратор сервера может создать пользователя без организации;
/// администратор организации — только с добавлением в свою организацию.
/// </summary>
public sealed record CreateUserRequest(
    string Login,
    string DisplayName,
    string? Email,
    string Password,
    Guid? OrganizationId = null,
    OrganizationRole? OrganizationRole = null,
    bool IsServerAdministrator = false,
    // Пароль временный: пользователь обязан сменить его при первом входе.
    bool RequirePasswordChange = true);

public sealed record UpdateUserRequest(
    string DisplayName,
    string? Email,
    bool IsActive);
