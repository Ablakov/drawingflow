using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool IsIdle => !IsBusy;

    /// <summary>Вызывается при изменении <see cref="IsBusy"/>; наследники обновляют доступность команд.</summary>
    protected virtual void OnBusyChanged() { }

    partial void OnIsBusyChanged(bool value) => OnBusyChanged();

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Выполняет операцию с индикацией занятости и единообразной обработкой ошибок API.
    /// Возвращает true при успехе. Ошибки аутентификации обрабатывает оболочка (событие истечения сессии).
    /// </summary>
    protected async Task<bool> RunAsync(Func<Task> action, ConnectionMonitor? monitor = null, ILogger? logger = null, bool clearError = true)
    {
        if (clearError)
        {
            ErrorMessage = null;
        }

        IsBusy = true;
        try
        {
            await action();
            monitor?.ReportSuccess();
            return true;
        }
        catch (ApiConnectionException ex)
        {
            ErrorMessage = ex.Message;
            monitor?.ReportFailure("Нет соединения с сервером");
            return false;
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            ErrorMessage = ex.Message;
            monitor?.ReportSuccess();
            return false;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            monitor?.ReportSuccess();
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Необработанная ошибка в {ViewModel}", GetType().Name);
            ErrorMessage = "Произошла непредвиденная ошибка. Подробности записаны в журнал приложения.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Страница оболочки: загружается при навигации, умеет обновляться.</summary>
public abstract partial class PageViewModelBase : ViewModelBase
{
    public abstract string Title { get; }

    public abstract Task LoadAsync(CancellationToken cancellationToken = default);

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();
}
