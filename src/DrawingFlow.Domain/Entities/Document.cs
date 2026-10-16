using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;
using DrawingFlow.Domain.Workflow;

namespace DrawingFlow.Domain.Entities;

/// <summary>
/// Документ проекта. Идентичность документа не зависит от имени файла и пути; содержимое хранится в неизменяемых версиях.
/// </summary>
public sealed class Document
{
    public const int NumberMaxLength = 100;
    public const int TitleMaxLength = 300;
    public const int DescriptionMaxLength = 4000;
    public const int DisciplineMaxLength = 100;
    public const int TypeMaxLength = 100;

    private Document() { }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public string NormalizedDocumentNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Discipline { get; private set; }
    public string? DocumentType { get; private set; }
    public DocumentStatus Status { get; private set; }
    public Guid? CurrentVersionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Токен оптимистичной блокировки (PostgreSQL xmin).</summary>
    public uint RowVersion { get; private set; }

    public Project? Project { get; private set; }
    public ICollection<DocumentVersion> Versions { get; private set; } = new List<DocumentVersion>();

    public static Document Create(Guid projectId, string documentNumber, string title, string? description, string? discipline, string? documentType, Guid createdByUserId, DateTime nowUtc)
    {
        var number = Guard.DocumentNumber(documentNumber);
        Guard.Utc(nowUtc, nameof(nowUtc));
        return new Document
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            DocumentNumber = number,
            NormalizedDocumentNumber = Guard.NormalizeDocumentNumber(number),
            Title = Guard.Required(title, "title", "Наименование", TitleMaxLength),
            Description = Guard.Optional(description, "description", "Описание", DescriptionMaxLength),
            Discipline = Guard.Optional(discipline, "discipline", "Раздел (дисциплина)", DisciplineMaxLength),
            DocumentType = Guard.Optional(documentType, "documentType", "Тип документа", TypeMaxLength),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = createdByUserId,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void UpdateMetadata(string title, string? description, string? discipline, string? documentType, DateTime nowUtc)
    {
        Title = Guard.Required(title, "title", "Наименование", TitleMaxLength);
        Description = Guard.Optional(description, "description", "Описание", DescriptionMaxLength);
        Discipline = Guard.Optional(discipline, "discipline", "Раздел (дисциплина)", DisciplineMaxLength);
        DocumentType = Guard.Optional(documentType, "documentType", "Тип документа", TypeMaxLength);
        UpdatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
    }

    /// <summary>
    /// Регистрирует неизменяемую версию. Идентификатор версии создаётся заранее (он входит в ключ хранилища),
    /// номер версии выдаёт вызывающая сторона под блокировкой строки документа.
    /// Документ переходит в состояние «В работе»: согласование предыдущих версий не наследуется.
    /// Указатель на текущую версию устанавливается отдельным шагом <see cref="MakeCurrent"/> после сохранения версии:
    /// таблицы Documents и DocumentVersions ссылаются друг на друга, и строка версии должна существовать раньше ссылки на неё.
    /// </summary>
    public DocumentVersion RegisterVersion(
        Guid versionId,
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
        if (!DocumentWorkflow.AllowsNewVersion(Status))
        {
            throw new DomainRuleException("Документ находится в архиве: загрузка новых версий невозможна. Сначала восстановите документ.");
        }

        if (versionId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор версии обязателен: он используется в ключе хранилища.", nameof(versionId));
        }

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Номер версии должен быть положительным.");
        }

        if (Versions.Any(v => v.VersionNumber == versionNumber))
        {
            throw new ConflictException($"Версия № {versionNumber} уже зарегистрирована.");
        }

        Guard.Utc(nowUtc, nameof(nowUtc));
        var version = DocumentVersion.Create(versionId, Id, versionNumber, originalFileName, storageKey, contentType, fileSize, sha256Hash, createdByUserId, createdByAgentId, comment, idempotencyKey, nowUtc);
        Versions.Add(version);
        if (Status != DocumentStatus.InProgress)
        {
            DocumentWorkflow.EnsureTransition(Status, DocumentStatus.InProgress);
            Status = DocumentStatus.InProgress;
        }

        UpdatedAtUtc = nowUtc;
        return version;
    }

    /// <summary>Делает зарегистрированную версию текущей.</summary>
    public void MakeCurrent(DocumentVersion version)
    {
        if (version.DocumentId != Id || !Versions.Contains(version))
        {
            throw new ArgumentException("Версия не принадлежит этому документу.", nameof(version));
        }

        CurrentVersionId = version.Id;
    }

    // ---------- Согласование: решения всегда относятся к текущей версии ----------

    /// <summary>Отправляет текущую версию на проверку. Допустимо только из состояния «В работе» для ещё не проверявшейся версии.</summary>
    public void SubmitForReview(DocumentVersion version, DateTime nowUtc)
    {
        EnsureCurrent(version);
        if (Status != DocumentStatus.InProgress)
        {
            throw new DomainRuleException($"Отправить на проверку можно документ в состоянии «В работе» (сейчас: «{DocumentWorkflow.Name(Status)}»).");
        }

        if (version.Status != VersionStatus.Registered)
        {
            throw new DomainRuleException("Эта версия уже проходила согласование. Загрузите новую версию и отправьте её на проверку.");
        }

        TransitionTo(DocumentStatus.InReview, nowUtc);
        version.SetStatus(VersionStatus.InReview);
    }

    public Review Approve(DocumentVersion version, Guid reviewerUserId, string? comment, DateTime nowUtc)
    {
        EnsureCurrent(version);
        EnsureInReview(version);
        TransitionTo(DocumentStatus.Approved, nowUtc);
        version.SetStatus(VersionStatus.Approved);
        return Review.Create(Id, version.Id, reviewerUserId, ReviewDecision.Approved, comment, nowUtc);
    }

    /// <summary>Возврат на доработку: комментарий обязателен (проверяется при создании решения).</summary>
    public Review RequestRevision(DocumentVersion version, Guid reviewerUserId, string? comment, DateTime nowUtc)
    {
        EnsureCurrent(version);
        EnsureInReview(version);
        var review = Review.Create(Id, version.Id, reviewerUserId, ReviewDecision.RevisionRequired, comment, nowUtc);
        TransitionTo(DocumentStatus.RevisionRequired, nowUtc);
        version.SetStatus(VersionStatus.RevisionRequired);
        return review;
    }

    /// <summary>Выпуск согласованной версии. Выпущенная версия остаётся в истории и не заменяется.</summary>
    public void Release(DocumentVersion version, DateTime nowUtc)
    {
        EnsureCurrent(version);
        if (Status != DocumentStatus.Approved || version.Status != VersionStatus.Approved)
        {
            throw new DomainRuleException($"Выпустить можно только согласованную версию (сейчас документ: «{DocumentWorkflow.Name(Status)}»).");
        }

        TransitionTo(DocumentStatus.Released, nowUtc);
        version.SetStatus(VersionStatus.Released);
    }

    private void EnsureCurrent(DocumentVersion version)
    {
        if (version.DocumentId != Id || version.Id != CurrentVersionId)
        {
            throw new DomainRuleException("Действие допустимо только для текущей версии документа.");
        }
    }

    private void EnsureInReview(DocumentVersion version)
    {
        if (Status != DocumentStatus.InReview || version.Status != VersionStatus.InReview)
        {
            throw new DomainRuleException($"Решение можно принять только по версии, находящейся на проверке (сейчас документ: «{DocumentWorkflow.Name(Status)}»).");
        }
    }

    public void TransitionTo(DocumentStatus target, DateTime nowUtc)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ValidationException("status", "Недопустимый статус документа.");
        }

        DocumentWorkflow.EnsureTransition(Status, target);
        Status = target;
        UpdatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
    }

    public void Archive(DateTime nowUtc) => TransitionTo(DocumentStatus.Archived, nowUtc);

    /// <summary>Возврат из архива: без версий — в черновик, с версиями — в работу.</summary>
    public void Restore(DateTime nowUtc) => TransitionTo(CurrentVersionId is null ? DocumentStatus.Draft : DocumentStatus.InProgress, nowUtc);
}
