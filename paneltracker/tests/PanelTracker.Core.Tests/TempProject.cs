using PanelTracker.Core.Models;

namespace PanelTracker.Core.Tests;

/// <summary>Временная корневая папка проекта с папками статусов по умолчанию; удаляется после теста.</summary>
public sealed class TempProject : IDisposable
{
    public TempProject(bool createStatusFolders = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "paneltracker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Settings = TrackerSettings.CreateDefault();
        Settings.RootFolder = Root;
        if (createStatusFolders)
        {
            foreach (var status in Settings.Statuses)
            {
                Directory.CreateDirectory(Path.Combine(Root, status.Folder));
            }
        }
    }

    public string Root { get; }

    public TrackerSettings Settings { get; }

    public string FolderOf(string statusKey) => Path.Combine(Root, Settings.FindStatus(statusKey)!.Folder);

    /// <summary>Создаёт файл чертежа в папке статуса и возвращает его полный путь.</summary>
    public string AddFile(string statusKey, string fileName, DateTime? lastWriteUtc = null)
    {
        var path = Path.Combine(FolderOf(statusKey), fileName);
        File.WriteAllText(path, "%PDF-1.4 test " + fileName);
        if (lastWriteUtc is { } ts)
        {
            File.SetLastWriteTimeUtc(path, ts);
        }

        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // временная папка — не критично
        }
    }
}
