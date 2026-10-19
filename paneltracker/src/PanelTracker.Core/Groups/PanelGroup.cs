namespace PanelTracker.Core.Groups;

/// <summary>Группа панелей (например, «A: Solid panel»). Хранится в groups.json в корне проекта.</summary>
public sealed class PanelGroup
{
    public required string Key { get; set; }

    public required string Name { get; set; }

    public string? Color { get; set; }

    public List<string> Panels { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Key) ? Name : $"{Key}: {Name}";

    public bool Contains(string panelId) => Panels.Any(p => string.Equals(p, panelId, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => DisplayName;
}
