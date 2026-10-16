namespace DrawingFlow.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSigningKeyLength = 32;

    public string Issuer { get; set; } = "DrawingFlow";
    public string Audience { get; set; } = "DrawingFlow.Clients";
    /// <summary>Секретный ключ подписи (HMAC-SHA256), не менее 32 символов. Задаётся через конфигурацию/переменную окружения Jwt__SigningKey.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || SigningKey.Length < MinSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Параметр {SectionName}:SigningKey должен содержать не менее {MinSigningKeyLength} символов. Задайте его в конфигурации или переменной окружения Jwt__SigningKey.");
        }

        if (AccessTokenLifetimeMinutes is < 1 or > 24 * 60)
        {
            throw new InvalidOperationException($"Параметр {SectionName}:AccessTokenLifetimeMinutes должен быть в диапазоне 1–1440.");
        }

        if (RefreshTokenLifetimeDays is < 1 or > 365)
        {
            throw new InvalidOperationException($"Параметр {SectionName}:RefreshTokenLifetimeDays должен быть в диапазоне 1–365.");
        }
    }
}
