using CommunityToolkit.Mvvm.ComponentModel;

namespace DrawingFlow.Desktop.Services;

/// <summary>Состояние связи с сервером для строки состояния. Обновляется страницами после вызовов API.</summary>
public sealed partial class ConnectionMonitor : ObservableObject
{
    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _statusText = "Подключение…";

    [ObservableProperty]
    private DateTime? _lastSuccessUtc;

    public void ReportSuccess()
    {
        LastSuccessUtc = DateTime.UtcNow;
        IsOnline = true;
        StatusText = "Соединение с сервером установлено";
    }

    public void ReportFailure(string message)
    {
        IsOnline = false;
        StatusText = message;
    }
}
