using PanelTracker.Core.Models;

namespace PanelTracker.Core.Scanning;

/// <summary>Сканирует папки статусов и строит список панелей. Статус панели равен папке, где лежит её актуальный файл.</summary>
public static class ProjectScanner
{
    public static ScanResult Scan(string rootFolder, TrackerSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);
        var extensions = new HashSet<string>(settings.FileExtensions.Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        var filesByPanel = new Dictionary<string, List<PanelFile>>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<StatusDefinition>();
        var searchOption = settings.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var status in settings.Statuses)
        {
            var folder = Path.Combine(rootFolder, status.Folder);
            if (!Directory.Exists(folder))
            {
                missing.Add(status);
                continue;
            }

            List<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(folder, "*", searchOption).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                missing.Add(status);
                continue;
            }

            foreach (var path in paths)
            {
                var fileName = Path.GetFileName(path);
                if (fileName.StartsWith("~$", StringComparison.Ordinal) || fileName.StartsWith('.'))
                {
                    continue;
                }

                if (!extensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }

                var parsed = PanelFileNameParser.ParsePath(path);
                if (string.IsNullOrWhiteSpace(parsed.PanelId))
                {
                    continue;
                }

                DateTime lastWrite;
                try
                {
                    lastWrite = File.GetLastWriteTimeUtc(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lastWrite = DateTime.MinValue;
                }

                var file = new PanelFile(parsed.PanelId, parsed.Revision, path, status.Key, lastWrite);
                if (!filesByPanel.TryGetValue(parsed.PanelId, out var list))
                {
                    list = [];
                    filesByPanel[parsed.PanelId] = list;
                }

                list.Add(file);
            }
        }

        var panels = filesByPanel
            .Select(pair => BuildPanel(pair.Key, pair.Value, settings))
            .OrderBy(p => p.Id, PanelIdComparer.Instance)
            .ToList();

        return new ScanResult
        {
            RootFolder = rootFolder,
            ScannedAtUtc = DateTime.UtcNow,
            Panels = panels,
            MissingFolders = missing,
        };
    }

    /// <summary>Актуальный файл — со старшей ревизией; при равенстве — изменённый позже; затем — более поздний по порядку статусов.</summary>
    private static PanelInfo BuildPanel(string id, List<PanelFile> files, TrackerSettings settings)
    {
        var ordered = files
            .OrderByDescending(f => f.Revision ?? 0)
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => settings.OrderOf(f.StatusKey))
            .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var current = ordered[0];
        var statusKeys = files
            .Select(f => f.StatusKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(settings.OrderOf)
            .ToList();
        return new PanelInfo
        {
            Id = current.PanelId,
            CurrentFile = current,
            Files = ordered,
            DuplicateStatusKeys = statusKeys,
        };
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
