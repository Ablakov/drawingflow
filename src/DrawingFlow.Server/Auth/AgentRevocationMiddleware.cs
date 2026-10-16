using DrawingFlow.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Server.Auth;

/// <summary>
/// Отзыв ключа агента действует немедленно: при каждом запросе агента проверяется, что установка существует и не отозвана,
/// даже если срок действия токена ещё не истёк.
/// </summary>
public sealed class AgentRevocationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IAppDbContext db)
    {
        if (currentUser.IsAgent && currentUser.AgentId is Guid agentId)
        {
            var revoked = await db.AgentInstallations.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => (bool?)(a.RevokedAtUtc != null))
                .FirstOrDefaultAsync(context.RequestAborted);
            if (revoked is null or true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Ключ агента отозван",
                    Detail = "Ключ этой установки агента отозван администратором. Зарегистрируйте агента заново.",
                    Instance = context.Request.Path,
                    Extensions = { ["errorCode"] = "agent_revoked", ["traceId"] = context.TraceIdentifier },
                }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
                return;
            }
        }

        await next(context);
    }
}
