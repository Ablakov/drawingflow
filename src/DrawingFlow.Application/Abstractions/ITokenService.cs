using DrawingFlow.Domain.Entities;

namespace DrawingFlow.Application.Abstractions;

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>Генерирует криптографически случайное значение refresh-токена (выдаётся клиенту).</summary>
    string CreateRefreshTokenValue();

    /// <summary>Хеш refresh-токена для хранения в БД (SHA-256).</summary>
    string HashRefreshToken(string refreshTokenValue);

    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAgentAccessToken(AgentInstallation agent);

    /// <summary>Генерирует ключ установки агента (выдаётся один раз).</summary>
    string CreateAgentKeyValue();

    /// <summary>Хеш ключа агента для хранения в БД (SHA-256).</summary>
    string HashAgentKey(string agentKey);
}
