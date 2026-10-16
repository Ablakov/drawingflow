using System.Text.RegularExpressions;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>Папка на рабочей станции (локальная или сетевая), за которой следит агент и которая привязана к проекту.</summary>
public sealed class WatchedFolder
{
    public const int PathMaxLength = 1000;
    public const int PatternsMaxLength = 1000;
    public const int NumberPatternMaxLength = 500;
    public const string DefaultIncludePatterns = "*";
    /// <summary>По умолчанию номер документа — начальная часть имени файла до пробела или подчёркивания.</summary>
    public const string DefaultDocumentNumberPattern = @"^(?<number>[^\s_]+)";

    private WatchedFolder() { }

    public Guid Id { get; private set; }
    public Guid AgentInstallationId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string NormalizedPath { get; private set; } = string.Empty;
    /// <summary>Маски включения через «;», например <c>*.dwg;*.pdf</c>.</summary>
    public string IncludePatterns { get; private set; } = DefaultIncludePatterns;
    public string? ExcludePatterns { get; private set; }
    public FileMatchingRule MatchingRule { get; private set; }
    public string? DocumentNumberPattern { get; private set; }
    public bool AutoRegisterUnknown { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public AgentInstallation? Agent { get; private set; }
    public Project? Project { get; private set; }

    public static WatchedFolder Create(
        Guid agentInstallationId,
        Guid projectId,
        string path,
        string? includePatterns,
        string? excludePatterns,
        FileMatchingRule matchingRule,
        string? documentNumberPattern,
        bool autoRegisterUnknown,
        bool isEnabled,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        var folder = new WatchedFolder
        {
            Id = Guid.CreateVersion7(),
            AgentInstallationId = agentInstallationId,
            ProjectId = projectId,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = createdByUserId,
        };
        folder.Apply(path, includePatterns, excludePatterns, matchingRule, documentNumberPattern, autoRegisterUnknown, isEnabled, nowUtc);
        return folder;
    }

    public void Update(string path, string? includePatterns, string? excludePatterns, FileMatchingRule matchingRule, string? documentNumberPattern, bool autoRegisterUnknown, bool isEnabled, DateTime nowUtc)
        => Apply(path, includePatterns, excludePatterns, matchingRule, documentNumberPattern, autoRegisterUnknown, isEnabled, Guard.Utc(nowUtc, nameof(nowUtc)));

    private void Apply(string path, string? includePatterns, string? excludePatterns, FileMatchingRule matchingRule, string? documentNumberPattern, bool autoRegisterUnknown, bool isEnabled, DateTime nowUtc)
    {
        Path = ValidatePath(path);
        NormalizedPath = NormalizePath(Path);
        IncludePatterns = Guard.Optional(includePatterns, "includePatterns", "Маски включения", PatternsMaxLength) ?? DefaultIncludePatterns;
        ExcludePatterns = Guard.Optional(excludePatterns, "excludePatterns", "Маски исключения", PatternsMaxLength);
        if (!Enum.IsDefined(matchingRule))
        {
            throw new ValidationException("matchingRule", "Недопустимое правило сопоставления.");
        }

        MatchingRule = matchingRule;
        DocumentNumberPattern = ValidateNumberPattern(matchingRule, documentNumberPattern);
        AutoRegisterUnknown = autoRegisterUnknown;
        IsEnabled = isEnabled;
        UpdatedAtUtc = nowUtc;
    }

    public void Disable(DateTime nowUtc)
    {
        IsEnabled = false;
        UpdatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
    }

    public static string ValidatePath(string? value)
    {
        var path = Guard.Required(value, "path", "Путь к папке", PathMaxLength);
        if (path.Any(char.IsControl))
        {
            throw new ValidationException("path", "Путь содержит недопустимые символы.");
        }

        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>Пути Windows нечувствительны к регистру; сравнение выполняется по нормализованной форме.</summary>
    public static string NormalizePath(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    public static string? ValidateNumberPattern(FileMatchingRule rule, string? pattern)
    {
        var trimmed = Guard.Optional(pattern, "documentNumberPattern", "Шаблон номера документа", NumberPatternMaxLength);
        if (rule != FileMatchingRule.NumberFromFileName)
        {
            return trimmed;
        }

        var effective = trimmed ?? DefaultDocumentNumberPattern;
        try
        {
            var regex = new Regex(effective, RegexOptions.None, TimeSpan.FromMilliseconds(200));
            if (!regex.GetGroupNames().Contains("number"))
            {
                throw new ValidationException("documentNumberPattern", "Шаблон должен содержать именованную группу «number», например ^(?<number>[^\\s_]+).");
            }
        }
        catch (ArgumentException)
        {
            throw new ValidationException("documentNumberPattern", "Некорректное регулярное выражение шаблона номера документа.");
        }

        return trimmed;
    }
}
