using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Auth;
using DrawingFlow.Contracts.Users;

namespace DrawingFlow.UnitTests.Client;

public sealed class ApiClientTests
{
    private static readonly JsonSerializerOptions Json = DrawingFlowApiClient.CreateJsonOptions();

    private sealed class ScriptedHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> script) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await script(request, Requests.Count);
        }
    }

    private static UserDto SampleUser() => new(Guid.NewGuid(), "ivanov", "Иванов", null, false, true, DateTime.UtcNow, null);

    private static AuthSession Session(string access, string refresh, DateTime accessExpires) =>
        new(access, accessExpires, refresh, DateTime.UtcNow.AddDays(10), SampleUser());

    private static (DrawingFlowApiClient Client, SessionManager Sessions, ScriptedHandler Inner) Build(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> script, AuthSession? session)
    {
        var inner = new ScriptedHandler(script);
        var sessions = new SessionManager(new InMemorySessionStore());
        if (session is not null)
        {
            sessions.SetAsync(session).GetAwaiter().GetResult();
        }

        var handler = new AuthenticatedHttpHandler(sessions, Json) { InnerHandler = inner };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new DrawingFlowApiClient(http, sessions), sessions, inner);
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body, options: Json) };

    private static HttpResponseMessage Problem(HttpStatusCode status, string detail, string? errorCode = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent($$"""{"title":"Ошибка","status":{{(int)status}},"detail":"{{detail}}","errorCode":"{{errorCode ?? "x"}}"}""", System.Text.Encoding.UTF8, "application/problem+json"),
        };
        return response;
    }

    [Fact]
    public async Task Attaches_bearer_token_to_authenticated_requests()
    {
        var (client, _, inner) = Build((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new CurrentUserDto(SampleUser(), []))),
            Session("access-1", "refresh-1", DateTime.UtcNow.AddMinutes(30)));

        var me = await client.GetCurrentUserAsync();

        Assert.Equal("ivanov", me.User.Login);
        Assert.Equal("Bearer", inner.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("access-1", inner.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Anonymous_requests_do_not_carry_token()
    {
        var (client, _, inner) = Build((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, new DrawingFlow.Contracts.ServerInfoDto("DrawingFlow", "1", DateTime.UtcNow))),
            Session("access-1", "refresh-1", DateTime.UtcNow.AddMinutes(30)));

        await client.GetServerInfoAsync();

        Assert.Null(inner.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Refreshes_and_retries_once_after_401()
    {
        var newAuth = new AuthResponse("access-2", DateTime.UtcNow.AddHours(1), "refresh-2", DateTime.UtcNow.AddDays(30), SampleUser());
        var (client, sessions, inner) = Build((request, n) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, newAuth));
            }

            return Task.FromResult(request.Headers.Authorization?.Parameter == "access-2"
                ? JsonResponse(HttpStatusCode.OK, new CurrentUserDto(SampleUser(), []))
                : Problem(HttpStatusCode.Unauthorized, "Срок действия сессии истёк."));
        }, Session("access-1", "refresh-1", DateTime.UtcNow.AddMinutes(30)));

        var me = await client.GetCurrentUserAsync();

        Assert.Equal("ivanov", me.User.Login);
        Assert.Equal(3, inner.Requests.Count); // исходный запрос, refresh, повтор
        Assert.Equal("access-2", sessions.Current!.AccessToken);
        Assert.Equal("refresh-2", sessions.Current.RefreshToken);
    }

    [Fact]
    public async Task Proactively_refreshes_expiring_access_token()
    {
        var newAuth = new AuthResponse("access-2", DateTime.UtcNow.AddHours(1), "refresh-2", DateTime.UtcNow.AddDays(30), SampleUser());
        var (client, _, inner) = Build((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/auth/refresh"
                ? JsonResponse(HttpStatusCode.OK, newAuth)
                : JsonResponse(HttpStatusCode.OK, new CurrentUserDto(SampleUser(), []))),
            Session("access-1", "refresh-1", DateTime.UtcNow.AddSeconds(10)));

        await client.GetCurrentUserAsync();

        Assert.Equal("/api/auth/refresh", inner.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("access-2", inner.Requests[1].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Expired_refresh_token_clears_session_and_raises_event()
    {
        var expired = false;
        var (client, sessions, _) = Build((request, _) => Task.FromResult(Problem(HttpStatusCode.Unauthorized, "Сессия недействительна. Выполните вход заново.")),
            Session("access-1", "refresh-1", DateTime.UtcNow.AddMinutes(30)));
        sessions.Expired += (_, _) => expired = true;

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetCurrentUserAsync());

        Assert.True(ex.IsUnauthorized);
        Assert.True(expired);
        Assert.Null(sessions.Current);
    }

    [Fact]
    public async Task Problem_details_become_readable_exception()
    {
        var (client, _, _) = Build((_, _) => Task.FromResult(Problem(HttpStatusCode.Conflict, "Проект с таким кодом уже существует в организации.", "conflict")),
            Session("access-1", "refresh-1", DateTime.UtcNow.AddMinutes(30)));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.CreateProjectAsync(Guid.NewGuid(), new DrawingFlow.Contracts.Projects.CreateProjectRequest("A", "B")));

        Assert.True(ex.IsConflict);
        Assert.Equal("conflict", ex.ErrorCode);
        Assert.Equal("Проект с таким кодом уже существует в организации.", ex.Message);
    }

    [Fact]
    public async Task Connection_failure_becomes_connection_exception()
    {
        var (client, _, _) = Build((_, _) => throw new HttpRequestException("connection refused"), null);

        var ex = await Assert.ThrowsAsync<ApiConnectionException>(() => client.GetServerInfoAsync());

        Assert.Contains("localhost:5080", ex.Message);
    }

    [Fact]
    public async Task Login_stores_session()
    {
        var auth = new AuthResponse("access-1", DateTime.UtcNow.AddHours(1), "refresh-1", DateTime.UtcNow.AddDays(30), SampleUser());
        var (client, sessions, inner) = Build((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, auth)), null);

        var session = await client.LoginAsync("ivanov", "Ivanov12345", "test");

        Assert.Equal("access-1", session.AccessToken);
        Assert.Same(session, sessions.Current);
        Assert.Null(inner.Requests[0].Headers.Authorization);
    }

    [Theory]
    [InlineData("https://server.local/", true)]
    [InlineData("http://localhost:5080/", true)]
    [InlineData("http://127.0.0.1:5080/", true)]
    [InlineData("http://server.local/", false)]
    public void Server_address_policy(string address, bool allowed)
    {
        Assert.Equal(allowed, ApiClientOptions.IsAllowedAddress(new Uri(address), out _));
    }
}
