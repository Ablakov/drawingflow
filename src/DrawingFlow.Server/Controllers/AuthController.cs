using DrawingFlow.Application.Auth;
using DrawingFlow.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrawingFlow.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public Task<AuthResponse> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => authService.LoginAsync(request, cancellationToken);

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public Task<AuthResponse> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
        => authService.RefreshAsync(request, cancellationToken);

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("password")]
    public Task<AuthResponse> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
        => authService.ChangePasswordAsync(request, cancellationToken);
}

public static class RateLimitPolicies
{
    public const string Login = "login";
}
