using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

/// <summary>Администрирование сервера: все организации и все пользователи. Только для администраторов сервера.</summary>
public sealed partial class AdministrationViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    ConnectionMonitor connection,
    ILogger<AdministrationViewModel> logger) : PageViewModelBase
{
    private const int PageSize = 50;

    public override string Title => "Администрирование сервера";

    public ObservableCollection<OrganizationDto> Organizations { get; } = [];

    public ObservableCollection<UserDto> Users { get; } = [];

    [ObservableProperty]
    private string _userSearch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsersPageInfo))]
    [NotifyCanExecuteChangedFor(nameof(PreviousUsersPageCommand))]
    private int _usersPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsersPageInfo))]
    private int _usersTotal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextUsersPageCommand))]
    private int _usersTotalPages;

    public string UsersPageInfo => UsersTotal == 0 ? "Нет пользователей" : $"Страница {UsersPage} из {Math.Max(UsersTotalPages, 1)} · всего {UsersTotal}";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!shell.IsServerAdministrator)
        {
            ErrorMessage = "Раздел доступен только администратору сервера.";
            return;
        }

        await RunAsync(async () =>
        {
            var organizationsTask = api.GetOrganizationsAsync(cancellationToken);
            var usersTask = api.GetUsersAsync(string.IsNullOrWhiteSpace(UserSearch) ? null : UserSearch.Trim(), UsersPage, PageSize, cancellationToken);

            Organizations.Clear();
            foreach (var organization in await organizationsTask)
            {
                Organizations.Add(organization);
            }

            var users = await usersTask;
            Users.Clear();
            foreach (var user in users.Items)
            {
                Users.Add(user);
            }

            UsersTotal = users.TotalCount;
            UsersTotalPages = users.TotalPages;
        }, connection, logger);
    }

    [RelayCommand]
    private async Task CreateOrganizationAsync()
    {
        var dialog = new CreateOrganizationDialogViewModel(api);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
            await shell.ReloadOrganizationsAsync(dialog.Result?.Id);
        }
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        var dialog = new CreateUserDialogViewModel(api, organizationId: null, canAssignOwner: false, allowServerAdministrator: true);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleUserActiveAsync(UserDto? user)
    {
        if (user is null)
        {
            return;
        }

        var question = user.IsActive
            ? $"Заблокировать пользователя «{user.DisplayName}» ({user.Login})? Все его сессии будут завершены."
            : $"Разблокировать пользователя «{user.DisplayName}» ({user.Login})?";
        if (!dialogs.Confirm(question, "Пользователи сервера"))
        {
            return;
        }

        if (await RunAsync(() => api.UpdateUserAsync(user.Id, new UpdateUserRequest(user.DisplayName, user.Email, !user.IsActive)), connection, logger))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task SearchUsersAsync()
    {
        UsersPage = 1;
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextUsersPageAsync()
    {
        UsersPage++;
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousUsersPageAsync()
    {
        UsersPage--;
        return LoadAsync();
    }

    private bool CanGoNext() => UsersPage < UsersTotalPages;

    private bool CanGoPrevious() => UsersPage > 1;
}
