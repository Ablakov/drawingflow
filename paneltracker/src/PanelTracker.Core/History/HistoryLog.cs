using System.Text.Json;
using PanelTracker.Core.Json;

namespace PanelTracker.Core.History;

/// <summary>Журнал переходов статусов: по одной JSON-строке на событие (history.jsonl в корне проекта).</summary>
public sealed class HistoryLog
{
    public const string FileName = "history.jsonl";

    private readonly object _gate = new();

    public HistoryLog(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public void Append(HistoryEntry entry) => AppendRange([entry]);

    public void AppendRange(IEnumerable<HistoryEntry> entries)
    {
        var lines = entries.Select(e => JsonSerializer.Serialize(e, JsonDefaults.Compact)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllLines(FilePath, lines);
        }
    }

    /// <summary>Читает весь журнал; повреждённые строки пропускаются.</summary>
    public IReadOnlyList<HistoryEntry> ReadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var result = new List<HistoryEntry>();
            foreach (var line in File.ReadLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonDefaults.Compact);
                    if (entry is not null)
                    {
                        result.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // повреждённая строка — пропускаем, журнал остаётся читаемым
                }
            }

            return result;
        }
    }

    public IReadOnlyList<HistoryEntry> ReadForPanel(string panelId) =>
        ReadAll().Where(e => string.Equals(e.PanelId, panelId, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Последний комментарий указанного вида для панели (например, текст проблемы).</summary>
    public string? LatestComment(string panelId, HistoryAction action) =>
        ReadForPanel(panelId).LastOrDefault(e => e.Action == action && !string.IsNullOrWhiteSpace(e.Comment))?.Comment;

    /// <summary>Статус, в котором панель была до последнего перевода в <paramref name="statusKey"/>.</summary>
    public string? StatusBeforeLastTransitionTo(string panelId, string statusKey)
    {
        var entries = ReadForPanel(panelId);
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (string.Equals(entry.ToStatus, statusKey, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry.FromStatus, statusKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(entry.FromStatus))
            {
                return entry.FromStatus;
            }
        }

        return null;
    }
}
