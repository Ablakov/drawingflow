using System.Text.Json;
using DrawingFlow.Agent;
using DrawingFlow.Agent.Hosting;
using DrawingFlow.Agent.Queue;
using DrawingFlow.Agent.Security;
using DrawingFlow.Agent.Sync;
using DrawingFlow.Agent.Watching;
using DrawingFlow.Client;
using DrawingFlow.Client.Logging;
using DrawingFlow.Contracts.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(serviceOptions => serviceOptions.ServiceName = "DrawingFlow Agent");

var options = new AgentOptions();
builder.Configuration.GetSection(AgentOptions.SectionName).Bind(options);
options.Validate();
Directory.CreateDirectory(options.ResolvedDataDirectory);

var credentialStore = new AgentCredentialStore(options.CredentialPath, NullLogger<AgentCredentialStore>.Instance);

// ---------- Режим командной строки: register / status ----------
if (args.Length > 0 && string.Equals(args[0], "register", StringComparison.OrdinalIgnoreCase))
{
    return await RegisterAsync(args, credentialStore);
}

if (args.Length > 0 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
{
    return await StatusAsync(options, credentialStore);
}

var credential = credentialStore.Load();
if (credential is null)
{
    Console.Error.WriteLine("Агент не зарегистрирован. Выполните: DrawingFlow.Agent register --server https://сервер --key <ключ агента>");
    return 2;
}

// ---------- Журналирование: журнал событий Windows (служба) и файл в каталоге данных ----------
if (OperatingSystem.IsWindows())
{
    ConfigureWindowsEventLog(builder.Logging);
}

builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(options.ResolvedDataDirectory, "logs"), "agent"));

// ---------- Служба ----------
var json = DrawingFlowApiClient.CreateJsonOptions();
var tokenHandler = new AgentTokenHandler(
    () => new AgentTokenRequest(credential.AgentKey, Environment.MachineName, AgentOptions.AgentVersion), json)
{
    InnerHandler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) },
};
var httpClient = new HttpClient(tokenHandler) { BaseAddress = credential.ServerAddress, Timeout = Timeout.InfiniteTimeSpan };
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DrawingFlow.Agent/" + AgentOptions.AgentVersion);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(tokenHandler);
builder.Services.AddSingleton(new DrawingFlowApiClient(httpClient, new SessionManager(new InMemorySessionStore())));
builder.Services.AddSingleton<IAgentClock, SystemAgentClock>();
builder.Services.AddDbContextFactory<AgentQueueDbContext>(db => db.UseSqlite($"Data Source={options.QueueDatabasePath}"));
builder.Services.AddSingleton<SyncQueue>();
builder.Services.AddSingleton<SyncEngine>();
builder.Services.AddSingleton<FolderWatcherHost>();
builder.Services.AddHostedService<AgentHostedService>();

var host = builder.Build();
await host.RunAsync();
return 0;

// ---------- Команды ----------

// Вызывается только после проверки OperatingSystem.IsWindows(); анализатор не отслеживает защиту внутри лямбды настроек.
#pragma warning disable CA1416
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static void ConfigureWindowsEventLog(ILoggingBuilder logging) =>
    logging.AddEventLog(settings =>
    {
        settings.SourceName = "DrawingFlow Agent";
        settings.Filter = (_, level) => level >= LogLevel.Warning;
    });
#pragma warning restore CA1416

static async Task<int> RegisterAsync(string[] args, AgentCredentialStore store)
{
    string? server = null, key = null;
    for (var i = 1; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--server": server = args[++i]; break;
            case "--key": key = args[++i]; break;
        }
    }

    if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("Использование: DrawingFlow.Agent register --server https://сервер --key <ключ агента>");
        return 2;
    }

    if (!server.EndsWith('/'))
    {
        server += "/";
    }

    if (!Uri.TryCreate(server, UriKind.Absolute, out var address))
    {
        Console.Error.WriteLine("Некорректный адрес сервера.");
        return 2;
    }

    if (!ApiClientOptions.IsAllowedAddress(address, out var warning))
    {
        Console.Error.WriteLine(warning);
        return 2;
    }

    using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
    var client = new DrawingFlowApiClient(http, new SessionManager(new InMemorySessionStore()));
    try
    {
        var token = await client.ExchangeAgentKeyAsync(new AgentTokenRequest(key.Trim(), Environment.MachineName, AgentOptions.AgentVersion));
        store.Save(new AgentCredential(address, key.Trim(), DateTime.UtcNow));
        Console.WriteLine($"Агент «{token.AgentName}» зарегистрирован на {address}. Ключ сохранён: {store.Path}");
        return 0;
    }
    catch (ApiException ex)
    {
        Console.Error.WriteLine("Сервер отклонил ключ агента: " + ex.Message);
        return 1;
    }
    catch (ApiConnectionException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task<int> StatusAsync(AgentOptions options, AgentCredentialStore store)
{
    var credential = store.Load();
    Console.WriteLine(credential is null
        ? "Регистрация: нет (выполните команду register)"
        : $"Регистрация: сервер {credential.ServerAddress}, ключ сохранён {credential.SavedAtUtc:u}");
    Console.WriteLine($"Каталог данных: {options.ResolvedDataDirectory}");
    if (!File.Exists(options.QueueDatabasePath))
    {
        Console.WriteLine("Очередь: база ещё не создана");
        return 0;
    }

    var dbOptions = new DbContextOptionsBuilder<AgentQueueDbContext>().UseSqlite($"Data Source={options.QueueDatabasePath}").Options;
    await using var db = new AgentQueueDbContext(dbOptions);
    var operations = await db.Operations.AsNoTracking().OrderByDescending(o => o.UpdatedAtUtc).Take(20).ToListAsync();
    var counts = await db.Operations.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
    Console.WriteLine("Очередь: " + string.Join(", ", counts.Select(c => $"{c.Key}={c.Count}")));
    foreach (var operation in operations)
    {
        Console.WriteLine($"  {operation.UpdatedAtUtc:u} {operation.Status,-16} {operation.FileName} {operation.LastError}");
    }

    return 0;
}
