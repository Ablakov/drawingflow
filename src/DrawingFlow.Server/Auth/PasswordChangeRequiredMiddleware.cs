using DrawingFlow.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Auth;

/// <summary>
/// Пользователь с временным паролем может только сменить пароль, продлить сессию, посмотреть свой профиль и выйти.
/// Признак берётся из токена; после смены пароля выдаётся новая сессия без признака.
/// </summary>
public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedPrefixes =
    [
        "/api/auth/password",
        "/api/auth/logout",
        "/api/auth/refresh",
        "/api/users/me",
        "/api/server/info",
        "/health",
    ];

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        if (currentUser.IsAuthenticated && currentUser.PasswordChangeRequired
            && !AllowedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Требуется смена пароля",
                Detail = "Вы вошли с временным паролем. Смените пароль, чтобы продолжить работу.",
                Instance = context.Request.Path,
                Extensions = { ["errorCode"] = "password_change_required", ["traceId"] = context.TraceIdentifier },
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }
}
