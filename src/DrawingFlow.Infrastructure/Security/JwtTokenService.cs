using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DrawingFlow.Infrastructure.Security;

public static class DrawingFlowClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Login = "login";
    public const string DisplayName = "display_name";
    public const string ServerAdministrator = "server_admin";
    public const string PrincipalType = "principal_type";
    public const string PrincipalTypeUser = "user";
    public const string PrincipalTypeAgent = "agent";
    public const string OrganizationId = "org";
    public const string PasswordChangeRequired = "pwd_change";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenLifetimeDays);

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));

    public AccessToken CreateAccessToken(User user)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(DrawingFlowClaims.Subject, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(DrawingFlowClaims.Login, user.Login),
                new Claim(DrawingFlowClaims.DisplayName, user.DisplayName),
                new Claim(DrawingFlowClaims.ServerAdministrator, user.IsServerAdministrator ? "true" : "false"),
                new Claim(DrawingFlowClaims.PasswordChangeRequired, user.MustChangePassword ? "true" : "false"),
                new Claim(DrawingFlowClaims.PrincipalType, DrawingFlowClaims.PrincipalTypeUser),
            ]),
            SigningCredentials = new SigningCredentials(CreateSigningKey(_options), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }

    public AccessToken CreateAgentAccessToken(AgentInstallation agent)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(DrawingFlowClaims.Subject, agent.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(DrawingFlowClaims.DisplayName, agent.Name),
                new Claim(DrawingFlowClaims.OrganizationId, agent.OrganizationId.ToString()),
                new Claim(DrawingFlowClaims.PrincipalType, DrawingFlowClaims.PrincipalTypeAgent),
            ]),
            SigningCredentials = new SigningCredentials(CreateSigningKey(_options), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }

    public string CreateAgentKeyValue() => CreateRefreshTokenValue();

    public string HashAgentKey(string agentKey) => HashRefreshToken(agentKey);

    public string CreateRefreshTokenValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    public string HashRefreshToken(string refreshTokenValue)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenValue));
        return Convert.ToHexStringLower(hash);
    }
}
