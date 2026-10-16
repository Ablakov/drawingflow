using DrawingFlow.Application.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DrawingFlow.Server.Infrastructure;

public sealed class StorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.ProbeAsync(cancellationToken);
            return HealthCheckResult.Healthy("Хранилище файлов доступно на запись.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Хранилище файлов недоступно.", ex);
        }
    }
}
