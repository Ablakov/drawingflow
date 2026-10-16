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

public sealed partial class ProjectDetailsViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<ProjectDetailsViewModel> logger) : PageViewModelBase
{
    public override string Title => Project?.Name ?? "Проект";

    public Guid ProjectId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(StatusName))]
    [NotifyPropertyChangedFor(nameof(MyRoleName))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanManageMembers))]
    private ProjectDto? _project;

    public ObservableCollection<ProjectMemberDto> Members { get; } = [];

    [ObservableProperty]
    private bool _membersLoaded;

    public string StatusName => Project is null ? string.Empty : Names.Of(Project.Status);

    public string MyRoleName => Project is null ? string.Empty : Names.Of(Project.CurrentUserRole);

    public bool CanEdit => Project?.Can(ProjectPermissions.EditSettings) == true;

    public bool CanManageMembers => Project?.Can(ProjectPermissions.ManageMembers) == true;

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            var projectTask = api.GetProjectAsync(ProjectId, cancellationToken);
            var membersTask = api.GetProjectMembersAsync(ProjectId, cancellationToken);
            Project = await projectTask;
            Members.Clear();
            foreach (var member in await membersTask)
            {
                Members.Add(member);
            }

            MembersLoaded = true;
        }, connection, logger);
    }

    [RelayCommand]
    private void Back() => shell.NavigateTo(ShellViewModel.ProjectsKey);

    [RelayCommand]
    private void OpenDocuments() => shell.ShowDocuments(ProjectId);

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Project is null || !CanEdit)
        {
            return;
        }

        var dialog = new ProjectEditorDialogViewModel(api, Project.OrganizationId, Project);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
            await shell.ReloadOrganizationsAsync();
        }
    }

    [RelayCommand]
    private async Task AddMemberAsync()
    {
        if (Project is null || !CanManageMembers)
        {
            return;
        }

        IReadOnlyList<Contracts.Organizations.OrganizationMemberDto> candidates = [];
        var loaded = await RunAsync(async () =>
        {
            var organizationMembers = await api.GetOrganizationMembersAsync(Project.OrganizationId);
            var existing = Members.Select(m => m.UserId).ToHashSet();
            candidates = organizationMembers.Where(m => m.IsActive && !existing.Contains(m.UserId)).ToList();
        }, connection, logger);
        if (!loaded)
        {
            return;
        }

        if (candidates.Count == 0)
        {
            dialogs.ShowInfo("Все участники организации уже добавлены в проект. Сначала добавьте пользователя в организацию.", "Участники проекта");
            return;
        }

        var dialog = new ProjectMemberDialogViewModel(api, Project.Id, existing: null, candidates);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ChangeMemberRoleAsync(ProjectMemberDto? member)
    {
        if (Project is null || member is null || !CanManageMembers)
        {
            return;
        }

        var dialog = new ProjectMemberDialogViewModel(api, Project.Id, member, candidates: []);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveMemberAsync(ProjectMemberDto? member)
    {
        if (Project is null || member is null || !CanManageMembers)
        {
            return;
        }

        if (!dialogs.Confirm($"Удалить участника «{member.DisplayName}» из проекта «{Project.Name}»?", "Удаление участника"))
        {
            return;
        }

        if (await RunAsync(() => api.RemoveProjectMemberAsync(Project.Id, member.UserId), connection, logger))
        {
            await LoadAsync();
        }
    }
}
