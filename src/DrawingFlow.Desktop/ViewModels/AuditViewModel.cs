using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed partial class AuditViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    ConnectionMonitor connection,
    ILogger<AuditViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 50;

    public override string Title => "Журнал аудита";

    public ObservableCollection<AuditEventDto> Events { get; } = [];

    [ObservableProperty]
    private string _actionFilter = string.Empty;

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

    public string PageInfo => TotalCount == 0 ? "Нет событий" : $"Страница {Page} из {Math.Max(TotalPages, 1)} · всего {TotalCount}";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var organization = shell.SelectedOrganization;
        if (organization is null)
        {
            Events.Clear();
            LoadedOnce = true;
            return;
        }

        await RunAsync(async () =>
        {
            var query = new AuditListQuery(
                Action: string.IsNullOrWhiteSpace(ActionFilter) ? null : ActionFilter.Trim(),
                Page: Page,
                PageSize: PageSize);
            var result = await api.GetAuditAsync(organization.Id, query, cancellationToken);
            Events.Clear();
            foreach (var evt in result.Items)
            {
                Events.Add(evt);
            }

            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
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
}
