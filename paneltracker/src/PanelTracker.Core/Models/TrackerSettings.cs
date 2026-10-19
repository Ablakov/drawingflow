namespace PanelTracker.Core.Models;

/// <summary>Содержимое файла settings.json рядом с программой.</summary>
public sealed class TrackerSettings
{
    /// <summary>Корневая папка проекта с подпапками статусов.</summary>
    public string? RootFolder { get; set; }

    /// <summary>Расширения файлов чертежей (с точкой), без учёта регистра.</summary>
    public List<string> FileExtensions { get; set; } = [".pdf"];

    /// <summary>Искать файлы также во вложенных папках внутри папок статусов.</summary>
    public bool IncludeSubfolders { get; set; }

    /// <summary>Период принудительного пересканирования папок, секунд.</summary>
    public int RescanIntervalSeconds { get; set; } = 10;

    /// <summary>Пауза после последнего события файловой системы перед пересканированием, мс.</summary>
    public int DebounceMilliseconds { get; set; } = 600;

    /// <summary>Имя пользователя для журнала истории; пусто — имя учётной записи Windows.</summary>
    public string? UserName { get; set; }

    /// <summary>Статусы в порядке отображения.</summary>
    public List<StatusDefinition> Statuses { get; set; } = CreateDefaultStatuses();

    public static TrackerSettings CreateDefault() => new();

    public static List<StatusDefinition> CreateDefaultStatuses() =>
    [
        new() { Key = "ready_to_check", Name = "Готово к проверке", Folder = "01 Ready to Check", Color = "#F2A93B" },
        new() { Key = "ready_to_back_check", Name = "Готово к обратной проверке", Folder = "02 Ready to Back Check", Color = "#3B82F6" },
        new() { Key = "ready_to_send", Name = "Готово к отправке", Folder = "03 Ready to Send", Color = "#8B5CF6" },
        new() { Key = "sent", Name = "Отправлено заказчику", Folder = "04 Sent to client", Color = "#22A95A", Role = StatusRole.Sent },
        new() { Key = "problem", Name = "Проблема", Folder = "05 Problem", Color = "#E5484D", Role = StatusRole.Problem },
        new() { Key = "hold", Name = "HOLD (заблокировано)", Folder = "06 Locked", Color = "#6B7280", Role = StatusRole.Hold },
    ];

    public StatusDefinition? FindStatus(string? key) =>
        key is null ? null : Statuses.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    public StatusDefinition? FindByRole(StatusRole role) => Statuses.FirstOrDefault(s => s.Role == role);

    public StatusDefinition? FindByFolder(string folderName) =>
        Statuses.FirstOrDefault(s => string.Equals(s.Folder, folderName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Порядковый номер статуса в списке; неизвестный статус — в конец.</summary>
    public int OrderOf(string? key)
    {
        var index = Statuses.FindIndex(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>Проверяет конфигурацию и возвращает список ошибок (пусто — всё в порядке).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Statuses.Count == 0)
        {
            errors.Add("Не задано ни одного статуса.");
        }

        foreach (var status in Statuses)
        {
            if (string.IsNullOrWhiteSpace(status.Key))
            {
                errors.Add("У статуса пустой ключ (key).");
            }

            if (string.IsNullOrWhiteSpace(status.Folder) || status.Folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                errors.Add($"У статуса «{status.Key}» недопустимое имя папки.");
            }
        }

        foreach (var group in Statuses.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Ключ статуса «{group.Key}» повторяется.");
        }

        foreach (var group in Statuses.GroupBy(s => s.Folder, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Папка «{group.Key}» назначена нескольким статусам.");
        }

        if (FileExtensions.Count == 0)
        {
            errors.Add("Не задано ни одного расширения файлов.");
        }

        if (RescanIntervalSeconds < 1)
        {
            errors.Add("Период пересканирования должен быть не меньше 1 секунды.");
        }

        return errors;
    }
}
