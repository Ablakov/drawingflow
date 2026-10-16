using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed record SyncStatusOption(SyncOperationStatus? Value, bool ActiveOnly, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Очередь синхронизации организации: состояния операций агентов, ошибки, разрешение конфликтов сопоставления.</summary>
public sealed partial class SyncQueueViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<SyncQueueViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 50;

    public override string Title => "Очередь синхронизации";

    public ObservableCollection<SyncOperationDto> Operations { get; } = [];

    public IReadOnlyList<SyncStatusOption> StatusOptions { get; } =
        new[] { new SyncStatusOption(null, true, "Незавершённые"), new SyncStatusOption(null, false, "Все") }
            .Concat(Enum.GetValues<SyncOperationStatus>().Select(s => new SyncStatusOption(s, false, Names.Of(s))))
            .ToList();

    [ObservableProperty]
    private SyncStatusOption? _selectedStatus;

    [ObservableProperty]
    private SyncOperationDto? _selectedOperation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _loadedOnce;

    [ObservableProperty]
    private SyncSummaryDto? _summary;

    public bool IsEmpty => LoadedOnce && TotalCount == 0 && !HasError;

    public string PageInfo => TotalCount == 0 ? "Нет операций" : $"Страница {Page} из {Math.Max(TotalPages, 1)} · всего {TotalCount}";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var organization = shell.SelectedOrganization;
        if (organization is null)
        {
            Operations.Clear();
            LoadedOnce = true;
            return;
        }

        SelectedStatus ??= StatusOptions[0];
        await RunAsync(async () =>
        {
            var query = new SyncOperationListQuery(Status: SelectedStatus?.Value, ActiveOnly: SelectedStatus?.ActiveOnly ?? false, Page: Page, PageSize: PageSize);
            var operationsTask = api.GetSyncOperationsAsync(organization.Id, query, cancellationToken);
            var summaryTask = api.GetSyncSummaryAsync(organization.Id, cancellationToken);

            var result = await operationsTask;
            Operations.Clear();
            foreach (var operation in result.Items)
            {
                Operations.Add(operation);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            Summary = await summaryTask;
        }, connection, logger);
        LoadedOnce = true;
    }

    [RelayCommand]
    private Task ApplyFilterAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPageAsync()
    {
        Page++;
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousPageAsync()
    {
        Page--;
        return LoadAsync();
    }

    private bool CanGoNext() => Page < TotalPages;

    private bool CanGoPrevious() => Page > 1;

    [RelayCommand]
    private async Task RetryAsync(SyncOperationDto? operation)
    {
        if (operation is null)
        {
            return;
        }

        if (await RunAsync(() => api.RetrySyncOperationAsync(operation.Id), connection, logger))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ResolveAsync(SyncOperationDto? operation)
    {
        if (operation is null)
        {
            return;
        }

        var dialog = new ResolveOperationDialogViewModel(api, operation);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DismissAsync(SyncOperationDto? operation)
    {
        if (operation is null)
        {
            return;
        }

        if (!dialogs.Confirm($"Отклонить операцию для файла «{operation.FileName}»? Агент удалит её из очереди; файл и документы не изменяются.", "Очередь синхронизации"))
        {
            return;
        }

        if (await RunAsync(() => api.DismissSyncOperationAsync(operation.Id), connection, logger))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void OpenDocument(SyncOperationDto? operation)
    {
        if (operation?.DocumentId is Guid documentId)
        {
            shell.ShowDocument(documentId);
        }
    }
}
