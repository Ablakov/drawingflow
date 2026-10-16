using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>Решение проверяющего по конкретной версии документа. Запись неизменяема.</summary>
public sealed class Review
{
    public const int CommentMaxLength = 2000;

    private Review() { }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public ReviewDecision Decision { get; private set; }
    public string? Comment { get; private set; }
    public DateTime DecidedAtUtc { get; private set; }

    public Document? Document { get; private set; }
    public DocumentVersion? Version { get; private set; }
    public User? Reviewer { get; private set; }

    internal static Review Create(Guid documentId, Guid documentVersionId, Guid reviewerUserId, ReviewDecision decision, string? comment, DateTime nowUtc)
    {
        if (!Enum.IsDefined(decision))
        {
            throw new ValidationException("decision", "Недопустимое решение.");
        }

        var trimmed = Guard.Optional(comment, "comment", "Комментарий", CommentMaxLength);
        if (decision == ReviewDecision.RevisionRequired && trimmed is null)
        {
            throw new ValidationException("comment", "Укажите замечания: комментарий обязателен при возврате на доработку.");
        }

        return new Review
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            ReviewerUserId = reviewerUserId,
            Decision = decision,
            Comment = trimmed,
            DecidedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
        };
    }
}
