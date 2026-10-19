using PanelTracker.Core.History;
using PanelTracker.Core.Models;

namespace PanelTracker.Core.Operations;

/// <summary>Переводы панелей между статусами: физическое перемещение файла в папку статуса плюс запись в историю.</summary>
public sealed class PanelOperations
{
    private readonly string _rootFolder;
    private readonly TrackerSettings _settings;
    private readonly HistoryLog _history;
    private readonly string _userName;
    private readonly string? _machine;

    public PanelOperations(string rootFolder, TrackerSettings settings, HistoryLog history, string userName, string? machine = null)
    {
        _rootFolder = rootFolder;
        _settings = settings;
        _history = history;
        _userName = userName;
        _machine = machine;
    }

    /// <summary>Перемещает актуальный файл каждой панели в папку статуса <paramref name="statusKey"/>.</summary>
    public OperationReport MoveToStatus(IEnumerable<PanelInfo> panels, string statusKey, string? comment = null, HistoryAction action = HistoryAction.Moved)
    {
        var target = _settings.FindStatus(statusKey) ?? throw new ArgumentException($"Статус «{statusKey}» не найден в настройках.", nameof(statusKey));
        var report = new OperationReport();
        foreach (var panel in panels)
        {
            MoveOne(panel, target, comment, action, report);
        }

        return report;
    }

    /// <summary>Блокирует панели: перемещает в папку статуса с ролью HOLD.</summary>
    public OperationReport Hold(IEnumerable<PanelInfo> panels, string? comment = null)
    {
        var hold = RequireRole(StatusRole.Hold, "HOLD");
        return MoveToStatus(panels, hold.Key, comment, HistoryAction.Held);
    }

    /// <summary>Снимает блокировку: возвращает панель в статус, из которого она попала в HOLD, иначе в первый обычный статус.</summary>
    public OperationReport Release(IEnumerable<PanelInfo> panels)
    {
        var hold = RequireRole(StatusRole.Hold, "HOLD");
        var fallback = _settings.Statuses.FirstOrDefault(s => s.Role == StatusRole.Normal) ?? _settings.Statuses[0];
        var report = new OperationReport();
        foreach (var panel in panels)
        {
            if (!string.Equals(panel.StatusKey, hold.Key, StringComparison.OrdinalIgnoreCase))
            {
                report.AddSkipped(panel.Id);
                continue;
            }

            var previousKey = _history.StatusBeforeLastTransitionTo(panel.Id, hold.Key);
            var target = _settings.FindStatus(previousKey) ?? fallback;
            if (target.Role == StatusRole.Hold)
            {
                target = fallback;
            }

            MoveOne(panel, target, null, HistoryAction.Released, report);
        }

        return report;
    }

    /// <summary>Отмечает панели как проблемные: перемещает в папку статуса «Проблема» и сохраняет комментарий.</summary>
    public OperationReport MarkProblem(IEnumerable<PanelInfo> panels, string comment)
    {
        var problem = RequireRole(StatusRole.Problem, "Проблема");
        var report = new OperationReport();
        foreach (var panel in panels)
        {
            if (string.Equals(panel.StatusKey, problem.Key, StringComparison.OrdinalIgnoreCase))
            {
                // Уже в «Проблеме»: только обновляем комментарий.
                _history.Append(CreateEntry(panel, panel.StatusKey, problem.Key, HistoryAction.ProblemMarked, comment));
                report.AddSkipped(panel.Id);
                continue;
            }

            MoveOne(panel, problem, comment, HistoryAction.ProblemMarked, report);
        }

        return report;
    }

    private void MoveOne(PanelInfo panel, StatusDefinition target, string? comment, HistoryAction action, OperationReport report)
    {
        if (string.Equals(panel.StatusKey, target.Key, StringComparison.OrdinalIgnoreCase))
        {
            report.AddSkipped(panel.Id);
            return;
        }

        var source = panel.CurrentFile.FullPath;
        var targetFolder = Path.Combine(_rootFolder, target.Folder);
        var destination = Path.Combine(targetFolder, panel.CurrentFile.FileName);
        try
        {
            if (!File.Exists(source))
            {
                report.AddError(panel.Id, $"Файл не найден: {source}");
                return;
            }

            Directory.CreateDirectory(targetFolder);
            if (File.Exists(destination))
            {
                report.AddError(panel.Id, $"В папке «{target.Folder}» уже есть файл {panel.CurrentFile.FileName}.");
                return;
            }

            File.Move(source, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            report.AddError(panel.Id, ex.Message);
            return;
        }

        _history.Append(CreateEntry(panel, panel.StatusKey, target.Key, action, comment));
        report.AddMoved(panel.Id);
    }

    private HistoryEntry CreateEntry(PanelInfo panel, string from, string to, HistoryAction action, string? comment) => new()
    {
        Timestamp = DateTimeOffset.Now,
        User = _userName,
        Machine = _machine,
        PanelId = panel.Id,
        FileName = panel.CurrentFile.FileName,
        FromStatus = from,
        ToStatus = to,
        Action = action,
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
    };

    private StatusDefinition RequireRole(StatusRole role, string title) =>
        _settings.FindByRole(role) ?? throw new InvalidOperationException($"В настройках нет статуса с ролью «{title}».");
}
