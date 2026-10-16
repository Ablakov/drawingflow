using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

/// <summary>Добавление участника организации по логину или изменение его роли.</summary>
public sealed partial class OrganizationMemberDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly Guid _organizationId;
    private readonly OrganizationMemberDto? _existing;

    public OrganizationMemberDialogViewModel(DrawingFlowApiClient api, Guid organizationId, OrganizationMemberDto? existing, bool canAssignOwner)
    {
        _api = api;
        _organizationId = organizationId;
        _existing = existing;
        Roles = RoleOptions.Organization(canAssignOwner);
        _login = existing?.Login ?? string.Empty;
        _selectedRole = Roles.FirstOrDefault(r => r.Value == (existing?.Role ?? OrganizationRole.Member)) ?? Roles[0];
    }

    public override string Title => IsEdit ? "Роль участника организации" : "Добавить участника организации";

    public override string ConfirmText => IsEdit ? "Сохранить" : "Добавить";

    public bool IsEdit => _existing is not null;

    public bool IsCreate => !IsEdit;

    public string MemberDisplayName => _existing is null ? string.Empty : $"{_existing.DisplayName} ({_existing.Login})";

    public IReadOnlyList<RoleOption<OrganizationRole>> Roles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _login;

    [ObservableProperty]
    private RoleOption<OrganizationRole> _selectedRole;

    protected override bool CanConfirm() => base.CanConfirm() && (IsEdit || !string.IsNullOrWhiteSpace(Login));

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        if (IsEdit)
        {
            await _api.UpdateOrganizationMemberAsync(_organizationId, _existing!.UserId, new UpdateOrganizationMemberRequest(SelectedRole.Value));
        }
        else
        {
            await _api.AddOrganizationMemberAsync(_organizationId, new AddOrganizationMemberRequest(Login.Trim(), SelectedRole.Value));
        }
    });
}
