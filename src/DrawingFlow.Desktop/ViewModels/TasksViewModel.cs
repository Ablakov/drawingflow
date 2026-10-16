using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed record TaskFilterOption(WorkflowTaskStatus? Status, WorkflowTaskType? Type, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Мои согласования и задачи в выбранной организации.</summary>
public sealed partial class TasksViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<TasksViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 50;

    public override string Title => "Мои задачи и согласования";

    public ObservableCollection<WorkflowTaskDto> Tasks { get; } = [];

    public IReadOnlyList<TaskFilterOption> FilterOptions { get; } =
    [
        new TaskFilterOption(WorkflowTaskStatus.Open, null, "Открытые"),
        new TaskFilterOption(WorkflowTaskStatus.Open, WorkflowTaskType.Review, "Мои согласования"),
        new TaskFilterOption(WorkflowTaskStatus.Open, WorkflowTaskType.Rework, "На доработку"),
        new TaskFilterOption(WorkflowTaskStatus.Completed, null, "Завершённые"),
        new TaskFilterOption(WorkflowTaskStatus.Cancelled, null, "Отменённые"),
        new TaskFilterOption(null, null, "Все"),
    ];

    [ObservableProperty]
    private TaskFilterOption? _selectedFilter;

    [ObservableProperty]
    private WorkflowTaskDto? _selectedTask;

    [ObservableProperty]
    private WorkflowSummaryDto? _summary;

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

    public bool IsEmpty => LoadedOnce && TotalCount == 0 && !HasError;

    public string PageInfo => TotalCount == 0 ? "Нет задач" : $"Страница {Page} из {Math.Max(TotalPages, 1)} · всего {TotalCount}";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var organization = shell.SelectedOrganization;
        if (organization is null)
        {
            Tasks.Clear();
            LoadedOnce = true;
            return;
        }

        SelectedFilter ??= FilterOptions[0];
        await RunAsync(async () =>
        {
            var query = new WorkflowTaskListQuery(SelectedFilter?.Status, SelectedFilter?.Type, null, Page, PageSize);
            var tasksTask = api.GetMyTasksAsync(organization.Id, query, cancellationToken);
            var summaryTask = api.GetWorkflowSummaryAsync(organization.Id, cancellationToken);
            var result = await tasksTask;
            Tasks.Clear();
            foreach (var task in result.Items)
            {
                Tasks.Add(task);
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
    private void OpenDocument(WorkflowTaskDto? task)
    {
        if (task is not null)
        {
            shell.ShowDocument(task.DocumentId);
        }
    }

    [RelayCommand]
    private Task ApproveAsync(WorkflowTaskDto? task) => DecideAsync(task, ReviewDecision.Approved);

    [RelayCommand]
    private Task RequestRevisionAsync(WorkflowTaskDto? task) => DecideAsync(task, ReviewDecision.RevisionRequired);

    private async Task DecideAsync(WorkflowTaskDto? task, ReviewDecision decision)
    {
        if (task is null || task.Type != WorkflowTaskType.Review || task.Status != WorkflowTaskStatus.Open)
        {
            return;
        }

        var dialog = new ReviewDecisionDialogViewModel(api, task.DocumentId, task.DocumentVersionId, $"{task.DocumentNumber} — {task.DocumentTitle}, версия {task.VersionNumber}", decision);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task CancelTaskAsync(WorkflowTaskDto? task)
    {
        if (task is null || task.Status != WorkflowTaskStatus.Open)
        {
            return;
        }

        if (!dialogs.Confirm($"Отменить задачу «{Names.Of(task.Type)}» по документу {task.DocumentNumber}?", "Задачи"))
        {
            return;
        }

        if (await RunAsync(() => api.CancelWorkflowTaskAsync(task.Id, new CancelTaskRequest("Отменена пользователем")), connection, logger))
        {
            await LoadAsync();
        }
    }
}
