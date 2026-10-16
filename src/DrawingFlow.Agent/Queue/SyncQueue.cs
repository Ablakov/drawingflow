using DrawingFlow.Contracts.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Agent.Queue;

/// <summary>Устойчивая очередь операций в SQLite: переживает перезапуск службы и компьютера.</summary>
public sealed class SyncQueue(IDbContextFactory<AgentQueueDbContext> contextFactory, ILogger<SyncQueue> logger)
{
    private const string SchemaKey = "schema_version";

    /// <summary>Создаёт схему при первом запуске; при несовместимой версии схемы пересоздаёт базу (операции будут обнаружены сверкой заново).</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var version = await db.Settings.FindAsync([SchemaKey], cancellationToken);
        if (version is null)
        {
            db.Settings.Add(new AgentSetting { Key = SchemaKey, Value = AgentQueueDbContext.SchemaVersion.ToString() });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (version.Value != AgentQueueDbContext.SchemaVersion.ToString())
        {
            logger.LogWarning("Схема локальной очереди устарела ({Old} → {New}); база пересоздаётся", version.Value, AgentQueueDbContext.SchemaVersion);
            await db.Database.EnsureDeletedAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Settings.Add(new AgentSetting { Key = SchemaKey, Value = AgentQueueDbContext.SchemaVersion.ToString() });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Операции, прерванные остановкой процесса, возвращаются в очередь.</summary>
    public async Task<int> RecoverInFlightAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var inFlight = await db.Operations.Where(o => o.Status == SyncOperationStatus.Processing).ToListAsync(cancellationToken);
        foreach (var operation in inFlight)
        {
            operation.Status = SyncOperationStatus.Pending;
            operation.NextAttemptAtUtc = null;
            operation.LastObservedAtUtc = null;
            operation.UpdatedAtUtc = nowUtc;
            operation.ReportPending = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return inFlight.Count;
    }

    /// <summary>
    /// Регистрирует кандидата на загрузку. Для пути существует не более одной активной операции:
    /// повторные события лишь обновляют наблюдение; операция с ошибкой возвращается в очередь; требующая разрешения — ждёт пользователя.
    /// </summary>
    public async Task<LocalSyncOperation?> EnqueueCandidateAsync(Guid folderId, Guid projectId, string filePath, string fileName, long size, DateTime modifiedAtUtc, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Operations
            .Where(o => o.FilePath == filePath && o.Status != SyncOperationStatus.Completed && o.Status != SyncOperationStatus.Cancelled && o.Status != SyncOperationStatus.Dismissed)
            .OrderByDescending(o => o.DetectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            var changed = existing.LastObservedSize != size || existing.LastObservedModifiedAtUtc != modifiedAtUtc;
            if (changed)
            {
                existing.LastObservedSize = size;
                existing.LastObservedModifiedAtUtc = modifiedAtUtc;
                existing.LastObservedAtUtc = nowUtc;
                existing.Sha256 = null;
            }

            if (existing.Status == SyncOperationStatus.Failed || (existing.Status == SyncOperationStatus.NeedsResolution && changed && existing.DocumentId is not null))
            {
                existing.Status = SyncOperationStatus.Pending;
                existing.RetryCount = 0;
                existing.NextAttemptAtUtc = null;
                existing.LastError = null;
                existing.ReportPending = true;
            }

            existing.UpdatedAtUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var state = await db.FileStates.FindAsync([filePath], cancellationToken);
        if (state is not null && state.Size == size && state.ModifiedAtUtc == modifiedAtUtc && state.DocumentVersionId is not null)
        {
            // Файл уже загружен в этом состоянии.
            return null;
        }

        var operation = new LocalSyncOperation
        {
            Id = Guid.CreateVersion7(),
            WatchedFolderId = folderId,
            ProjectId = projectId,
            FilePath = filePath,
            FileName = fileName,
            DetectedAtUtc = nowUtc,
            LastObservedSize = size,
            LastObservedModifiedAtUtc = modifiedAtUtc,
            LastObservedAtUtc = nowUtc,
            Status = SyncOperationStatus.Pending,
            UpdatedAtUtc = nowUtc,
            ReportPending = true,
        };
        db.Operations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
        return operation;
    }

    public async Task<List<LocalSyncOperation>> GetDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Operations.AsNoTracking()
            .Where(o => o.Status == SyncOperationStatus.Pending && (o.NextAttemptAtUtc == null || o.NextAttemptAtUtc <= nowUtc))
            .OrderBy(o => o.DetectedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<LocalSyncOperation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<List<LocalSyncOperation>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Operations.AsNoTracking().OrderByDescending(o => o.UpdatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(LocalSyncOperation operation, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Operations.Update(operation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Operations.Where(o => o.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<List<LocalSyncOperation>> GetUnreportedAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Operations.AsNoTracking().Where(o => o.ReportPending).OrderBy(o => o.UpdatedAtUtc).Take(take).ToListAsync(cancellationToken);
    }

    public async Task MarkReportedAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.ToList();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Operations.Where(o => list.Contains(o.Id)).ExecuteUpdateAsync(s => s.SetProperty(o => o.ReportPending, false), cancellationToken);
    }

    public async Task<(int Pending, int Failed, int NeedsResolution)> CountsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var groups = await db.Operations.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        int Of(SyncOperationStatus status) => groups.FirstOrDefault(g => g.Key == status)?.Count ?? 0;
        return (Of(SyncOperationStatus.Pending) + Of(SyncOperationStatus.Processing), Of(SyncOperationStatus.Failed), Of(SyncOperationStatus.NeedsResolution));
    }

    public async Task<FileState?> GetFileStateAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.FileStates.AsNoTracking().FirstOrDefaultAsync(f => f.FilePath == filePath, cancellationToken);
    }

    public async Task UpsertFileStateAsync(FileState state, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.FileStates.FindAsync([state.FilePath], cancellationToken);
        if (existing is null)
        {
            db.FileStates.Add(state);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(state);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await db.Settings.FindAsync([key], cancellationToken))?.Value;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.Settings.FindAsync([key], cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new AgentSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeCompletedAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Operations
            .Where(o => !o.ReportPending && o.CompletedAtUtc != null && o.CompletedAtUtc < olderThanUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
