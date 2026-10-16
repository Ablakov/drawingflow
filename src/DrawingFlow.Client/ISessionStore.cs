namespace DrawingFlow.Client;

/// <summary>Хранилище сессии (например, файл, зашифрованный DPAPI на Windows).</summary>
public interface ISessionStore
{
    Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemorySessionStore : ISessionStore
{
    private AuthSession? _session;

    public Task<AuthSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session);

    public Task SaveAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        _session = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _session = null;
        return Task.CompletedTask;
    }
}
