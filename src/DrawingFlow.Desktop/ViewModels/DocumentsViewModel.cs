using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed record DocumentStatusOption(DocumentStatus? Value, string Name)
{
    public override string ToString() => Name;
}

public sealed record DisciplineOption(string? Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Реестр документов выбранного проекта: поиск, фильтры, серверная сортировка и пагинация.</summary>
public sealed partial class DocumentsViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    DesktopSettingsStore settings,
    ConnectionMonitor connection,
    ILogger<DocumentsViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 50;
    private bool _suppressReload;

    public override string Title => "Документы";

    /// <summary>Проект, который нужно выбрать при открытии страницы (например, из карточки проекта).</summary>
    public Guid? PreselectedProjectId { get; set; }

    public ObservableCollection<ProjectDto> Projects { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    private ProjectDto? _selectedProject;

    public bool HasProject => SelectedProject is not null;

    public ObservableCollection<DocumentDto> Documents { get; } = [];

    public IReadOnlyList<DocumentStatusOption> StatusOptions { get; } =
        new[] { new DocumentStatusOption(null, "Все статусы") }
            .Concat(Enum.GetValues<DocumentStatus>().Select(s => new DocumentStatusOption(s, Names.Of(s))))
            .ToList();

    public ObservableCollection<DisciplineOption> DisciplineOptions { get; } = [new DisciplineOption(null, "Все разделы")];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DocumentStatusOption? _selectedStatus;

    [ObservableProperty]
    private DisciplineOption? _selectedDiscipline;

    [ObservableProperty]
    private DocumentSortField _sortField = DocumentSortField.Number;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private DocumentDto? _selectedDocument;

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
    private bool _canRegister;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _loadedOnce;

    public bool IsEmpty => LoadedOnce && HasProject && TotalCount == 0 && !HasError;

    public string PageInfo => TotalCount == 0 ? "Нет документов" : $"Страница {Page} из {Math.Max(TotalPages, 1)} · всего {TotalCount}";

    public string SortIndicator => $"Сортировка: {SortFieldName(SortField)} {(SortDescending ? "↓" : "↑")}";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var organization = shell.SelectedOrganization;
        if (organization is null)
        {
            Projects.Clear();
            SelectedProject = null;
            LoadedOnce = true;
            return;
        }

        SelectedStatus ??= StatusOptions[0];
        SelectedDiscipline ??= DisciplineOptions[0];

        if (Projects.Count == 0)
        {
            var loaded = await RunAsync(async () =>
            {
                var projects = await api.GetProjectsAsync(organization.Id, new ProjectListQuery(Sort: ProjectSortField.Name, PageSize: PageRequest.MaxPageSize), cancellationToken);
                _suppressReload = true;
                try
                {
                    Projects.Clear();
                    foreach (var project in projects.Items)
                    {
                        Projects.Add(project);
                    }

                    var wanted = PreselectedProjectId ?? settings.Current.LastProjectId;
                    SelectedProject = Projects.FirstOrDefault(p => p.Id == wanted) ?? Projects.FirstOrDefault();
                }
                finally
                {
                    _suppressReload = false;
                }
            }, connection, logger);

            if (!loaded)
            {
                LoadedOnce = true;
                return;
            }
        }

        await LoadDocumentsAsync(cancellationToken);
    }

    partial void OnSelectedProjectChanged(ProjectDto? value)
    {
        CanRegister = value?.Can(ProjectPermissions.RegisterDocuments) == true;
        if (value is not null)
        {
            settings.Update(s => s.LastProjectId = value.Id);
        }

        if (_suppressReload)
        {
            return;
        }

        Page = 1;
        ResetDisciplines();
        _ = LoadDocumentsAsync();
    }

    private async Task LoadDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var project = SelectedProject;
        if (project is null)
        {
            Documents.Clear();
            TotalCount = 0;
            LoadedOnce = true;
            return;
        }

        await RunAsync(async () =>
        {
            var query = new DocumentListQuery(
                Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Status: SelectedStatus?.Value,
                Discipline: SelectedDiscipline?.Value,
                Sort: SortField,
                Descending: SortDescending,
                Page: Page,
                PageSize: PageSize);
            var documentsTask = api.GetDocumentsAsync(project.Id, query, cancellationToken);
            var disciplinesTask = DisciplineOptions.Count <= 1 ? api.GetDocumentDisciplinesAsync(project.Id, cancellationToken) : null;

            var result = await documentsTask;
            Documents.Clear();
            foreach (var document in result.Items)
            {
                Documents.Add(document);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;

            if (disciplinesTask is not null)
            {
                var selected = SelectedDiscipline?.Value;
                foreach (var discipline in await disciplinesTask)
                {
                    DisciplineOptions.Add(new DisciplineOption(discipline, discipline));
                }

                SelectedDiscipline = DisciplineOptions.FirstOrDefault(o => o.Value == selected) ?? DisciplineOptions[0];
            }
        }, connection, logger);

        LoadedOnce = true;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SortIndicator));
    }

    private void ResetDisciplines()
    {
        _suppressReload = true;
        try
        {
            while (DisciplineOptions.Count > 1)
            {
                DisciplineOptions.RemoveAt(DisciplineOptions.Count - 1);
            }

            SelectedDiscipline = DisciplineOptions[0];
        }
        finally
        {
            _suppressReload = false;
        }
    }

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return LoadDocumentsAsync();
    }

    [RelayCommand]
    private Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        SelectedStatus = StatusOptions[0];
        SelectedDiscipline = DisciplineOptions[0];
        Page = 1;
        return LoadDocumentsAsync();
    }

    [RelayCommand]
    private Task SortByAsync(string? field)
    {
        if (Enum.TryParse<DocumentSortField>(field, out var parsed))
        {
            if (parsed == SortField)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                SortField = parsed;
                SortDescending = parsed is DocumentSortField.UpdatedAt or DocumentSortField.CreatedAt;
            }

            Page = 1;
        }

        return LoadDocumentsAsync();
    }

    private bool CanGoNext() => Page < TotalPages;

    private bool CanGoPrevious() => Page > 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPageAsync()
    {
        Page++;
        return LoadDocumentsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousPageAsync()
    {
        Page--;
        return LoadDocumentsAsync();
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedDocument is not null)
        {
            shell.ShowDocument(SelectedDocument.Id);
        }
    }

    [RelayCommand]
    private void RegisterDocument()
    {
        var project = SelectedProject;
        if (project is null || !CanRegister)
        {
            return;
        }

        var dialog = new DocumentEditorDialogViewModel(api, project.Id, existing: null);
        if (dialogs.ShowDialog(dialog) && dialog.Result is not null)
        {
            shell.ShowDocument(dialog.Result.Id);
        }
    }

    private static string SortFieldName(DocumentSortField field) => field switch
    {
        DocumentSortField.Title => "по наименованию",
        DocumentSortField.UpdatedAt => "по дате изменения",
        DocumentSortField.CreatedAt => "по дате регистрации",
        DocumentSortField.Status => "по статусу",
        _ => "по номеру",
    };
}
