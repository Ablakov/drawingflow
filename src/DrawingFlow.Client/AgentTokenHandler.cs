using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DrawingFlow.Contracts.Agents;

namespace DrawingFlow.Client;

public enum AgentAuthState
{
    Unauthenticated = 0,
    Authenticated = 1,
    /// <summary>Сервер отклонил ключ установки: он отозван или неизвестен.</summary>
    Revoked = 2,
}

/// <summary>
/// Аутентификация агента: обменивает ключ установки на короткоживущий токен, заранее продлевает его
/// и один раз повторяет запрос после 401. Отзыв ключа переводит обработчик в состояние <see cref="AgentAuthState.Revoked"/>.
/// </summary>
public sealed class AgentTokenHandler : DelegatingHandler
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(90);
    private readonly Func<AgentTokenRequest> credentialProvider;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTime _expiresAtUtc;

    public AgentTokenHandler(Func<AgentTokenRequest> credentialProvider, JsonSerializerOptions jsonOptions)
    {
        this.credentialProvider = credentialProvider;
        this.jsonOptions = jsonOptions;
    }

    /// <summary>Создаёт обработчик с ранее выданным токеном (например, сохранённым между перезапусками); при отказе сервера токен обновляется ключом.</summary>
    public AgentTokenHandler(Func<AgentTokenRequest> credentialProvider, JsonSerializerOptions jsonOptions, string restoredAccessToken, DateTime restoredExpiresAtUtc)
        : this(credentialProvider, jsonOptions)
    {
        _accessToken = restoredAccessToken;
        _expiresAtUtc = restoredExpiresAtUtc;
        State = AgentAuthState.Authenticated;
    }

    public AgentAuthState State { get; private set; }

    public Guid? AgentId { get; private set; }

    public string? AgentName { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public DateTime? TokenExpiresAtUtc => _accessToken is null ? null : _expiresAtUtc;

    /// <summary>Состояние изменилось (получен токен, ключ отозван, связь восстановлена).</summary>
    public event EventHandler<AgentAuthState>? StateChanged;

    /// <summary>Сбрасывает признак отзыва, чтобы повторить обмен ключа (например, после замены ключа в хранилище).</summary>
    public void Reset()
    {
        _accessToken = null;
        SetState(AgentAuthState.Unauthenticated);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AuthenticatedHttpHandler.AnonymousOption, out var anonymous) && anonymous)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var token = await EnsureTokenAsync(request.RequestUri, force: false, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var canRetry = request.Content is null || await TryBufferAsync(request.Content, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !canRetry)
        {
            return response;
        }

        var refreshed = await EnsureTokenAsync(request.RequestUri, force: true, cancellationToken).ConfigureAwait(false);
        if (refreshed is null || refreshed == token)
        {
            return response;
        }

        response.Dispose();
        using var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> EnsureTokenAsync(Uri? requestUri, bool force, CancellationToken cancellationToken)
    {
        if (requestUri is null)
        {
            return _accessToken;
        }

        if (!force && _accessToken is not null && _expiresAtUtc - DateTime.UtcNow > RefreshWindow)
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _accessToken is not null && _expiresAtUtc - DateTime.UtcNow > RefreshWindow)
            {
                return _accessToken;
            }

            if (State == AgentAuthState.Revoked)
            {
                return null;
            }

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(requestUri, "/api/agents/token"))
            {
                Content = JsonContent.Create(credentialProvider(), options: jsonOptions),
            };
            tokenRequest.Options.Set(AuthenticatedHttpHandler.AnonymousOption, true);

            using var response = await base.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _accessToken = null;
                SetState(AgentAuthState.Revoked);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Временная ошибка сервера: оставляем прежний токен (если есть), запрос вернёт исходную ошибку.
                return _accessToken;
            }

            var payload = await response.Content.ReadFromJsonAsync<AgentTokenResponse>(jsonOptions, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return _accessToken;
            }

            _accessToken = payload.AccessToken;
            _expiresAtUtc = payload.ExpiresAtUtc;
            AgentId = payload.AgentId;
            AgentName = payload.AgentName;
            OrganizationId = payload.OrganizationId;
            SetState(AgentAuthState.Authenticated);
            return _accessToken;
        }
        catch (HttpRequestException)
        {
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void SetState(AgentAuthState state)
    {
        if (State != state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }

    private static async Task<bool> TryBufferAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is null or > 8 * 1024 * 1024)
        {
            return false;
        }

        await content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers.Where(h => h.Key != "Authorization"))
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }
}
