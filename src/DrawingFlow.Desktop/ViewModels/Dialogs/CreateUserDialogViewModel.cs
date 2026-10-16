using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class CreateUserDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly Guid? _organizationId;

    public CreateUserDialogViewModel(DrawingFlowApiClient api, Guid? organizationId, bool canAssignOwner, bool allowServerAdministrator)
    {
        _api = api;
        _organizationId = organizationId;
        AllowServerAdministrator = allowServerAdministrator;
        Roles = RoleOptions.Organization(canAssignOwner);
        _selectedRole = Roles[0];
    }

    public override string Title => "Новый пользователь";

    public override string ConfirmText => "Создать";

    public bool HasOrganization => _organizationId is not null;

    public bool AllowServerAdministrator { get; }

    public UserDto? Result { get; private set; }

    public IReadOnlyList<RoleOption<OrganizationRole>> Roles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _login = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _passwordConfirmation = string.Empty;

    [ObservableProperty]
    private RoleOption<OrganizationRole> _selectedRole;

    [ObservableProperty]
    private bool _isServerAdministrator;

    [ObservableProperty]
    private bool _requirePasswordChange = true;

    protected override bool CanConfirm() => base.CanConfirm()
        && !string.IsNullOrWhiteSpace(Login)
        && !string.IsNullOrWhiteSpace(DisplayName)
        && !string.IsNullOrEmpty(Password)
        && !string.IsNullOrEmpty(PasswordConfirmation);

    protected override async Task<bool> OnConfirmAsync()
    {
        if (Password != PasswordConfirmation)
        {
            ErrorMessage = "Пароли не совпадают.";
            return false;
        }

        if (Password.Length < 8 || !Password.Any(char.IsLetter) || !Password.Any(char.IsDigit))
        {
            ErrorMessage = "Пароль должен содержать не менее 8 символов, включая буквы и цифры.";
            return false;
        }

        return await RunAsync(async () =>
        {
            Result = await _api.CreateUserAsync(new CreateUserRequest(
                Login.Trim(),
                DisplayName.Trim(),
                string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
                Password,
                _organizationId,
                _organizationId is null ? null : SelectedRole.Value,
                AllowServerAdministrator && IsServerAdministrator,
                RequirePasswordChange));
        });
    }
}
