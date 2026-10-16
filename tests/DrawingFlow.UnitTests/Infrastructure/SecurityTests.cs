using DrawingFlow.Application.Abstractions;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Infrastructure.Options;
using DrawingFlow.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DrawingFlow.UnitTests.Infrastructure;

public sealed class SecurityTests
{
    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static JwtOptions ValidOptions() => new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "0123456789abcdef0123456789abcdef-test",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 7,
    };

    [Fact]
    public void Password_hasher_round_trip()
    {
        var hasher = new IdentityPasswordHasher();
        var hash = hasher.Hash("Ivanov12345");

        Assert.NotEqual("Ivanov12345", hash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(hash, "Ivanov12345"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(hash, "ivanov12345"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify("garbage", "Ivanov12345"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify("", "Ivanov12345"));
    }

    [Fact]
    public async Task Access_token_contains_expected_claims_and_validates()
    {
        var options = ValidOptions();
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options), new FixedClock(Now));
        var user = User.Create("ivanov", "Иванов И.И.", null, "hash", isServerAdministrator: true, Now);

        var token = service.CreateAccessToken(user);
        Assert.Equal(Now.AddMinutes(15), token.ExpiresAtUtc);

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(options),
            ValidateLifetime = false,
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(user.Id.ToString(), result.ClaimsIdentity.FindFirst(DrawingFlowClaims.Subject)!.Value);
        Assert.Equal("ivanov", result.ClaimsIdentity.FindFirst(DrawingFlowClaims.Login)!.Value);
        Assert.Equal("true", result.ClaimsIdentity.FindFirst(DrawingFlowClaims.ServerAdministrator)!.Value);
        Assert.Equal(DrawingFlowClaims.PrincipalTypeUser, result.ClaimsIdentity.FindFirst(DrawingFlowClaims.PrincipalType)!.Value);
    }

    [Fact]
    public async Task Token_signed_with_other_key_is_rejected()
    {
        var options = ValidOptions();
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options), new FixedClock(Now));
        var token = service.CreateAccessToken(User.Create("ivanov", "Иванов", null, "hash", false, Now));

        var other = ValidOptions();
        other.SigningKey = "another-key-another-key-another-key-123";
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(other),
            ValidateLifetime = false,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Refresh_token_values_are_random_and_hashes_are_stable()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(ValidOptions()), new FixedClock(Now));
        var first = service.CreateRefreshTokenValue();
        var second = service.CreateRefreshTokenValue();

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 43);
        Assert.Equal(service.HashRefreshToken(first), service.HashRefreshToken(first));
        Assert.NotEqual(service.HashRefreshToken(first), service.HashRefreshToken(second));
        Assert.Equal(64, service.HashRefreshToken(first).Length);
        Assert.Equal(TimeSpan.FromDays(7), service.RefreshTokenLifetime);
    }

    [Fact]
    public void Jwt_options_validation_rejects_short_key()
    {
        var options = ValidOptions();
        options.SigningKey = "short";
        Assert.Throws<InvalidOperationException>(options.Validate);
        options.SigningKey = "0123456789abcdef0123456789abcdef";
        options.Validate();
    }
}
