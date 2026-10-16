using System.Windows;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Desktop.ViewModels;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using DrawingFlow.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.Services;

/// <summary>Управляет окнами приложения: вход → главное окно → выход/истечение сессии → вход.</summary>
public sealed class ShellCoordinator(
    IServiceProvider services,
    SessionManager sessions,
    DesktopSettingsStore settings,
    ILogger<ShellCoordinator> logger)
{
    private LoginWindow? _loginWindow;
    private MainWindow? _mainWindow;
    private bool _switching;

    public async Task StartAsync()
    {
        sessions.Expired += OnSessionExpired;

        string? message = null;
        if (sessions.Current is not null && settings.BaseAddress is not null)
        {
            try
            {
                var api = services.GetRequiredService<DrawingFlowApiClient>();
                var me = await api.GetCurrentUserAsync();
                await ShowMainAsync(me);
                return;
            }
            catch (ApiException ex) when (ex.IsUnauthorized)
            {
                await sessions.ClearAsync();
                message = "Сохранённая сессия недействительна. Выполните вход.";
            }
            catch (ApiConnectionException ex)
            {
                message = ex.Message;
            }
            catch (ApiException ex)
            {
                logger.LogWarning(ex, "Не удалось восстановить сессию");
                message = ex.Message;
            }
        }

        ShowLogin(message);
    }

    public void ShowLogin(string? message)
    {
        var viewModel = services.GetRequiredService<LoginViewModel>();
        viewModel.StatusMessage = message;
        viewModel.LoggedIn += OnLoggedIn;

        _loginWindow = new LoginWindow { DataContext = viewModel };
        _loginWindow.Closed += OnWindowClosed;
        Application.Current.MainWindow = _loginWindow;
        _loginWindow.Show();
        _loginWindow.Activate();
    }

    public async Task LogoutAsync()
    {
        try
        {
            var api = services.GetRequiredService<DrawingFlowApiClient>();
            await api.LogoutAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при выходе; локальная сессия очищена");
            await sessions.ClearAsync();
        }

        SwitchToLogin(null);
    }

    private async void OnLoggedIn(object? sender, CurrentUserDto me)
    {
        try
        {
            await ShowMainAsync(me);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось открыть главное окно");
            MessageBox.Show(ex.Message, "DrawingFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ShowMainAsync(CurrentUserDto me)
    {
        if (me.User.MustChangePassword)
        {
            var changed = await EnsurePasswordChangedAsync();
            if (changed is null)
            {
                await LogoutAsync();
                return;
            }

            me = changed;
        }

        var shell = services.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync(me);

        _switching = true;
        try
        {
            _mainWindow = new MainWindow { DataContext = shell };
            _mainWindow.Closed += OnWindowClosed;
            Application.Current.MainWindow = _mainWindow;
            _mainWindow.Show();

            if (_loginWindow is not null)
            {
                var login = _loginWindow;
                _loginWindow = null;
                login.Closed -= OnWindowClosed;
                login.Close();
            }
        }
        finally
        {
            _switching = false;
        }
    }

    /// <summary>Временный пароль: смена обязательна, отказ означает выход. Возвращает обновлённый профиль или null.</summary>
    private async Task<CurrentUserDto?> EnsurePasswordChangedAsync()
    {
        var dialogs = services.GetRequiredService<IDialogService>();
        var api = services.GetRequiredService<DrawingFlowApiClient>();
        dialogs.ShowInfo("Вы вошли с временным паролем. Для продолжения работы необходимо задать новый пароль.", "Смена пароля");
        var dialog = new ChangePasswordDialogViewModel(api, required: true);
        if (!dialogs.ShowDialog(dialog))
        {
            return null;
        }

        try
        {
            return await api.GetCurrentUserAsync();
        }
        catch (Exception ex) when (ex is ApiException or ApiConnectionException)
        {
            logger.LogWarning(ex, "Не удалось перечитать профиль после смены пароля");
            return null;
        }
    }

    private void SwitchToLogin(string? message)
    {
        _switching = true;
        try
        {
            ShowLogin(message);
            if (_mainWindow is not null)
            {
                var main = _mainWindow;
                _mainWindow = null;
                main.Closed -= OnWindowClosed;
                main.Close();
            }
        }
        finally
        {
            _switching = false;
        }
    }

    private void OnSessionExpired(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_mainWindow is not null)
            {
                SwitchToLogin("Сессия истекла. Выполните вход заново.");
            }
        });
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!_switching)
        {
            Application.Current.Shutdown();
        }
    }
}
