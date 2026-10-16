namespace DrawingFlow.Infrastructure.Options;

/// <summary>Первичная инициализация: администратор сервера создаётся только если в базе ещё нет пользователей.</summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public BootstrapAdministrator? Administrator { get; set; }
}

public sealed class BootstrapAdministrator
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Администратор";
    public string? Email { get; set; }
    /// <summary>Требовать смену пароля при первом входе администратора (рекомендуется для эксплуатации).</summary>
    public bool MustChangePassword { get; set; } = true;
}
