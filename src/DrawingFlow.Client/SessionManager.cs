namespace DrawingFlow.Client;

/// <summary>
/// Текущая сессия в памяти процесса плюс сохранение в <see cref="ISessionStore"/>.
/// Единственный экземпляр на приложение; уведомляет об изменении и об истечении сессии.
/// </summary>
public sealed class SessionManager(ISessionStore store)
{
    private readonly object _gate = new();
    private AuthSession? _current;
    private bool _initialized;

    public AuthSession? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsAuthenticated => Current is not null;

    /// <summary>Сессия изменилась (вход, обновление токенов, выход).</summary>
    public event EventHandler? Changed;

    /// <summary>Сессию не удалось продлить: требуется повторный вход.</summary>
    public event EventHandler? Expired;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var loaded = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _current = loaded;
            _initialized = true;
        }
    }

    public async Task SetAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _current = session;
        }

        await store.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _current = null;
        }

        await store.ClearAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Сбрасывает сессию и уведомляет подписчиков, что требуется повторный вход.</summary>
    public async Task ExpireAsync(CancellationToken cancellationToken = default)
    {
        await ClearAsync(cancellationToken).ConfigureAwait(false);
        Expired?.Invoke(this, EventArgs.Empty);
    }
}
