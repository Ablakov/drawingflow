using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>Задача пользователю в рамках согласования: проверить версию или доработать документ.</summary>
public sealed class WorkflowTask
{
    public const int CommentMaxLength = 2000;

    private WorkflowTask() { }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid DocumentVersionId { get; private set; }
    public Guid AssigneeUserId { get; private set; }
    public WorkflowTaskType Type { get; private set; }
    public WorkflowTaskStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    /// <summary>Поручение при создании либо итог при завершении/отмене.</summary>
    public string? Comment { get; private set; }

    public Project? Project { get; private set; }
    public Document? Document { get; private set; }
    public DocumentVersion? Version { get; private set; }
    public User? Assignee { get; private set; }

    public static WorkflowTask Create(Guid projectId, Guid documentId, Guid documentVersionId, Guid assigneeUserId, WorkflowTaskType type, Guid? createdByUserId, DateTime? dueAtUtc, string? comment, DateTime nowUtc)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ValidationException("type", "Недопустимый тип задачи.");
        }

        Guard.Utc(nowUtc, nameof(nowUtc));
        if (dueAtUtc is DateTime due)
        {
            Guard.Utc(due, nameof(dueAtUtc));
            if (due < nowUtc.AddMinutes(-1))
            {
                throw new ValidationException("dueAtUtc", "Срок задачи не может быть в прошлом.");
            }
        }

        return new WorkflowTask
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            AssigneeUserId = assigneeUserId,
            Type = type,
            Status = WorkflowTaskStatus.Open,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = createdByUserId,
            DueAtUtc = dueAtUtc,
            Comment = Guard.Optional(comment, "comment", "Комментарий", CommentMaxLength),
        };
    }

    public bool IsOpen => Status == WorkflowTaskStatus.Open;

    public void Complete(Guid byUserId, DateTime nowUtc, string? comment)
    {
        EnsureOpen();
        Status = WorkflowTaskStatus.Completed;
        CompletedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        CompletedByUserId = byUserId;
        Comment = Guard.Optional(comment, "comment", "Комментарий", CommentMaxLength) ?? Comment;
    }

    public void Cancel(Guid? byUserId, DateTime nowUtc, string? reason)
    {
        EnsureOpen();
        Status = WorkflowTaskStatus.Cancelled;
        CompletedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        CompletedByUserId = byUserId;
        Comment = Guard.Optional(reason, "comment", "Причина", CommentMaxLength) ?? Comment;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("Задача уже завершена или отменена.");
        }
    }
}
