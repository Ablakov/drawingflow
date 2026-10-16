using System.Reflection;

namespace DrawingFlow.Agent;

/// <summary>Настройки службы агента (раздел «Agent» конфигурации).</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Каталог данных агента: очередь SQLite и защищённый ключ установки.</summary>
    public string? DataDirectory { get; set; }

    public TimeSpan ProcessingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConfigurationRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ReconcileInterval { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Объединение всплесков событий файловой системы по одному пути.</summary>
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Размер и время изменения файла не должны меняться в течение этого окна.</summary>
    public TimeSpan StabilityWindow { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxRetries { get; set; } = 8;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(15);
    public int MaxOperationsPerCycle { get; set; } = 20;
    /// <summary>Завершённые операции старше этого срока удаляются из локальной очереди.</summary>
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(30);
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>Каталоги, которые никогда не синхронизируются (например, кэш Desktop или хранилище сервера на той же машине).</summary>
    public string[] ExcludedRoots { get; set; } = [];

    /// <summary>Временные и служебные файлы, исключаемые всегда.</summary>
    public string[] AlwaysExcludePatterns { get; set; } =
        ["~$*", "*.tmp", "*.temp", "*.bak", "*.part", "*.crdownload", "*.dwl", "*.dwl2", "*.lck", "*.lock", "Thumbs.db", "desktop.ini", ".DS_Store", "*.lnk"];

    public string ResolvedDataDirectory => string.IsNullOrWhiteSpace(DataDirectory) ? DefaultDataDirectory : Path.GetFullPath(DataDirectory);

    public string QueueDatabasePath => Path.Combine(ResolvedDataDirectory, "queue.db");

    public string CredentialPath => Path.Combine(ResolvedDataDirectory, "agent.credential");

    public static string DefaultDataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DrawingFlow", "Agent")
        : Path.Combine(AppContext.BaseDirectory, "data");

    public static string AgentVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";

    public void Validate()
    {
        if (StabilityWindow <= TimeSpan.Zero || DebounceDelay < TimeSpan.Zero || MaxRetries < 0 || InitialRetryDelay <= TimeSpan.Zero || MaxRetryDelay < InitialRetryDelay)
        {
            throw new InvalidOperationException("Некорректные интервалы в разделе Agent конфигурации.");
        }
    }
}
