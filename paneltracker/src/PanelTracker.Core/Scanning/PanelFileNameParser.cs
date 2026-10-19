using System.Text.RegularExpressions;

namespace PanelTracker.Core.Scanning;

/// <summary>Разбирает имя файла без расширения на идентификатор панели и номер ревизии.</summary>
/// <remarks>Поддерживаются суффиксы вида <c>N021_R2</c>, <c>N021-R2</c>, <c>N021 R2</c>, <c>N021_rev2</c>, <c>N021 (R2)</c>.</remarks>
public static partial class PanelFileNameParser
{
    public static ParsedPanelName Parse(string fileNameWithoutExtension)
    {
        var name = fileNameWithoutExtension.Trim();
        var match = RevisionSuffix().Match(name);
        if (match.Success && match.Groups["rev"].Success && int.TryParse(match.Groups["rev"].Value, out var revision))
        {
            return new ParsedPanelName(match.Groups["id"].Value.Trim(), revision);
        }

        return new ParsedPanelName(name, null);
    }

    public static ParsedPanelName ParsePath(string path) => Parse(Path.GetFileNameWithoutExtension(path));

    [GeneratedRegex(@"^(?<id>.+?)(?:[ _\-]+(?:rev|r)\.?\s*(?<rev>\d+)|\s*\((?:rev|r)\.?\s*(?<rev>\d+)\))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevisionSuffix();
}

public sealed record ParsedPanelName(string PanelId, int? Revision);
