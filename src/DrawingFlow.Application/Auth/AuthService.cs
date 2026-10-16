using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Common;
using DrawingFlow.Contracts.Auth;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Application.Auth;

public sealed class AuthService(
    IAppDbContext db,
    IClock clock,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ICurrentUser currentUser,
    AuditService audit,
    ILogger<AuthService> logger)
{
    public const string InvalidCredentialsMessage = "Неверный логин или пароль.";
    public const string SessionInvalidMessage = "Сессия недействительна. Выполните вход заново.";
    public const string AccountBlockedMessage = "Учётная запись заблокирована. Обратитесь к администратору.";

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var login = request.Login?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (login.Length == 0 || password.Length == 0)
        {
            throw new ValidationException("login", "Введите логин и пароль.");
        }

        var normalizedLogin = Guard.NormalizeLogin(login);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedLogin == normalizedLogin, cancellationToken);

        // Для неизвестного логина всё равно выполняем проверку хеша, чтобы выровнять время ответа.
        var verification = user is null
            ? passwordHasher.Verify(passwordHasher.Hash(Guid.NewGuid().ToString("N")), password)
            : passwordHasher.Verify(user.PasswordHash, password);

        if (user is null || verification == PasswordVerificationResult.Failed || !user.IsActive)
        {
            var reason = user is null ? "unknown_login" : !user.IsActive ? "inactive" : "wrong_password";
            audit.RecordAs(
                actorName: login.Length > AuditEvent.ActorNameMaxLength ? login[..AuditEvent.ActorNameMaxLength] : login,
                actorUserId: user?.Id,
                AuditActions.UserLoginFailed,
                AuditEntityTypes.User,
                user?.Id.ToString(),
                details: new { reason });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Неудачная попытка входа ({Reason}) для логина {Login}", reason, login);
            throw new UnauthorizedException(user is { IsActive: false } && verification != PasswordVerificationResult.Failed
                ? AccountBlockedMessage
                : InvalidCredentialsMessage);
        }

        var now = clock.UtcNow;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.RehashPassword(passwordHasher.Hash(password));
        }

        user.RecordLogin(now);
        var response = IssueSession(user, request.ClientDescription, now);
        audit.RecordAs(user.DisplayName, user.Id, AuditActions.UserLoggedIn, AuditEntityTypes.User, user.Id.ToString(),
            details: new { client = request.ClientDescription });
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new UnauthorizedException(SessionInvalidMessage);
        }

        var hash = tokenService.HashRefreshToken(request.RefreshToken.Trim());
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = clock.UtcNow;

        if (token?.User is null)
        {
            throw new UnauthorizedException(SessionInvalidMessage);
        }

        if (!token.IsActive(now))
        {
            if (token.RevokedAtUtc is not null && token.ReplacedByTokenHash is not null)
            {
                // Повторное предъявление уже обменянного токена — признак утечки: отзываем все сессии пользователя.
                await RevokeAllActiveTokensAsync(token.UserId, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Повторное использование refresh-токена пользователем {UserId}; все сессии отозваны", token.UserId);
            }

            throw new UnauthorizedException(SessionInvalidMessage);
        }

        if (!token.User.IsActive)
        {
            throw new UnauthorizedException(AccountBlockedMessage);
        }

        var response = IssueSession(token.User, token.ClientDescription, now);
        token.Revoke(now, tokenService.HashRefreshToken(response.RefreshToken));
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var hash = tokenService.HashRefreshToken(request.RefreshToken.Trim());
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var now = clock.UtcNow;
        if (token is null || !token.IsActive(now))
        {
            return;
        }

        token.Revoke(now);
        audit.RecordAs(token.User?.DisplayName ?? token.UserId.ToString(), token.UserId, AuditActions.UserLoggedOut,
            AuditEntityTypes.User, token.UserId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Смена пароля текущим пользователем. Все прежние сессии отзываются, возвращается новая.</summary>
    public async Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException();

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            throw new ValidationException("currentPassword", "Текущий пароль указан неверно.");
        }

        Guard.Password(request.NewPassword);
        var now = clock.UtcNow;
        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword!), now);
        await RevokeAllActiveTokensAsync(userId, now, cancellationToken);
        var response = IssueSession(user, "password_change", now);
        audit.Record(AuditActions.UserPasswordChanged, AuditEntityTypes.User, userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return response;
    }

    private AuthResponse IssueSession(User user, string? clientDescription, DateTime now)
    {
        var refreshValue = tokenService.CreateRefreshTokenValue();
        var refreshToken = RefreshToken.Issue(user.Id, tokenService.HashRefreshToken(refreshValue), now, tokenService.RefreshTokenLifetime, clientDescription);
        db.RefreshTokens.Add(refreshToken);
        var accessToken = tokenService.CreateAccessToken(user);
        return new AuthResponse(accessToken.Value, accessToken.ExpiresAtUtc, refreshValue, refreshToken.ExpiresAtUtc, user.ToDto());
    }

    private async Task RevokeAllActiveTokensAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.Revoke(now);
        }
    }
}
