
namespace DrawingFlow.Contracts.Auth;

public sealed record LoginRequest(
    string Login,
    string Password,
    string? ClientDescription = null);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Users.UserDto User);
