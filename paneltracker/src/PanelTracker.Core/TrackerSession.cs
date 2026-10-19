using PanelTracker.Core.Groups;
using PanelTracker.Core.History;
using PanelTracker.Core.Models;
using PanelTracker.Core.Operations;
using PanelTracker.Core.Scanning;

namespace PanelTracker.Core;

/// <summary>
/// Открытый проект: корневая папка, группы, история и операции. Хранит последний результат сканирования
/// и при обновлении фиксирует в истории переходы, сделанные вне программы (в проводнике).
/// </summary>
public sealed class TrackerSession
{
    public const string ExternalUser = "проводник";

    private readonly Dictionary<string, string> _knownStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _primed;

    public TrackerSession(string rootFolder, TrackerSettings settings, string userName, string? machine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);
        RootFolder = Path.GetFullPath(rootFolder);
        Settings = settings;
        UserName = userName;
        Groups = new GroupStore(Path.Combine(RootFolder, GroupStore.FileName));
        Groups.Load();
        History = new HistoryLog(Path.Combine(RootFolder, HistoryLog.FileName));
        Operations = new PanelOperations(RootFolder, settings, History, userName, machine);
    }

    public string RootFolder { get; }

    public TrackerSettings Settings { get; }

    public string UserName { get; }

    public GroupStore Groups { get; }

    public HistoryLog History { get; }

    public PanelOperations Operations { get; }

    public ScanResult? LastScan { get; private set; }

    /// <summary>Создаёт отсутствующие папки статусов.</summary>
    public IReadOnlyList<string> EnsureStatusFolders()
    {
        var created = new List<string>();
        foreach (var status in Settings.Statuses)
        {
            var folder = Path.Combine(RootFolder, status.Folder);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                created.Add(status.Folder);
            }
        }

        return created;
    }

    /// <summary>Пересканирует папки. Изменения статусов, не сделанные через программу, записываются в историю как «обнаружено».</summary>
    public ScanResult Refresh()
    {
        lock (_gate)
        {
            var scan = ProjectScanner.Scan(RootFolder, Settings);
            if (_primed)
            {
                var detected = new List<HistoryEntry>();
                foreach (var panel in scan.Panels)
                {
                    if (_knownStatuses.TryGetValue(panel.Id, out var previous)
                        && !string.Equals(previous, panel.StatusKey, StringComparison.OrdinalIgnoreCase))
                    {
                        detected.Add(new HistoryEntry
                        {
                            Timestamp = DateTimeOffset.Now,
                            User = ExternalUser,
                            PanelId = panel.Id,
                            FileName = panel.CurrentFile.FileName,
                            FromStatus = previous,
                            ToStatus = panel.StatusKey,
                            Action = HistoryAction.Detected,
                        });
                    }
                }

                History.AppendRange(detected);
            }

            _knownStatuses.Clear();
            foreach (var panel in scan.Panels)
            {
                _knownStatuses[panel.Id] = panel.StatusKey;
            }

            _primed = true;
            LastScan = scan;
            return scan;
        }
    }

    /// <summary>Операции выполняются под той же блокировкой, что и сканирование: пересканирование не увидит
    /// «наполовину выполненный» перевод и не запишет его в историю как сделанный вне программы.</summary>
    public OperationReport MoveToStatus(IEnumerable<PanelInfo> panels, string statusKey)
    {
        lock (_gate)
        {
            return Track(Operations.MoveToStatus(panels, statusKey), statusKey);
        }
    }

    public OperationReport Hold(IEnumerable<PanelInfo> panels)
    {
        lock (_gate)
        {
            return Track(Operations.Hold(panels), Settings.FindByRole(StatusRole.Hold)?.Key);
        }
    }

    public OperationReport Release(IEnumerable<PanelInfo> panels)
    {
        lock (_gate)
        {
            var report = Operations.Release(panels);
            // Целевой статус у каждой панели свой — узнаем из истории.
            foreach (var id in report.Moved)
            {
                var last = History.ReadForPanel(id).LastOrDefault(e => e.Action == HistoryAction.Released);
                if (last?.ToStatus is { } to)
                {
                    _knownStatuses[id] = to;
                }
            }

            return report;
        }
    }

    public OperationReport MarkProblem(IEnumerable<PanelInfo> panels, string comment)
    {
        lock (_gate)
        {
            return Track(Operations.MarkProblem(panels, comment), Settings.FindByRole(StatusRole.Problem)?.Key);
        }
    }

    public BoardStatistics Statistics() =>
        BoardStatistics.Compute(LastScan ?? Refresh(), Settings);

    private OperationReport Track(OperationReport report, string? targetStatusKey)
    {
        if (targetStatusKey is null)
        {
            return report;
        }

        foreach (var id in report.Moved)
        {
            _knownStatuses[id] = targetStatusKey;
        }

        return report;
    }
}
