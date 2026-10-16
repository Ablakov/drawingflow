using System.Net;
using System.Net.Http.Json;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Auth;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Infrastructure.Persistence;
using DrawingFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrawingFlow.IntegrationTests.Api;

public sealed class SecurityHardeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Temporary_password_restricts_access_until_changed()
    {
        var admin = await factory.LoginAdminAsync();
        var login = "temp_" + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.CreateUserAsync(new CreateUserRequest(login, "Новый сотрудник", null, "Temp12345"));
        Assert.True(created.MustChangePassword);

        var client = await factory.LoginAsync(login, "Temp12345");
        Assert.True(client.Sessions.Current!.User.MustChangePassword);
        var me = await client.GetCurrentUserAsync();
        Assert.True(me.User.MustChangePassword);

        var blocked = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => client.GetOrganizationsAsync());
        Assert.Equal("password_change_required", blocked.ErrorCode);
        Assert.Contains("временным паролем", blocked.Message);

        var renewed = await client.ChangePasswordAsync("Temp12345", "Strong12345");
        Assert.False(renewed.User.MustChangePassword);
        Assert.Empty(await client.GetOrganizationsAsync());
        Assert.False((await factory.LoginAsync(login, "Strong12345")).Sessions.Current!.User.MustChangePassword);

        // Пользователь без требования смены пароля работает сразу.
        var free = await admin.CreateUserAsync(new CreateUserRequest("free_" + Guid.NewGuid().ToString("N")[..8], "Без ограничений", null, "Free12345", RequirePasswordChange: false));
        Assert.False(free.MustChangePassword);
    }

    [Fact]
    public async Task Server_logs_never_contain_secrets()
    {
        var s = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Секреты"));
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ЛОГ-1", "Проверка журнала"));
        await s.Engineer.UploadDocumentVersionAsync(document.Id, new MemoryStream([1, 2, 3]), "файл.dwg", null, null, Guid.NewGuid().ToString("N"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.CreateApiClient().LoginAsync(s.Login("engineer"), "WrongPassword123", null));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.CreateApiClient().ExchangeAgentKeyAsync(new AgentTokenRequest("bogus-agent-key-value-123456")));

        var logs = factory.Logs.ToArray();
        Assert.NotEmpty(logs);
        var secrets = new[]
        {
            Scenario.Password, ApiFactory.AdminPassword, "WrongPassword123", registration.AgentKey, "bogus-agent-key-value-123456",
            s.Engineer.Sessions.Current!.AccessToken, s.Engineer.Sessions.Current.RefreshToken,
        };
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(logs, line => line.Contains(secret, StringComparison.Ordinal));
        }

        // Неудачные попытки входа и отклонённые ключи фиксируются без самих секретов.
        Assert.Contains(logs, line => line.Contains("Неудачная попытка входа") && line.Contains(s.Login("engineer")));
        Assert.Contains(logs, line => line.Contains("Отклонён ключ агента") && line.Contains("bogus-ag"));
    }

    [Fact]
    public async Task Forwarded_client_address_is_used_only_when_reverse_proxy_mode_is_enabled()
    {
        // По умолчанию заголовок игнорируется.
        var raw = factory.CreateRawClient();
        raw.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.5");
        var login = "fwd_" + Guid.NewGuid().ToString("N")[..8];
        var admin = await factory.LoginAdminAsync();
        await admin.CreateUserAsync(new CreateUserRequest(login, "Прокси", null, Scenario.Password, RequirePasswordChange: false));
        var response = await raw.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, Scenario.Password, "proxy-test"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var address = await db.AuditEvents.Where(e => e.Action == "user.logged_in" && e.ActorName == "Прокси").Select(e => e.ClientAddress).FirstAsync();
            Assert.NotEqual("203.0.113.5", address);
        }

        // За доверенным прокси адрес берётся из заголовка.
        await using var proxied = new ApiFactory(factory) { ReverseProxyEnabled = true };
        var proxiedRaw = proxied.CreateRawClient();
        proxiedRaw.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");
        var ok = await proxiedRaw.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, Scenario.Password, "proxy-test-2"), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var scope = proxied.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var address = await db.AuditEvents.Where(e => e.Action == "user.logged_in" && e.ActorName == "Прокси").OrderByDescending(e => e.Id).Select(e => e.ClientAddress).FirstAsync();
            Assert.Equal("203.0.113.7", address);
        }
    }

    [Fact]
    public async Task Problem_details_do_not_leak_internal_details()
    {
        var s = await Scenario.CreateAsync(factory);
        var raw = factory.CreateRawClient(s.Engineer.Sessions.Current!.AccessToken);
        var response = await raw.GetAsync($"/api/projects/{Guid.NewGuid()}/documents");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(ApiFactory.Json);
        Assert.Equal("Проект не найден.", problem!.Detail);
        Assert.NotNull(problem.TraceId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("   at ", body);
    }
}
