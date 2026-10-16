using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Application.Abstractions;

/// <summary>Сведения о текущем субъекте запроса (пользователь). Заполняется хостом из токена.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Login { get; }
    string? DisplayName { get; }
    bool IsServerAdministrator { get; }
    string? ClientAddress { get; }
    /// <summary>Токен выдан пользователю с временным паролем: до смены пароля операции ограничены.</summary>
    bool PasswordChangeRequired { get; }

    /// <summary>Запрос выполняет агент (служба на рабочей станции), а не пользователь.</summary>
    bool IsAgent { get; }
    Guid? AgentId { get; }
    Guid? AgentOrganizationId { get; }
}

public static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw currentUser.IsAgent
                ? new ForbiddenException("Операция недоступна агенту синхронизации.")
                : new UnauthorizedException();
        }

        return currentUser.UserId.Value;
    }

    public static Guid RequireAgentId(this ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsAgent || currentUser.AgentId is null)
        {
            throw new UnauthorizedException("Операция доступна только агенту синхронизации.");
        }

        return currentUser.AgentId.Value;
    }

    public static void RequireServerAdministrator(this ICurrentUser currentUser)
    {
        currentUser.RequireUserId();
        if (!currentUser.IsServerAdministrator)
        {
            throw new ForbiddenException("Операция доступна только администратору сервера.");
        }
    }
}
