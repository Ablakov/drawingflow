using DrawingFlow.Contracts.Agents;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Agent.Queue;

/// <summary>Локальная операция синхронизации. Идентификатор служит ключом идемпотентности загрузки на сервер.</summary>
public sealed class LocalSyncOperation
{
    public Guid Id { get; set; }
    public Guid WatchedFolderId { get; set; }
    public Guid ProjectId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public long? LastObservedSize { get; set; }
    public DateTime? LastObservedModifiedAtUtc { get; set; }
    public DateTime? LastObservedAtUtc { get; set; }
    public string? Sha256 { get; set; }
    public SyncOperationStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public string? CandidatesJson { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    /// <summary>Состояние ещё не передано на сервер.</summary>
    public bool ReportPending { get; set; }

    public bool IsActive => Status is SyncOperationStatus.Pending or SyncOperationStatus.Processing;
}

/// <summary>Последнее известное состояние файла: позволяет отсеивать ложные изменения при сверке.</summary>
public sealed class FileState
{
    public string FilePath { get; set; } = string.Empty;
    public Guid WatchedFolderId { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? DocumentVersionId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AgentSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class AgentQueueDbContext(DbContextOptions<AgentQueueDbContext> options) : DbContext(options)
{
    public const int SchemaVersion = 1;

    public DbSet<LocalSyncOperation> Operations => Set<LocalSyncOperation>();
    public DbSet<FileState> FileStates => Set<FileState>();
    public DbSet<AgentSetting> Settings => Set<AgentSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalSyncOperation>(b =>
        {
            b.ToTable("SyncOperations");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Status).HasConversion<string>();
            b.HasIndex(o => new { o.Status, o.NextAttemptAtUtc });
            b.HasIndex(o => o.FilePath);
            b.HasIndex(o => o.ReportPending);
        });
        modelBuilder.Entity<FileState>(b =>
        {
            b.ToTable("FileStates");
            b.HasKey(f => f.FilePath);
            b.HasIndex(f => f.WatchedFolderId);
        });
        modelBuilder.Entity<AgentSetting>(b =>
        {
            b.ToTable("Settings");
            b.HasKey(s => s.Key);
        });
    }
}
