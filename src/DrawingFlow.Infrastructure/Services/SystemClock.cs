using DrawingFlow.Application.Abstractions;

namespace DrawingFlow.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
