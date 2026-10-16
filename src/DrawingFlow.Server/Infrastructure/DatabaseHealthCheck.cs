using DrawingFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DrawingFlow.Server.Infrastructure;

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("База данных доступна.")
                : HealthCheckResult.Unhealthy("Не удалось подключиться к базе данных.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ошибка подключения к базе данных.", ex);
        }
    }
}
