using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DrawingFlow.Agent.Queue;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Agent.Sync;

/// <summary>Состояние папки наблюдения на стороне агента.</summary>
public sealed record FolderStatus(Guid FolderId, string Path, bool IsAvailable, string? Error, DateTime CheckedAtUtc);

/// <summary>
/// Ядро агента: конфигурация, сверка папок, конвейер обработки операций (стабильность → хеш → сопоставление → загрузка → подтверждение),
/// отчёты серверу и применение действий пользователей. Все шаги вызываются явно, что позволяет тестировать их без таймеров.
/// </summary>
public sealed class SyncEngine(
    DrawingFlowApiClient api,
    AgentTokenHandler auth,
    SyncQueue queue,
    AgentOptions options,
    IAgentClock clock,
    ILogger<SyncEngine> logger)
{
    private const string ConfigurationKey = "configuration";
    private static readonly JsonSerializerOptions Json = DrawingFlowApiClient.CreateJsonOptions();
    private readonly Dictionary<Guid, FolderStatus> _folderStatuses = new();
    private readonly object _gate = new();
    private AgentConfigurationDto? _configuration;

    public AgentConfigurationDto? Configuration => _configuration;

    public IReadOnlyList<WatchedFolderDto> EnabledFolders =>
        _configuration?.Folders.Where(f => f.IsEnabled).ToList() ?? [];

    public IReadOnlyCollection<FolderStatus> FolderStatuses
    {
        get
        {
            lock (_gate)
            {
                return _folderStatuses.Values.ToList();
            }
        }
    }

    public bool IsRevoked => auth.State == AgentAuthState.Revoked;

    public event EventHandler<AgentConfigurationDto>? ConfigurationChanged;

    // ---------- Конфигурация ----------

    /// <summary>Загружает конфигурацию с сервера; при недоступности использует последнюю сохранённую копию.</summary>
    public async Task<bool> RefreshConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await api.GetAgentConfigurationAsync(cancellationToken);
            await queue.SetSettingAsync(ConfigurationKey, JsonSerializer.Serialize(configuration, Json), cancellationToken);
            ApplyConfiguration(configuration);
            return true;
        }
        catch (ApiConnectionException ex)
        {
            logger.LogWarning("Сервер недоступен, конфигурация не обновлена: {Message}", ex.Message);
        }
        catch (ApiException ex) when (ex.IsUnauthorized || ex.IsForbidden)
        {
            logger.LogError("Сервер отклонил агента: {Message}", ex.Message);
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Не удалось получить конфигурацию: {Message}", ex.Message);
        }

        if (_configuration is null)
        {
            var cached = await queue.GetSettingAsync(ConfigurationKey, cancellationToken);
            if (cached is not null)
            {
                try
                {
                    var configuration = JsonSerializer.Deserialize<AgentConfigurationDto>(cached, Json);
                    if (configuration is not null)
                    {
                        ApplyConfiguration(configuration);
                        logger.LogInformation("Используется сохранённая конфигурация ({Count} папок)", configuration.Folders.Count);
                    }
                }
                catch (JsonException)
                {
                    logger.LogWarning("Сохранённая конфигурация повреждена и проигнорирована");
                }
            }
        }

        return false;
    }

    private void ApplyConfiguration(AgentConfigurationDto configuration)
    {
        var changed = _configuration is null || JsonSerializer.Serialize(_configuration.Folders, Json) != JsonSerializer.Serialize(configuration.Folders, Json);
        _configuration = configuration;
        if (changed)
        {
            ConfigurationChanged?.Invoke(this, configuration);
        }
    }

    // ---------- Обнаружение ----------

    /// <summary>Полная сверка всех включённых папок: истина — содержимое диска, а не события файловой системы.</summary>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        var enqueued = 0;
        foreach (var folder in EnabledFolders)
        {
            enqueued += await ReconcileFolderAsync(folder, cancellationToken);
        }

        return enqueued;
    }

    public async Task<int> ReconcileFolderAsync(WatchedFolderDto folder, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!Directory.Exists(folder.Path))
        {
            SetFolderStatus(folder, false, "Папка недоступна или не существует", now);
            return 0;
        }

        var enqueued = 0;
        try
        {
            var searchOption = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var path in Directory.EnumerateFiles(folder.Path, "*", searchOption))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await ConsiderFileAsync(folder, path, cancellationToken))
                {
                    enqueued++;
                }
            }

            SetFolderStatus(folder, true, null, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetFolderStatus(folder, false, ex.Message, now);
            logger.LogWarning(ex, "Ошибка сверки папки {Path}", folder.Path);
        }

        return enqueued;
    }

    /// <summary>Уведомление от наблюдателя файловой системы (после дебаунса).</summary>
    public async Task NotifyChangeAsync(Guid folderId, string path, CancellationToken cancellationToken)
    {
        var folder = EnabledFolders.FirstOrDefault(f => f.Id == folderId);
        if (folder is not null)
        {
            await ConsiderFileAsync(folder, path, cancellationToken);
        }
    }

    private async Task<bool> ConsiderFileAsync(WatchedFolderDto folder, string path, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);
        if (!FileMatcher.IsEligible(fileName, folder, options.AlwaysExcludePatterns) || IsInExcludedRoot(path))
        {
            return false;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var operation = await queue.EnqueueCandidateAsync(folder.Id, folder.ProjectId, info.FullName, fileName, info.Length, info.LastWriteTimeUtc, clock.UtcNow, cancellationToken);
        return operation is not null;
    }

    private bool IsInExcludedRoot(string path)
    {
        foreach (var root in options.ExcludedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(full, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void SetFolderStatus(WatchedFolderDto folder, bool available, string? error, DateTime now)
    {
        lock (_gate)
        {
            var previous = _folderStatuses.GetValueOrDefault(folder.Id);
            if (previous is null || previous.IsAvailable != available)
            {
                if (available)
                {
                    logger.LogInformation("Папка доступна: {Path}", folder.Path);
                }
                else
                {
                    logger.LogWarning("Папка недоступна: {Path} ({Error})", folder.Path, error);
                }
            }

            _folderStatuses[folder.Id] = new FolderStatus(folder.Id, folder.Path, available, error, now);
        }
    }

    // ---------- Обработка ----------

    /// <summary>Обрабатывает операции, срок которых наступил. Возвращает число обработанных операций.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        if (IsRevoked)
        {
            return 0;
        }

        var due = await queue.GetDueAsync(clock.UtcNow, options.MaxOperationsPerCycle, cancellationToken);
        foreach (var operation in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessOperationAsync(operation, cancellationToken);
        }

        return due.Count;
    }

    public async Task ProcessOperationAsync(LocalSyncOperation operation, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var folder = _configuration?.Folders.FirstOrDefault(f => f.Id == operation.WatchedFolderId);
        if (folder is null || !folder.IsEnabled)
        {
            await CompleteAsync(operation, SyncOperationStatus.Cancelled, "Папка наблюдения отключена или удалена", cancellationToken);
            return;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(operation.FilePath);
            if (!info.Exists)
            {
                await CompleteAsync(operation, SyncOperationStatus.Cancelled, "Файл удалён или перемещён до загрузки; данные на сервере не изменены", cancellationToken);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ScheduleRetryAsync(operation, "Файл недоступен: " + ex.Message, cancellationToken);
            return;
        }

        // 1. Стабильность: размер и время изменения не менялись в течение окна, файл открывается на чтение.
        if (operation.LastObservedAtUtc is null || operation.LastObservedSize != info.Length || operation.LastObservedModifiedAtUtc != info.LastWriteTimeUtc
            || now - operation.LastObservedAtUtc.Value < options.StabilityWindow)
        {
            if (operation.LastObservedSize != info.Length || operation.LastObservedModifiedAtUtc != info.LastWriteTimeUtc || operation.LastObservedAtUtc is null)
            {
                operation.LastObservedSize = info.Length;
                operation.LastObservedModifiedAtUtc = info.LastWriteTimeUtc;
                operation.LastObservedAtUtc = now;
                operation.Sha256 = null;
            }

            operation.NextAttemptAtUtc = now + options.StabilityWindow;
            operation.UpdatedAtUtc = now;
            await queue.SaveAsync(operation, cancellationToken);
            return;
        }

        if (!TryOpenForRead(operation.FilePath, out var lockError))
        {
            await ScheduleRetryAsync(operation, lockError, cancellationToken);
            return;
        }

        operation.Status = SyncOperationStatus.Processing;
        operation.UpdatedAtUtc = now;
        operation.ReportPending = true;
        await queue.SaveAsync(operation, cancellationToken);

        try
        {
            // 2. Хеш и проверка «изменилось ли содержимое».
            operation.Sha256 ??= await ComputeSha256Async(operation.FilePath, cancellationToken);
            var state = await queue.GetFileStateAsync(operation.FilePath, cancellationToken);
            if (state is not null && state.Sha256 == operation.Sha256 && state.DocumentVersionId is not null)
            {
                operation.DocumentId = state.DocumentId;
                operation.DocumentVersionId = state.DocumentVersionId;
                await CompleteAsync(operation, SyncOperationStatus.Completed, null, cancellationToken, "Содержимое не изменилось: версия уже зарегистрирована");
                return;
            }

            // 3. Сопоставление с документом.
            if (operation.DocumentId is null)
            {
                var resolved = await ResolveDocumentAsync(folder, operation, cancellationToken);
                if (!resolved)
                {
                    return;
                }
            }

            // 4. Загрузка с проверкой хеша сервером; ключ идемпотентности — идентификатор операции.
            DocumentVersionDto version;
            await using (var stream = new FileStream(operation.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                version = await api.UploadDocumentVersionAsync(
                    operation.DocumentId!.Value,
                    stream,
                    operation.FileName,
                    contentType: null,
                    comment: $"Автоматическая загрузка агентом «{_configuration?.Name}» из папки {folder.Path}",
                    idempotencyKey: operation.Id.ToString("N"),
                    sha256Hash: operation.Sha256,
                    progress: null,
                    cancellationToken);
            }

            // 5. Подтверждение: сервер вернул версию с совпадающим хешем.
            if (!string.Equals(version.Sha256Hash, operation.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                await ScheduleRetryAsync(operation, "Сервер вернул версию с другой контрольной суммой; загрузка будет повторена", cancellationToken);
                return;
            }

            operation.DocumentVersionId = version.Id;
            await queue.UpsertFileStateAsync(new FileState
            {
                FilePath = operation.FilePath,
                WatchedFolderId = folder.Id,
                Sha256 = operation.Sha256,
                Size = operation.LastObservedSize ?? info.Length,
                ModifiedAtUtc = operation.LastObservedModifiedAtUtc ?? info.LastWriteTimeUtc,
                DocumentId = operation.DocumentId,
                DocumentVersionId = version.Id,
                UpdatedAtUtc = clock.UtcNow,
            }, cancellationToken);
            await CompleteAsync(operation, SyncOperationStatus.Completed, null, cancellationToken);
            logger.LogInformation("Файл {File} загружен как версия {Version} документа {DocumentId}", operation.FileName, version.VersionNumber, operation.DocumentId);
        }
        catch (ApiConnectionException ex)
        {
            await ScheduleRetryAsync(operation, "Нет связи с сервером: " + ex.Message, cancellationToken);
        }
        catch (ApiException ex)
        {
            await HandleApiErrorAsync(operation, ex, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ScheduleRetryAsync(operation, "Ошибка чтения файла: " + ex.Message, cancellationToken);
        }
    }

    private async Task<bool> ResolveDocumentAsync(WatchedFolderDto folder, LocalSyncOperation operation, CancellationToken cancellationToken)
    {
        var number = FileMatcher.ExtractDocumentNumber(folder, operation.FileName);
        var byFileName = folder.MatchingRule == FileMatchingRule.VersionFileName ? operation.FileName : null;
        if (number is null && byFileName is null)
        {
            await CompleteAsync(operation, SyncOperationStatus.NeedsResolution, "Не удалось извлечь номер документа из имени файла по правилу папки. Укажите документ вручную.", cancellationToken);
            return false;
        }

        var candidates = await api.LookupDocumentsAsync(operation.ProjectId, number, byFileName, cancellationToken);
        if (candidates.Count == 1)
        {
            operation.DocumentId = candidates[0].Id;
            return true;
        }

        if (candidates.Count > 1)
        {
            operation.CandidatesJson = JsonSerializer.Serialize(candidates.Select(c => new SyncCandidateDto(c.Id, c.DocumentNumber, c.Title)).ToList(), Json);
            await CompleteAsync(operation, SyncOperationStatus.NeedsResolution, $"Найдено несколько документов ({candidates.Count}) для файла «{operation.FileName}». Выберите документ вручную.", cancellationToken);
            return false;
        }

        if (folder.AutoRegisterUnknown && number is not null)
        {
            try
            {
                var created = await api.RegisterDocumentAsync(operation.ProjectId, new RegisterDocumentRequest(number, Path.GetFileNameWithoutExtension(operation.FileName)), cancellationToken);
                operation.DocumentId = created.Id;
                logger.LogInformation("Автоматически зарегистрирован документ {Number} для файла {File}", number, operation.FileName);
                return true;
            }
            catch (ApiException ex) when (ex.IsConflict)
            {
                // Документ появился параллельно — ищем ещё раз на следующем проходе.
                await ScheduleRetryAsync(operation, "Документ был зарегистрирован параллельно; повтор сопоставления", cancellationToken);
                return false;
            }
        }

        await CompleteAsync(operation, SyncOperationStatus.NeedsResolution,
            number is not null
                ? $"Документ с номером «{number}» не найден в проекте. Выберите документ вручную или включите автоматическую регистрацию для папки."
                : $"Документ с файлом «{operation.FileName}» не найден в проекте. Выберите документ вручную.",
            cancellationToken);
        return false;
    }

    private async Task HandleApiErrorAsync(LocalSyncOperation operation, ApiException ex, CancellationToken cancellationToken)
    {
        switch (ex.StatusCode)
        {
            case HttpStatusCode.BadRequest:
                // Чаще всего файл изменился во время передачи: наблюдаем стабильность заново.
                operation.Sha256 = null;
                operation.LastObservedAtUtc = null;
                await ScheduleRetryAsync(operation, "Сервер отклонил файл: " + ex.Message, cancellationToken);
                break;
            case HttpStatusCode.NotFound:
            case HttpStatusCode.Conflict:
            case HttpStatusCode.UnprocessableEntity:
                operation.DocumentId = ex.StatusCode == HttpStatusCode.NotFound ? null : operation.DocumentId;
                await CompleteAsync(operation, SyncOperationStatus.NeedsResolution, ex.Message, cancellationToken);
                break;
            case HttpStatusCode.RequestEntityTooLarge:
                await CompleteAsync(operation, SyncOperationStatus.Failed, ex.Message, cancellationToken);
                break;
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                if (IsRevoked)
                {
                    operation.Status = SyncOperationStatus.Pending;
                    operation.NextAttemptAtUtc = clock.UtcNow + options.MaxRetryDelay;
                    operation.LastError = "Ключ агента отозван. Обратитесь к администратору.";
                    operation.UpdatedAtUtc = clock.UtcNow;
                    await queue.SaveAsync(operation, cancellationToken);
                }
                else
                {
                    await ScheduleRetryAsync(operation, ex.Message, cancellationToken);
                }

                break;
            default:
                await ScheduleRetryAsync(operation, ex.Message, cancellationToken);
                break;
        }
    }

    private async Task ScheduleRetryAsync(LocalSyncOperation operation, string error, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        operation.RetryCount++;
        operation.LastError = error;
        operation.UpdatedAtUtc = now;
        operation.ReportPending = true;
        if (RetryPolicy.IsExhausted(operation.RetryCount, options))
        {
            operation.Status = SyncOperationStatus.Failed;
            operation.NextAttemptAtUtc = null;
            logger.LogError("Операция {Operation} для файла {File} переведена в состояние ошибки после {Retries} попыток: {Error}", operation.Id, operation.FileName, operation.RetryCount, error);
        }
        else
        {
            operation.Status = SyncOperationStatus.Pending;
            operation.NextAttemptAtUtc = now + RetryPolicy.Delay(operation.RetryCount, options);
            logger.LogWarning("Повтор {Retry}/{Max} для файла {File} в {Next:HH:mm:ss}: {Error}", operation.RetryCount, options.MaxRetries, operation.FileName, operation.NextAttemptAtUtc, error);
        }

        await queue.SaveAsync(operation, cancellationToken);
    }

    private async Task CompleteAsync(LocalSyncOperation operation, SyncOperationStatus status, string? error, CancellationToken cancellationToken, string? note = null)
    {
        var now = clock.UtcNow;
        operation.Status = status;
        operation.LastError = error ?? note;
        operation.NextAttemptAtUtc = null;
        operation.UpdatedAtUtc = now;
        operation.CompletedAtUtc = status is SyncOperationStatus.Completed or SyncOperationStatus.Cancelled ? now : null;
        operation.ReportPending = true;
        await queue.SaveAsync(operation, cancellationToken);
    }

    // ---------- Отчёты и действия ----------

    /// <summary>Передаёт серверу неотправленные состояния операций и применяет полученные действия пользователей.</summary>
    public async Task<int> ReportAsync(CancellationToken cancellationToken)
    {
        if (IsRevoked)
        {
            return 0;
        }

        var unreported = await queue.GetUnreportedAsync(100, cancellationToken);
        if (unreported.Count == 0)
        {
            return 0;
        }

        var reports = unreported.Select(o => new ReportSyncOperationRequest(
            o.Id, o.WatchedFolderId, o.ProjectId, o.FilePath, o.FileName, o.LastObservedSize, o.Sha256, o.Status, o.RetryCount, o.LastError,
            o.DocumentId, o.DocumentVersionId, o.DetectedAtUtc, DeserializeCandidates(o.CandidatesJson))).ToList();
        try
        {
            var actions = await api.ReportSyncOperationsAsync(reports, cancellationToken);
            await queue.MarkReportedAsync(unreported.Select(o => o.Id), cancellationToken);
            await ApplyActionsAsync(actions, cancellationToken);
            return unreported.Count;
        }
        catch (ApiConnectionException ex)
        {
            logger.LogWarning("Отчёт не отправлен (нет связи): {Message}", ex.Message);
            return 0;
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Сервер отклонил отчёт: {Message}", ex.Message);
            return 0;
        }
    }

    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken)
    {
        if (IsRevoked)
        {
            return false;
        }

        try
        {
            var (pending, failed, unresolved) = await queue.CountsAsync(cancellationToken);
            await api.SendAgentHeartbeatAsync(new AgentHeartbeatRequest(Environment.MachineName, AgentOptions.AgentVersion, pending, failed, unresolved), cancellationToken);
            var actions = await api.GetAgentActionsAsync(cancellationToken);
            await ApplyActionsAsync(actions, cancellationToken);
            return true;
        }
        catch (ApiConnectionException ex)
        {
            logger.LogWarning("Сервер недоступен: {Message}", ex.Message);
            return false;
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Ошибка сердцебиения: {Message}", ex.Message);
            return false;
        }
    }

    public async Task ApplyActionsAsync(IReadOnlyList<AgentActionDto> actions, CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            var operation = await queue.GetAsync(action.OperationId, cancellationToken);
            var now = clock.UtcNow;
            if (operation is null)
            {
                logger.LogWarning("Сервер запросил действие {Action} для неизвестной операции {Operation}", action.Action, action.OperationId);
                continue;
            }

            switch (action.Action)
            {
                case SyncRequestedAction.Dismiss:
                    await queue.DeleteAsync(operation.Id, cancellationToken);
                    logger.LogInformation("Операция {Operation} отклонена пользователем и удалена из очереди", operation.Id);
                    break;
                case SyncRequestedAction.Retry:
                case SyncRequestedAction.UploadToDocument:
                    if (action.Action == SyncRequestedAction.UploadToDocument && action.DocumentId is Guid documentId)
                    {
                        operation.DocumentId = documentId;
                    }

                    operation.Status = SyncOperationStatus.Pending;
                    operation.RetryCount = 0;
                    operation.NextAttemptAtUtc = null;
                    operation.LastObservedAtUtc = null;
                    operation.LastError = null;
                    operation.CandidatesJson = null;
                    operation.CompletedAtUtc = null;
                    operation.UpdatedAtUtc = now;
                    operation.ReportPending = true;
                    await queue.SaveAsync(operation, cancellationToken);
                    logger.LogInformation("Операция {Operation} возвращена в очередь по запросу пользователя ({Action})", operation.Id, action.Action);
                    break;
            }
        }
    }

    public Task<int> PurgeAsync(CancellationToken cancellationToken) => queue.PurgeCompletedAsync(clock.UtcNow - options.CompletedRetention, cancellationToken);

    // ---------- Помощники ----------

    private static bool TryOpenForRead(string path, out string error)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            error = string.Empty;
            return true;
        }
        catch (IOException ex)
        {
            error = "Файл занят другим процессом или недоступен: " + ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = "Нет доступа к файлу: " + ex.Message;
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static IReadOnlyList<SyncCandidateDto>? DeserializeCandidates(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<SyncCandidateDto>>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
