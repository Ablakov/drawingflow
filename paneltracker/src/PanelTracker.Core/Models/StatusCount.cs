namespace PanelTracker.Core.Models;

/// <summary>Счётчик по одному статусу.</summary>
public sealed record StatusCount(StatusDefinition Status, int Count, double Percent);

/// <summary>Сводка по доске для верхней панели счётчиков.</summary>
public sealed class BoardStatistics
{
    public required int Total { get; init; }

    public required int Sent { get; init; }

    public required int Duplicates { get; init; }

    public required IReadOnlyList<StatusCount> ByStatus { get; init; }

    /// <summary>Панели со статусом, которого нет в настройках (папка известна, ключа нет).</summary>
    public required int Unknown { get; init; }

    public double SentPercent => Total == 0 ? 0 : Sent * 100.0 / Total;

    public static BoardStatistics Compute(ScanResult scan, TrackerSettings settings)
    {
        var total = scan.Panels.Count;
        var counts = settings.Statuses
            .Select(s =>
            {
                var count = scan.Panels.Count(p => string.Equals(p.StatusKey, s.Key, StringComparison.OrdinalIgnoreCase));
                return new StatusCount(s, count, total == 0 ? 0 : count * 100.0 / total);
            })
            .ToList();
        var known = counts.Sum(c => c.Count);
        var sent = counts.Where(c => c.Status.Role == StatusRole.Sent).Sum(c => c.Count);
        return new BoardStatistics
        {
            Total = total,
            Sent = sent,
            Duplicates = scan.Panels.Count(p => p.IsDuplicate),
            ByStatus = counts,
            Unknown = total - known,
        };
    }
}
