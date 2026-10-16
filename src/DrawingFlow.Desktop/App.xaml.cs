using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using System.IO;
using DrawingFlow.Client;
using DrawingFlow.Client.Logging;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        var builder = Host.CreateApplicationBuilder(e.Args);
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(new FileLoggerProvider(LogDirectory, "desktop"));
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        ConfigureServices(builder.Services);

        _host = builder.Build();
        await _host.StartAsync();

        try
        {
            await _host.Services.GetRequiredService<SessionManager>().InitializeAsync();
            await _host.Services.GetRequiredService<ShellCoordinator>().StartAsync();
        }
        catch (Exception ex)
        {
            _host.Services.GetRequiredService<ILogger<App>>().LogCritical(ex, "Ошибка запуска приложения");
            MessageBox.Show($"Не удалось запустить приложение:\n{ex.Message}", "DrawingFlow", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Журнал приложения: %LOCALAPPDATA%\DrawingFlow\logs, один файл в день, хранение 14 дней.</summary>
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrawingFlow", "logs");

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DesktopSettingsStore>();
        services.AddSingleton<IServerAddressProvider>(sp => sp.GetRequiredService<DesktopSettingsStore>());
        services.AddSingleton<ISessionStore, ProtectedFileSessionStore>();
        services.AddDrawingFlowApiClient();

        services.AddSingleton<ConnectionMonitor>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFileLauncher, FileLauncher>();
        services.AddSingleton<LocalFileCache>();
        services.AddSingleton<ShellCoordinator>();
        services.AddSingleton<ShellViewModel>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProjectsViewModel>();
        services.AddTransient<ProjectDetailsViewModel>();
        services.AddTransient<DocumentsViewModel>();
        services.AddTransient<DocumentDetailsViewModel>();
        services.AddTransient<AgentsViewModel>();
        services.AddTransient<SyncQueueViewModel>();
        services.AddTransient<TasksViewModel>();
        services.AddTransient<OrganizationMembersViewModel>();
        services.AddTransient<AuditViewModel>();
        services.AddTransient<AdministrationViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Необработанное исключение в потоке интерфейса");
        MessageBox.Show(
            "Произошла непредвиденная ошибка. Подробности записаны в журнал приложения.\n\n" + e.Exception.Message,
            "DrawingFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
