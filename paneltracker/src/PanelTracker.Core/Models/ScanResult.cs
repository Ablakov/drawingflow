namespace PanelTracker.Core.Models;

/// <summary>Результат сканирования папок статусов.</summary>
public sealed class ScanResult
{
    public required string RootFolder { get; init; }

    public required DateTime ScannedAtUtc { get; init; }

    /// <summary>Панели, отсортированные по номеру.</summary>
    public required IReadOnlyList<PanelInfo> Panels { get; init; }

    /// <summary>Панели, файлы которых найдены в нескольких папках статусов.</summary>
    public IReadOnlyList<PanelInfo> Duplicates => Panels.Where(p => p.IsDuplicate).ToList();

    /// <summary>Статусы, папки которых отсутствуют на диске.</summary>
    public required IReadOnlyList<StatusDefinition> MissingFolders { get; init; }

    public PanelInfo? FindPanel(string id) => Panels.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
