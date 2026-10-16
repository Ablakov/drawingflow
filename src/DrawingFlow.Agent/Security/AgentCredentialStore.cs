using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Agent.Security;

/// <summary>Ключ установки агента и адрес сервера.</summary>
public sealed record AgentCredential(Uri ServerAddress, string AgentKey, DateTime SavedAtUtc);

/// <summary>
/// Хранит ключ установки в каталоге данных агента. На Windows файл зашифрован DPAPI в области компьютера
/// (читать может только эта машина, в т. ч. служба под системной учётной записью). На других ОС — файл с правами 0600 (режим разработки).
/// </summary>
public sealed class AgentCredentialStore(string path, ILogger<AgentCredentialStore> logger)
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("DrawingFlow.Agent.Credential.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Path { get; } = path;

    public bool Exists => File.Exists(Path);

    public AgentCredential? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(Path);
            if (OperatingSystem.IsWindows())
            {
                bytes = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.LocalMachine);
            }

            return JsonSerializer.Deserialize<AgentCredential>(bytes, Json);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Не удалось прочитать ключ агента из {Path}", Path);
            return null;
        }
    }

    public void Save(AgentCredential credential)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credential, Json);
        if (OperatingSystem.IsWindows())
        {
            bytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(Path, bytes);
        }
        else
        {
            File.WriteAllBytes(Path, bytes);
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            logger.LogWarning("Ключ агента сохранён без шифрования (не Windows): {Path}. Используйте только для разработки.", Path);
        }
    }

    public void Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
