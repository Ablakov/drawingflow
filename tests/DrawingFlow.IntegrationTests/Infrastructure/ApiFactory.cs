using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using DrawingFlow.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.IntegrationTests.Infrastructure;

/// <summary>Захват журнала сервера для проверки отсутствия секретов.</summary>
public sealed class CaptureLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(sink, categoryName);

    public void Dispose() { }

    private sealed class CaptureLogger(ConcurrentQueue<string> sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)} {exception}");
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminLogin = "admin";
    public const string AdminPassword = "Admin12345";

    /// <summary>Лимит размера файла по умолчанию в тестах — 1 МиБ, чтобы проверка превышения выполнялась быстро.</summary>
    public const long MaxFileSizeBytes = 1024 * 1024;

    public static readonly JsonSerializerOptions Json = DrawingFlowApiClient.CreateJsonOptions();

    private readonly bool _ownsResources;

    public ApiFactory()
    {
        Database = new PostgresTestDatabase();
        StorageRoot = Path.Combine(Path.GetTempPath(), "drawingflow-tests", Guid.NewGuid().ToString("N"));
        _ownsResources = true;
    }

    /// <summary>«Перезапущенный сервер»: новый хост над той же базой и тем же хранилищем. База и каталог принадлежат исходной фабрике.
    /// Конструктор не публичный: xUnit допускает у фикстуры класса только один публичный конструктор.</summary>
    internal ApiFactory(ApiFactory original)
    {
        Database = original.Database;
        StorageRoot = original.StorageRoot;
        MaxFileSize = original.MaxFileSize;
        _ownsResources = false;
    }

    public PostgresTestDatabase Database { get; }

    public string StorageRoot { get; }

    public long MaxFileSize { get; init; } = MaxFileSizeBytes;

    public int LoginRateLimitPerMinute { get; init; } = 100_000;

    public bool ReverseProxyEnabled { get; init; }

    public ConcurrentQueue<string> Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DrawingFlow"] = Database.ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
                ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
                ["Jwt:AccessTokenLifetimeMinutes"] = "60",
                ["Jwt:RefreshTokenLifetimeDays"] = "7",
                ["Bootstrap:Administrator:Login"] = AdminLogin,
                ["Bootstrap:Administrator:Password"] = AdminPassword,
                ["Bootstrap:Administrator:DisplayName"] = "Администратор тестов",
                ["Bootstrap:Administrator:MustChangePassword"] = "false",
                ["RateLimits:LoginPerMinute"] = LoginRateLimitPerMinute.ToString(),
                ["Storage:RootPath"] = StorageRoot,
                ["Storage:MaxFileSizeBytes"] = MaxFileSize.ToString(),
                ["ReverseProxy:Enabled"] = ReverseProxyEnabled ? "true" : "false",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:DrawingFlow"] = "Information",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Error",
            });
        });
        builder.ConfigureLogging(logging => logging.AddProvider(new CaptureLoggerProvider(Logs)));
    }

    /// <summary>Типизированный клиент с настоящим обработчиком токенов, направленный в TestServer.</summary>
    public DrawingFlowApiClient CreateApiClient()
    {
        var sessions = new SessionManager(new InMemorySessionStore());
        var http = CreateDefaultClient(new AuthenticatedHttpHandler(sessions, Json));
        return new DrawingFlowApiClient(http, sessions);
    }

    public async Task<DrawingFlowApiClient> LoginAsync(string login, string password)
    {
        var client = CreateApiClient();
        await client.LoginAsync(login, password, "integration-tests");
        return client;
    }

    public Task<DrawingFlowApiClient> LoginAdminAsync() => LoginAsync(AdminLogin, AdminPassword);

    /// <summary>«Сырой» HttpClient с токеном для проверки кодов ответа и тела ошибок.</summary>
    public HttpClient CreateRawClient(string? accessToken = null)
    {
        var client = CreateClient();
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    Task IAsyncLifetime.InitializeAsync() => _ownsResources ? Database.InitializeAsync() : Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (!_ownsResources)
        {
            return;
        }

        await Database.DisposeAsync();
        try
        {
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог; остатки не влияют на тесты.
        }
    }
}
