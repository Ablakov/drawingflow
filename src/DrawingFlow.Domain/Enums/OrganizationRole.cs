namespace DrawingFlow.Domain.Enums;

/// <summary>Роль пользователя в организации.</summary>
public enum OrganizationRole
{
    /// <summary>Обычный участник: права определяются ролями в проектах.</summary>
    Member = 0,
    /// <summary>Администратор организации: управляет участниками, проектами и агентами.</summary>
    OrganizationAdmin = 1,
    /// <summary>Владелец организации: все права администратора плюс настройки организации и назначение владельцев.</summary>
    Owner = 2,
}
