using System.IO;
using System.Security.Cryptography;
using DrawingFlow.Contracts.Documents;

namespace DrawingFlow.Desktop.Services;

/// <summary>
/// Локальная копия скачанных версий: %LOCALAPPDATA%\DrawingFlow\cache\{документ}\v{номер}\{имя файла}.
/// Файл считается актуальным только при совпадении SHA-256 с зарегистрированной версией.
/// </summary>
public sealed class LocalFileCache
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrawingFlow", "cache");

    public string GetPath(DocumentVersionDto version)
    {
        var safeName = SanitizeFileName(version.OriginalFileName);
        return Path.Combine(Root, version.DocumentId.ToString("N"), $"v{version.VersionNumber}", safeName);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public async Task<bool> IsValidAsync(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return string.Equals(await ComputeSha256Async(path, cancellationToken), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) || cleaned.Trim('.').Length == 0 ? "file" : cleaned;
    }
}
