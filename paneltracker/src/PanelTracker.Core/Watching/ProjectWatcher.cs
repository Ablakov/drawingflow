namespace PanelTracker.Core.Watching;

/// <summary>
/// Следит за корневой папкой проекта: события FileSystemWatcher объединяются (дебаунс), а периодический таймер
/// гарантирует пересканирование даже если события потерялись (сетевые диски, переполнение буфера).
/// </summary>
public sealed class ProjectWatcher : IDisposable
{
    private readonly string _rootFolder;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _rescanInterval;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Timer? _periodicTimer;
    private bool _disposed;

    public ProjectWatcher(string rootFolder, TimeSpan debounce, TimeSpan rescanInterval)
    {
        _rootFolder = rootFolder;
        _debounce = debounce;
        _rescanInterval = rescanInterval;
    }

    /// <summary>Вызывается в фоновом потоке, когда нужно пересканировать папки.</summary>
    public event Action<WatchTrigger>? ChangesDetected;

    /// <summary>Вызывается при ошибке наблюдателя (например, папка стала недоступна).</summary>
    public event Action<Exception>? WatcherFailed;

    public bool IsWatching
    {
        get
        {
            lock (_gate)
            {
                return _watcher is { EnableRaisingEvents: true };
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _debounceTimer ??= new Timer(_ => Raise(WatchTrigger.FileSystemEvent), null, Timeout.Infinite, Timeout.Infinite);
            _periodicTimer ??= new Timer(_ => Raise(WatchTrigger.Periodic), null, _rescanInterval, _rescanInterval);
            TryStartWatcher();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _periodicTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void TryStartWatcher()
    {
        if (_watcher is not null || !Directory.Exists(_rootFolder))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(_rootFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (_, _) => Bump();
            watcher.Deleted += (_, _) => Bump();
            watcher.Changed += (_, _) => Bump();
            watcher.Renamed += (_, _) => Bump();
            watcher.Error += (_, e) => OnError(e.GetException());
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            WatcherFailed?.Invoke(ex);
        }
    }

    private void Bump()
    {
        lock (_gate)
        {
            _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnError(Exception exception)
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        WatcherFailed?.Invoke(exception);
        Raise(WatchTrigger.FileSystemEvent);
    }

    private void Raise(WatchTrigger trigger)
    {
        if (trigger == WatchTrigger.Periodic)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                TryStartWatcher(); // папка могла появиться или наблюдатель упал — пробуем снова
            }
        }

        ChangesDetected?.Invoke(trigger);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _debounceTimer?.Dispose();
            _periodicTimer?.Dispose();
        }
    }
}

public enum WatchTrigger
{
    FileSystemEvent,
    Periodic,
}
