namespace DrawingFlow.Contracts.Documents;

// Значения совпадают с DrawingFlow.Domain.Enums (тест паритета).
public enum DocumentStatus
{
    Draft = 0,
    InProgress = 1,
    InReview = 2,
    RevisionRequired = 3,
    Approved = 4,
    Released = 5,
    Archived = 6,
}

public enum VersionStatus
{
    Registered = 0,
    InReview = 1,
    Approved = 2,
    RevisionRequired = 3,
    Released = 4,
    Superseded = 5,
}

public sealed record DocumentVersionDto(
    Guid Id,
    Guid DocumentId,
    int VersionNumber,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    string Sha256Hash,
    VersionStatus Status,
    string? Comment,
    DateTime CreatedAtUtc,
    Guid? CreatedByUserId,
    string CreatedByName,
    bool IsCurrent);

public sealed record DocumentDto(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string DocumentNumber,
    string Title,
    string? Description,
    string? Discipline,
    string? DocumentType,
    DocumentStatus Status,
    DocumentVersionDto? CurrentVersion,
    int VersionCount,
    DateTime CreatedAtUtc,
    string CreatedByName,
    DateTime UpdatedAtUtc,
    // Токен оптимистичной блокировки: передаётся обратно в UpdateDocumentRequest.
    long RowVersion,
    // Действующие права текущего пользователя в проекте документа (имена из ProjectPermissions).
    IReadOnlyList<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

public sealed record RegisterDocumentRequest(
    string DocumentNumber,
    string Title,
    string? Description = null,
    string? Discipline = null,
    string? DocumentType = null);

public sealed record UpdateDocumentRequest(
    string Title,
    string? Description,
    string? Discipline,
    string? DocumentType,
    long RowVersion);

public enum DocumentSortField
{
    Number = 0,
    Title = 1,
    UpdatedAt = 2,
    CreatedAt = 3,
    Status = 4,
}

public sealed record DocumentListQuery(
    string? Search = null,
    DocumentStatus? Status = null,
    string? Discipline = null,
    DocumentSortField Sort = DocumentSortField.Number,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

/// <summary>Параметры загрузки версии; передаются в строке запроса, содержимое файла — тело запроса (application/octet-stream).</summary>
public sealed record UploadVersionParameters(
    string FileName,
    string IdempotencyKey,
    string? Comment = null,
    // Контрольная сумма, вычисленная клиентом; при несовпадении с серверной загрузка отклоняется.
    string? Sha256Hash = null);
