using DrawingFlow.Contracts.Documents;

namespace DrawingFlow.Contracts.Workflow;

// Значения совпадают с DrawingFlow.Domain.Enums (тест паритета).
public enum ReviewDecision
{
    Approved = 0,
    RevisionRequired = 1,
}

public enum WorkflowTaskType
{
    Review = 0,
    Rework = 1,
}

public enum WorkflowTaskStatus
{
    Open = 0,
    Completed = 1,
    Cancelled = 2,
}

/// <summary>Действия согласования, доступные текущему пользователю по документу (вычисляет сервер).</summary>
public static class WorkflowActions
{
    public const string Submit = "Submit";
    public const string Approve = "Approve";
    public const string RequestRevision = "RequestRevision";
    public const string Release = "Release";
}

public sealed record ReviewDto(
    Guid Id,
    Guid DocumentId,
    Guid DocumentVersionId,
    int VersionNumber,
    Guid ReviewerUserId,
    string ReviewerName,
    ReviewDecision Decision,
    string? Comment,
    DateTime DecidedAtUtc);

public sealed record WorkflowTaskDto(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    Guid DocumentId,
    string DocumentNumber,
    string DocumentTitle,
    Guid DocumentVersionId,
    int VersionNumber,
    Guid AssigneeUserId,
    string AssigneeName,
    WorkflowTaskType Type,
    WorkflowTaskStatus Status,
    DateTime CreatedAtUtc,
    string CreatedByName,
    DateTime? DueAtUtc,
    DateTime? CompletedAtUtc,
    string? Comment);

public sealed record DocumentWorkflowDto(
    DocumentDto Document,
    IReadOnlyList<ReviewDto> Reviews,
    IReadOnlyList<WorkflowTaskDto> Tasks,
    IReadOnlyList<string> AvailableActions)
{
    public bool Can(string action) => AvailableActions.Contains(action, StringComparer.Ordinal);
}

public sealed record ReviewerCandidateDto(Guid UserId, string DisplayName, string Login, ProjectRole Role);

public sealed record SubmitForReviewRequest(Guid VersionId, IReadOnlyList<Guid> ReviewerUserIds, DateTime? DueAtUtc = null, string? Comment = null);

public sealed record ReviewDecisionRequest(Guid VersionId, ReviewDecision Decision, string? Comment);

public sealed record ReleaseRequest(Guid VersionId, string? Comment = null);

public sealed record CancelTaskRequest(string? Comment = null);

public sealed record WorkflowTaskListQuery(
    WorkflowTaskStatus? Status = null,
    WorkflowTaskType? Type = null,
    Guid? ProjectId = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>Сводка согласования для главной страницы.</summary>
public sealed record WorkflowSummaryDto(int MyOpenReviews, int MyOpenReworks, int DocumentsInReview, int DocumentsAwaitingRelease);
