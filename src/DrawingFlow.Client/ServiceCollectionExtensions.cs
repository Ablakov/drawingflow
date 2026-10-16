using Microsoft.Extensions.DependencyInjection;

namespace DrawingFlow.Client;

public sealed class ApiClientOptions
{
    public const string SectionName = "Server";

    /// <summary>Базовый адрес сервера, например https://drawingflow.company.local/.</summary>
    public Uri? BaseAddress { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    public static bool IsAllowedAddress(Uri address, out string? warning)
    {
        warning = null;
        if (address.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        if (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)
        {
            warning = "Соединение без TLS допускается только с локальным сервером разработки.";
            return true;
        }

        warning = "Используйте адрес сервера с протоколом https://. Передача учётных данных по незащищённому соединению запрещена.";
        return false;
    }
}

/// <summary>Источник базового адреса сервера. По умолчанию — <see cref="ApiClientOptions"/>; Desktop подставляет изменяемые настройки.</summary>
public interface IServerAddressProvider
{
    Uri? BaseAddress { get; }
}

internal sealed class OptionsServerAddressProvider(Microsoft.Extensions.Options.IOptionsMonitor<ApiClientOptions> options) : IServerAddressProvider
{
    public Uri? BaseAddress => options.CurrentValue.BaseAddress;
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует клиент API. Базовый адрес берётся из <see cref="ApiClientOptions"/>, которые приложение может менять
    /// во время работы (например, в окне входа): адрес подставляется при создании каждого HttpClient.
    /// </summary>
    public static IServiceCollection AddDrawingFlowApiClient(this IServiceCollection services, Action<ApiClientOptions>? configure = null)
    {
        services.AddOptions<ApiClientOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton(DrawingFlowApiClient.CreateJsonOptions());
        if (!services.Any(d => d.ServiceType == typeof(IServerAddressProvider)))
        {
            services.AddSingleton<IServerAddressProvider, OptionsServerAddressProvider>();
        }

        services.TryAddSingletonSessionStore();
        services.AddSingleton<SessionManager>();
        services.AddTransient<AuthenticatedHttpHandler>();
        services.AddHttpClient(nameof(DrawingFlowApiClient), (sp, http) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ApiClientOptions>>().CurrentValue;
                http.BaseAddress = sp.GetRequiredService<IServerAddressProvider>().BaseAddress ?? options.BaseAddress;
                // Тайм-аут обычных запросов задаёт сам клиент; передача файлов не ограничивается по времени.
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.UserAgent.ParseAdd("DrawingFlow.Client/0.1");
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddTypedClient((http, sp) => new DrawingFlowApiClient(http, sp.GetRequiredService<SessionManager>())
            {
                RequestTimeout = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ApiClientOptions>>().CurrentValue.Timeout,
            });
        return services;
    }

    private static void TryAddSingletonSessionStore(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(ISessionStore)))
        {
            services.AddSingleton<ISessionStore, InMemorySessionStore>();
        }
    }
}
