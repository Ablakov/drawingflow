using DrawingFlow.Application.Storage;
using DrawingFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Infrastructure.Storage;

public sealed record MissingStorageObject(Guid VersionId, Guid DocumentId, string DocumentNumber, int VersionNumber, string StorageKey);

public sealed record StorageVerificationReport(int CheckedVersions, IReadOnlyList<MissingStorageObject> Missing)
{
    public bool IsConsistent => Missing.Count == 0;
}

/// <summary>Сверка зарегистрированных версий с объектами хранилища (только чтение). Используется командой «--verify-storage» и после восстановления из копии.</summary>
public sealed class StorageVerifier(AppDbContext db, IFileStorage storage)
{
    public async Task<StorageVerificationReport> VerifyAsync(CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var checkedCount = 0;
        var missing = new List<MissingStorageObject>();
        Guid? last = null;
        while (true)
        {
            var query = db.DocumentVersions.AsNoTracking().OrderBy(v => v.Id).AsQueryable();
            if (last is Guid lastId)
            {
                query = query.Where(v => v.Id.CompareTo(lastId) > 0);
            }

            var batch = await query.Take(batchSize)
                .Select(v => new { v.Id, v.DocumentId, v.VersionNumber, v.StorageKey, v.Document!.DocumentNumber })
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var version in batch)
            {
                checkedCount++;
                if (!await storage.ExistsAsync(version.StorageKey, cancellationToken))
                {
                    missing.Add(new MissingStorageObject(version.Id, version.DocumentId, version.DocumentNumber, version.VersionNumber, version.StorageKey));
                }
            }

            last = batch[^1].Id;
        }

        return new StorageVerificationReport(checkedCount, missing);
    }
}
