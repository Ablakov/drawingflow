using System.IO;
using System.Windows;
using System.Windows.Threading;
using PanelTracker.App.Services;
using PanelTracker.App.ViewModels;
using PanelTracker.App.Views;
using PanelTracker.Core.Models;

namespace PanelTracker.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsStore.FileName);
        TrackerSettings settings;
        try
        {
            settings = SettingsStore.LoadOrCreate(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show($"Не удалось прочитать {settingsPath}:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Будут использованы настройки по умолчанию.",
                "Panel Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            settings = TrackerSettings.CreateDefault();
        }

        _viewModel = new MainViewModel(settings, settingsPath, new WpfUserInteraction(), Dispatcher);
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
        _viewModel.OpenInitialProject();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Непредвиденная ошибка:{Environment.NewLine}{e.Exception.Message}", "Panel Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
