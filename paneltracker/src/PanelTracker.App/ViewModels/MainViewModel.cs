using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PanelTracker.App.Services;
using PanelTracker.Core;
using PanelTracker.Core.History;
using PanelTracker.Core.Models;
using PanelTracker.Core.Operations;
using PanelTracker.Core.Scanning;
using PanelTracker.Core.Watching;

namespace PanelTracker.App.ViewModels;

/// <summary>Главное окно: доска, счётчики, вкладки «Дубликаты», «История», «Группы», «Настройки».</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IUserInteraction _ui;
    private readonly Dispatcher _dispatcher;
    private readonly string _settingsPath;
    private readonly Dictionary<string, PanelTileViewModel> _tilesById = new(StringComparer.OrdinalIgnoreCase);
    private TrackerSettings _settings;
    private TrackerSession? _session;
    private ProjectWatcher? _watcher;
    private int _refreshing;
    private bool _refreshPending;
    private bool _refreshLoudPending;
    private int _lastHistoryCount = -1;
    private string? _lastDuplicateSignature;
    private string? _lastStatisticsSignature;

    public MainViewModel(TrackerSettings settings, string settingsPath, IUserInteraction ui, Dispatcher dispatcher)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _ui = ui;
        _dispatcher = dispatcher;

        PanelsView = new ListCollectionView(Panels)
        {
            CustomSort = Comparer<PanelTileViewModel>.Create((a, b) => PanelIdComparer.Instance.Compare(a.Id, b.Id)),
            Filter = o => o is PanelTileViewModel tile && MatchesFilters(tile),
        };
        HistoryView = new ListCollectionView(HistoryRows)
        {
            Filter = o => o is HistoryRowViewModel row && MatchesHistoryFilter(row),
        };

        RebuildStatusLists();
        RebuildGroupFilters();
    }

    public string SettingsPath => _settingsPath;

    public TrackerSettings Settings => _settings;

    // ----- Состояние -----

    [ObservableProperty]
    public partial string? RootFolder { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Выберите корневую папку проекта.";

    [ObservableProperty]
    public partial string LastScanText { get; set; } = "—";

    [ObservableProperty]
    public partial string WatcherText { get; set; } = "Слежение: выключено";

    [ObservableProperty]
    public partial bool IsProjectOpen { get; set; }

    [ObservableProperty]
    public partial string MissingFoldersText { get; set; } = string.Empty;

    // ----- Доска -----

    public ObservableCollection<PanelTileViewModel> Panels { get; } = [];

    public ListCollectionView PanelsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public ObservableCollection<StatusFilterItem> StatusFilters { get; } = [];

    [ObservableProperty]
    public partial StatusFilterItem? SelectedStatusFilter { get; set; }

    public ObservableCollection<GroupFilterItem> GroupFilters { get; } = [];

    [ObservableProperty]
    public partial GroupFilterItem? SelectedGroupFilter { get; set; }

    [ObservableProperty]
    public partial int VisibleCount { get; set; }

    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    /// <summary>Статусы для подменю «Перевести в статус» и вкладки «Настройки».</summary>
    public ObservableCollection<StatusDefinition> Statuses { get; } = [];

    // ----- Счётчики -----

    public ObservableCollection<StatusCounterViewModel> Counters { get; } = [];

    [ObservableProperty]
    public partial int Total { get; set; }

    [ObservableProperty]
    public partial int Sent { get; set; }

    [ObservableProperty]
    public partial double SentPercent { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Отправлено заказчику 0 из 0";

    [ObservableProperty]
    public partial int DuplicateCount { get; set; }

    // ----- Дубликаты, история, группы -----

    public ObservableCollection<DuplicateRowViewModel> DuplicateRows { get; } = [];

    public ObservableCollection<HistoryRowViewModel> HistoryRows { get; } = [];

    public ListCollectionView HistoryView { get; }

    [ObservableProperty]
    public partial string HistoryFilter { get; set; } = string.Empty;

    public ObservableCollection<GroupViewModel> Groups { get; } = [];

    public ObservableCollection<StatisticsRowViewModel> StatisticsRows { get; } = [];

    [ObservableProperty]
    public partial GroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial string? SelectedGroupMember { get; set; }

    public IReadOnlyList<PanelTileViewModel> SelectedPanels => Panels.Where(p => p.IsSelected).ToList();

    public bool HasSelection => Panels.Any(p => p.IsSelected);

    // ----- Реакция на изменение свойств -----

    partial void OnSearchTextChanged(string value) => ApplyBoardFilter();

    partial void OnSelectedStatusFilterChanged(StatusFilterItem? value) => ApplyBoardFilter();

    partial void OnSelectedGroupFilterChanged(GroupFilterItem? value) => ApplyBoardFilter();

    partial void OnHistoryFilterChanged(string value) => HistoryView.Refresh();

    /// <summary>Вызывается представлением после изменения выделения на доске.</summary>
    public void OnSelectionChanged()
    {
        SelectedCount = Panels.Count(p => p.IsSelected);
        OnPropertyChanged(nameof(HasSelection));
        NotifySelectionCommands();
    }

    private void NotifySelectionCommands()
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        OpenLocationCommand.NotifyCanExecuteChanged();
        MoveToStatusCommand.NotifyCanExecuteChanged();
        HoldCommand.NotifyCanExecuteChanged();
        ReleaseCommand.NotifyCanExecuteChanged();
        MarkProblemCommand.NotifyCanExecuteChanged();
        AddToGroupCommand.NotifyCanExecuteChanged();
        RemoveFromGroupCommand.NotifyCanExecuteChanged();
        AddSelectedToGroupCommand.NotifyCanExecuteChanged();
    }

    // ----- Открытие проекта -----

    public void OpenInitialProject()
    {
        var folder = _settings.RootFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (Directory.Exists(folder))
        {
            OpenProject(folder);
        }
        else
        {
            StatusMessage = $"Папка проекта из настроек не найдена: {folder}";
        }
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        var folder = _ui.PickFolder(RootFolder);
        if (folder is null)
        {
            return;
        }

        if (OpenProject(folder))
        {
            _settings.RootFolder = folder;
            TrySaveSettings();
        }
    }

    private bool OpenProject(string folder)
    {
        var errors = _settings.Validate();
        if (errors.Count > 0)
        {
            _ui.ShowError("Ошибки в settings.json:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            return false;
        }

        CloseProject();
        try
        {
            _session = new TrackerSession(folder, _settings, _settings.UserName is { Length: > 0 } u ? u : Environment.UserName, Environment.MachineName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _ui.ShowError($"Не удалось открыть проект:{Environment.NewLine}{ex.Message}");
            StatusMessage = "Проект не открыт.";
            return false;
        }

        RootFolder = _session.RootFolder;
        IsProjectOpen = true;
        RebuildGroups();
        RebuildGroupFilters();

        _watcher = new ProjectWatcher(
            _session.RootFolder,
            TimeSpan.FromMilliseconds(Math.Max(100, _settings.DebounceMilliseconds)),
            TimeSpan.FromSeconds(Math.Max(1, _settings.RescanIntervalSeconds)));
        _watcher.ChangesDetected += trigger => _dispatcher.InvokeAsync(() => _ = RefreshAsync(silent: true));
        _watcher.WatcherFailed += ex => _dispatcher.InvokeAsync(() => WatcherText = "Слежение: ошибка — " + ex.Message);
        _watcher.Start();
        WatcherText = _watcher.IsWatching ? "Слежение: включено" : "Слежение: только периодический опрос";
        StatusMessage = $"Проект открыт: {_session.RootFolder}";
        _ = RefreshAsync(silent: true);
        return true;
    }

    private void CloseProject()
    {
        _watcher?.Dispose();
        _watcher = null;
        _session = null;
        RootFolder = null;
        IsProjectOpen = false;
        _lastHistoryCount = -1;
        _lastDuplicateSignature = null;
        _lastStatisticsSignature = null;
        StatisticsRows.Clear();
        Panels.Clear();
        _tilesById.Clear();
        DuplicateRows.Clear();
        HistoryRows.Clear();
        Groups.Clear();
    }

    // ----- Обновление -----

    [RelayCommand]
    private Task RefreshAsync() => RefreshAsync(silent: false);

    private async Task RefreshAsync(bool silent)
    {
        if (_session is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            _refreshPending = true;
            _refreshLoudPending |= !silent;
            return;
        }

        try
        {
            var session = _session;
            var scan = await Task.Run(session.Refresh);
            if (!ReferenceEquals(session, _session))
            {
                return; // проект сменили, пока шло сканирование
            }

            ApplyScan(scan);
            if (!silent)
            {
                StatusMessage = $"Обновлено: {scan.Panels.Count} панелей.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Ошибка обновления: " + ex.Message;
        }
        finally
        {
            _refreshing = 0;
            if (_refreshPending)
            {
                _refreshPending = false;
                var loud = _refreshLoudPending;
                _refreshLoudPending = false;
                _ = RefreshAsync(silent: !loud);
            }
        }
    }

    private void ApplyScan(ScanResult scan)
    {
        if (_session is null)
        {
            return;
        }

        var history = _session.History.ReadAll();
        var problemComments = history
            .Where(e => e.Action == HistoryAction.ProblemMarked && !string.IsNullOrWhiteSpace(e.Comment))
            .GroupBy(e => e.PanelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Comment!, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var viewChanged = false;
        foreach (var panel in scan.Panels)
        {
            seen.Add(panel.Id);
            if (!_tilesById.TryGetValue(panel.Id, out var tile))
            {
                tile = new PanelTileViewModel(panel.Id);
                _tilesById[panel.Id] = tile;
                Panels.Add(tile);
                viewChanged = true;
            }

            var groupKeys = _session.Groups.GroupsOf(panel.Id).Select(g => g.Key).ToList();
            problemComments.TryGetValue(panel.Id, out var comment);
            viewChanged |= tile.Update(panel, _settings, groupKeys, comment);
        }

        foreach (var gone in _tilesById.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Panels.Remove(_tilesById[gone]);
            _tilesById.Remove(gone);
            viewChanged = true;
        }

        if (viewChanged)
        {
            ApplyBoardFilter();
        }
        else
        {
            OnSelectionChanged();
        }

        var stats = BoardStatistics.Compute(scan, _settings);
        Total = stats.Total;
        Sent = stats.Sent;
        SentPercent = stats.SentPercent;
        DuplicateCount = stats.Duplicates;
        ProgressText = $"Отправлено заказчику {stats.Sent} из {stats.Total} ({stats.SentPercent:0}%)";
        foreach (var counter in Counters)
        {
            var count = stats.ByStatus.FirstOrDefault(c => ReferenceEquals(c.Status, counter.Status) || c.Status.Key == counter.Status.Key);
            if (count is not null)
            {
                counter.Update(count);
            }
        }

        var duplicates = scan.Duplicates;
        var signature = string.Join("\n", duplicates.SelectMany(d => d.Files.Select(f => f.FullPath + "|" + f.LastWriteTimeUtc.Ticks)));
        if (!string.Equals(signature, _lastDuplicateSignature, StringComparison.Ordinal))
        {
            _lastDuplicateSignature = signature;
            DuplicateRows.Clear();
            foreach (var dup in duplicates)
            {
                foreach (var file in dup.Files.OrderBy(f => _settings.OrderOf(f.StatusKey)))
                {
                    var status = _settings.FindStatus(file.StatusKey);
                    DuplicateRows.Add(new DuplicateRowViewModel(
                        dup.Id,
                        status?.Name ?? file.StatusKey,
                        status?.Color ?? "#9E9E9E",
                        file.FileName,
                        file.LastWriteTimeUtc.ToLocalTime(),
                        file.FullPath));
                }
            }
        }

        if (history.Count != _lastHistoryCount)
        {
            _lastHistoryCount = history.Count;
            RebuildHistory(history);
        }

        RebuildStatistics(scan);
        LastScanText = "Последнее сканирование: " + scan.ScannedAtUtc.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
        MissingFoldersText = scan.MissingFolders.Count == 0
            ? string.Empty
            : "Нет папок: " + string.Join(", ", scan.MissingFolders.Select(s => s.Folder));
        if (_watcher is not null)
        {
            WatcherText = _watcher.IsWatching ? "Слежение: включено" : "Слежение: только периодический опрос";
        }
    }

    private void RebuildStatistics(ScanResult scan)
    {
        if (_session is null)
        {
            return;
        }

        var rows = GroupStatistics.Compute(scan, _session.Groups.Groups, _settings);
        var signature = string.Join("\n", rows.Select(r => r.Title + ":" + string.Join(",", r.CountsByStatus)));
        if (string.Equals(signature, _lastStatisticsSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatisticsSignature = signature;
        StatisticsRows.Clear();
        foreach (var row in rows)
        {
            var cells = row.CountsByStatus
                .Select((count, i) => new StatisticsCell(count, _settings.Statuses[i].Color))
                .ToList();
            StatisticsRows.Add(new StatisticsRowViewModel(
                row.Title,
                cells,
                row.Total,
                $"{row.Sent} ({row.SentPercent:0}%)",
                row.Title == GroupStatistics.TotalTitle));
        }
    }

    private void RebuildHistory(IReadOnlyList<HistoryEntry> history)
    {
        HistoryRows.Clear();
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var e = history[i];
            HistoryRows.Add(new HistoryRowViewModel(
                e.Timestamp,
                e.User,
                e.PanelId,
                e.FileName ?? string.Empty,
                StatusName(e.FromStatus),
                StatusName(e.ToStatus),
                ActionName(e.Action),
                e.Comment ?? string.Empty));
        }

        HistoryView.Refresh();
    }

    private string StatusName(string? key) => key is null ? string.Empty : _settings.FindStatus(key)?.Name ?? key;

    private static string ActionName(HistoryAction action) => action switch
    {
        HistoryAction.Moved => "Перевод",
        HistoryAction.Held => "Блокировка",
        HistoryAction.Released => "Снятие блокировки",
        HistoryAction.ProblemMarked => "Проблема",
        HistoryAction.Detected => "Обнаружено (вне программы)",
        _ => action.ToString(),
    };

    // ----- Фильтры -----

    private bool MatchesFilters(PanelTileViewModel tile)
    {
        if (!string.IsNullOrWhiteSpace(SearchText)
            && !tile.Id.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
            && !tile.FileName.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SelectedStatusFilter?.Status is { } status && !string.Equals(tile.StatusKey, status.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SelectedGroupFilter switch
        {
            { Kind: GroupFilterKind.WithoutGroup } => tile.GroupKeys.Count == 0,
            { Kind: GroupFilterKind.Group, GroupKey: { } key } => tile.GroupKeys.Contains(key, StringComparer.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private bool MatchesHistoryFilter(HistoryRowViewModel row) =>
        string.IsNullOrWhiteSpace(HistoryFilter)
        || row.PanelId.Contains(HistoryFilter.Trim(), StringComparison.OrdinalIgnoreCase)
        || row.FileName.Contains(HistoryFilter.Trim(), StringComparison.OrdinalIgnoreCase);

    private void ApplyBoardFilter()
    {
        // Скрытая фильтром плитка не должна оставаться выделенной: иначе массовые команды затронут невидимые панели.
        foreach (var tile in Panels)
        {
            if (tile.IsSelected && !MatchesFilters(tile))
            {
                tile.IsSelected = false;
            }
        }

        PanelsView.Refresh();
        VisibleCount = PanelsView.Count;
        OnSelectionChanged();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = StatusFilters.FirstOrDefault();
        SelectedGroupFilter = GroupFilters.FirstOrDefault();
    }

    [RelayCommand]
    private void FilterByStatus(StatusDefinition? status)
    {
        var item = StatusFilters.FirstOrDefault(f => ReferenceEquals(f.Status, status));
        // Повторный щелчок по тому же счётчику снимает фильтр.
        SelectedStatusFilter = item is not null && !ReferenceEquals(SelectedStatusFilter, item) ? item : StatusFilters.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectAllVisible()
    {
        foreach (var item in PanelsView.Cast<PanelTileViewModel>())
        {
            item.IsSelected = true;
        }

        OnSelectionChanged();
    }

    private void RebuildStatusLists()
    {
        Statuses.Clear();
        Counters.Clear();
        StatusFilters.Clear();
        StatusFilters.Add(new StatusFilterItem("Все статусы", null));
        foreach (var status in _settings.Statuses)
        {
            Statuses.Add(status);
            Counters.Add(new StatusCounterViewModel(status));
            StatusFilters.Add(new StatusFilterItem(status.Name, status));
        }

        SelectedStatusFilter = StatusFilters[0];
    }

    private void RebuildGroupFilters()
    {
        var currentKey = SelectedGroupFilter?.GroupKey;
        var currentKind = SelectedGroupFilter?.Kind ?? GroupFilterKind.All;
        GroupFilters.Clear();
        GroupFilters.Add(new GroupFilterItem("Все группы", GroupFilterKind.All, null));
        GroupFilters.Add(new GroupFilterItem("Без группы", GroupFilterKind.WithoutGroup, null));
        if (_session is not null)
        {
            foreach (var group in _session.Groups.Groups)
            {
                GroupFilters.Add(new GroupFilterItem(group.DisplayName, GroupFilterKind.Group, group.Key));
            }
        }

        SelectedGroupFilter = GroupFilters.FirstOrDefault(f => f.Kind == currentKind && string.Equals(f.GroupKey, currentKey, StringComparison.OrdinalIgnoreCase))
            ?? GroupFilters[0];
    }

    // ----- Контекстное меню плитки -----

    private IReadOnlyList<PanelInfo> SelectedPanelInfos() => SelectedPanels.Select(p => p.Panel).ToList();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFile()
    {
        var selected = SelectedPanels;
        if (selected.Count > 5 && !_ui.Confirm($"Открыть {selected.Count} файлов?"))
        {
            return;
        }

        foreach (var tile in selected)
        {
            Try(() => ShellLauncher.OpenFile(tile.Panel.CurrentFile.FullPath), "Не удалось открыть файл");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenLocation()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in SelectedPanels)
        {
            if (folders.Add(tile.Panel.CurrentFile.Directory))
            {
                Try(() => ShellLauncher.RevealInExplorer(tile.Panel.CurrentFile.FullPath), "Не удалось открыть расположение");
            }
        }
    }

    [RelayCommand]
    private void OpenPath(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            Try(() => ShellLauncher.RevealInExplorer(path), "Не удалось открыть расположение");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveToStatus(StatusDefinition? status)
    {
        if (_session is null || status is null)
        {
            return;
        }

        RunOperation(() => _session.MoveToStatus(SelectedPanelInfos(), status.Key), $"Перевод в «{status.Name}»");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Hold()
    {
        if (_session is null)
        {
            return;
        }

        if (_settings.FindByRole(StatusRole.Hold) is null)
        {
            _ui.ShowError("В settings.json нет статуса с ролью hold.");
            return;
        }

        RunOperation(() => _session.Hold(SelectedPanelInfos()), "Блокировка (HOLD)");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Release()
    {
        if (_session is null)
        {
            return;
        }

        if (_settings.FindByRole(StatusRole.Hold) is null)
        {
            _ui.ShowError("В settings.json нет статуса с ролью hold.");
            return;
        }

        RunOperation(() => _session.Release(SelectedPanelInfos()), "Снятие блокировки");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MarkProblem()
    {
        if (_session is null)
        {
            return;
        }

        if (_settings.FindByRole(StatusRole.Problem) is null)
        {
            _ui.ShowError("В settings.json нет статуса с ролью problem.");
            return;
        }

        var ids = string.Join(", ", SelectedPanels.Select(p => p.Id).Take(8));
        var comment = _ui.Prompt("Отметить «Проблема»", $"Комментарий к проблеме ({ids}):");
        if (comment is null)
        {
            return;
        }

        RunOperation(() => _session.MarkProblem(SelectedPanelInfos(), comment), "Отметка «Проблема»");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddToGroup(GroupViewModel? group)
    {
        if (_session is null || group is null)
        {
            return;
        }

        int added;
        try
        {
            added = _session.Groups.AddPanels(group.Key, SelectedPanels.Select(p => p.Id));
        }
        catch (InvalidOperationException ex)
        {
            _ui.ShowError(ex.Message);
            return;
        }

        SaveGroups();
        group.Refresh();
        StatusMessage = $"В группу «{group.DisplayName}» добавлено панелей: {added}.";
        _ = RefreshAsync(silent: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveFromGroup(GroupViewModel? group)
    {
        if (_session is null || group is null)
        {
            return;
        }

        int removed;
        try
        {
            removed = _session.Groups.RemovePanels(group.Key, SelectedPanels.Select(p => p.Id));
        }
        catch (InvalidOperationException ex)
        {
            _ui.ShowError(ex.Message);
            return;
        }

        SaveGroups();
        group.Refresh();
        StatusMessage = $"Из группы «{group.DisplayName}» убрано панелей: {removed}.";
        _ = RefreshAsync(silent: true);
    }

    private async void RunOperation(Func<OperationReport> operation, string title)
    {
        OperationReport report;
        try
        {
            StatusMessage = title + "…";
            report = await Task.Run(operation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _ui.ShowError($"{title}: {ex.Message}");
            return;
        }

        StatusMessage = $"{title}: перемещено {report.Moved.Count}, пропущено {report.Skipped.Count}, ошибок {report.Errors.Count}.";
        if (report.HasErrors)
        {
            _ui.ShowError($"{title} — ошибки:{Environment.NewLine}" + string.Join(Environment.NewLine, report.Errors.Select(e => $"{e.PanelId}: {e.Message}")));
        }

        _ = RefreshAsync(silent: true);
    }

    private void Try(Action action, string title)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _ui.ShowError($"{title}: {ex.Message}");
        }
    }

    // ----- Группы -----

    private void RebuildGroups()
    {
        var selectedKey = SelectedGroup?.Key;
        Groups.Clear();
        if (_session is null)
        {
            return;
        }

        foreach (var group in _session.Groups.Groups)
        {
            Groups.Add(new GroupViewModel(group));
        }

        SelectedGroup = Groups.FirstOrDefault(g => string.Equals(g.Key, selectedKey, StringComparison.OrdinalIgnoreCase)) ?? Groups.FirstOrDefault();
    }

    private void SaveGroups()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            _session.Groups.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.ShowError("Не удалось сохранить groups.json: " + ex.Message);
        }
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (_session is null)
        {
            return;
        }

        var key = _ui.Prompt("Новая группа", "Ключ группы (например, E):");
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var name = _ui.Prompt("Новая группа", "Название группы:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _session.Groups.AddGroup(key, name);
        }
        catch (InvalidOperationException ex)
        {
            _ui.ShowError(ex.Message);
            return;
        }

        SaveGroups();
        RebuildGroups();
        RebuildGroupFilters();
        SelectedGroup = Groups.FirstOrDefault(g => string.Equals(g.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void RenameGroup()
    {
        if (_session is null || SelectedGroup is null)
        {
            return;
        }

        var name = _ui.Prompt("Переименовать группу", "Новое название:", SelectedGroup.Group.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _session.Groups.RenameGroup(SelectedGroup.Key, name);
        SaveGroups();
        RebuildGroups();
        RebuildGroupFilters();
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (_session is null || SelectedGroup is null)
        {
            return;
        }

        if (!_ui.Confirm($"Удалить группу «{SelectedGroup.DisplayName}»? Файлы панелей не затрагиваются."))
        {
            return;
        }

        _session.Groups.RemoveGroup(SelectedGroup.Key);
        SaveGroups();
        RebuildGroups();
        RebuildGroupFilters();
        _ = RefreshAsync(silent: true);
    }

    [RelayCommand]
    private void RemoveMember(string? panelId)
    {
        if (_session is null || SelectedGroup is null || string.IsNullOrEmpty(panelId))
        {
            return;
        }

        _session.Groups.RemovePanels(SelectedGroup.Key, [panelId]);
        SaveGroups();
        SelectedGroup.Refresh();
        _ = RefreshAsync(silent: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddSelectedToGroup()
    {
        if (SelectedGroup is null)
        {
            StatusMessage = "Сначала выберите группу в списке слева.";
            return;
        }

        AddToGroup(SelectedGroup);
    }

    // ----- Настройки -----

    [RelayCommand]
    private void OpenSettingsFile() => Try(() => ShellLauncher.OpenFile(_settingsPath), "Не удалось открыть settings.json");

    [RelayCommand]
    private void OpenRootFolder()
    {
        if (RootFolder is not null)
        {
            Try(() => ShellLauncher.RevealInExplorer(RootFolder), "Не удалось открыть папку проекта");
        }
    }

    [RelayCommand]
    private void ReloadSettings()
    {
        TrackerSettings loaded;
        try
        {
            loaded = SettingsStore.LoadOrCreate(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _ui.ShowError("Не удалось прочитать settings.json: " + ex.Message);
            return;
        }

        var errors = loaded.Validate();
        if (errors.Count > 0)
        {
            _ui.ShowError("Ошибки в settings.json:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            return;
        }

        _settings = loaded;
        OnPropertyChanged(nameof(Settings));
        RebuildStatusLists();
        if ((RootFolder ?? _settings.RootFolder) is { Length: > 0 } folder && Directory.Exists(folder))
        {
            OpenProject(folder);
        }

        StatusMessage = "Настройки перечитаны.";
    }

    [RelayCommand]
    private void CreateStatusFolders()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var created = _session.EnsureStatusFolders();
            StatusMessage = created.Count == 0 ? "Все папки статусов уже существуют." : "Созданы папки: " + string.Join(", ", created);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.ShowError("Не удалось создать папки: " + ex.Message);
        }

        _ = RefreshAsync(silent: true);
    }

    private void TrySaveSettings()
    {
        try
        {
            SettingsStore.Save(_settingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ui.ShowError("Не удалось сохранить settings.json: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
