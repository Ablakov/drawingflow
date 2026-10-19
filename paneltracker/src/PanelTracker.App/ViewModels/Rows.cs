namespace PanelTracker.App.ViewModels;

/// <summary>Строка вкладки «История».</summary>
public sealed record HistoryRowViewModel(
    DateTimeOffset Timestamp,
    string User,
    string PanelId,
    string FileName,
    string FromStatus,
    string ToStatus,
    string ActionName,
    string Comment)
{
    public string Time => Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>Строка вкладки «Дубликаты»: один файл панели-дубликата.</summary>
public sealed record DuplicateRowViewModel(
    string PanelId,
    string StatusName,
    string Color,
    string FileName,
    DateTime ModifiedLocal,
    string FullPath);

/// <summary>Элемент фильтра по группе.</summary>
public sealed record GroupFilterItem(string Title, GroupFilterKind Kind, string? GroupKey)
{
    public override string ToString() => Title;
}

public enum GroupFilterKind
{
    All,
    WithoutGroup,
    Group,
}

/// <summary>Элемент фильтра по статусу; Status == null — все статусы.</summary>
public sealed record StatusFilterItem(string Title, Core.Models.StatusDefinition? Status)
{
    public override string ToString() => Title;
}

/// <summary>Ячейка сводной таблицы «группа × статус».</summary>
public sealed record StatisticsCell(int Count, string Color);

/// <summary>Строка вкладки «Статистика».</summary>
public sealed record StatisticsRowViewModel(string Title, IReadOnlyList<StatisticsCell> Cells, int Total, string SentText, bool IsTotal);
