using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrawingFlow.Client;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.Services;

/// <summary>
/// Хранит сессию (refresh-токен) в %LOCALAPPDATA%\DrawingFlow\session.bin, зашифрованной DPAPI
/// в области текущего пользователя Windows. Если «запомнить меня» выключено, на диск ничего не пишется.
/// </summary>
public sealed class ProtectedFileSessionStore(DesktopSettingsStore settings, ILogger<ProtectedFileSessionStore> logger) : ISessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DrawingFlow.Desktop.Session.v1");
    private static readonly JsonSerializerOptions Json = DrawingFlowApiClient.CreateJsonOptions();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrawingFlow", "session.bin");

    public Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Task.FromResult<AuthSession?>(null);
            }

            var protectedBytes = File.ReadAllBytes(FilePath);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<AuthSession>(bytes, Json);
            if (session is null || session.RefreshTokenExpired(DateTime.UtcNow))
            {
                File.Delete(FilePath);
                return Task.FromResult<AuthSession?>(null);
            }

            return Task.FromResult<AuthSession?>(session);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Сохранённая сессия повреждена или недоступна; потребуется повторный вход");
            TryDelete();
            return Task.FromResult<AuthSession?>(null);
        }
    }

    public Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        if (!settings.Current.RememberSession)
        {
            TryDelete();
            return Task.CompletedTask;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(session, Json);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, protectedBytes);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось сохранить сессию в {Path}", FilePath);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        TryDelete();
        return Task.CompletedTask;
    }

    private void TryDelete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Не удалось удалить файл сессии {Path}", FilePath);
        }
    }
}
