using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed partial class DashboardViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    ConnectionMonitor connection,
    ILogger<DashboardViewModel> logger) : PageViewModelBase
{
    public override string Title => "Главная";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizationRoleName))]
    private OrganizationDto? _organization;

    public string OrganizationRoleName => Organization is null ? string.Empty : Names.Of(Organization.CurrentUserRole);

    [ObservableProperty]
    private string _serverVersion = "—";

    [ObservableProperty]
    private bool _canViewAudit;

    public ObservableCollection<ProjectDto> RecentProjects { get; } = [];

    public ObservableCollection<AuditEventDto> RecentEvents { get; } = [];

    public ObservableCollection<DocumentDto> RecentDocuments { get; } = [];

    [ObservableProperty]
    private WorkflowSummaryDto? _workflowSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncStatusText))]
    [NotifyPropertyChangedFor(nameof(SyncHasProblems))]
    private SyncSummaryDto? _sync;

    public string SyncStatusText => Sync is null
        ? "—"
        : Sync.AgentsTotal == 0
            ? "Агенты не зарегистрированы"
            : $"Агентов на связи: {Sync.AgentsOnline} из {Sync.AgentsTotal - Sync.AgentsRevoked}";

    public bool SyncHasProblems => Sync is { } s && (s.FailedOperations > 0 || s.UnresolvedOperations > 0);

    public string ServerAddress => shell.ServerAddress;

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Organization = shell.SelectedOrganization;
        CanViewAudit = Organization?.Can(OrganizationPermissions.ViewAudit) == true;

        await RunAsync(async () =>
        {
            var infoTask = api.GetServerInfoAsync(cancellationToken);
            Task<PagedResult<ProjectDto>>? projectsTask = null;
            Task<PagedResult<AuditEventDto>>? auditTask = null;
            Task<IReadOnlyList<DocumentDto>>? documentsTask = null;
            Task<SyncSummaryDto>? syncTask = null;
            Task<WorkflowSummaryDto>? workflowTask = null;
            if (Organization is not null)
            {
                syncTask = api.GetSyncSummaryAsync(Organization.Id, cancellationToken);
                workflowTask = api.GetWorkflowSummaryAsync(Organization.Id, cancellationToken);
                projectsTask = api.GetProjectsAsync(Organization.Id, new ProjectListQuery(Sort: ProjectSortField.UpdatedAt, Descending: true, PageSize: 6), cancellationToken);
                documentsTask = api.GetRecentDocumentsAsync(Organization.Id, 8, cancellationToken);
                if (CanViewAudit)
                {
                    auditTask = api.GetAuditAsync(Organization.Id, new AuditListQuery(PageSize: 8), cancellationToken);
                }
            }

            var info = await infoTask;
            ServerVersion = info.Version;

            RecentProjects.Clear();
            if (projectsTask is not null)
            {
                foreach (var project in (await projectsTask).Items)
                {
                    RecentProjects.Add(project);
                }
            }

            Sync = syncTask is null ? null : await syncTask;
            WorkflowSummary = workflowTask is null ? null : await workflowTask;

            RecentDocuments.Clear();
            if (documentsTask is not null)
            {
                foreach (var document in await documentsTask)
                {
                    RecentDocuments.Add(document);
                }
            }

            RecentEvents.Clear();
            if (auditTask is not null)
            {
                foreach (var evt in (await auditTask).Items)
                {
                    RecentEvents.Add(evt);
                }
            }
        }, connection, logger);
    }

    [RelayCommand]
    private void OpenProjects() => shell.NavigateTo(ShellViewModel.ProjectsKey);

    [RelayCommand]
    private void OpenMembers() => shell.NavigateTo(ShellViewModel.MembersKey);

    [RelayCommand]
    private void OpenSyncQueue() => shell.NavigateTo(ShellViewModel.SyncQueueKey);

    [RelayCommand]
    private void OpenTasks() => shell.NavigateTo(ShellViewModel.TasksKey);

    [RelayCommand]
    private void OpenDocument(DocumentDto? document)
    {
        if (document is not null)
        {
            shell.ShowDocument(document.Id);
        }
    }

    [RelayCommand]
    private void OpenProject(ProjectDto? project)
    {
        if (project is not null)
        {
            shell.ShowProject(project.Id);
        }
    }
}
