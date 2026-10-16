using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Application.Documents;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.Application.Workflow;

/// <summary>
/// Согласование документов: отправка на проверку, решения проверяющих, выпуск, задачи.
/// Решение всегда относится к конкретной (текущей) версии; новая версия сбрасывает согласование и отменяет открытые задачи.
/// Модель решения — одиночная: первое решение назначенного проверяющего (или руководителя проекта) определяет исход.
/// </summary>
public sealed class WorkflowService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit,
    DocumentService documents)
{
    public const string TaskNotFound = "Задача не найдена.";

    // ---------- Чтение ----------

    public async Task<DocumentWorkflowDto> GetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(documentId, cancellationToken);
        var projectAccess = await access.RequireProjectAsync(document.ProjectId, ProjectPermission.View, cancellationToken);
        return await BuildAsync(document, projectAccess, cancellationToken);
    }

    /// <summary>Кто может быть назначен проверяющим: участники проекта с ролью «Проверяющий» или «Руководитель проекта», а также администраторы организации.</summary>
    public async Task<IReadOnlyList<ReviewerCandidateDto>> GetReviewerCandidatesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        var members = await db.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId && (m.Role == Dom.ProjectRole.Reviewer || m.Role == Dom.ProjectRole.ProjectManager) && m.User!.IsActive)
            .Select(m => new { m.UserId, m.User!.DisplayName, m.User.Login, m.Role })
            .ToListAsync(cancellationToken);
        var admins = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == projectAccess.Project.OrganizationId
                        && (m.Role == Dom.OrganizationRole.Owner || m.Role == Dom.OrganizationRole.OrganizationAdmin) && m.User!.IsActive)
            .Select(m => new { m.UserId, m.User!.DisplayName, m.User.Login })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, ReviewerCandidateDto>();
        foreach (var member in members)
        {
            result[member.UserId] = new ReviewerCandidateDto(member.UserId, member.DisplayName, member.Login, member.Role.ToApi());
        }

        foreach (var admin in admins)
        {
            result.TryAdd(admin.UserId, new ReviewerCandidateDto(admin.UserId, admin.DisplayName, admin.Login, ProjectRole.ProjectManager));
        }

        return result.Values.OrderBy(c => c.DisplayName).ToList();
    }

    public async Task<PagedResult<WorkflowTaskDto>> ListMyTasksAsync(Guid organizationId, WorkflowTaskListQuery query, CancellationToken cancellationToken)
    {
        await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        var userId = currentUser.RequireUserId();
        var tasks = db.WorkflowTasks.AsNoTracking().Where(t => t.AssigneeUserId == userId && t.Project!.OrganizationId == organizationId);
        return await PageAsync(ApplyFilters(tasks, query), query, cancellationToken);
    }

    public async Task<PagedResult<WorkflowTaskDto>> ListProjectTasksAsync(Guid projectId, WorkflowTaskListQuery query, CancellationToken cancellationToken)
    {
        await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        var tasks = db.WorkflowTasks.AsNoTracking().Where(t => t.ProjectId == projectId);
        return await PageAsync(ApplyFilters(tasks, query), query, cancellationToken);
    }

    public async Task<WorkflowSummaryDto> GetSummaryAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        var userId = currentUser.RequireUserId();

        var myOpen = await db.WorkflowTasks.AsNoTracking()
            .Where(t => t.AssigneeUserId == userId && t.Status == Dom.WorkflowTaskStatus.Open && t.Project!.OrganizationId == organizationId)
            .GroupBy(t => t.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        IQueryable<Document> visible = db.Documents.AsNoTracking().Where(d => d.Project!.OrganizationId == organizationId);
        if (!organizationAccess.Permissions.Has(OrganizationPermission.ViewAllProjects))
        {
            var myProjects = await db.ProjectMemberships.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.ProjectId).ToListAsync(cancellationToken);
            visible = visible.Where(d => myProjects.Contains(d.ProjectId));
        }

        var statuses = await visible
            .Where(d => d.Status == Dom.DocumentStatus.InReview || d.Status == Dom.DocumentStatus.Approved)
            .GroupBy(d => d.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new WorkflowSummaryDto(
            myOpen.FirstOrDefault(g => g.Key == Dom.WorkflowTaskType.Review)?.Count ?? 0,
            myOpen.FirstOrDefault(g => g.Key == Dom.WorkflowTaskType.Rework)?.Count ?? 0,
            statuses.FirstOrDefault(s => s.Key == Dom.DocumentStatus.InReview)?.Count ?? 0,
            statuses.FirstOrDefault(s => s.Key == Dom.DocumentStatus.Approved)?.Count ?? 0);
    }

    // ---------- Команды ----------

    public async Task<DocumentWorkflowDto> SubmitAsync(Guid documentId, SubmitForReviewRequest request, CancellationToken cancellationToken)
    {
        var (document, version, projectAccess) = await LoadAsync(documentId, request.VersionId, ProjectPermission.SubmitForReview, cancellationToken);
        var userId = currentUser.RequireUserId();
        var reviewerIds = request.ReviewerUserIds?.Distinct().ToList() ?? [];
        if (reviewerIds.Count == 0)
        {
            throw new ValidationException("reviewerUserIds", "Укажите хотя бы одного проверяющего.");
        }

        var candidates = (await GetReviewerCandidatesAsync(document.ProjectId, cancellationToken)).Select(c => c.UserId).ToHashSet();
        var invalid = reviewerIds.Where(id => !candidates.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            throw new ValidationException("reviewerUserIds", "Среди выбранных есть пользователи, которые не могут быть проверяющими в этом проекте.");
        }

        var now = clock.UtcNow;
        document.SubmitForReview(version, now);
        await CancelOpenTasksAsync(documentId, userId, "Документ повторно отправлен на проверку", now, cancellationToken);

        var dueAtUtc = Paging.ToUtc(request.DueAtUtc);
        foreach (var reviewerId in reviewerIds)
        {
            db.WorkflowTasks.Add(WorkflowTask.Create(document.ProjectId, document.Id, version.Id, reviewerId, Dom.WorkflowTaskType.Review, userId, dueAtUtc, request.Comment, now));
        }

        audit.Record(AuditActions.DocumentSubmittedForReview, AuditEntityTypes.Document, document.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId,
            new { version.VersionNumber, reviewers = reviewerIds, request.Comment });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(documentId, cancellationToken);
    }

    public async Task<DocumentWorkflowDto> DecideAsync(Guid documentId, ReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        var (document, version, projectAccess) = await LoadAsync(documentId, request.VersionId, ProjectPermission.Review, cancellationToken);
        var userId = currentUser.RequireUserId();
        var openTasks = await db.WorkflowTasks
            .Where(t => t.DocumentId == documentId && t.Status == Dom.WorkflowTaskStatus.Open)
            .ToListAsync(cancellationToken);
        var myTask = openTasks.FirstOrDefault(t => t.Type == Dom.WorkflowTaskType.Review && t.DocumentVersionId == version.Id && t.AssigneeUserId == userId);
        var isManager = projectAccess.EffectiveRole == Dom.ProjectRole.ProjectManager;
        if (myTask is null && !isManager)
        {
            throw new ForbiddenException("Решение может принять назначенный проверяющий или руководитель проекта.");
        }

        var now = clock.UtcNow;
        Review review;
        if (request.Decision == ReviewDecision.Approved)
        {
            review = document.Approve(version, userId, request.Comment, now);
        }
        else if (request.Decision == ReviewDecision.RevisionRequired)
        {
            review = document.RequestRevision(version, userId, request.Comment, now);
        }
        else
        {
            throw new ValidationException("decision", "Недопустимое решение.");
        }

        db.Reviews.Add(review);
        myTask?.Complete(userId, now, request.Comment);
        foreach (var task in openTasks.Where(t => t.IsOpen))
        {
            task.Cancel(userId, now, "Решение по версии принято другим проверяющим");
        }

        if (review.Decision == Dom.ReviewDecision.RevisionRequired)
        {
            var submitter = await db.WorkflowTasks.AsNoTracking()
                .Where(t => t.DocumentVersionId == version.Id && t.Type == Dom.WorkflowTaskType.Review)
                .OrderByDescending(t => t.CreatedAtUtc)
                .Select(t => t.CreatedByUserId)
                .FirstOrDefaultAsync(cancellationToken);
            var assignee = version.CreatedByUserId ?? submitter ?? document.CreatedByUserId;
            db.WorkflowTasks.Add(WorkflowTask.Create(document.ProjectId, document.Id, version.Id, assignee, Dom.WorkflowTaskType.Rework, userId, null, request.Comment, now));
        }

        audit.Record(review.Decision == Dom.ReviewDecision.Approved ? AuditActions.DocumentApproved : AuditActions.DocumentRevisionRequested,
            AuditEntityTypes.Review, review.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId,
            new { documentId, version.VersionNumber, review.Comment });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(documentId, cancellationToken);
    }

    public async Task<DocumentWorkflowDto> ReleaseAsync(Guid documentId, ReleaseRequest request, CancellationToken cancellationToken)
    {
        var (document, version, projectAccess) = await LoadAsync(documentId, request.VersionId, ProjectPermission.Release, cancellationToken);
        var now = clock.UtcNow;
        document.Release(version, now);
        await CancelOpenTasksAsync(documentId, currentUser.RequireUserId(), "Документ выпущен", now, cancellationToken);
        audit.Record(AuditActions.DocumentReleased, AuditEntityTypes.Document, document.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId,
            new { version.VersionNumber, request.Comment });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(documentId, cancellationToken);
    }

    public async Task<WorkflowTaskDto> CancelTaskAsync(Guid taskId, CancelTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await db.WorkflowTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException(TaskNotFound);
        var projectAccess = await access.RequireProjectAsync(task.ProjectId, ProjectPermission.View, cancellationToken);
        var userId = currentUser.RequireUserId();
        if (projectAccess.EffectiveRole != Dom.ProjectRole.ProjectManager && task.CreatedByUserId != userId)
        {
            throw new ForbiddenException("Отменить задачу может руководитель проекта или тот, кто её создал.");
        }

        task.Cancel(userId, clock.UtcNow, request.Comment ?? "Отменена пользователем");
        audit.Record(AuditActions.WorkflowTaskCancelled, AuditEntityTypes.WorkflowTask, task.Id.ToString(), projectAccess.Project.OrganizationId, task.ProjectId, new { task.DocumentId, request.Comment });
        await db.SaveChangesAsync(cancellationToken);
        return (await PageAsync(db.WorkflowTasks.AsNoTracking().Where(t => t.Id == taskId), new WorkflowTaskListQuery(), cancellationToken)).Items[0];
    }

    // ---------- Помощники ----------

    private async Task<(Document Document, DocumentVersion Version, ProjectAccess Access)> LoadAsync(Guid documentId, Guid versionId, ProjectPermission permission, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new NotFoundException(DocumentService.DocumentNotFound);
        var projectAccess = await access.RequireProjectAsync(document.ProjectId, permission, cancellationToken);
        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.DocumentId == documentId, cancellationToken)
            ?? throw new NotFoundException(DocumentService.VersionNotFound);
        return (document, version, projectAccess);
    }

    private async Task CancelOpenTasksAsync(Guid documentId, Guid? byUserId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var open = await db.WorkflowTasks.Where(t => t.DocumentId == documentId && t.Status == Dom.WorkflowTaskStatus.Open).ToListAsync(cancellationToken);
        foreach (var task in open)
        {
            task.Cancel(byUserId, now, reason);
        }
    }

    private async Task<DocumentWorkflowDto> BuildAsync(DocumentDto document, ProjectAccess projectAccess, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var reviewRows = await db.Reviews.AsNoTracking()
            .Where(r => r.DocumentId == document.Id)
            .OrderByDescending(r => r.DecidedAtUtc)
            .Select(r => new { Review = r, r.Version!.VersionNumber, r.Reviewer!.DisplayName })
            .ToListAsync(cancellationToken);
        var reviews = reviewRows.Select(r => new ReviewDto(r.Review.Id, r.Review.DocumentId, r.Review.DocumentVersionId, r.VersionNumber, r.Review.ReviewerUserId, r.DisplayName,
            (ReviewDecision)(int)r.Review.Decision, r.Review.Comment, r.Review.DecidedAtUtc)).ToList();

        var tasks = (await PageAsync(db.WorkflowTasks.AsNoTracking().Where(t => t.DocumentId == document.Id), new WorkflowTaskListQuery(PageSize: PageRequest.MaxPageSize), cancellationToken)).Items;

        var actions = new List<string>();
        var current = document.CurrentVersion;
        if (document.Status == DocumentStatus.InProgress && current is not null && projectAccess.Permissions.Has(ProjectPermission.SubmitForReview))
        {
            actions.Add(WorkflowActions.Submit);
        }

        if (document.Status == DocumentStatus.InReview && current is not null && projectAccess.Permissions.Has(ProjectPermission.Review))
        {
            var assigned = tasks.Any(t => t.Type == WorkflowTaskType.Review && t.Status == WorkflowTaskStatus.Open && t.DocumentVersionId == current.Id && t.AssigneeUserId == userId);
            if (assigned || projectAccess.EffectiveRole == Dom.ProjectRole.ProjectManager)
            {
                actions.Add(WorkflowActions.Approve);
                actions.Add(WorkflowActions.RequestRevision);
            }
        }

        if (document.Status == DocumentStatus.Approved && current is not null && projectAccess.Permissions.Has(ProjectPermission.Release))
        {
            actions.Add(WorkflowActions.Release);
        }

        return new DocumentWorkflowDto(document, reviews, tasks, actions);
    }

    private static IQueryable<WorkflowTask> ApplyFilters(IQueryable<WorkflowTask> tasks, WorkflowTaskListQuery query)
    {
        if (query.Status is not null)
        {
            var status = (Dom.WorkflowTaskStatus)(int)query.Status.Value;
            tasks = tasks.Where(t => t.Status == status);
        }

        if (query.Type is not null)
        {
            var type = (Dom.WorkflowTaskType)(int)query.Type.Value;
            tasks = tasks.Where(t => t.Type == type);
        }

        if (query.ProjectId is not null)
        {
            tasks = tasks.Where(t => t.ProjectId == query.ProjectId);
        }

        return tasks;
    }

    private async Task<PagedResult<WorkflowTaskDto>> PageAsync(IQueryable<WorkflowTask> tasks, WorkflowTaskListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await tasks.CountAsync(cancellationToken);
        var rows = await tasks
            .OrderBy(t => t.Status).ThenByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                Task = t,
                t.Project!.Code,
                t.Document!.DocumentNumber,
                t.Document.Title,
                t.Version!.VersionNumber,
                AssigneeName = t.Assignee!.DisplayName,
            })
            .ToListAsync(cancellationToken);

        var creatorIds = rows.Where(r => r.Task.CreatedByUserId.HasValue).Select(r => r.Task.CreatedByUserId!.Value).Distinct().ToList();
        var creators = creatorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(u => creatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var items = rows.Select(r => new WorkflowTaskDto(
            r.Task.Id, r.Task.ProjectId, r.Code, r.Task.DocumentId, r.DocumentNumber, r.Title, r.Task.DocumentVersionId, r.VersionNumber,
            r.Task.AssigneeUserId, r.AssigneeName, (WorkflowTaskType)(int)r.Task.Type, (WorkflowTaskStatus)(int)r.Task.Status,
            r.Task.CreatedAtUtc, r.Task.CreatedByUserId is Guid c ? creators.GetValueOrDefault(c, string.Empty) : "Система",
            r.Task.DueAtUtc, r.Task.CompletedAtUtc, r.Task.Comment)).ToList();
        return new PagedResult<WorkflowTaskDto>(items, page, pageSize, total);
    }
}
