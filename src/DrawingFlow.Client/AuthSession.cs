using DrawingFlow.Contracts.Auth;
using DrawingFlow.Contracts.Users;

namespace DrawingFlow.Client;

/// <summary>Сохранённая сессия клиента. Содержит секреты — хранить только в защищённом виде.</summary>
public sealed record AuthSession(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserDto User)
{
    public static AuthSession From(AuthResponse response) => new(
        response.AccessToken,
        response.AccessTokenExpiresAtUtc,
        response.RefreshToken,
        response.RefreshTokenExpiresAtUtc,
        response.User);

    public bool AccessTokenExpiresWithin(TimeSpan window, DateTime nowUtc) => AccessTokenExpiresAtUtc <= nowUtc.Add(window);

    public bool RefreshTokenExpired(DateTime nowUtc) => RefreshTokenExpiresAtUtc <= nowUtc;
}
