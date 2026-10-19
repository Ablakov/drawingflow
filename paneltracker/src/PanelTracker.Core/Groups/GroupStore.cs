using System.Text.Json;
using PanelTracker.Core.Json;
using PanelTracker.Core.Scanning;

namespace PanelTracker.Core.Groups;

/// <summary>Чтение, изменение и сохранение групп панелей в файле groups.json.</summary>
public sealed class GroupStore
{
    public const string FileName = "groups.json";

    private readonly List<PanelGroup> _groups = [];

    public GroupStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public IReadOnlyList<PanelGroup> Groups => _groups;

    public static List<PanelGroup> CreateDefaultGroups() =>
    [
        new() { Key = "A", Name = "Solid panel" },
        new() { Key = "B", Name = "Spandrel" },
        new() { Key = "C", Name = "HM door panel" },
        new() { Key = "D", Name = "Corner panel" },
    ];

    /// <summary>Загружает группы; если файла нет — использует набор по умолчанию (файл будет создан при первом сохранении).</summary>
    public void Load()
    {
        _groups.Clear();
        if (!File.Exists(FilePath))
        {
            _groups.AddRange(CreateDefaultGroups());
            return;
        }

        var document = JsonSerializer.Deserialize<GroupsDocument>(File.ReadAllText(FilePath), JsonDefaults.Indented);
        _groups.AddRange(document?.Groups ?? []);
        foreach (var group in _groups)
        {
            group.Panels = group.Panels
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, PanelIdComparer.Instance)
                .ToList();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new GroupsDocument { Groups = _groups }, JsonDefaults.Indented));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public PanelGroup? Find(string key) => _groups.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    public PanelGroup AddGroup(string key, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Find(key) is not null)
        {
            throw new InvalidOperationException($"Группа с ключом «{key}» уже существует.");
        }

        var group = new PanelGroup { Key = key.Trim(), Name = name.Trim() };
        _groups.Add(group);
        return group;
    }

    public void RenameGroup(string key, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var group = Find(key) ?? throw new InvalidOperationException($"Группа «{key}» не найдена.");
        group.Name = newName.Trim();
    }

    public bool RemoveGroup(string key)
    {
        var group = Find(key);
        return group is not null && _groups.Remove(group);
    }

    /// <summary>Добавляет панели в группу; возвращает число фактически добавленных.</summary>
    public int AddPanels(string key, IEnumerable<string> panelIds)
    {
        var group = Find(key) ?? throw new InvalidOperationException($"Группа «{key}» не найдена.");
        var added = 0;
        foreach (var id in panelIds)
        {
            if (string.IsNullOrWhiteSpace(id) || group.Contains(id))
            {
                continue;
            }

            group.Panels.Add(id.Trim());
            added++;
        }

        group.Panels.Sort(PanelIdComparer.Instance);
        return added;
    }

    /// <summary>Убирает панели из группы; возвращает число фактически убранных.</summary>
    public int RemovePanels(string key, IEnumerable<string> panelIds)
    {
        var group = Find(key) ?? throw new InvalidOperationException($"Группа «{key}» не найдена.");
        var set = new HashSet<string>(panelIds, StringComparer.OrdinalIgnoreCase);
        return group.Panels.RemoveAll(set.Contains);
    }

    public IReadOnlyList<PanelGroup> GroupsOf(string panelId) => _groups.Where(g => g.Contains(panelId)).ToList();

    private sealed class GroupsDocument
    {
        public List<PanelGroup> Groups { get; set; } = [];
    }
}
