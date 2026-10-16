using CommunityToolkit.Mvvm.Input;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

/// <summary>Базовая модель модального диалога: заголовок, кнопки «ОК»/«Отмена», запрос закрытия.</summary>
public abstract partial class DialogViewModelBase : ViewModelBase
{
    public abstract string Title { get; }

    public virtual string ConfirmText => "Сохранить";

    public virtual double Width => 480;

    public virtual bool ShowCancel => true;

    public event Action<bool>? CloseRequested;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (await OnConfirmAsync())
        {
            CloseRequested?.Invoke(true);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    protected virtual bool CanConfirm() => !IsBusy;

    /// <summary>Выполняет действие диалога; true — закрыть диалог с подтверждением.</summary>
    protected abstract Task<bool> OnConfirmAsync();

    protected override void OnBusyChanged() => ConfirmCommand.NotifyCanExecuteChanged();
}
