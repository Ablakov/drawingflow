using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;

namespace DrawingFlow.Desktop.ViewModels;

public sealed partial class SettingsViewModel : PageViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly ShellViewModel _shell;
    private readonly ShellCoordinator _coordinator;
    private readonly DesktopSettingsStore _settings;
    private readonly ISessionStore _sessionStore;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(
        DrawingFlowApiClient api,
        ShellViewModel shell,
        ShellCoordinator coordinator,
        DesktopSettingsStore settings,
        ISessionStore sessionStore,
        IDialogService dialogs)
    {
        _api = api;
        _shell = shell;
        _coordinator = coordinator;
        _settings = settings;
        _sessionStore = sessionStore;
        _dialogs = dialogs;
        _rememberSession = settings.Current.RememberSession;
    }

    public override string Title => "Настройки";

    public string ServerAddress => _settings.Current.ServerAddress ?? string.Empty;

    public string UserDisplayName => _shell.CurrentUser?.DisplayName ?? string.Empty;

    public string UserLogin => _shell.CurrentUser?.Login ?? string.Empty;

    public string AppVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";

    public string SettingsPath => DesktopSettingsStore.FilePath;

    public string LogDirectory => App.LogDirectory;

    [ObservableProperty]
    private bool _rememberSession;

    public override Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    partial void OnRememberSessionChanged(bool value)
    {
        _settings.Update(s => s.RememberSession = value);
        if (!value)
        {
            // Сессия остаётся активной в памяти, но сохранённая копия удаляется.
            _ = _sessionStore.ClearAsync();
        }
        else if (_api.Sessions.Current is { } session)
        {
            _ = _sessionStore.SaveAsync(session);
        }
    }

    [RelayCommand]
    private void ChangePassword()
    {
        var dialog = new ChangePasswordDialogViewModel(_api);
        if (_dialogs.ShowDialog(dialog))
        {
            _dialogs.ShowInfo("Пароль изменён. Другие сеансы этой учётной записи завершены.", "Смена пароля");
        }
    }

    [RelayCommand]
    private Task LogoutAsync() => _coordinator.LogoutAsync();

    [RelayCommand]
    private async Task ChangeServerAsync()
    {
        if (_dialogs.Confirm("Для смены сервера необходимо выйти из текущей учётной записи. Продолжить?", "Смена сервера"))
        {
            await _coordinator.LogoutAsync();
        }
    }
}
