using DrawingFlow.Agent;
using DrawingFlow.Agent.Queue;
using DrawingFlow.Agent.Sync;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrawingFlow.IntegrationTests.Infrastructure;

public sealed class FakeAgentClock : IAgentClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}

/// <summary>Переключаемый обрыв сети между агентом и сервером.</summary>
public sealed class NetworkToggleHandler : DelegatingHandler
{
    public bool Offline { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Offline ? throw new HttpRequestException("сеть недоступна (имитация)") : base.SendAsync(request, cancellationToken);
}

public sealed class SqliteContextFactory(string path) : IDbContextFactory<AgentQueueDbContext>
{
    public AgentQueueDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AgentQueueDbContext>().UseSqlite($"Data Source={path}").Options);
}

/// <summary>
/// Настоящее ядро агента (очередь SQLite во временном каталоге, папка наблюдения во временном каталоге),
/// подключённое к тестовому серверу через обработчик токена агента. Таймеры заменены явными вызовами и поддельными часами.
/// </summary>
public sealed class AgentHarness : IAsyncDisposable
{
    private AgentHarness() { }

    public required string Root { get; init; }
    public required string WatchedPath { get; init; }
    public required string QueuePath { get; init; }
    public required AgentRegistrationResultDto Registration { get; init; }
    public required WatchedFolderDto Folder { get; init; }
    public required AgentOptions Options { get; init; }
    public required FakeAgentClock Clock { get; init; }
    public NetworkToggleHandler Network { get; private set; } = new();
    public AgentTokenHandler Auth { get; private set; } = null!;
    public DrawingFlowApiClient Api { get; private set; } = null!;
    public SyncQueue Queue { get; private set; } = null!;
    public SyncEngine Engine { get; private set; } = null!;

    public static async Task<AgentHarness> CreateAsync(ApiFactory factory, Scenario scenario, bool autoRegister = false, string includePatterns = "*.dwg;*.txt;*.pdf", string? numberPattern = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "drawingflow-agent-tests", Guid.NewGuid().ToString("N"));
        var watched = Path.Combine(root, "watched");
        Directory.CreateDirectory(watched);

        var registration = await scenario.OrgAdmin.RegisterAgentAsync(scenario.Organization.Id, new RegisterAgentRequest("Тестовый агент " + scenario.Suffix));
        var folder = await scenario.Engineer.CreateWatchedFolderAsync(registration.Agent.Id,
            new SaveWatchedFolderRequest(scenario.Project.Id, watched, includePatterns, "~$*", FileMatchingRule.NumberFromFileName, numberPattern, autoRegister, true));

        var harness = new AgentHarness
        {
            Root = root,
            WatchedPath = watched,
            QueuePath = Path.Combine(root, "queue.db"),
            Registration = registration,
            Folder = folder,
            Options = new AgentOptions
            {
                DataDirectory = root,
                StabilityWindow = TimeSpan.FromSeconds(1),
                InitialRetryDelay = TimeSpan.FromSeconds(1),
                MaxRetryDelay = TimeSpan.FromSeconds(8),
                MaxRetries = 3,
                DebounceDelay = TimeSpan.Zero,
            },
            Clock = new FakeAgentClock(),
        };
        harness.Bind(factory);
        await harness.RestartAsync();
        return harness;
    }

    /// <summary>Переподключает агента к другому хосту (перезапуск сервера) и, как служба после старта, перечитывает конфигурацию.</summary>
    public async Task BindAsync(ApiFactory factory)
    {
        Bind(factory);
        await Engine.RefreshConfigurationAsync(CancellationToken.None);
    }

    private void Bind(ApiFactory factory)
    {
        var json = DrawingFlowApiClient.CreateJsonOptions();
        // Обработчики нельзя переиспользовать после первого запроса: создаём новые, сохраняя признак «сеть отключена».
        Network = new NetworkToggleHandler { Offline = Network.Offline };
        Auth = new AgentTokenHandler(() => new AgentTokenRequest(Registration.AgentKey, "TEST-PC", "test"), json);
        var http = factory.CreateDefaultClient(Auth, Network);
        Api = new DrawingFlowApiClient(http, new SessionManager(new InMemorySessionStore()));
        if (Engine is not null)
        {
            Engine = new SyncEngine(Api, Auth, Queue, Options, Clock, NullLogger<SyncEngine>.Instance);
        }
    }

    /// <summary>Имитация перезапуска службы: новые экземпляры очереди и ядра над той же базой SQLite.</summary>
    public async Task RestartAsync()
    {
        Queue = new SyncQueue(new SqliteContextFactory(QueuePath), NullLogger<SyncQueue>.Instance);
        await Queue.InitializeAsync(CancellationToken.None);
        await Queue.RecoverInFlightAsync(Clock.UtcNow, CancellationToken.None);
        Engine = new SyncEngine(Api, Auth, Queue, Options, Clock, NullLogger<SyncEngine>.Instance);
        await Engine.RefreshConfigurationAsync(CancellationToken.None);
    }

    public string WriteFile(string name, string content)
    {
        var path = Path.Combine(WatchedPath, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Крутит конвейер, пока есть активные операции (или исчерпан лимит циклов), продвигая часы между циклами.</summary>
    public async Task<(int Pending, int Failed, int NeedsResolution)> RunUntilSettledAsync(int maxCycles = 12)
    {
        (int Pending, int Failed, int NeedsResolution) counts = default;
        for (var i = 0; i < maxCycles; i++)
        {
            await Engine.ProcessDueAsync(CancellationToken.None);
            counts = await Queue.CountsAsync(CancellationToken.None);
            if (counts.Pending == 0)
            {
                break;
            }

            Clock.Advance(Options.StabilityWindow + Options.MaxRetryDelay + TimeSpan.FromSeconds(1));
        }

        return counts;
    }

    public async Task<LocalSyncOperation?> SingleOperationAsync()
    {
        var operations = await Queue.ListAsync(CancellationToken.None);
        return operations.Count == 1 ? operations[0] : null;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
