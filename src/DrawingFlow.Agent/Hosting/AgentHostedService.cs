using DrawingFlow.Agent.Sync;
using DrawingFlow.Agent.Watching;
using DrawingFlow.Client;
using DrawingFlow.Agent.Queue;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Agent.Hosting;

/// <summary>Главный цикл службы: восстановление очереди, конфигурация, наблюдение, обработка, отчёты, сердцебиение, сверка.</summary>
public sealed class AgentHostedService(
    SyncEngine engine,
    SyncQueue queue,
    FolderWatcherHost watchers,
    AgentTokenHandler auth,
    AgentOptions options,
    ILogger<AgentHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Агент DrawingFlow {Version} запускается", AgentOptions.AgentVersion);
        await queue.InitializeAsync(stoppingToken);
        var recovered = await queue.RecoverInFlightAsync(DateTime.UtcNow, stoppingToken);
        if (recovered > 0)
        {
            logger.LogInformation("Возвращено в очередь после перезапуска: {Count} операций", recovered);
        }

        auth.StateChanged += (_, state) =>
        {
            if (state == AgentAuthState.Revoked)
            {
                logger.LogError("Ключ агента отозван сервером. Операции приостановлены; требуется повторная регистрация агента.");
            }
            else if (state == AgentAuthState.Authenticated)
            {
                logger.LogInformation("Агент аутентифицирован на сервере как «{Name}»", auth.AgentName);
            }
        };
        engine.ConfigurationChanged += (_, configuration) => watchers.Apply(configuration.Folders);

        await engine.RefreshConfigurationAsync(stoppingToken);
        if (engine.Configuration is not null)
        {
            watchers.Apply(engine.Configuration.Folders);
        }

        watchers.Start();
        await engine.HeartbeatAsync(stoppingToken);
        await engine.ReconcileAsync(stoppingToken);

        var now = DateTime.UtcNow;
        var nextHeartbeat = now + options.HeartbeatInterval;
        var nextConfiguration = now + options.ConfigurationRefreshInterval;
        var nextReconcile = now + options.ReconcileInterval;
        var nextPurge = now + TimeSpan.FromHours(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await engine.ProcessDueAsync(stoppingToken);
                await engine.ReportAsync(stoppingToken);

                now = DateTime.UtcNow;
                if (now >= nextHeartbeat)
                {
                    await engine.HeartbeatAsync(stoppingToken);
                    nextHeartbeat = now + options.HeartbeatInterval;
                }

                if (now >= nextConfiguration)
                {
                    await engine.RefreshConfigurationAsync(stoppingToken);
                    nextConfiguration = now + options.ConfigurationRefreshInterval;
                }

                if (now >= nextReconcile)
                {
                    if (engine.Configuration is not null)
                    {
                        watchers.Apply(engine.Configuration.Folders); // переподключение папок, ставших доступными
                    }

                    await engine.ReconcileAsync(stoppingToken);
                    nextReconcile = now + options.ReconcileInterval;
                }

                if (now >= nextPurge)
                {
                    await engine.PurgeAsync(stoppingToken);
                    nextPurge = now + TimeSpan.FromHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Непредвиденная ошибка цикла агента; работа продолжается");
            }

            try
            {
                await Task.Delay(options.ProcessingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        watchers.Dispose();
        logger.LogInformation("Агент DrawingFlow остановлен");
    }
}
