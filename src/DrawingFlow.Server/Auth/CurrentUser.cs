using System.Security.Claims;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Infrastructure.Security;

namespace DrawingFlow.Server.Auth;

/// <summary>Текущий субъект запроса (пользователь или агент) из утверждений JWT.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    private string? PrincipalType => Principal?.FindFirstValue(DrawingFlowClaims.PrincipalType);

    private Guid? Subject
    {
        get
        {
            var value = Principal?.FindFirstValue(DrawingFlowClaims.Subject);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && Subject is not null;

    public bool IsAgent => string.Equals(PrincipalType, DrawingFlowClaims.PrincipalTypeAgent, StringComparison.Ordinal);

    public Guid? UserId => IsAgent ? null : Subject;

    public Guid? AgentId => IsAgent ? Subject : null;

    public Guid? AgentOrganizationId
    {
        get
        {
            var value = Principal?.FindFirstValue(DrawingFlowClaims.OrganizationId);
            return IsAgent && Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Login => Principal?.FindFirstValue(DrawingFlowClaims.Login);

    public string? DisplayName => Principal?.FindFirstValue(DrawingFlowClaims.DisplayName);

    public bool IsServerAdministrator =>
        !IsAgent && string.Equals(Principal?.FindFirstValue(DrawingFlowClaims.ServerAdministrator), "true", StringComparison.OrdinalIgnoreCase);

    public string? ClientAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool PasswordChangeRequired =>
        !IsAgent && string.Equals(Principal?.FindFirstValue(DrawingFlowClaims.PasswordChangeRequired), "true", StringComparison.OrdinalIgnoreCase);
}
