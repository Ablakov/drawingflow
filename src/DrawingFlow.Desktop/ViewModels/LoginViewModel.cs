using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly DesktopSettingsStore _settings;
    private readonly ILogger<LoginViewModel> _logger;

    public LoginViewModel(IServiceProvider services, DesktopSettingsStore settings, ILogger<LoginViewModel> logger)
    {
        _services = services;
        _settings = settings;
        _logger = logger;
        _serverAddress = settings.Current.ServerAddress ?? string.Empty;
        _login = settings.Current.LastLogin ?? string.Empty;
        _rememberSession = settings.Current.RememberSession;
    }

    public event EventHandler<CurrentUserDto>? LoggedIn;

    public string AppVersion => "Версия " + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckServerCommand))]
    private string _serverAddress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _login;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberSession;

    /// <summary>Информационное сообщение (не ошибка): причина показа окна, результат проверки сервера.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    private bool CanLogin() => !IsBusy
        && !string.IsNullOrWhiteSpace(ServerAddress)
        && !string.IsNullOrWhiteSpace(Login)
        && !string.IsNullOrEmpty(Password);

    private bool CanCheckServer() => !IsBusy && !string.IsNullOrWhiteSpace(ServerAddress);

    protected override void OnBusyChanged()
    {
        LoginCommand.NotifyCanExecuteChanged();
        CheckServerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCheckServer))]
    private async Task CheckServerAsync()
    {
        if (!TryApplyServerAddress(out _))
        {
            return;
        }

        StatusMessage = null;
        await RunAsync(async () =>
        {
            var api = _services.GetRequiredService<DrawingFlowApiClient>();
            var info = await api.GetServerInfoAsync();
            StatusMessage = $"Сервер доступен: {info.ProductName} {info.Version}";
        }, logger: _logger);
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        if (!TryApplyServerAddress(out _))
        {
            return;
        }

        StatusMessage = null;
        var login = Login.Trim();
        _settings.Update(s =>
        {
            s.RememberSession = RememberSession;
            s.LastLogin = login;
        });

        CurrentUserDto? me = null;
        var ok = await RunAsync(async () =>
        {
            var api = _services.GetRequiredService<DrawingFlowApiClient>();
            await api.LoginAsync(login, Password, $"DrawingFlow Desktop, {Environment.MachineName}");
            me = await api.GetCurrentUserAsync();
        }, logger: _logger);

        if (ok && me is not null)
        {
            Password = string.Empty;
            LoggedIn?.Invoke(this, me);
        }
    }

    private bool TryApplyServerAddress(out Uri? address)
    {
        address = null;
        var text = ServerAddress.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = "Укажите корректный адрес сервера, например https://drawingflow.company.local";
            return false;
        }

        if (!ApiClientOptions.IsAllowedAddress(uri, out var warning))
        {
            ErrorMessage = warning;
            return false;
        }

        address = uri;
        ServerAddress = uri.ToString();
        _settings.Update(s => s.ServerAddress = uri.ToString());
        return true;
    }
}
