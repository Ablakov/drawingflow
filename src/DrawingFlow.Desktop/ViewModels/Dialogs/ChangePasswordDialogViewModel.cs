using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class ChangePasswordDialogViewModel(DrawingFlowApiClient api, bool required = false) : DialogViewModelBase
{
    public override string Title => required ? "Требуется смена временного пароля" : "Смена пароля";

    public override string ConfirmText => "Сменить пароль";

    /// <summary>При обязательной смене отмена недоступна: закрытие окна означает выход из системы.</summary>
    public override bool ShowCancel => !required;

    public bool IsRequired => required;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _newPasswordConfirmation = string.Empty;

    protected override bool CanConfirm() => base.CanConfirm()
        && !string.IsNullOrEmpty(CurrentPassword)
        && !string.IsNullOrEmpty(NewPassword)
        && !string.IsNullOrEmpty(NewPasswordConfirmation);

    protected override async Task<bool> OnConfirmAsync()
    {
        if (NewPassword != NewPasswordConfirmation)
        {
            ErrorMessage = "Новые пароли не совпадают.";
            return false;
        }

        if (NewPassword.Length < 8 || !NewPassword.Any(char.IsLetter) || !NewPassword.Any(char.IsDigit))
        {
            ErrorMessage = "Пароль должен содержать не менее 8 символов, включая буквы и цифры.";
            return false;
        }

        return await RunAsync(() => api.ChangePasswordAsync(CurrentPassword, NewPassword));
    }
}
