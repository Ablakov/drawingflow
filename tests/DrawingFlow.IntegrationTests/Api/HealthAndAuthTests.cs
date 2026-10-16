using System.Net;
using System.Net.Http.Json;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Auth;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Infrastructure.Persistence;
using DrawingFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrawingFlow.IntegrationTests.Api;

public sealed class HealthAndAuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_endpoints_are_anonymous_and_healthy()
    {
        var client = factory.CreateRawClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Server_info_is_anonymous()
    {
        var info = await factory.CreateApiClient().GetServerInfoAsync();
        Assert.Equal("DrawingFlow", info.ProductName);
        Assert.NotEqual(default, info.ServerTimeUtc);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_russian_problem_details()
    {
        var response = await factory.CreateRawClient().GetAsync("/api/organizations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(ApiFactory.Json);
        Assert.Equal("unauthorized", problem!.ErrorCode);
        Assert.Equal("Требуется вход в систему.", problem.Detail);
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        var response = await factory.CreateRawClient("not-a-jwt").GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_admin_can_login_and_read_profile()
    {
        var client = await factory.LoginAdminAsync();
        var me = await client.GetCurrentUserAsync();

        Assert.Equal(ApiFactory.AdminLogin, me.User.Login);
        Assert.True(me.User.IsServerAdministrator);
        Assert.NotNull(me.User.LastLoginAtUtc);
    }

    [Fact]
    public async Task Bootstrap_is_idempotent()
    {
        using var scope = factory.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        Assert.False(await initializer.BootstrapAsync(CancellationToken.None));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedLogin == ApiFactory.AdminLogin.ToUpperInvariant()));
    }

    [Fact]
    public async Task Wrong_password_returns_401_and_is_audited()
    {
        var client = factory.CreateApiClient();
        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => client.LoginAsync(ApiFactory.AdminLogin, "wrong-password-1", null));
        Assert.Equal("Неверный логин или пароль.", ex.Message);
        Assert.Null(client.Sessions.Current);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Action == "user.login_failed" && e.ActorName == ApiFactory.AdminLogin));
    }

    [Fact]
    public async Task Unknown_login_gets_same_message_as_wrong_password()
    {
        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.CreateApiClient().LoginAsync("nobody_" + Guid.NewGuid().ToString("N")[..6], "whatever123", null));
        Assert.Equal("Неверный логин или пароль.", ex.Message);
    }

    [Fact]
    public async Task Empty_credentials_are_a_validation_error()
    {
        var response = await factory.CreateRawClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("", "", null), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(ApiFactory.Json);
        Assert.Equal("validation", problem!.ErrorCode);
    }

    [Fact]
    public async Task Malformed_json_does_not_leak_serializer_internals()
    {
        var response = await factory.CreateRawClient().PostAsync("/api/auth/login", new StringContent("{\"login\":", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JsonException", body);
        Assert.DoesNotContain("Expected depth", body);
        Assert.Contains("Некорректный формат", body);
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_detects_reuse()
    {
        var raw = factory.CreateRawClient();
        var login = await raw.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminLogin, ApiFactory.AdminPassword, "rotation"), ApiFactory.Json);
        var first = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;

        var refresh = await raw.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(first.RefreshToken), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var second = (await refresh.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // Повтор старого токена отклоняется и отзывает всю цепочку.
        var reuse = await raw.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(first.RefreshToken), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        var afterReuse = await raw.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(second.RefreshToken), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token_and_is_idempotent()
    {
        var raw = factory.CreateRawClient();
        var login = await raw.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminLogin, ApiFactory.AdminPassword, "logout"), ApiFactory.Json);
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFactory.Json))!;

        Assert.Equal(HttpStatusCode.NoContent, (await raw.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(session.RefreshToken), ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await raw.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(session.RefreshToken), ApiFactory.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(session.RefreshToken), ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Typed_client_refreshes_transparently_when_access_token_is_rejected()
    {
        var client = await factory.LoginAdminAsync();
        var session = client.Sessions.Current!;
        // Портим access-токен, сохранив действующий refresh-токен: обработчик должен обновить сессию и повторить запрос.
        await client.Sessions.SetAsync(session with { AccessToken = session.AccessToken[..^4] + "xxxx" });

        var me = await client.GetCurrentUserAsync();

        Assert.Equal(ApiFactory.AdminLogin, me.User.Login);
        Assert.NotEqual(session.RefreshToken, client.Sessions.Current!.RefreshToken);
    }

    [Fact]
    public async Task Change_password_revokes_other_sessions_and_returns_new_one()
    {
        var admin = await factory.LoginAdminAsync();
        var login = "pwd_" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(new CreateUserRequest(login, "Смена пароля", null, "OldPass12345"));

        var sessionA = await factory.LoginAsync(login, "OldPass12345");
        var sessionB = await factory.LoginAsync(login, "OldPass12345");
        var refreshB = sessionB.Sessions.Current!.RefreshToken;

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => sessionA.ChangePasswordAsync("wrong", "NewPass12345"));
        var renewed = await sessionA.ChangePasswordAsync("OldPass12345", "NewPass12345");
        Assert.NotNull(renewed.AccessToken);

        var raw = factory.CreateRawClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshB), ApiFactory.Json)).StatusCode);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.LoginAsync(login, "OldPass12345"));
        var me = await (await factory.LoginAsync(login, "NewPass12345")).GetCurrentUserAsync();
        Assert.Equal(login, me.User.Login);
    }

    [Fact]
    public async Task Deactivated_user_cannot_login_or_refresh()
    {
        var admin = await factory.LoginAdminAsync();
        var login = "inactive_" + Guid.NewGuid().ToString("N")[..8];
        var user = await admin.CreateUserAsync(new CreateUserRequest(login, "Блокируемый", null, Scenario.Password));
        var victim = await factory.LoginAsync(login, Scenario.Password);
        var refresh = victim.Sessions.Current!.RefreshToken;

        await admin.UpdateUserAsync(user.Id, new UpdateUserRequest(user.DisplayName, null, IsActive: false));

        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.LoginAsync(login, Scenario.Password));
        Assert.Contains("заблокирована", ex.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateRawClient().PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refresh), ApiFactory.Json)).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_own_account()
    {
        var admin = await factory.LoginAdminAsync();
        var me = await admin.GetCurrentUserAsync();
        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => admin.UpdateUserAsync(me.User.Id, new UpdateUserRequest(me.User.DisplayName, null, IsActive: false)));
        Assert.Equal("domain_rule", ex.ErrorCode);
    }
}

public sealed class RateLimitTests
{
    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client()
    {
        var factory = new ApiFactory { LoginRateLimitPerMinute = 3 };
        await ((IAsyncLifetime)factory).InitializeAsync();
        try
        {
            var raw = factory.CreateRawClient();
            for (var i = 0; i < 3; i++)
            {
                var response = await raw.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost", "wrong-password-1", null), ApiFactory.Json);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            var limited = await raw.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost", "wrong-password-1", null), ApiFactory.Json);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            var problem = await limited.Content.ReadFromJsonAsync<ApiProblem>(ApiFactory.Json);
            Assert.Equal("Слишком много попыток", problem!.Title);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }
}
