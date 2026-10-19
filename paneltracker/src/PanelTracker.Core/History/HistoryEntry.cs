namespace PanelTracker.Core.History;

/// <summary>Одна строка файла history.jsonl.</summary>
public sealed record HistoryEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    public required string User { get; init; }

    public string? Machine { get; init; }

    public required string PanelId { get; init; }

    public string? FileName { get; init; }

    public string? FromStatus { get; init; }

    public string? ToStatus { get; init; }

    public required HistoryAction Action { get; init; }

    public string? Comment { get; init; }
}
