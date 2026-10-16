using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>Пользователь сервера. Один пользователь может состоять в нескольких организациях.</summary>
public sealed class User
{
    public const int DisplayNameMaxLength = 200;

    private User() { }

    public Guid Id { get; private set; }
    public string Login { get; private set; } = string.Empty;
    public string NormalizedLogin { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsServerAdministrator { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>Пароль временный: до его смены доступны только вход, смена пароля и выход.</summary>
    public bool MustChangePassword { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }
    public DateTime? PasswordChangedAtUtc { get; private set; }

    public ICollection<OrganizationMembership> OrganizationMemberships { get; private set; } = new List<OrganizationMembership>();

    public static User Create(string login, string displayName, string? email, string passwordHash, bool isServerAdministrator, DateTime nowUtc, bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Хеш пароля обязателен.", nameof(passwordHash));
        }

        var validLogin = Guard.Login(login);
        return new User
        {
            Id = Guid.CreateVersion7(),
            Login = validLogin,
            NormalizedLogin = Guard.NormalizeLogin(validLogin),
            DisplayName = Guard.Required(displayName, "displayName", "Имя", DisplayNameMaxLength),
            Email = Guard.Email(email),
            PasswordHash = passwordHash,
            IsServerAdministrator = isServerAdministrator,
            IsActive = true,
            MustChangePassword = mustChangePassword,
            CreatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
        };
    }

    public void UpdateProfile(string displayName, string? email)
    {
        DisplayName = Guard.Required(displayName, "displayName", "Имя", DisplayNameMaxLength);
        Email = Guard.Email(email);
    }

    public void SetPasswordHash(string passwordHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Хеш пароля обязателен.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
        PasswordChangedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        MustChangePassword = false;
    }

    /// <summary>Администратор требует сменить пароль при следующем входе (например, после выдачи временного пароля).</summary>
    public void RequirePasswordChange() => MustChangePassword = true;

    /// <summary>Обновляет хеш после смены алгоритма/параметров хеширования; сам пароль не меняется.</summary>
    public void RehashPassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Хеш пароля обязателен.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    public void RecordLogin(DateTime nowUtc) => LastLoginAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));

    public void SetServerAdministrator(bool value) => IsServerAdministrator = value;

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new DomainRuleException("Пользователь уже заблокирован.");
        }

        IsActive = false;
    }

    public void Activate()
    {
        if (IsActive)
        {
            throw new DomainRuleException("Пользователь уже активен.");
        }

        IsActive = true;
    }
}
