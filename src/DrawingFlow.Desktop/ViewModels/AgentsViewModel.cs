using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

/// <summary>Агенты организации и их папки наблюдения (администраторы организации).</summary>
public sealed partial class AgentsViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<AgentsViewModel> logger) : PageViewModelBase
{
    public override string Title => "Агенты синхронизации";

    public ObservableCollection<AgentDto> Agents { get; } = [];

    public ObservableCollection<WatchedFolderDto> Folders { get; } = [];

    public ObservableCollection<ProjectDto> Projects { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAgent))]
    [NotifyPropertyChangedFor(nameof(CanConfigureSelected))]
    private AgentDto? _selectedAgent;

    [ObservableProperty]
    private WatchedFolderDto? _selectedFolder;

    [ObservableProperty]
    private bool _canManage;

    [ObservableProperty]
    private bool _foldersLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _loadedOnce;

    public bool HasSelectedAgent => SelectedAgent is not null;

    public bool CanConfigureSelected => SelectedAgent is { IsRevoked: false } && CanManage;

    public bool IsEmpty => LoadedOnce && Agents.Count == 0 && !HasError;

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var organization = shell.SelectedOrganization;
        CanManage = organization?.Can(OrganizationPermissions.ManageAgents) == true;
        if (organization is null)
        {
            Agents.Clear();
            LoadedOnce = true;
            return;
        }

        var selectedId = SelectedAgent?.Id;
        await RunAsync(async () =>
        {
            var agentsTask = api.GetAgentsAsync(organization.Id, cancellationToken);
            var projectsTask = Projects.Count == 0
                ? api.GetProjectsAsync(organization.Id, new ProjectListQuery(Sort: ProjectSortField.Name, PageSize: PageRequest.MaxPageSize), cancellationToken)
                : null;

            Agents.Clear();
            foreach (var agent in await agentsTask)
            {
                Agents.Add(agent);
            }

            if (projectsTask is not null)
            {
                foreach (var project in (await projectsTask).Items)
                {
                    Projects.Add(project);
                }
            }

            SelectedAgent = Agents.FirstOrDefault(a => a.Id == selectedId) ?? Agents.FirstOrDefault();
        }, connection, logger);
        LoadedOnce = true;
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSelectedAgentChanged(AgentDto? value)
    {
        Folders.Clear();
        FoldersLoaded = false;
        if (value is not null)
        {
            _ = LoadFoldersAsync(value.Id);
        }
    }

    private async Task LoadFoldersAsync(Guid agentId)
    {
        await RunAsync(async () =>
        {
            var folders = await api.GetWatchedFoldersAsync(agentId);
            if (SelectedAgent?.Id != agentId)
            {
                return;
            }

            Folders.Clear();
            foreach (var folder in folders)
            {
                Folders.Add(folder);
            }

            FoldersLoaded = true;
        }, connection, logger, clearError: false);
    }

    [RelayCommand]
    private async Task RegisterAgentAsync()
    {
        var organization = shell.SelectedOrganization;
        if (organization is null || !CanManage)
        {
            return;
        }

        var dialog = new RegisterAgentDialogViewModel(api, organization.Id);
        if (dialogs.ShowDialog(dialog) && dialog.Result is not null)
        {
            dialogs.ShowDialog(new AgentKeyDialogViewModel(dialog.Result));
            await LoadAsync();
            SelectedAgent = Agents.FirstOrDefault(a => a.Id == dialog.Result.Agent.Id);
        }
    }

    [RelayCommand]
    private async Task RevokeAgentAsync(AgentDto? agent)
    {
        if (agent is null || agent.IsRevoked || !CanManage)
        {
            return;
        }

        if (!dialogs.Confirm(
                $"Отозвать ключ агента «{agent.Name}»? Агент немедленно потеряет доступ к серверу, его папки наблюдения будут отключены. Для возобновления потребуется новая регистрация.",
                "Отзыв ключа агента"))
        {
            return;
        }

        if (await RunAsync(() => api.RevokeAgentAsync(agent.Id), connection, logger))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (SelectedAgent is null || !CanConfigureSelected)
        {
            return;
        }

        if (Projects.Count == 0)
        {
            dialogs.ShowInfo("В организации нет проектов. Сначала создайте проект.", "Папки наблюдения");
            return;
        }

        var dialog = new WatchedFolderDialogViewModel(api, SelectedAgent.Id, Projects, existing: null);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadFoldersAsync(SelectedAgent.Id);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task EditFolderAsync(WatchedFolderDto? folder)
    {
        if (folder is null || SelectedAgent is null || !CanManage)
        {
            return;
        }

        var dialog = new WatchedFolderDialogViewModel(api, SelectedAgent.Id, Projects, folder);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadFoldersAsync(SelectedAgent.Id);
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(WatchedFolderDto? folder)
    {
        if (folder is null || SelectedAgent is null || !CanManage)
        {
            return;
        }

        if (!dialogs.Confirm($"Удалить папку наблюдения «{folder.Path}»? Файлы на рабочей станции и документы на сервере не затрагиваются.", "Папки наблюдения"))
        {
            return;
        }

        if (await RunAsync(() => api.DeleteWatchedFolderAsync(folder.Id), connection, logger))
        {
            await LoadFoldersAsync(SelectedAgent.Id);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void OpenQueue() => shell.NavigateTo(ShellViewModel.SyncQueueKey);
}
