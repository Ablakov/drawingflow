using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed partial class OrganizationMembersViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<OrganizationMembersViewModel> logger) : PageViewModelBase
{
    public override string Title => "Участники организации";

    public ObservableCollection<OrganizationMemberDto> Members { get; } = [];

    [ObservableProperty]
    private OrganizationDto? _organization;

    [ObservableProperty]
    private bool _canManageMembers;

    [ObservableProperty]
    private bool _canCreateUsers;

    [ObservableProperty]
    private bool _canAssignOwner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _loadedOnce;

    public bool IsEmpty => LoadedOnce && Members.Count == 0 && !HasError;

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Organization = shell.SelectedOrganization;
        CanManageMembers = Organization?.Can(OrganizationPermissions.ManageMembers) == true;
        CanCreateUsers = Organization?.Can(OrganizationPermissions.CreateUsers) == true;
        CanAssignOwner = Organization?.CurrentUserRole == OrganizationRole.Owner;
        if (Organization is null)
        {
            Members.Clear();
            LoadedOnce = true;
            return;
        }

        await RunAsync(async () =>
        {
            var members = await api.GetOrganizationMembersAsync(Organization.Id, cancellationToken);
            Members.Clear();
            foreach (var member in members)
            {
                Members.Add(member);
            }
        }, connection, logger);
        LoadedOnce = true;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task AddMemberAsync()
    {
        if (Organization is null || !CanManageMembers)
        {
            return;
        }

        var dialog = new OrganizationMemberDialogViewModel(api, Organization.Id, existing: null, CanAssignOwner);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
            await shell.ReloadOrganizationsAsync();
        }
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (Organization is null || !CanCreateUsers)
        {
            return;
        }

        var dialog = new CreateUserDialogViewModel(api, Organization.Id, CanAssignOwner, allowServerAdministrator: false);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
            await shell.ReloadOrganizationsAsync();
        }
    }

    [RelayCommand]
    private async Task ChangeRoleAsync(OrganizationMemberDto? member)
    {
        if (Organization is null || member is null || !CanManageMembers)
        {
            return;
        }

        var dialog = new OrganizationMemberDialogViewModel(api, Organization.Id, member, CanAssignOwner);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveMemberAsync(OrganizationMemberDto? member)
    {
        if (Organization is null || member is null || !CanManageMembers)
        {
            return;
        }

        if (!dialogs.Confirm(
                $"Удалить «{member.DisplayName}» из организации «{Organization.Name}»? Пользователь потеряет доступ ко всем её проектам.",
                "Удаление участника"))
        {
            return;
        }

        if (await RunAsync(() => api.RemoveOrganizationMemberAsync(Organization.Id, member.UserId), connection, logger))
        {
            await LoadAsync();
            await shell.ReloadOrganizationsAsync();
        }
    }
}
