using System.Text.RegularExpressions;

namespace DrawingFlow.Application.Storage;

/// <summary>
/// Внутренние ключи хранилища: формируются сервером из идентификаторов, не содержат пользовательских имён и путей.
/// Формат: yyyy/MM/{documentId}/{versionId}[.ext].
/// </summary>
public static partial class StorageKeys
{
    public const int MaxLength = 200;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*(/[a-z0-9][a-z0-9._-]*)*$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^[a-z0-9]{1,10}$")]
    private static partial Regex ExtensionPattern();

    public static string Create(Guid documentId, Guid versionId, string? originalFileName, DateTime nowUtc)
    {
        var extension = SanitizeExtension(originalFileName);
        return $"{nowUtc:yyyy}/{nowUtc:MM}/{documentId:N}/{versionId:N}{extension}";
    }

    /// <summary>Расширение сохраняется только если оно состоит из 1–10 латинских букв/цифр; иначе отбрасывается.</summary>
    public static string SanitizeExtension(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
        {
            return string.Empty;
        }

        var extension = fileName[(dot + 1)..].ToLowerInvariant();
        return ExtensionPattern().IsMatch(extension) ? "." + extension : string.Empty;
    }

    public static bool IsValid(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.Length <= MaxLength
        && !key.Contains("..", StringComparison.Ordinal)
        && KeyPattern().IsMatch(key);
}
