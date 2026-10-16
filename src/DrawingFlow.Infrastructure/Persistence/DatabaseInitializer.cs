using DrawingFlow.Application.Abstractions;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrawingFlow.Infrastructure.Persistence;

/// <summary>Применение миграций и первичная инициализация (администратор сервера).</summary>
public sealed class DatabaseInitializer(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IClock clock,
    IOptions<BootstrapOptions> bootstrapOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Схема базы данных актуальна.");
            return;
        }

        logger.LogInformation("Применение миграций: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Миграции применены.");
    }

    /// <summary>Создаёт администратора сервера, если в базе ещё нет ни одного пользователя.</summary>
    public async Task<bool> BootstrapAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return false;
        }

        var admin = bootstrapOptions.Value.Administrator;
        if (admin is null || string.IsNullOrWhiteSpace(admin.Login) || string.IsNullOrWhiteSpace(admin.Password))
        {
            logger.LogWarning(
                "В базе нет пользователей, а параметры Bootstrap:Administrator не заданы. " +
                "Задайте Bootstrap__Administrator__Login и Bootstrap__Administrator__Password и перезапустите сервер.");
            return false;
        }

        Guard.Password(admin.Password);
        var user = User.Create(admin.Login, admin.DisplayName, admin.Email, passwordHasher.Hash(admin.Password), isServerAdministrator: true, clock.UtcNow, admin.MustChangePassword);
        db.Users.Add(user);
        db.AuditEvents.Add(AuditEvent.Create(clock.UtcNow, AuditActions.UserCreated, AuditEntityTypes.User, user.Id.ToString(), "bootstrap",
            detailsJson: $$"""{"login":"{{user.Login}}","isServerAdministrator":true,"source":"bootstrap"}"""));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Создан администратор сервера {Login}. Смените пароль после первого входа.", user.Login);
        return true;
    }
}
