namespace DrawingFlow.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "DrawingFlow";

    /// <summary>Применять миграции при старте. Для production рекомендуется false и явный запуск «--migrate».</summary>
    public bool MigrateOnStartup { get; set; }
}
