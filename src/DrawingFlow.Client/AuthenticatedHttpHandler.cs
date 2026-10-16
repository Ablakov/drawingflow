using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DrawingFlow.Contracts.Auth;

namespace DrawingFlow.Client;

/// <summary>
/// Добавляет Bearer-токен к запросам, заранее продлевает истекающий access-токен и один раз повторяет запрос после 401.
/// Запросы, помеченные <see cref="AnonymousOption"/>, выполняются без токена.
/// </summary>
public sealed class AuthenticatedHttpHandler(SessionManager sessions, JsonSerializerOptions jsonOptions) : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> AnonymousOption = new("DrawingFlow.Anonymous");

    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(60);
    private const long MaxBufferedContentLength = 8 * 1024 * 1024;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AnonymousOption, out var anonymous) && anonymous)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var session = sessions.Current;
        if (session is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (session.AccessTokenExpiresWithin(RefreshWindow, DateTime.UtcNow))
        {
            session = await RefreshAsync(session, request.RequestUri, cancellationToken).ConfigureAwait(false) ?? session;
        }

        var canRetry = await TryBufferContentAsync(request, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !canRetry)
        {
            return response;
        }

        var refreshed = await RefreshAsync(session, request.RequestUri, cancellationToken).ConfigureAwait(false);
        if (refreshed is null)
        {
            return response;
        }

        response.Dispose();
        using var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AuthSession?> RefreshAsync(AuthSession stale, Uri? requestUri, CancellationToken cancellationToken)
    {
        if (requestUri is null)
        {
            return null;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = sessions.Current;
            if (current is null)
            {
                return null;
            }

            // Другой поток уже обновил сессию.
            if (!ReferenceEquals(current, stale) && current.RefreshToken != stale.RefreshToken)
            {
                return current;
            }

            if (current.RefreshTokenExpired(DateTime.UtcNow))
            {
                await sessions.ExpireAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(requestUri, "/api/auth/refresh"))
            {
                Content = JsonContent.Create(new RefreshTokenRequest(current.RefreshToken), options: jsonOptions),
            };
            refreshRequest.Options.Set(AnonymousOption, true);

            using var response = await base.SendAsync(refreshRequest, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await sessions.ExpireAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Временная ошибка сервера: сессию не сбрасываем, запрос вернёт исходную ошибку.
                return null;
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(jsonOptions, cancellationToken).ConfigureAwait(false);
            if (auth is null)
            {
                return null;
            }

            var refreshed = AuthSession.From(auth);
            await sessions.SetAsync(refreshed, cancellationToken).ConfigureAwait(false);
            return refreshed;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<bool> TryBufferContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return true;
        }

        var length = request.Content.Headers.ContentLength;
        if (length is null or > MaxBufferedContentLength)
        {
            // Потоковая загрузка больших файлов: повтор невозможен, полагаемся на упреждающее продление токена.
            return false;
        }

        await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers)
        {
            if (header.Key != "Authorization")
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
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
