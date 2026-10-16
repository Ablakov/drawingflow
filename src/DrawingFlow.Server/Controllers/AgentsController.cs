using DrawingFlow.Application.Agents;
using DrawingFlow.Application.Sync;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Controllers;

/// <summary>Администрирование агентов, папок наблюдения и очереди синхронизации (от имени пользователей).</summary>
[ApiController]
public sealed class AgentsController(AgentService agents, WatchedFolderService folders, SyncOperationService operations) : ControllerBase
{
    // ---------- Агенты ----------

    [HttpGet("api/organizations/{organizationId:guid}/agents")]
    public Task<IReadOnlyList<AgentDto>> List(Guid organizationId, CancellationToken cancellationToken)
        => agents.ListAsync(organizationId, cancellationToken);

    [HttpPost("api/organizations/{organizationId:guid}/agents")]
    public async Task<ActionResult<AgentRegistrationResultDto>> Register(Guid organizationId, [FromBody] RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        var result = await agents.RegisterAsync(organizationId, request, cancellationToken);
        return Created($"/api/agents/{result.Agent.Id}", result);
    }

    [HttpPost("api/agents/{agentId:guid}/revoke")]
    public Task<AgentDto> Revoke(Guid agentId, CancellationToken cancellationToken)
        => agents.RevokeAsync(agentId, cancellationToken);

    [HttpGet("api/organizations/{organizationId:guid}/sync-summary")]
    public Task<SyncSummaryDto> Summary(Guid organizationId, CancellationToken cancellationToken)
        => agents.GetSummaryAsync(organizationId, cancellationToken);

    // ---------- Папки наблюдения ----------

    [HttpGet("api/agents/{agentId:guid}/folders")]
    public Task<IReadOnlyList<WatchedFolderDto>> ListFolders(Guid agentId, CancellationToken cancellationToken)
        => folders.ListForAgentAsync(agentId, cancellationToken);

    [HttpPost("api/agents/{agentId:guid}/folders")]
    public async Task<ActionResult<WatchedFolderDto>> CreateFolder(Guid agentId, [FromBody] SaveWatchedFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await folders.CreateAsync(agentId, request, cancellationToken);
        return Created($"/api/watched-folders/{folder.Id}", folder);
    }

    [HttpPut("api/watched-folders/{folderId:guid}")]
    public Task<WatchedFolderDto> UpdateFolder(Guid folderId, [FromBody] SaveWatchedFolderRequest request, CancellationToken cancellationToken)
        => folders.UpdateAsync(folderId, request, cancellationToken);

    [HttpDelete("api/watched-folders/{folderId:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid folderId, CancellationToken cancellationToken)
    {
        await folders.DeleteAsync(folderId, cancellationToken);
        return NoContent();
    }

    // ---------- Очередь синхронизации ----------

    [HttpGet("api/organizations/{organizationId:guid}/sync-operations")]
    public Task<PagedResult<SyncOperationDto>> ListOperations(Guid organizationId, [FromQuery] SyncOperationListQuery query, CancellationToken cancellationToken)
        => operations.ListAsync(organizationId, query, cancellationToken);

    [HttpPost("api/sync-operations/{operationId:guid}/resolve")]
    public Task<SyncOperationDto> Resolve(Guid operationId, [FromBody] ResolveSyncOperationRequest request, CancellationToken cancellationToken)
        => operations.ResolveAsync(operationId, request, cancellationToken);

    [HttpPost("api/sync-operations/{operationId:guid}/retry")]
    public Task<SyncOperationDto> Retry(Guid operationId, CancellationToken cancellationToken)
        => operations.RetryAsync(operationId, cancellationToken);

    [HttpPost("api/sync-operations/{operationId:guid}/dismiss")]
    public Task<SyncOperationDto> Dismiss(Guid operationId, CancellationToken cancellationToken)
        => operations.DismissAsync(operationId, cancellationToken);
}
