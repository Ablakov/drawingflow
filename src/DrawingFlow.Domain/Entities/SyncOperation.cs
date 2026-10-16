using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>
/// Серверная проекция операции синхронизации агента. Идентификатор задаёт агент (он же ключ идемпотентности загрузки),
/// поэтому повторные отчёты после перезапуска не создают дубликатов.
/// </summary>
public sealed class SyncOperation
{
    public const int PathMaxLength = 1000;
    public const int ErrorMaxLength = 2000;

    private static readonly SyncOperationStatus[] TerminalStatuses = [SyncOperationStatus.Completed, SyncOperationStatus.Cancelled, SyncOperationStatus.Dismissed];

    private SyncOperation() { }

    public Guid Id { get; private set; }
    public Guid AgentInstallationId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? WatchedFolderId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string FilePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long? FileSize { get; private set; }
    public string? Sha256Hash { get; private set; }
    public SyncOperationStatus Status { get; private set; }
    public DateTime DetectedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public Guid? DocumentId { get; private set; }
    public Guid? DocumentVersionId { get; private set; }
    /// <summary>Кандидаты для ручного сопоставления (JSON).</summary>
    public string? Candidates { get; private set; }
    public SyncRequestedAction RequestedAction { get; private set; }
    public Guid? RequestedDocumentId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public DateTime? RequestedAtUtc { get; private set; }

    public AgentInstallation? Agent { get; private set; }
    public WatchedFolder? WatchedFolder { get; private set; }
    public Project? Project { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status);

    public static SyncOperation Create(Guid id, Guid agentInstallationId, Guid organizationId, Guid? watchedFolderId, Guid projectId, string filePath, string fileName, DateTime detectedAtUtc, DateTime nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("operationId", "Идентификатор операции обязателен.");
        }

        return new SyncOperation
        {
            Id = id,
            AgentInstallationId = agentInstallationId,
            OrganizationId = organizationId,
            WatchedFolderId = watchedFolderId,
            ProjectId = projectId,
            FilePath = Guard.Required(filePath, "filePath", "Путь к файлу", PathMaxLength),
            FileName = Guard.FileName(fileName),
            Status = SyncOperationStatus.Pending,
            DetectedAtUtc = Guard.Utc(detectedAtUtc, nameof(detectedAtUtc)),
            UpdatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
        };
    }

    /// <summary>Применяет отчёт агента. Запрошенное пользователем действие снимается, когда агент сдвинул операцию с места.</summary>
    public void ApplyReport(SyncOperationStatus status, long? fileSize, string? sha256Hash, int retryCount, string? lastError, Guid? documentId, Guid? documentVersionId, string? candidatesJson, DateTime nowUtc)
    {
        if (!Enum.IsDefined(status) || status == SyncOperationStatus.Dismissed)
        {
            throw new ValidationException("status", "Недопустимый статус операции в отчёте агента.");
        }

        if (Status == SyncOperationStatus.Dismissed)
        {
            // Отклонённая пользователем операция не возвращается к жизни отчётами агента.
            return;
        }

        Guard.Utc(nowUtc, nameof(nowUtc));
        Status = status;
        FileSize = fileSize is < 0 ? null : fileSize;
        Sha256Hash = string.IsNullOrWhiteSpace(sha256Hash) ? null : Guard.Sha256(sha256Hash);
        RetryCount = Math.Max(0, retryCount);
        LastError = Guard.Optional(lastError, "lastError", "Ошибка", ErrorMaxLength);
        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        Candidates = candidatesJson;
        UpdatedAtUtc = nowUtc;
        CompletedAtUtc = IsTerminal ? nowUtc : null;

        if (status is not (SyncOperationStatus.Failed or SyncOperationStatus.NeedsResolution))
        {
            ClearRequest();
        }
    }

    public void RequestRetry(Guid userId, DateTime nowUtc)
    {
        if (Status is not (SyncOperationStatus.Failed or SyncOperationStatus.NeedsResolution))
        {
            throw new DomainRuleException("Повторить можно только операцию с ошибкой или требующую разрешения.");
        }

        SetRequest(SyncRequestedAction.Retry, null, userId, nowUtc);
    }

    public void RequestUpload(Guid documentId, Guid userId, DateTime nowUtc)
    {
        if (Status is not (SyncOperationStatus.Failed or SyncOperationStatus.NeedsResolution))
        {
            throw new DomainRuleException("Сопоставить с документом можно только операцию с ошибкой или требующую разрешения.");
        }

        SetRequest(SyncRequestedAction.UploadToDocument, documentId, userId, nowUtc);
    }

    public void Dismiss(Guid userId, DateTime nowUtc)
    {
        if (IsTerminal)
        {
            throw new DomainRuleException("Операция уже завершена.");
        }

        Guard.Utc(nowUtc, nameof(nowUtc));
        Status = SyncOperationStatus.Dismissed;
        CompletedAtUtc = nowUtc;
        SetRequest(SyncRequestedAction.Dismiss, null, userId, nowUtc);
    }

    private void SetRequest(SyncRequestedAction action, Guid? documentId, Guid userId, DateTime nowUtc)
    {
        RequestedAction = action;
        RequestedDocumentId = documentId;
        RequestedByUserId = userId;
        RequestedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        UpdatedAtUtc = nowUtc;
    }

    private void ClearRequest()
    {
        RequestedAction = SyncRequestedAction.None;
        RequestedDocumentId = null;
        RequestedByUserId = null;
        RequestedAtUtc = null;
    }
}
