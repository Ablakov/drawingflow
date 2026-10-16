using System.Buffers;
using System.Security.Cryptography;
using DrawingFlow.Application.Storage;
using DrawingFlow.Domain.Exceptions;
using DrawingFlow.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrawingFlow.Infrastructure.Storage;

/// <summary>
/// Файловое хранилище: объект записывается во временный файл с подсчётом SHA-256 и размера, затем атомарно перемещается
/// под внутренний ключ. Ключи проверяются; выход за пределы корневого каталога невозможен.
/// </summary>
public sealed class FileSystemStorage : IFileStorage
{
    private const int BufferSize = 128 * 1024;
    private readonly string _root;
    private readonly string _tempDirectory;
    private readonly long _maxFileSize;
    private readonly ILogger<FileSystemStorage> _logger;

    public FileSystemStorage(IOptions<StorageOptions> options, IHostEnvironment environment, ILogger<FileSystemStorage> logger)
    {
        var value = options.Value;
        value.Validate();
        _root = Path.GetFullPath(Path.IsPathRooted(value.RootPath) ? value.RootPath : Path.Combine(environment.ContentRootPath, value.RootPath));
        _tempDirectory = Path.Combine(_root, ".tmp");
        _maxFileSize = value.MaxFileSizeBytes;
        _logger = logger;
    }

    public string RootPath => _root;

    public async Task<StoredFile> SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken)
    {
        var finalPath = ResolvePath(storageKey);
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_tempDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            tempPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N") + ".part");

            long total = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    int read;
                    while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > _maxFileSize)
                        {
                            throw new ValidationException("file", $"Размер файла превышает допустимый предел {FormatSize(_maxFileSize)}.");
                        }

                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                await file.FlushAsync(cancellationToken);
            }

            if (File.Exists(finalPath))
            {
                // Объекты неизменяемы: перезапись существующего ключа — ошибка программы, а не пользователя.
                throw new InvalidOperationException($"Объект хранилища «{storageKey}» уже существует.");
            }

            File.Move(tempPath, finalPath);
            tempPath = null;
            return new StoredFile(total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Ошибка записи в хранилище файлов ({Root})", _root);
            throw new StorageException();
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
            }
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(storageKey);
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogError("Объект хранилища {StorageKey} отсутствует ({Path})", storageKey, path);
                throw new StorageException("Файл этой версии отсутствует в хранилище. Обратитесь к администратору: требуется восстановление из резервной копии.");
            }

            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Ошибка чтения из хранилища файлов ({Root})", _root);
            throw new StorageException();
        }
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult(File.Exists(ResolvePath(storageKey)));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        TryDelete(ResolvePath(storageKey));
        return Task.CompletedTask;
    }

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_tempDirectory);
            var probe = Path.Combine(_tempDirectory, "probe-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllBytesAsync(probe, [1], cancellationToken);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException($"Хранилище файлов недоступно на запись: {_root}");
        }
    }

    private string ResolvePath(string storageKey)
    {
        if (!StorageKeys.IsValid(storageKey))
        {
            throw new InvalidOperationException($"Недопустимый ключ хранилища «{storageKey}».");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ключ хранилища выходит за пределы корневого каталога.");
        }

        return fullPath;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось удалить файл {Path}", path);
        }
    }

    private static readonly System.Globalization.CultureInfo Russian = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Размер в читаемом виде с русским десятичным разделителем (не зависит от культуры процесса).</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => string.Create(Russian, $"{bytes / (1024.0 * 1024 * 1024):0.#} ГБ"),
        >= 1024L * 1024 => string.Create(Russian, $"{bytes / (1024.0 * 1024):0.#} МБ"),
        >= 1024 => string.Create(Russian, $"{bytes / 1024.0:0.#} КБ"),
        _ => $"{bytes} Б",
    };
}
