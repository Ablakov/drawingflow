using System.IO;
using System.Text.Json;
using DrawingFlow.Client;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.Services;

public sealed class DesktopSettings
{
    public string? ServerAddress { get; set; }
    public bool RememberSession { get; set; } = true;
    public string? LastLogin { get; set; }
    public Guid? LastOrganizationId { get; set; }
    public Guid? LastProjectId { get; set; }
}

/// <summary>Настройки приложения в %APPDATA%\DrawingFlow\settings.json. Также отдаёт адрес сервера клиенту API.</summary>
public sealed class DesktopSettingsStore : IServerAddressProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ILogger<DesktopSettingsStore> _logger;
    private readonly object _gate = new();

    public DesktopSettingsStore(ILogger<DesktopSettingsStore> logger)
    {
        _logger = logger;
        Current = Load();
    }

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrawingFlow");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public DesktopSettings Current { get; private set; }

    public Uri? BaseAddress
    {
        get
        {
            var address = Current.ServerAddress;
            return !string.IsNullOrWhiteSpace(address) && Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
        }
    }

    public void Update(Action<DesktopSettings> change)
    {
        lock (_gate)
        {
            change(Current);
            Save(Current);
        }
    }

    private DesktopSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath), Json) ?? new DesktopSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось прочитать настройки {Path}; используются значения по умолчанию", FilePath);
        }

        return new DesktopSettings();
    }

    private void Save(DesktopSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось сохранить настройки {Path}", FilePath);
        }
    }
}
