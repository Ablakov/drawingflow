using System.Text.RegularExpressions;
using DrawingFlow.Contracts.Agents;

namespace DrawingFlow.Agent.Sync;

/// <summary>Маски файлов вида <c>*.dwg;*.pdf</c> и правила извлечения номера документа из имени файла.</summary>
public static class FileMatcher
{
    public const string DefaultDocumentNumberPattern = @"^(?<number>[^\s_]+)";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static bool MatchesAny(string fileName, string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
        {
            return false;
        }

        foreach (var pattern in Split(patterns))
        {
            if (GlobToRegex(pattern).IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Файл подходит под маски включения папки и не попадает под исключения (папки и глобальные).</summary>
    public static bool IsEligible(string fileName, WatchedFolderDto folder, IEnumerable<string> alwaysExclude)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.StartsWith('.'))
        {
            return false;
        }

        var include = string.IsNullOrWhiteSpace(folder.IncludePatterns) ? "*" : folder.IncludePatterns;
        if (!MatchesAny(fileName, include))
        {
            return false;
        }

        if (MatchesAny(fileName, folder.ExcludePatterns))
        {
            return false;
        }

        return !MatchesAny(fileName, string.Join(';', alwaysExclude));
    }

    /// <summary>Извлекает номер документа из имени файла по правилу папки; null — правило не применимо или номер не найден.</summary>
    public static string? ExtractDocumentNumber(WatchedFolderDto folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        switch (folder.MatchingRule)
        {
            case FileMatchingRule.FileNameIsNumber:
                return stem.Length == 0 ? null : stem;
            case FileMatchingRule.NumberFromFileName:
                try
                {
                    var regex = new Regex(string.IsNullOrWhiteSpace(folder.DocumentNumberPattern) ? DefaultDocumentNumberPattern : folder.DocumentNumberPattern, RegexOptions.None, RegexTimeout);
                    var match = regex.Match(stem);
                    var number = match.Success ? match.Groups["number"].Value.Trim() : null;
                    return string.IsNullOrEmpty(number) ? null : number;
                }
                catch (ArgumentException)
                {
                    return null;
                }
                catch (RegexMatchTimeoutException)
                {
                    return null;
                }
            default:
                return null;
        }
    }

    public static IEnumerable<string> Split(string patterns) =>
        patterns.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly Dictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static Regex GlobToRegex(string glob)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(glob, out var cached))
            {
                return cached;
            }

            var pattern = "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            Cache[glob] = regex;
            return regex;
        }
    }
}
