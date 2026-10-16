using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Workflow;

namespace DrawingFlow.Desktop.Localization;

/// <summary>Русские названия ролей, статусов и действий аудита для интерфейса.</summary>
public static class Names
{
    public static string Of(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "Владелец",
        OrganizationRole.OrganizationAdmin => "Администратор организации",
        OrganizationRole.Member => "Участник",
        _ => role.ToString(),
    };

    public static string Of(ProjectRole role) => role switch
    {
        ProjectRole.ProjectManager => "Руководитель проекта",
        ProjectRole.Engineer => "Инженер",
        ProjectRole.Reviewer => "Проверяющий",
        ProjectRole.Viewer => "Наблюдатель",
        _ => role.ToString(),
    };

    public static string Of(ProjectStatus status) => status switch
    {
        ProjectStatus.Active => "Активный",
        ProjectStatus.Archived => "В архиве",
        _ => status.ToString(),
    };

    public static string Of(DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => "Черновик",
        DocumentStatus.InProgress => "В работе",
        DocumentStatus.InReview => "На проверке",
        DocumentStatus.RevisionRequired => "Требует доработки",
        DocumentStatus.Approved => "Согласован",
        DocumentStatus.Released => "Выпущен",
        DocumentStatus.Archived => "В архиве",
        _ => status.ToString(),
    };

    public static string Of(VersionStatus status) => status switch
    {
        VersionStatus.Registered => "Зарегистрирована",
        VersionStatus.InReview => "На проверке",
        VersionStatus.Approved => "Согласована",
        VersionStatus.RevisionRequired => "Требует доработки",
        VersionStatus.Released => "Выпущена",
        VersionStatus.Superseded => "Заменена",
        _ => status.ToString(),
    };

    private static readonly System.Globalization.CultureInfo Russian = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public static string Of(SyncOperationStatus status) => status switch
    {
        SyncOperationStatus.Pending => "Ожидает",
        SyncOperationStatus.Processing => "Выполняется",
        SyncOperationStatus.Completed => "Завершена",
        SyncOperationStatus.Failed => "Ошибка",
        SyncOperationStatus.NeedsResolution => "Требует разрешения",
        SyncOperationStatus.Cancelled => "Отменена (файл исчез)",
        SyncOperationStatus.Dismissed => "Отклонена",
        _ => status.ToString(),
    };

    public static string Of(FileMatchingRule rule) => rule switch
    {
        FileMatchingRule.NumberFromFileName => "Номер из имени файла (шаблон)",
        FileMatchingRule.FileNameIsNumber => "Имя файла = номер документа",
        FileMatchingRule.VersionFileName => "Совпадение с файлом текущей версии",
        _ => rule.ToString(),
    };

    public static string Of(SyncRequestedAction action) => action switch
    {
        SyncRequestedAction.None => "",
        SyncRequestedAction.Retry => "Запрошен повтор",
        SyncRequestedAction.UploadToDocument => "Ожидает загрузки в выбранный документ",
        SyncRequestedAction.Dismiss => "Отклонена пользователем",
        _ => action.ToString(),
    };

    public static string Of(ReviewDecision decision) => decision switch
    {
        ReviewDecision.Approved => "Согласовано",
        ReviewDecision.RevisionRequired => "Возвращено на доработку",
        _ => decision.ToString(),
    };

    public static string Of(WorkflowTaskType type) => type switch
    {
        WorkflowTaskType.Review => "Проверка версии",
        WorkflowTaskType.Rework => "Доработка документа",
        _ => type.ToString(),
    };

    public static string Of(WorkflowTaskStatus status) => status switch
    {
        WorkflowTaskStatus.Open => "Открыта",
        WorkflowTaskStatus.Completed => "Завершена",
        WorkflowTaskStatus.Cancelled => "Отменена",
        _ => status.ToString(),
    };

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => string.Create(Russian, $"{bytes / (1024.0 * 1024 * 1024):0.##} ГБ"),
        >= 1024L * 1024 => string.Create(Russian, $"{bytes / (1024.0 * 1024):0.#} МБ"),
        >= 1024 => string.Create(Russian, $"{bytes / 1024.0:0.#} КБ"),
        _ => $"{bytes} Б",
    };

    public static string OfAuditAction(string action) => action switch
    {
        "user.logged_in" => "Вход в систему",
        "user.login_failed" => "Неудачная попытка входа",
        "user.logged_out" => "Выход из системы",
        "user.created" => "Создан пользователь",
        "user.password_changed" => "Сменён пароль",
        "user.deactivated" => "Пользователь заблокирован",
        "user.activated" => "Пользователь разблокирован",
        "organization.created" => "Создана организация",
        "organization.updated" => "Изменена организация",
        "organization.member_added" => "Добавлен участник организации",
        "organization.member_role_changed" => "Изменена роль участника организации",
        "organization.member_removed" => "Удалён участник организации",
        "project.created" => "Создан проект",
        "project.updated" => "Изменён проект",
        "project.member_added" => "Добавлен участник проекта",
        "project.member_role_changed" => "Изменена роль участника проекта",
        "project.member_removed" => "Удалён участник проекта",
        "document.registered" => "Зарегистрирован документ",
        "document.updated" => "Изменены сведения о документе",
        "document.archived" => "Документ отправлен в архив",
        "document.restored" => "Документ восстановлен из архива",
        "document.version_uploaded" => "Загружена версия документа",
        "document.version_downloaded" => "Скачана версия документа",
        "agent.registered" => "Зарегистрирован агент",
        "agent.revoked" => "Отозван ключ агента",
        "watched_folder.created" => "Добавлена папка наблюдения",
        "watched_folder.updated" => "Изменена папка наблюдения",
        "watched_folder.deleted" => "Удалена папка наблюдения",
        "sync_operation.resolved" => "Файл сопоставлен с документом",
        "sync_operation.retried" => "Запрошен повтор синхронизации",
        "sync_operation.dismissed" => "Операция синхронизации отклонена",
        "document.submitted_for_review" => "Документ отправлен на проверку",
        "document.approved" => "Версия согласована",
        "document.revision_requested" => "Версия возвращена на доработку",
        "document.released" => "Документ выпущен",
        "workflow_task.cancelled" => "Задача отменена",
        _ => action,
    };

    public static string OfEntityType(string entityType) => entityType switch
    {
        "User" => "Пользователь",
        "Organization" => "Организация",
        "Project" => "Проект",
        "Document" => "Документ",
        "DocumentVersion" => "Версия документа",
        "Agent" => "Агент",
        "WatchedFolder" => "Папка наблюдения",
        "SyncOperation" => "Операция синхронизации",
        "Review" => "Решение по версии",
        "WorkflowTask" => "Задача",
        _ => entityType,
    };
}

public sealed record RoleOption<TRole>(TRole Value, string Name) where TRole : struct, Enum
{
    public override string ToString() => Name;
}

public static class RoleOptions
{
    public static IReadOnlyList<RoleOption<OrganizationRole>> Organization(bool canAssignOwner) =>
        Enum.GetValues<OrganizationRole>()
            .Where(r => canAssignOwner || r != OrganizationRole.Owner)
            .Select(r => new RoleOption<OrganizationRole>(r, Names.Of(r)))
            .ToList();

    public static IReadOnlyList<RoleOption<ProjectRole>> Project() =>
        Enum.GetValues<ProjectRole>()
            .Select(r => new RoleOption<ProjectRole>(r, Names.Of(r)))
            .ToList();

    public static IReadOnlyList<RoleOption<DocumentStatus>> DocumentStatuses() =>
        Enum.GetValues<DocumentStatus>()
            .Select(s => new RoleOption<DocumentStatus>(s, Names.Of(s)))
            .ToList();

    public static IReadOnlyList<RoleOption<ProjectStatus>> ProjectStatuses() =>
        Enum.GetValues<ProjectStatus>()
            .Select(s => new RoleOption<ProjectStatus>(s, Names.Of(s)))
            .ToList();
}
