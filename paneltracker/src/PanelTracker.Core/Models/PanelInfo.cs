namespace PanelTracker.Core.Models;

/// <summary>Панель на доске: актуальный файл (старшая ревизия) и все найденные файлы.</summary>
public sealed class PanelInfo
{
    public required string Id { get; init; }

    public required PanelFile CurrentFile { get; init; }

    public required IReadOnlyList<PanelFile> Files { get; init; }

    public string StatusKey => CurrentFile.StatusKey;

    public int? Revision => CurrentFile.Revision;

    /// <summary>Файлы панели лежат в двух и более папках статусов одновременно.</summary>
    public bool IsDuplicate => DuplicateStatusKeys.Count > 1;

    /// <summary>Статусы (папки), в которых есть файлы этой панели.</summary>
    public IReadOnlyList<string> DuplicateStatusKeys { get; init; } = [];

    public override string ToString() => $"{Id} [{StatusKey}] {CurrentFile.FileName}";
}
