using System.Net;
using System.Text;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.IntegrationTests.Infrastructure;

namespace DrawingFlow.IntegrationTests.Api;

/// <summary>Этап 4: полный цикл согласования и выпуска, права, обязательный комментарий, сброс согласования новой версией.</summary>
public sealed class WorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<DocumentVersionDto> UploadAsync(DrawingFlowApiClient client, Guid documentId, string content)
        => client.UploadDocumentVersionAsync(documentId, new MemoryStream(Encoding.UTF8.GetBytes(content)), "план.dwg", null, null, Guid.NewGuid().ToString("N"));

    private static async Task<Guid> UserIdAsync(DrawingFlowApiClient client) => (await client.GetCurrentUserAsync()).User.Id;

    [Fact]
    public async Task Full_review_and_release_cycle()
    {
        var s = await Scenario.CreateAsync(factory);
        var reviewerId = await UserIdAsync(s.Reviewer);
        var engineerId = await UserIdAsync(s.Engineer);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("СГ-1", "Согласуемый"));
        var v1 = await UploadAsync(s.Engineer, document.Id, "версия 1");

        // Кандидаты: проверяющий, руководитель проекта, администраторы организации; инженер и наблюдатель — нет.
        var candidates = await s.Engineer.GetReviewerCandidatesAsync(s.Project.Id);
        Assert.Contains(candidates, c => c.UserId == reviewerId && c.Role == ProjectRole.Reviewer);
        Assert.Contains(candidates, c => c.Login == s.Login("manager"));
        Assert.Contains(candidates, c => c.Login == s.Login("orgadmin"));
        Assert.DoesNotContain(candidates, c => c.UserId == engineerId);

        var before = await s.Engineer.GetDocumentWorkflowAsync(document.Id);
        Assert.Equal(new[] { WorkflowActions.Submit }, before.AvailableActions);
        Assert.Empty((await s.Viewer.GetDocumentWorkflowAsync(document.Id)).AvailableActions);

        // Отправка на проверку.
        var submitted = await s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [reviewerId], DateTime.UtcNow.AddDays(2), "Проверьте отметки"));
        Assert.Equal(DocumentStatus.InReview, submitted.Document.Status);
        Assert.Equal(VersionStatus.InReview, submitted.Document.CurrentVersion!.Status);
        var reviewTask = Assert.Single(submitted.Tasks);
        Assert.Equal(WorkflowTaskType.Review, reviewTask.Type);
        Assert.Equal(reviewerId, reviewTask.AssigneeUserId);
        Assert.Equal("Проверьте отметки", reviewTask.Comment);

        var myTasks = await s.Reviewer.GetMyTasksAsync(s.Organization.Id, new WorkflowTaskListQuery(Status: WorkflowTaskStatus.Open));
        Assert.Single(myTasks.Items);
        Assert.Equal(1, (await s.Reviewer.GetWorkflowSummaryAsync(s.Organization.Id)).MyOpenReviews);
        Assert.Contains(WorkflowActions.Approve, (await s.Reviewer.GetDocumentWorkflowAsync(document.Id)).AvailableActions);
        Assert.Empty((await s.Engineer.GetDocumentWorkflowAsync(document.Id)).AvailableActions);

        // Возврат на доработку требует комментария; решение принимает назначенный проверяющий.
        var noComment = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => s.Reviewer.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.RevisionRequired, "")));
        Assert.True(noComment.Errors!.ContainsKey("comment"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.Approved, null)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.Approved, null)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.Approved, null)));

        var revised = await s.Reviewer.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.RevisionRequired, "Нет отметок уровней"));
        Assert.Equal(DocumentStatus.RevisionRequired, revised.Document.Status);
        Assert.Equal(VersionStatus.RevisionRequired, revised.Document.CurrentVersion!.Status);
        var review = Assert.Single(revised.Reviews);
        Assert.Equal(ReviewDecision.RevisionRequired, review.Decision);
        Assert.Equal("Нет отметок уровней", review.Comment);
        Assert.Equal(WorkflowTaskStatus.Completed, revised.Tasks.Single(t => t.Type == WorkflowTaskType.Review).Status);
        var rework = revised.Tasks.Single(t => t.Type == WorkflowTaskType.Rework);
        Assert.Equal(engineerId, rework.AssigneeUserId);
        Assert.Equal(WorkflowTaskStatus.Open, rework.Status);
        Assert.Equal(1, (await s.Engineer.GetWorkflowSummaryAsync(s.Organization.Id)).MyOpenReworks);

        // Повторная отправка той же версии невозможна; новая версия сбрасывает согласование и закрывает задачи.
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [reviewerId])));
        var v2 = await UploadAsync(s.Engineer, document.Id, "версия 2");
        var afterUpload = await s.Engineer.GetDocumentWorkflowAsync(document.Id);
        Assert.Equal(DocumentStatus.InProgress, afterUpload.Document.Status);
        Assert.Equal(WorkflowTaskStatus.Cancelled, afterUpload.Tasks.Single(t => t.Id == rework.Id).Status);
        var versions = await s.Engineer.GetDocumentVersionsAsync(document.Id);
        Assert.Equal(VersionStatus.Superseded, versions.Single(v => v.Id == v1.Id).Status);
        Assert.Equal(VersionStatus.Registered, versions.Single(v => v.Id == v2.Id).Status);

        // Второй цикл: согласование и выпуск.
        await s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v2.Id, [reviewerId]));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Manager.ReleaseDocumentAsync(document.Id, new ReleaseRequest(v2.Id)));
        var approved = await s.Reviewer.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v2.Id, ReviewDecision.Approved, "Принято"));
        Assert.Equal(DocumentStatus.Approved, approved.Document.Status);
        Assert.Equal(2, approved.Reviews.Count);
        Assert.Contains(WorkflowActions.Release, (await s.Manager.GetDocumentWorkflowAsync(document.Id)).AvailableActions);
        Assert.Empty((await s.Engineer.GetDocumentWorkflowAsync(document.Id)).AvailableActions);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.ReleaseDocumentAsync(document.Id, new ReleaseRequest(v2.Id)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Reviewer.ReleaseDocumentAsync(document.Id, new ReleaseRequest(v2.Id)));
        var released = await s.Manager.ReleaseDocumentAsync(document.Id, new ReleaseRequest(v2.Id, "Выпуск в производство"));
        Assert.Equal(DocumentStatus.Released, released.Document.Status);
        Assert.Equal(VersionStatus.Released, released.Document.CurrentVersion!.Status);
        Assert.Empty(released.AvailableActions);

        // Выпущенная версия остаётся в истории: новая версия не меняет её статус.
        var v3 = await UploadAsync(s.Engineer, document.Id, "версия 3");
        var afterRelease = await s.Viewer.GetDocumentVersionsAsync(document.Id);
        Assert.Equal(VersionStatus.Released, afterRelease.Single(v => v.Id == v2.Id).Status);
        Assert.Equal(VersionStatus.Registered, afterRelease.Single(v => v.Id == v3.Id).Status);
        Assert.Equal(DocumentStatus.InProgress, (await s.Viewer.GetDocumentAsync(document.Id)).Status);
        Assert.Equal(2, (await s.Viewer.GetDocumentWorkflowAsync(document.Id)).Reviews.Count);

        var audit = await s.Owner.GetAuditAsync(s.Organization.Id, new AuditListQuery(ProjectId: s.Project.Id, PageSize: 100));
        Assert.Equal(2, audit.Items.Count(e => e.Action == "document.submitted_for_review"));
        Assert.Single(audit.Items, e => e.Action == "document.revision_requested");
        Assert.Single(audit.Items, e => e.Action == "document.approved");
        Assert.Single(audit.Items, e => e.Action == "document.released");
    }

    [Fact]
    public async Task Project_manager_may_decide_without_assignment_but_unassigned_reviewer_may_not()
    {
        var s = await Scenario.CreateAsync(factory);
        var managerId = await UserIdAsync(s.Manager);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("РП-1", "Решение руководителя"));
        var v1 = await UploadAsync(s.Engineer, document.Id, "v1");
        await s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [managerId]));

        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Reviewer.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.Approved, null)));
        Assert.Contains("назначенный проверяющий", ex.Message);
        Assert.Empty((await s.Reviewer.GetDocumentWorkflowAsync(document.Id)).AvailableActions);

        // Администратор организации действует как руководитель проекта и может решать без назначения.
        var approved = await s.OrgAdmin.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.Approved, null));
        Assert.Equal(DocumentStatus.Approved, approved.Document.Status);
        Assert.Equal(WorkflowTaskStatus.Cancelled, approved.Tasks.Single().Status);
    }

    [Fact]
    public async Task Invalid_transitions_and_requests_are_rejected()
    {
        var s = await Scenario.CreateAsync(factory);
        var reviewerId = await UserIdAsync(s.Reviewer);
        var viewerId = await UserIdAsync(s.Viewer);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("НП-1", "Недопустимые переходы"));

        // Без версии отправлять нечего; действий нет.
        Assert.Empty((await s.Engineer.GetDocumentWorkflowAsync(document.Id)).AvailableActions);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(Guid.NewGuid(), [reviewerId])));

        var v1 = await UploadAsync(s.Engineer, document.Id, "v1");
        var noReviewers = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [])));
        Assert.True(noReviewers.Errors!.ContainsKey("reviewerUserIds"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [viewerId])));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Manager.DecideReviewAsync(document.Id, new ReviewDecisionRequest(v1.Id, ReviewDecision.Approved, null)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Manager.ReleaseDocumentAsync(document.Id, new ReleaseRequest(v1.Id)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [reviewerId])));

        await s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [reviewerId]));
        var again = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [reviewerId])));
        Assert.Equal("domain_rule", again.ErrorCode);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Manager.ArchiveDocumentAsync(document.Id));

        // Архивный документ не участвует в согласовании.
        var archived = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("НП-2", "Архив"));
        var av = await UploadAsync(s.Engineer, archived.Id, "v1");
        await s.Manager.ArchiveDocumentAsync(archived.Id);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Engineer.SubmitForReviewAsync(archived.Id, new SubmitForReviewRequest(av.Id, [reviewerId])));
    }

    [Fact]
    public async Task Tasks_can_be_cancelled_by_creator_or_manager_and_listed_per_project()
    {
        var s = await Scenario.CreateAsync(factory);
        var reviewerId = await UserIdAsync(s.Reviewer);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ЗД-1", "Задачи"));
        var v1 = await UploadAsync(s.Engineer, document.Id, "v1");
        var submitted = await s.Engineer.SubmitForReviewAsync(document.Id, new SubmitForReviewRequest(v1.Id, [reviewerId]));
        var task = submitted.Tasks.Single();

        var projectTasks = await s.Viewer.GetProjectTasksAsync(s.Project.Id, new WorkflowTaskListQuery(Status: WorkflowTaskStatus.Open));
        Assert.Single(projectTasks.Items);
        Assert.Empty((await s.Engineer.GetMyTasksAsync(s.Organization.Id, new WorkflowTaskListQuery(Status: WorkflowTaskStatus.Open))).Items);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetProjectTasksAsync(s.Project.Id, new WorkflowTaskListQuery()));

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.CancelWorkflowTaskAsync(task.Id, new CancelTaskRequest()));
        var cancelled = await s.Engineer.CancelWorkflowTaskAsync(task.Id, new CancelTaskRequest("Проверка не нужна"));
        Assert.Equal(WorkflowTaskStatus.Cancelled, cancelled.Status);
        Assert.Equal("Проверка не нужна", cancelled.Comment);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Manager.CancelWorkflowTaskAsync(task.Id, new CancelTaskRequest()));

        // Документ остаётся на проверке; руководитель может принять решение без задачи.
        Assert.Equal(DocumentStatus.InReview, (await s.Viewer.GetDocumentAsync(document.Id)).Status);
        Assert.Contains(WorkflowActions.Approve, (await s.Manager.GetDocumentWorkflowAsync(document.Id)).AvailableActions);
        Assert.Equal(0, (await s.Reviewer.GetWorkflowSummaryAsync(s.Organization.Id)).MyOpenReviews);
        Assert.Equal(1, (await s.Manager.GetWorkflowSummaryAsync(s.Organization.Id)).DocumentsInReview);
    }
}
