using DrawingFlow.Application.Workflow;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Controllers;

/// <summary>Согласование документов: отправка на проверку, решения, выпуск, задачи пользователей.</summary>
[ApiController]
public sealed class WorkflowController(WorkflowService workflow) : ControllerBase
{
    [HttpGet("api/documents/{documentId:guid}/workflow")]
    public Task<DocumentWorkflowDto> Get(Guid documentId, CancellationToken cancellationToken)
        => workflow.GetAsync(documentId, cancellationToken);

    [HttpPost("api/documents/{documentId:guid}/workflow/submit")]
    public Task<DocumentWorkflowDto> Submit(Guid documentId, [FromBody] SubmitForReviewRequest request, CancellationToken cancellationToken)
        => workflow.SubmitAsync(documentId, request, cancellationToken);

    [HttpPost("api/documents/{documentId:guid}/workflow/decide")]
    public Task<DocumentWorkflowDto> Decide(Guid documentId, [FromBody] ReviewDecisionRequest request, CancellationToken cancellationToken)
        => workflow.DecideAsync(documentId, request, cancellationToken);

    [HttpPost("api/documents/{documentId:guid}/workflow/release")]
    public Task<DocumentWorkflowDto> Release(Guid documentId, [FromBody] ReleaseRequest request, CancellationToken cancellationToken)
        => workflow.ReleaseAsync(documentId, request, cancellationToken);

    [HttpGet("api/projects/{projectId:guid}/reviewers")]
    public Task<IReadOnlyList<ReviewerCandidateDto>> Reviewers(Guid projectId, CancellationToken cancellationToken)
        => workflow.GetReviewerCandidatesAsync(projectId, cancellationToken);

    [HttpGet("api/projects/{projectId:guid}/tasks")]
    public Task<PagedResult<WorkflowTaskDto>> ProjectTasks(Guid projectId, [FromQuery] WorkflowTaskListQuery query, CancellationToken cancellationToken)
        => workflow.ListProjectTasksAsync(projectId, query, cancellationToken);

    [HttpGet("api/organizations/{organizationId:guid}/tasks")]
    public Task<PagedResult<WorkflowTaskDto>> MyTasks(Guid organizationId, [FromQuery] WorkflowTaskListQuery query, CancellationToken cancellationToken)
        => workflow.ListMyTasksAsync(organizationId, query, cancellationToken);

    [HttpGet("api/organizations/{organizationId:guid}/workflow/summary")]
    public Task<WorkflowSummaryDto> Summary(Guid organizationId, CancellationToken cancellationToken)
        => workflow.GetSummaryAsync(organizationId, cancellationToken);

    [HttpPost("api/workflow-tasks/{taskId:guid}/cancel")]
    public Task<WorkflowTaskDto> CancelTask(Guid taskId, [FromBody] CancelTaskRequest request, CancellationToken cancellationToken)
        => workflow.CancelTaskAsync(taskId, request, cancellationToken);
}
