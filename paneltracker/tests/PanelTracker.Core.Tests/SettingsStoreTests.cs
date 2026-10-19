using PanelTracker.Core.Models;

namespace PanelTracker.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Creates_default_settings_file_when_missing()
    {
        using var project = new TempProject();
        var path = Path.Combine(project.Root, "settings.json");

        var settings = SettingsStore.LoadOrCreate(path);

        Assert.True(File.Exists(path));
        Assert.Equal(6, settings.Statuses.Count);
        Assert.Equal("01 Ready to Check", settings.Statuses[0].Folder);
        Assert.Equal(StatusRole.Sent, settings.FindStatus("sent")!.Role);
        Assert.Equal(StatusRole.Hold, settings.FindByRole(StatusRole.Hold)!.Role);
        Assert.Empty(settings.Validate());
        Assert.Contains("\"role\": \"sent\"", File.ReadAllText(path));
    }

    [Fact]
    public void Round_trips_custom_statuses()
    {
        using var project = new TempProject();
        var path = Path.Combine(project.Root, "settings.json");
        var settings = new TrackerSettings
        {
            RootFolder = @"D:\Проект",
            RescanIntervalSeconds = 5,
            Statuses =
            [
                new StatusDefinition { Key = "draft", Name = "Черновик", Folder = "1 Черновики", Color = "#123456" },
                new StatusDefinition { Key = "done", Name = "Готово", Folder = "2 Готово", Role = StatusRole.Sent },
            ],
        };

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.LoadOrCreate(path);

        Assert.Equal(@"D:\Проект", loaded.RootFolder);
        Assert.Equal(5, loaded.RescanIntervalSeconds);
        Assert.Equal(2, loaded.Statuses.Count);
        Assert.Equal("1 Черновики", loaded.Statuses[0].Folder);
        Assert.Equal("#123456", loaded.Statuses[0].Color);
        Assert.Equal(StatusRole.Sent, loaded.Statuses[1].Role);
    }

    [Fact]
    public void Validation_reports_duplicates_and_empty_values()
    {
        var settings = new TrackerSettings
        {
            Statuses =
            [
                new StatusDefinition { Key = "a", Name = "A", Folder = "X" },
                new StatusDefinition { Key = "A", Name = "A2", Folder = "x" },
                new StatusDefinition { Key = "", Name = "B", Folder = "bad/name" },
            ],
            FileExtensions = [],
            RescanIntervalSeconds = 0,
        };

        var errors = settings.Validate();

        Assert.Contains(errors, e => e.Contains("повторяется"));
        Assert.Contains(errors, e => e.Contains("нескольким статусам"));
        Assert.Contains(errors, e => e.Contains("пустой ключ"));
        Assert.Contains(errors, e => e.Contains("расширения"));
        Assert.Contains(errors, e => e.Contains("пересканирования"));
    }
}
