using DrawingFlow.Domain.Common;

namespace DrawingFlow.Domain.Entities;

/// <summary>
/// Refresh-токен пользователя. В БД хранится только SHA-256 хеш; сам токен выдаётся клиенту один раз.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? ClientDescription { get; private set; }

    public User? User { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTime nowUtc, TimeSpan lifetime, string? clientDescription)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Хеш токена обязателен.", nameof(tokenHash));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        Guard.Utc(nowUtc, nameof(nowUtc));
        return new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(lifetime),
            ClientDescription = Guard.Optional(clientDescription, "clientDescription", "Описание клиента", 200),
        };
    }

    public void Revoke(DateTime nowUtc, string? replacedByTokenHash = null)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
