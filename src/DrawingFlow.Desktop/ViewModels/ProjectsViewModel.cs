using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed record StatusFilterOption(ProjectStatus? Value, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class ProjectsViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<ProjectsViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 50;

    public override string Title => "Проекты";

    public ObservableCollection<ProjectDto> Projects { get; } = [];

    public IReadOnlyList<StatusFilterOption> StatusOptions { get; } =
    [
        new StatusFilterOption(null, "Все статусы"),
        new StatusFilterOption(ProjectStatus.Active, Names.Of(ProjectStatus.Active)),
        new StatusFilterOption(ProjectStatus.Archived, Names.Of(ProjectStatus.Archived)),
    ];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private StatusFilterOption? _selectedStatus;

    [ObservableProperty]
    private ProjectSortField _sortField = ProjectSortField.Name;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private ProjectDto? _selectedProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages;

    [ObservableProperty]
    private bool _canCreateProjects;

    [ObservableProperty]
    private bool _loadedOnce;

    public bool IsEmpty => LoadedOnce && TotalCount == 0 && !HasError;

    public string PageInfo => TotalCount == 0 ? "Нет проектов" : $"Страница {Page} из {Math.Max(TotalPages, 1)} · всего {TotalCount}";

    public string SortIndicator => $"Сортировка: {SortFieldName(SortField)} {(SortDescending ? "↓" : "↑")}";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var organization = shell.SelectedOrganization;
        CanCreateProjects = organization?.Can(OrganizationPermissions.CreateProjects) == true;
        if (organization is null)
        {
            Projects.Clear();
            TotalCount = 0;
            LoadedOnce = true;
            return;
        }

        SelectedStatus ??= StatusOptions[0];
        await RunAsync(async () =>
        {
            var query = new ProjectListQuery(
                Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Status: SelectedStatus?.Value,
                Sort: SortField,
                Descending: SortDescending,
                Page: Page,
                PageSize: PageSize);
            var result = await api.GetProjectsAsync(organization.Id, query, cancellationToken);
            Projects.Clear();
            foreach (var project in result.Items)
            {
                Projects.Add(project);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            if (Page > 1 && result.Items.Count == 0 && result.TotalCount > 0)
            {
                Page = result.TotalPages;
                await LoadAsync(cancellationToken);
            }
        }, connection, logger);
        LoadedOnce = true;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SortIndicator));
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        SelectedStatus = StatusOptions[0];
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task SortByAsync(string? field)
    {
        if (Enum.TryParse<ProjectSortField>(field, out var parsed))
        {
            if (parsed == SortField)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                SortField = parsed;
                SortDescending = parsed is ProjectSortField.UpdatedAt or ProjectSortField.CreatedAt;
            }

            Page = 1;
        }

        return LoadAsync();
    }

    private bool CanGoNext() => Page < TotalPages;

    private bool CanGoPrevious() => Page > 1;

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

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedProject is not null)
        {
            shell.ShowProject(SelectedProject.Id);
        }
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        var organization = shell.SelectedOrganization;
        if (organization is null || !CanCreateProjects)
        {
            return;
        }

        var dialog = new ProjectEditorDialogViewModel(api, organization.Id, existing: null);
        if (dialogs.ShowDialog(dialog) && dialog.Result is not null)
        {
            await shell.ReloadOrganizationsAsync();
            shell.ShowProject(dialog.Result.Id);
        }
    }

    private static string SortFieldName(ProjectSortField field) => field switch
    {
        ProjectSortField.Code => "по коду",
        ProjectSortField.UpdatedAt => "по дате изменения",
        ProjectSortField.CreatedAt => "по дате создания",
        _ => "по названию",
    };
}
