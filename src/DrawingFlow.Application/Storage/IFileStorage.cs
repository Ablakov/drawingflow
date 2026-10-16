namespace DrawingFlow.Application.Storage;

public sealed record StoredFile(long Size, string Sha256Hash);

/// <summary>
/// Хранилище содержимого версий. Объекты неизменяемы: ключ записывается один раз и никогда не перезаписывается.
/// Реализации: файловая система (этап 2), S3-совместимое хранилище (позже).
/// </summary>
public interface IFileStorage
{
    /// <summary>Сохраняет поток под ключом, вычисляя размер и SHA-256. Превышение лимита размера — <see cref="Domain.Exceptions.ValidationException"/>.</summary>
    Task<StoredFile> SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Удаляет объект (используется только для отката незавершённой загрузки). Отсутствие объекта не является ошибкой.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Проверка готовности: хранилище доступно на запись (пробный файл создаётся и удаляется).</summary>
    Task ProbeAsync(CancellationToken cancellationToken);
}
