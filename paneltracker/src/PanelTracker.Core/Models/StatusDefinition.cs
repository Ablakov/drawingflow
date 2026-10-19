namespace PanelTracker.Core.Models;

/// <summary>Статус панели. Статус панели равен папке, в которой лежит её PDF.</summary>
public sealed class StatusDefinition
{
    /// <summary>Устойчивый идентификатор статуса (латиницей), используется в истории.</summary>
    public required string Key { get; set; }

    /// <summary>Название для интерфейса.</summary>
    public required string Name { get; set; }

    /// <summary>Имя подпапки внутри корневой папки проекта.</summary>
    public required string Folder { get; set; }

    /// <summary>Цвет плитки в формате #RRGGBB.</summary>
    public string Color { get; set; } = "#9E9E9E";

    public StatusRole Role { get; set; } = StatusRole.Normal;

    public override string ToString() => $"{Key} ({Folder})";
}
