using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Client.Logging;

/// <summary>
/// Простой файловый журнал для Desktop и Agent: один файл в день, ротация по сроку хранения, без зависимостей.
/// Сообщения пишутся как есть — секреты (пароли, токены, ключи) в вызовах логирования не передаются по правилам проекта.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly LogLevel _minimumLevel;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string directory, string prefix, LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14)
    {
        _directory = directory;
        _prefix = prefix;
        _minimumLevel = minimumLevel;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.EnumerateFiles(directory, $"{prefix}-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-retentionDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Журнал не должен мешать работе приложения.
        }
    }

    public string CurrentFilePath => Path.Combine(_directory, $"{_prefix}-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(Abbreviate(level)).Append("] ")
            .Append(category).Append(": ").Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        line.AppendLine();
        lock (_gate)
        {
            try
            {
                File.AppendAllText(CurrentFilePath, line.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => level.ToString(),
    };

    public void Dispose() => _loggers.Clear();

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
            }
        }
    }
}
