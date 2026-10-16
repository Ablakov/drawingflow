using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>
/// Неизменяемая версия документа. После регистрации содержимое, хеш и метаданные файла не меняются;
/// допускается только смена <see cref="Status"/> в процессе согласования.
/// </summary>
public sealed class DocumentVersion
{
    public const int FileNameMaxLength = 260;
    public const int StorageKeyMaxLength = 200;
    public const int ContentTypeMaxLength = 200;
    public const int CommentMaxLength = 2000;
    public const int IdempotencyKeyMaxLength = 100;
    public const int Sha256Length = 64;
    public const string DefaultContentType = "application/octet-stream";

    private DocumentVersion() { }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    /// <summary>Внутренний ключ хранилища. Никогда не совпадает с пользовательским именем или путём файла.</summary>
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = DefaultContentType;
    public long FileSize { get; private set; }
    public string Sha256Hash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? CreatedByAgentId { get; private set; }
    public string? Comment { get; private set; }
    public VersionStatus Status { get; private set; }
    /// <summary>Ключ идемпотентности загрузки: повтор запроса с тем же ключом возвращает эту же версию.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public Document? Document { get; private set; }

    internal static DocumentVersion Create(
        Guid id,
        Guid documentId,
        int versionNumber,
        string originalFileName,
        string storageKey,
        string? contentType,
        long fileSize,
        string sha256Hash,
        Guid? createdByUserId,
        Guid? createdByAgentId,
        string? comment,
        string idempotencyKey,
        DateTime nowUtc)
    {
        if (fileSize < 0)
        {
            throw new ValidationException("fileSize", "Размер файла не может быть отрицательным.");
        }

        if (createdByUserId is null && createdByAgentId is null)
        {
            throw new ArgumentException("У версии должен быть автор: пользователь или агент.");
        }

        var key = Guard.Required(storageKey, "storageKey", "Ключ хранилища", StorageKeyMaxLength);
        return new DocumentVersion
        {
            Id = id,
            DocumentId = documentId,
            VersionNumber = versionNumber,
            OriginalFileName = Guard.FileName(originalFileName),
            StorageKey = key,
            ContentType = Guard.Optional(contentType, "contentType", "Тип содержимого", ContentTypeMaxLength) ?? DefaultContentType,
            FileSize = fileSize,
            Sha256Hash = Guard.Sha256(sha256Hash),
            CreatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
            CreatedByUserId = createdByUserId,
            CreatedByAgentId = createdByAgentId,
            Comment = Guard.Optional(comment, "comment", "Комментарий", CommentMaxLength),
            Status = VersionStatus.Registered,
            IdempotencyKey = Guard.Required(idempotencyKey, "idempotencyKey", "Ключ идемпотентности", IdempotencyKeyMaxLength),
        };
    }

    /// <summary>
    /// Новая версия вытесняет предыдущую, если та не была согласована или выпущена: история согласований и выпусков сохраняется.
    /// </summary>
    public bool Supersede()
    {
        if (Status is VersionStatus.Registered or VersionStatus.InReview or VersionStatus.RevisionRequired)
        {
            Status = VersionStatus.Superseded;
            return true;
        }

        return false;
    }

    public void SetStatus(VersionStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ValidationException("status", "Недопустимый статус версии.");
        }

        Status = status;
    }
}
