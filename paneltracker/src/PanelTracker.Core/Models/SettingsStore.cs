using System.Text.Json;
using PanelTracker.Core.Json;

namespace PanelTracker.Core.Models;

/// <summary>Чтение и запись settings.json. Если файла нет, создаёт его со значениями по умолчанию.</summary>
public static class SettingsStore
{
    public const string FileName = "settings.json";

    public static TrackerSettings LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var created = TrackerSettings.CreateDefault();
            Save(path, created);
            return created;
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<TrackerSettings>(json, JsonDefaults.Indented) ?? TrackerSettings.CreateDefault();
        if (settings.Statuses.Count == 0)
        {
            settings.Statuses = TrackerSettings.CreateDefaultStatuses();
        }

        return settings;
    }

    public static void Save(string path, TrackerSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonDefaults.Indented));
        File.Move(tmp, path, overwrite: true);
    }
}
