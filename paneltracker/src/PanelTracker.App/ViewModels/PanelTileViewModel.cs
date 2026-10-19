using CommunityToolkit.Mvvm.ComponentModel;
using PanelTracker.Core.Models;

namespace PanelTracker.App.ViewModels;

/// <summary>Плитка панели на доске.</summary>
public sealed partial class PanelTileViewModel : ObservableObject
{
    public PanelTileViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public PanelInfo Panel { get; private set; } = null!;

    [ObservableProperty]
    public partial string StatusKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Color { get; set; } = "#9E9E9E";

    [ObservableProperty]
    public partial string RevisionLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDuplicate { get; set; }

    [ObservableProperty]
    public partial bool IsHold { get; set; }

    [ObservableProperty]
    public partial bool IsProblem { get; set; }

    [ObservableProperty]
    public partial string GroupsLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ProblemComment { get; set; }

    [ObservableProperty]
    public partial string FileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ToolTipText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public IReadOnlyList<string> GroupKeys { get; private set; } = [];

    /// <summary>Обновляет плитку; возвращает true, если изменилось что-то, влияющее на фильтры или сортировку.</summary>
    public bool Update(PanelInfo panel, TrackerSettings settings, IReadOnlyList<string> groupKeys, string? problemComment)
    {
        var changed = !string.Equals(StatusKey, panel.StatusKey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(FileName, panel.CurrentFile.FileName, StringComparison.Ordinal)
            || !GroupKeys.SequenceEqual(groupKeys, StringComparer.OrdinalIgnoreCase);
        Panel = panel;
        var status = settings.FindStatus(panel.StatusKey);
        StatusKey = panel.StatusKey;
        StatusName = status?.Name ?? panel.StatusKey;
        Color = status?.Color ?? "#9E9E9E";
        RevisionLabel = panel.CurrentFile.RevisionLabel;
        IsDuplicate = panel.IsDuplicate;
        IsHold = status?.Role == StatusRole.Hold;
        IsProblem = status?.Role == StatusRole.Problem;
        GroupKeys = groupKeys;
        GroupsLabel = string.Join(" ", groupKeys);
        ProblemComment = IsProblem ? problemComment : null;
        FileName = panel.CurrentFile.FileName;
        ToolTipText = BuildToolTip(panel, settings, groupKeys);
        return changed;
    }

    private string BuildToolTip(PanelInfo panel, TrackerSettings settings, IReadOnlyList<string> groupKeys)
    {
        var lines = new List<string>
        {
            $"{panel.Id} — {StatusName}",
            $"Файл: {panel.CurrentFile.FileName}",
            $"Изменён: {panel.CurrentFile.LastWriteTimeUtc.ToLocalTime():g}",
        };
        if (panel.Revision is { } rev)
        {
            lines.Add($"Ревизия: R{rev}");
        }

        if (groupKeys.Count > 0)
        {
            lines.Add("Группы: " + string.Join(", ", groupKeys));
        }

        if (panel.IsDuplicate)
        {
            var folders = panel.DuplicateStatusKeys.Select(k => settings.FindStatus(k)?.Name ?? k);
            lines.Add("⚠ Дубликат: файлы в папках " + string.Join(", ", folders));
        }

        if (!string.IsNullOrWhiteSpace(ProblemComment))
        {
            lines.Add("Проблема: " + ProblemComment);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
