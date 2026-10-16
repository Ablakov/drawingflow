using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Organizations;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class CreateOrganizationDialogViewModel(DrawingFlowApiClient api) : DialogViewModelBase
{
    public override string Title => "Новая организация";

    public override string ConfirmText => "Создать";

    public OrganizationDto? Result { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _ownerLogin = string.Empty;

    protected override bool CanConfirm() => base.CanConfirm() && !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(Name);

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        Result = await api.CreateOrganizationAsync(new CreateOrganizationRequest(
            Code.Trim(),
            Name.Trim(),
            string.IsNullOrWhiteSpace(OwnerLogin) ? null : OwnerLogin.Trim()));
    });
}
