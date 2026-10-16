using DrawingFlow.Application.Abstractions;
using DrawingFlow.Infrastructure.Options;
using DrawingFlow.Infrastructure.Persistence;
using DrawingFlow.Infrastructure.Security;
using DrawingFlow.Application.Storage;
using DrawingFlow.Infrastructure.Services;
using DrawingFlow.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrawingFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDrawingFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<BootstrapOptions>().Bind(configuration.GetSection(BootstrapOptions.SectionName));
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        // Строка подключения читается при создании контекста, а не при регистрации:
        // так учитываются источники конфигурации, добавленные позже (тестовый хост, перезагрузка настроек).
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var liveConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = liveConfiguration.GetConnectionString(DatabaseOptions.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Не задана строка подключения ConnectionStrings:{DatabaseOptions.ConnectionStringName}. " +
                    "Укажите её в appsettings или переменной окружения ConnectionStrings__DrawingFlow.");
            }

            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<StorageVerifier>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IFileStorage, FileSystemStorage>();
        return services;
    }
}
