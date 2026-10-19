using PanelTracker.Core.Groups;

namespace PanelTracker.Core.Models;

/// <summary>Строка сводной таблицы «группа × статус».</summary>
public sealed record GroupStatisticsRow(string Title, IReadOnlyList<int> CountsByStatus, int Total, int Sent)
{
    public double SentPercent => Total == 0 ? 0 : Sent * 100.0 / Total;
}

/// <summary>Сводка по группам панелей: сколько панелей каждой группы в каждом статусе.</summary>
public static class GroupStatistics
{
    public const string WithoutGroupTitle = "Без группы";

    public const string TotalTitle = "Всего";

    /// <summary>Строки: по одной на группу (в порядке groups.json), затем «Без группы» и «Всего».
    /// Панель, состоящая в нескольких группах, учитывается в каждой из них.</summary>
    public static IReadOnlyList<GroupStatisticsRow> Compute(ScanResult scan, IReadOnlyList<PanelGroup> groups, TrackerSettings settings)
    {
        var rows = new List<GroupStatisticsRow>(groups.Count + 2);
        var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var members = scan.Panels.Where(p => group.Contains(p.Id)).ToList();
            grouped.UnionWith(members.Select(m => m.Id));
            rows.Add(Build(group.DisplayName, members, settings));
        }

        rows.Add(Build(WithoutGroupTitle, scan.Panels.Where(p => !grouped.Contains(p.Id)).ToList(), settings));
        rows.Add(Build(TotalTitle, scan.Panels, settings));
        return rows;
    }

    private static GroupStatisticsRow Build(string title, IReadOnlyList<PanelInfo> panels, TrackerSettings settings)
    {
        var counts = settings.Statuses
            .Select(s => panels.Count(p => string.Equals(p.StatusKey, s.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var sent = settings.Statuses
            .Select((s, i) => s.Role == StatusRole.Sent ? counts[i] : 0)
            .Sum();
        return new GroupStatisticsRow(title, counts, panels.Count, sent);
    }
}
