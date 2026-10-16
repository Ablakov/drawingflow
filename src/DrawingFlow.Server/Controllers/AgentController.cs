using DrawingFlow.Application.Agents;
using DrawingFlow.Contracts.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrawingFlow.Server.Controllers;

/// <summary>Конечные точки, которыми пользуется служба агента.</summary>
[ApiController]
public sealed class AgentController(AgentService agents) : ControllerBase
{
    /// <summary>Обмен ключа установки на короткоживущий токен агента.</summary>
    [HttpPost("api/agents/token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public Task<AgentTokenResponse> Token([FromBody] AgentTokenRequest request, CancellationToken cancellationToken)
        => agents.ExchangeKeyAsync(request, cancellationToken);

    [HttpGet("api/agent/configuration")]
    public Task<AgentConfigurationDto> Configuration(CancellationToken cancellationToken)
        => agents.GetConfigurationAsync(cancellationToken);

    [HttpPost("api/agent/heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        await agents.HeartbeatAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Пакет отчётов об операциях; в ответе — действия, запрошенные пользователями.</summary>
    [HttpPut("api/agent/operations")]
    public Task<IReadOnlyList<AgentActionDto>> Report([FromBody] List<ReportSyncOperationRequest> reports, CancellationToken cancellationToken)
        => agents.ReportOperationsAsync(reports, cancellationToken);

    [HttpGet("api/agent/operations/actions")]
    public Task<IReadOnlyList<AgentActionDto>> Actions(CancellationToken cancellationToken)
        => agents.GetActionsAsync(cancellationToken);
}
