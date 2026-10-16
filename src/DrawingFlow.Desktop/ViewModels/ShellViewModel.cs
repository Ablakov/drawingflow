using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed record NavItem(string Key, string Title, string Glyph);

/// <summary>Главное окно: пользователь, выбор организации, навигация, состояние соединения.</summary>
public sealed partial class ShellViewModel(
    IServiceProvider services,
    ShellCoordinator coordinator,
    DesktopSettingsStore settings,
    ConnectionMonitor connection,
    ILogger<ShellViewModel> logger) : ViewModelBase
{
    public const string DashboardKey = "dashboard";
    public const string ProjectsKey = "projects";
    public const string DocumentsKey = "documents";
    public const string AgentsKey = "agents";
    public const string SyncQueueKey = "sync";
    public const string TasksKey = "tasks";
    public const string MembersKey = "members";
    public const string AuditKey = "audit";
    public const string AdministrationKey = "administration";
    public const string SettingsKey = "settings";

    private static readonly HashSet<string> OrganizationScopedPages = [DashboardKey, ProjectsKey, DocumentsKey, MembersKey, AuditKey, AgentsKey, SyncQueueKey, TasksKey];

    private string _currentKey = DashboardKey;
    private bool _suppressNavigation;

    public ConnectionMonitor Connection => connection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserDisplayName))]
    [NotifyPropertyChangedFor(nameof(IsServerAdministrator))]
    private UserDto? _currentUser;

    public string UserDisplayName => CurrentUser?.DisplayName ?? string.Empty;

    public bool IsServerAdministrator => CurrentUser?.IsServerAdministrator == true;

    public ObservableCollection<OrganizationDto> Organizations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrganization))]
    [NotifyPropertyChangedFor(nameof(OrganizationRoleName))]
    private OrganizationDto? _selectedOrganization;

    public bool HasOrganization => SelectedOrganization is not null;

    public string OrganizationRoleName => SelectedOrganization is null ? string.Empty : Names.Of(SelectedOrganization.CurrentUserRole);

    [ObservableProperty]
    private bool _hasOrganizations = true;

    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    public string ServerAddress => settings.BaseAddress?.GetLeftPart(UriPartial.Authority) ?? string.Empty;

    public async Task InitializeAsync(CurrentUserDto me)
    {
        CurrentUser = me.User;
        OnPropertyChanged(nameof(ServerAddress));
        CurrentPage = null;
        _currentKey = DashboardKey;
        await ReloadOrganizationsAsync(selectId: settings.Current.LastOrganizationId);
        RebuildNavigation();
        NavigateTo(DashboardKey);
    }

    /// <summary>Перечитывает организации (например, после создания проекта меняются счётчики).</summary>
    public async Task ReloadOrganizationsAsync(Guid? selectId = null)
    {
        var keepId = selectId ?? SelectedOrganization?.Id;
        await RunAsync(async () =>
        {
            var api = services.GetRequiredService<DrawingFlowApiClient>();
            var organizations = await api.GetOrganizationsAsync();
            _suppressNavigation = true;
            try
            {
                Organizations.Clear();
                foreach (var organization in organizations)
                {
                    Organizations.Add(organization);
                }

                HasOrganizations = Organizations.Count > 0;
                SelectedOrganization = Organizations.FirstOrDefault(o => o.Id == keepId) ?? Organizations.FirstOrDefault();
            }
            finally
            {
                _suppressNavigation = false;
            }
        }, connection, logger);
    }

    partial void OnSelectedOrganizationChanged(OrganizationDto? value)
    {
        if (value is not null)
        {
            settings.Update(s => s.LastOrganizationId = value.Id);
        }

        if (_suppressNavigation)
        {
            return;
        }

        RebuildNavigation();
        if (OrganizationScopedPages.Contains(_currentKey))
        {
            NavigateTo(_currentKey);
        }
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null && !_suppressNavigation && value.Key != _currentKey)
        {
            NavigateTo(value.Key);
        }
    }

    [RelayCommand]
    public void NavigateTo(string key)
    {
        PageViewModelBase? page = key switch
        {
            DashboardKey => services.GetRequiredService<DashboardViewModel>(),
            ProjectsKey => services.GetRequiredService<ProjectsViewModel>(),
            DocumentsKey => services.GetRequiredService<DocumentsViewModel>(),
            SyncQueueKey => services.GetRequiredService<SyncQueueViewModel>(),
            TasksKey => services.GetRequiredService<TasksViewModel>(),
            AgentsKey when SelectedOrganization?.Can(OrganizationPermissions.ManageAgents) == true => services.GetRequiredService<AgentsViewModel>(),
            MembersKey => services.GetRequiredService<OrganizationMembersViewModel>(),
            AuditKey when SelectedOrganization?.Can(OrganizationPermissions.ViewAudit) == true => services.GetRequiredService<AuditViewModel>(),
            AdministrationKey when IsServerAdministrator => services.GetRequiredService<AdministrationViewModel>(),
            SettingsKey => services.GetRequiredService<SettingsViewModel>(),
            _ => null,
        };

        if (page is null)
        {
            return;
        }

        _currentKey = key;
        ShowPage(page);
        SyncSelectedNavItem(key);
    }

    public void ShowProject(Guid projectId)
    {
        var page = services.GetRequiredService<ProjectDetailsViewModel>();
        page.ProjectId = projectId;
        _currentKey = ProjectsKey;
        ShowPage(page);
        SyncSelectedNavItem(ProjectsKey);
    }

    public void ShowDocuments(Guid projectId)
    {
        var page = services.GetRequiredService<DocumentsViewModel>();
        page.PreselectedProjectId = projectId;
        _currentKey = DocumentsKey;
        ShowPage(page);
        SyncSelectedNavItem(DocumentsKey);
    }

    public void ShowDocument(Guid documentId)
    {
        var page = services.GetRequiredService<DocumentDetailsViewModel>();
        page.DocumentId = documentId;
        _currentKey = DocumentsKey;
        ShowPage(page);
        SyncSelectedNavItem(DocumentsKey);
    }

    [RelayCommand]
    private Task LogoutAsync() => coordinator.LogoutAsync();

    private void ShowPage(PageViewModelBase page)
    {
        CurrentPage = page;
        _ = LoadPageAsync(page);
    }

    private async Task LoadPageAsync(PageViewModelBase page)
    {
        try
        {
            await page.LoadAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка загрузки страницы {Page}", page.GetType().Name);
            page.ErrorMessage = "Не удалось загрузить страницу. Подробности записаны в журнал приложения.";
        }
    }

    private void RebuildNavigation()
    {
        _suppressNavigation = true;
        try
        {
            NavItems.Clear();
            NavItems.Add(new NavItem(DashboardKey, "Главная", ""));
            if (HasOrganization)
            {
                NavItems.Add(new NavItem(ProjectsKey, "Проекты", ""));
                NavItems.Add(new NavItem(MembersKey, "Участники", ""));
                if (SelectedOrganization!.Can(OrganizationPermissions.ViewAudit))
                {
                    NavItems.Add(new NavItem(AuditKey, "Журнал аудита", ""));
                }
            }

            if (IsServerAdministrator)
            {
                NavItems.Add(new NavItem(AdministrationKey, "Администрирование", ""));
            }

            NavItems.Add(new NavItem(SettingsKey, "Настройки", ""));
            SyncSelectedNavItem(_currentKey);
        }
        finally
        {
            _suppressNavigation = false;
        }
    }

    private void SyncSelectedNavItem(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (!ReferenceEquals(SelectedNavItem, item))
        {
            _suppressNavigation = true;
            SelectedNavItem = item;
            _suppressNavigation = false;
        }
    }
}
