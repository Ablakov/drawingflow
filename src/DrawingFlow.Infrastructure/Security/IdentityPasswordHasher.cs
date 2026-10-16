using DrawingFlow.Application.Abstractions;
using DrawingFlow.Domain.Entities;
using Identity = Microsoft.AspNetCore.Identity;

namespace DrawingFlow.Infrastructure.Security;

/// <summary>
/// Хеширование паролей через проверенную реализацию ASP.NET Core Identity (PBKDF2-HMAC-SHA512, формат V3).
/// Полный стек Identity (UserManager, cookie-аутентификация) не используется: клиентам нужны токены.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly Identity.PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public PasswordVerificationResult Verify(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || providedPassword is null)
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            return _inner.VerifyHashedPassword(null!, hashedPassword, providedPassword) switch
            {
                Identity.PasswordVerificationResult.Success => PasswordVerificationResult.Success,
                Identity.PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationResult.SuccessRehashNeeded,
                _ => PasswordVerificationResult.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}
