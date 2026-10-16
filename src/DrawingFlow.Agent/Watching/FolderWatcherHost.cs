using System.Collections.Concurrent;
using DrawingFlow.Agent.Sync;
using DrawingFlow.Contracts.Agents;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Agent.Watching;

/// <summary>
/// Наблюдатели файловой системы для включённых папок. События лишь подсказка: они объединяются по пути (дебаунс)
/// и передаются ядру как кандидаты; истина устанавливается периодической сверкой.
/// </summary>
public sealed class FolderWatcherHost(SyncEngine engine, AgentOptions options, ILogger<FolderWatcherHost> logger) : IDisposable
{
    private readonly Dictionary<Guid, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<(Guid FolderId, string Path), DateTime> _pending = new();
    private readonly object _gate = new();
    private Timer? _flushTimer;

    public int ActiveWatchers
    {
        get
        {
            lock (_gate)
            {
                return _watchers.Count;
            }
        }
    }

    public void Start()
    {
        _flushTimer ??= new Timer(_ => _ = FlushAsync(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Приводит набор наблюдателей в соответствие конфигурации.</summary>
    public void Apply(IReadOnlyList<WatchedFolderDto> folders)
    {
        lock (_gate)
        {
            var wanted = folders.Where(f => f.IsEnabled).ToDictionary(f => f.Id);
            foreach (var id in _watchers.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                _watchers[id].Dispose();
                _watchers.Remove(id);
            }

            foreach (var folder in wanted.Values)
            {
                if (_watchers.TryGetValue(folder.Id, out var existing))
                {
                    if (string.Equals(existing.Path, folder.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    existing.Dispose();
                    _watchers.Remove(folder.Id);
                }

                if (!Directory.Exists(folder.Path))
                {
                    continue; // недоступная папка проверяется при сверке и подключается, когда появится
                }

                try
                {
                    var watcher = new FileSystemWatcher(folder.Path)
                    {
                        IncludeSubdirectories = options.IncludeSubfolders,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 64 * 1024,
                    };
                    var folderId = folder.Id;
                    watcher.Created += (_, e) => Note(folderId, e.FullPath);
                    watcher.Changed += (_, e) => Note(folderId, e.FullPath);
                    watcher.Renamed += (_, e) => Note(folderId, e.FullPath);
                    watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "Ошибка наблюдателя папки {Path}; изменения будут подобраны сверкой", folder.Path);
                    watcher.EnableRaisingEvents = true;
                    _watchers[folder.Id] = watcher;
                    logger.LogInformation("Наблюдение за папкой запущено: {Path}", folder.Path);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Не удалось запустить наблюдение за папкой {Path}", folder.Path);
                }
            }
        }
    }

    private void Note(Guid folderId, string path) => _pending[(folderId, path)] = DateTime.UtcNow;

    /// <summary>Передаёт ядру пути, по которым события не приходили дольше окна дебаунса.</summary>
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - options.DebounceDelay;
        var ready = _pending.Where(p => p.Value <= threshold).Select(p => p.Key).ToList();
        foreach (var key in ready)
        {
            if (_pending.TryRemove(key, out _))
            {
                try
                {
                    await engine.NotifyChangeAsync(key.FolderId, key.Path, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка обработки события для {Path}", key.Path);
                }
            }
        }

        return ready.Count;
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        lock (_gate)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }
    }
}
