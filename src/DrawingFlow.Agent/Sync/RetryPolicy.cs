namespace DrawingFlow.Agent.Sync;

/// <summary>Экспоненциальная задержка повторов с верхней границей.</summary>
public static class RetryPolicy
{
    public static TimeSpan Delay(int retryCount, AgentOptions options)
    {
        var exponent = Math.Clamp(retryCount - 1, 0, 30);
        var ticks = options.InitialRetryDelay.Ticks * (double)(1L << exponent);
        return ticks >= options.MaxRetryDelay.Ticks ? options.MaxRetryDelay : TimeSpan.FromTicks((long)ticks);
    }

    public static bool IsExhausted(int retryCount, AgentOptions options) => retryCount > options.MaxRetries;
}

public interface IAgentClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemAgentClock : IAgentClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
